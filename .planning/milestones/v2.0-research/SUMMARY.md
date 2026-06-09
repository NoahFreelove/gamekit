# Project Research Summary

**Project:** GameKit v2.0 — Expansion: Providers, Lobby & Rating-Aware Play
**Domain:** Self-hosted GPL .NET 10 game-services library (additive milestone on mature v1.0 codebase)
**Researched:** 2026-06-05
**Confidence:** HIGH (stack versions GA-verified; architecture grounded in direct v1 codebase reads)

---

## Executive Summary

GameKit v2.0 adds four capability pillars to an already-shipped v1.0 codebase (~34k LOC, 7 NuGet packages): richer auth (Argon2, Google/Apple/Epic OAuth, account merge), matchmaking that uses real ratings (fixing the v1 EloRange rating=0 wart, plus rank decay, placement matches, regional pools, backfill), a new `GameKit.Lobby` real-time coordination package, and a horizontally-scalable Admin UI via SignalR + Redis backplane. Every addition is additive-only to existing tables (except the two v1 Out-of-Scope reversals — account merge and first-class regional pools, which require narrow schema additions in Auth and Matchmaking respectively). The established v1 patterns — per-package migrations with live-verified advisory lock keys, `BackgroundService` + Polly leader election, Scrutor pluggable strategies, optional-port null-object injection — are the law for every new package and modification.

The recommended build order places the zero-migration, zero-risk items first (Core `IPlayerRatingProvider` seam + all four stateless auth packages) to establish the rating wire and unblock everything else. Rankings depth (rating-aware brackets with guardrails, rank decay, placement) follows immediately because those EF migrations need to be stable before account merge reads the `player_ranks` schema. Regional pools require no migration (PoolName + key pattern already exist) and precede Lobby so the Lobby enqueue path inherits the correct queue key structure. Account merge is deliberately isolated as its own high-risk phase — built last among the core features, after all cross-package schemas are frozen. GameKit.Lobby and Admin multi-replica close out the milestone.

The single highest-risk item is account merge: a SERIALIZABLE transaction touching 8+ tables with irreversible FK re-pointing, a mandatory `account_merge_log` idempotency table for crash-resume, and a banned-player conflict policy. The second highest risk is the Apple Sign-In provider, which has three production-outage traps (ES256 client-secret expiry, `sub`-not-email identity key, first-login-only name/email capture). Every new package phase must begin with a Wave 0 advisory-lock live-verify step — this gate is non-negotiable per v1 precedent.

---

## Key Findings

### Recommended Stack

The v1.0 dependency set is frozen and unchanged. V2.0 adds exactly seven NuGet entries to `Directory.Packages.props`. All are GA on nuget.org as of 2026-06-05, all are GPL-compatible, and none introduce cloud or SaaS dependencies.

**New NuGet additions (complete list):**

| Package | Version | Used By | License |
|---------|---------|---------|---------|
| `Isopoh.Cryptography.Argon2` | `2.0.0` | `GameKit.Auth.Argon2` | CC0 (public domain) |
| `Isopoh.Cryptography.Blake2b` | `2.0.0` | `GameKit.Auth.Argon2` (transitive) | CC0 |
| `Isopoh.Cryptography.SecureArray` | `2.0.0` | `GameKit.Auth.Argon2` (transitive) | CC0 |
| `Microsoft.AspNetCore.Authentication.Google` | `10.0.8` | `GameKit.Auth.Google` | MIT |
| `AspNet.Security.OAuth.Apple` | `10.0.0` | `GameKit.Auth.Apple` | Apache-2.0 |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | `8.14.0` (min) | `GameKit.Auth.Apple` (transitive) | MIT |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `10.0.8` | `GameKit.Lobby`, `GameKit.Admin.UI` | MIT |

**Epic Games provider:** No NuGet package exists. Implement as a custom `OAuthHandler<EpicOAuthOptions>` extending `OAuthOptions` from the shared framework. Epic's endpoints are standard OAuth 2.0 auth-code; `OAuthHandler<T>` covers it with no additional NuGet dep. Open question: custom handler vs. aspnet-contrib — resolve in Phase 1 planning.

**SignalR hub core** (`Hub`, `IHubContext<T>`, `AddSignalR()`) is in `Microsoft.AspNetCore.App` — no NuGet pin needed.

**Do NOT add:** `Microsoft.Azure.SignalR` (cloud-only, hard GPL exclusion), any `Azure.*` SDK, `OpenIddict`, `Konscious.Security.Cryptography.Argon2`, the old `Microsoft.AspNetCore.SignalR.Redis` (1.x Redis client), any AI/LLM SDK, `MediatR >= 13`, `AutoMapper >= 13`.

**Argon2id tuning defaults** for `GameKitArgon2Options`: `MemoryCost=65536` (64 MiB), `TimeCost=3`, `Lanes=1`, `Threads=1`, `Type=HybridAddressing` (Argon2id), `HashLength=32`. Exceeds OWASP 2024 minimums.

**BCrypt → Argon2 migration:** No schema migration needed. Isopoh hashes begin `$argon2id$`; BCrypt begin `$2a$`/`$2b$`. Detect prefix at verify time, rehash on successful login. Open question at execution: confirm format-prefix detection is sufficient and no `algorithm` discriminator column migration is needed.

### Expected Features

V2.0 contains 13 deliverables across four packages. All are explicitly scoped in `PROJECT.md`.

**Table stakes (users expect these):**
- **Rating-aware matchmaking** — fixes the v1 EloRange rating=0 wart; `IPlayerRatingProvider` seam in Core
- **Placement matches** — `placement_matches_remaining` + `is_in_placement` on `player_ranks`; decrement on session complete; placement-pool isolation
- **Rank decay** — RD inflation only (not rating subtraction); `BackgroundService` with per-ladder thresholds; decay-immune below configurable tier
- **Regional matchmaking pools** — first-class `RegionName` on enqueue DTO; `mm:queue:{ladderId}:{regionName}` Redis key; `AllowedRegions` on `MatchmakingLadderConfig`; NO schema migration (PoolName column + key structure already exist)
- **Backfill** — `backfill` ticket type; `POST /api/matchmaking/backfill`; higher-priority queue; `ParticipationFraction` guard mandatory in same phase
- **Argon2 sibling hasher** — `GameKit.Auth.Argon2`; `IPasswordHasher` drop-in; rehash-on-login live migration
- **Google OAuth** — `GameKit.Auth.Google`; stateless, no migration; `sub` as `external_id`
- **Multi-replica Admin UI** — `RedisErrorRateCounter` replaces in-memory ring buffer; `AdminEventHub` + Redis backplane; fix Rank-adjust stub page

**Differentiators (competitive advantage):**
- **Account merge** with explicit conflict policy — take-higher rating, sum wins/losses, revoke source tokens, `account_merge_log` idempotency; SERIALIZABLE transaction; `IAccountMergePolicy` extension point
- **Rating-aware bracket expansion using RD** — bracket width `±k*RD` + time expansion, `MaxBracketWidth` cap + `MinPoolDepthBeforeBracketExpansion` guardrails shipped simultaneously with the rating seam
- **Rank decay via RD inflation** (not rating loss) — Glicko-2-correct, fairer than LoL-style point subtraction
- **Apple + Epic OAuth** — Apple mandatory for iOS games; Epic for cross-store distribution

**Explicitly deferred (do not revisit in v2.0):**
- Persistent general-purpose lobby chat history (GDPR/moderation obligations; `ILobbyMessageHandler` hook for operators)
- Friends graph (`GameKit.Social`) — next milestone
- Cross-region data federation — operator infrastructure concern
- Webhook bus on account merge — `IAccountMergeCompletedHandler` Scrutor-scanned instead

**Open question at execution:** `lobby_messages` — ARCHITECTURE.md recommends persisting (reconnect UX, admin moderation); FEATURES.md flags ephemeral-only to avoid GDPR obligations. Recommended resolution: persist with configurable 30-day retention cleanup job + `ILobbyMessageHandler` extension point. Confirm during Phase 5 planning.

### Architecture Approach

V2.0 is strictly additive within the established v1 dependency DAG. The critical new seam is `IPlayerRatingProvider` placed in `GameKit.Core`, implemented by `GameKit.Rankings`, injected as an optional `?` constructor parameter in `MatchmakingService.EnqueueAsync` — preserves package independence and the zero-rating v1 fallback for installs without Rankings. New packages slot as: Auth.Argon2/Google/Apple/Epic → Auth (leaf siblings); Lobby → Core + Matchmaking (new downward arc, no cycle).

**New and modified components:**

| Component | Package | Type | Migration |
|-----------|---------|------|-----------|
| `IPlayerRatingProvider` + `PlayerRatingSnapshot` | `GameKit.Core` | NEW interface | No |
| `PlayerRankingsProvider : IPlayerRatingProvider` | `GameKit.Rankings` | NEW impl | No |
| `MatchmakingService.EnqueueAsync` — real ratings | `GameKit.Matchmaking` | MODIFIED | No |
| `GameKit.Auth.Argon2` (full package) | NEW PACKAGE | — | No |
| `GameKit.Auth.Google/Apple/Epic` (3 packages) | NEW PACKAGES | — | No |
| `IAccountMergeService` + `account_merges` table | `GameKit.Auth` | NEW | YES (Auth key `-298890956`) |
| Rank decay `BackgroundService` + `LastDecayAt` | `GameKit.Rankings` | NEW | YES (Rankings key `-156812172`) |
| `PlacementMatchesRemaining` on `player_ranks` | `GameKit.Rankings` | MODIFIED | YES (Rankings key `-156812172`) |
| `AllowedRegions` + `RegionName` DTO | `GameKit.Matchmaking` | MODIFIED | No |
| Backfill ticket type + endpoint | `GameKit.Matchmaking` | NEW | No |
| `GameKit.Lobby` (3 tables) | NEW PACKAGE | — | YES (new key — live-verify) |
| `RedisErrorRateCounter` | `GameKit.Admin.UI` | NEW | No |
| `AdminEventHub` + `AdminLiveBroadcastService` | `GameKit.Admin.UI` | NEW | No |

**Advisory lock keys:** Auth and Rankings reuse existing keys with new migration timestamps. `GameKit.Lobby` requires a new key — Wave 0 live-verify gate: `SELECT hashtext('gamekit.lobby.migrations')::bigint` in Testcontainers.

### Critical Pitfalls

1. **Apple client-secret ES256 expiry → production outage at 6-month mark** — Use `GenerateClientSecret = true` (per-exchange, never cached). Load `.p8` from secrets manager. `ClientSecretExpiresAfter = TimeSpan.FromDays(170)`. Assert in integration test. Document ops rotation runbook. Warning signs: all Apple logins return `invalid_client` while other providers work.

2. **Account merge leaves orphaned FKs or splits on retry** — `account_merge_log` table with `pending → committed → redis_cleaned` state machine is mandatory. Postgres re-homing in SERIALIZABLE transaction; Redis cleanup after commit; idempotent resume on retry. Implement this table as the FIRST task of Phase 4. Test: kill process mid-merge, verify resume produces clean state.

3. **Rating feedback loop after wiring real ratings — sparse pools funnel high-RD players against top-rated players** — `MaxBracketWidth` cap and `MinPoolDepthBeforeBracketExpansion` guardrails MUST ship in the same commit as the `IPlayerRatingProvider` seam. Never wire real ratings without these guards. Unit test: bracket expansion stops at `MaxBracketWidth`.

4. **Advisory lock key collision for new packages** — Every new package with migrations needs `SELECT hashtext('gamekit.<pkg>.migrations')::bigint` live-verified in Testcontainers as Wave 0. Pairwise-distinctness test must include all five v1 keys as integer literals. `GameKit.Lobby` is the only new package requiring this in v2.0.

5. **SignalR Redis backplane requires sticky sessions — not optional** — The backplane routes messages, not connection handshakes. Without IP-hash or cookie-affinity at the load balancer, negotiate and WebSocket upgrade land on different replicas. Set `ChannelPrefix = RedisChannel.Literal("GameKit")`. Document in ops guide and `docker-compose.yml`.

6. **Apple `sub`-not-email identity key + first-login-only name/email** — Use `sub` (Apple User Identifier) as `external_id`, never the relay email. Store relay email in `player_identities.metadata` JSONB only. Name/email MUST be persisted on first login — Apple never resends them. Account merge UI offers "Merge by Provider Identity" (provider + external_id), not "Merge by Email" for Apple/Epic.

---

## Implications for Roadmap

Suggested phase structure: **6 phases**. Hard ordering constraints: (a) rating seam before rating-dependent work; (b) `player_ranks` schema final before account merge reads it; (c) account merge isolated — no downstream phases depend on it; (d) Lobby before Admin multi-replica to prove SignalR pattern first.

---

### Phase 1: Core Rating Seam + Stateless Auth Packages

**Rationale:** Zero-migration, zero-risk items. Rating seam unblocks all rating-dependent work. Auth packages are fully independent. Maximum parallelism.

**Delivers:**
- `IPlayerRatingProvider` interface in `GameKit.Core` + `PlayerRatingSnapshot` record
- `PlayerRankingsProvider` in `GameKit.Rankings`
- `MatchmakingService.EnqueueAsync` wired to real ratings with `MaxBracketWidth` + `MinPoolDepthBeforeBracketExpansion` guardrails (shipped simultaneously — not deferred)
- `GameKit.Auth.Argon2` (Isopoh.Cryptography.Argon2 2.0.0, rehash-on-login, no migration)
- `GameKit.Auth.Google` (Microsoft.AspNetCore.Authentication.Google 10.0.8, no migration)
- `GameKit.Auth.Apple` (AspNet.Security.OAuth.Apple 10.0.0, `GenerateClientSecret=true`, no migration)
- `GameKit.Auth.Epic` (custom `OAuthHandler<EpicOAuthOptions>`, no migration)

**Avoids:** Rating feedback loop (Pitfall 6) by shipping guardrails with the seam. Apple production outage (Pitfall 1) and relay-email identity collision (Pitfall 2) by correct Apple implementation from day one.

**Research flag:** Epic provider (custom handler vs. library) — resolve in planning before coding. Argon2 discriminator column question — resolve in planning (expected: format-prefix sufficient, no migration).

---

### Phase 2: Rankings Depth — Decay, Placement, Rating-Aware Brackets

**Rationale:** Depends on Phase 1. Groups Rankings-owned schema migrations together so `player_ranks` reaches final v2.0 shape before Phase 4 account merge reads it. Decay and placement are independent of each other within this phase.

**Delivers:**
- Rank decay `BackgroundService` (RD inflation only; per-ladder `InactivityThresholdDays` + `DecayThresholdRating` + `DecayProtectionDays`; dedicated Redis leader lock separate from matchmaking ticker lock; `RankAdjustAudit` rows tagged `reason="decay"`)
- Placement matches (`placement_matches_remaining` + `is_in_placement` on `player_ranks`; decrement on session complete; placement-pool `IsPlacementLadder` config variant; `MaxPlacementRatingGain` cap; win-rate > 0.9 triggers admin audit row)
- Rankings migrations for both (reuse existing advisory lock key `-156812172`)

**Avoids:** Rank decay double-penalty (Pitfall 7): RD inflation only, unit-test absent player loses no rating. Placement smurf exploit (Pitfall 8): separate placement pool, admin audit flag. Migration boundary (Pitfall 11): new columns in Rankings migrations only.

**Research flag:** None — standard patterns.

---

### Phase 3: Regional Matchmaking Pools + Backfill

**Rationale:** Regional pools need no migration (PoolName exists, key structure exists). Build before Phase 5 Lobby so `TryStartMatchmakingAsync` inherits the stable `RegionName` enqueue API. Backfill groups here as a Matchmaking extension.

**Delivers:**
- `AllowedRegions IReadOnlyList<string>` on `MatchmakingLadderConfig`; `RegionName string?` on enqueue DTO
- Redis key pattern `mm:queue:{ladderId}:{regionName}` with `__global` default (v1 backwards-compatible); startup legacy-key warning log
- Cross-region fallback: `MaxWaitBeforeGlobalFallbackSeconds int?` on `MatchmakingLadderConfig`
- `backfill` ticket type; `POST /api/matchmaking/backfill`; backfill-priority sorted set
- `BackfillJoinedAt` + `ParticipationFraction` on `GameSessionParticipant` (mandatory, not deferrable); `ParticipationFraction < MinParticipationForRatingChange` guard in `IRankingAlgorithm.Apply`

**Avoids:** Regional pool key leakage (Pitfall 10): key schema includes region segment + startup legacy-key warning. Backfill late-join rating penalty (Pitfall 9): `ParticipationFraction` guard ships in same phase.

**Research flag:** None — standard patterns.

---

### Phase 4: Account Merge (Isolated High-Risk Phase)

**Rationale:** Deliberately isolated. Depends on Phase 2 (`player_ranks` schema frozen) and Phase 1 (new `player_identities` rows from Apple/Google/Epic covered by merge logic). No downstream phases depend on it. Highest-risk operation in v2.0 — isolating it limits blast radius.

**Delivers:**
- `account_merges` table migration in `GameKit.Auth` (reuse existing key `-298890956`)
- `account_merge_log` idempotency table: `pending → committed → redis_cleaned` state machine; crash-resume on retry (FIRST task of this phase)
- `AccountMergeService` with SERIALIZABLE transaction, ID-order lock acquisition, 40001 retry via existing `SerializationFailureRetry` pattern
- Conflict policies: take-higher rating per ladder, sum wins/losses/draws, revoke all source refresh tokens, `PreserveMostRestrictive` ban policy, `IAccountMergePolicy` extension point
- Tombstone: source `player_id` soft-deleted with `merged_into_player_id` FK for traceability
- Admin UI "Account Merge" flow (requires `gamekit.admin.superadmin` policy; source_player_id NOT in API response)
- `admin_audit_log` entry: `action = "auth.account_merge"` referencing both player IDs

**Avoids:** FK corruption + partial-merge (Pitfall 3): `account_merge_log` idempotent resume. Banned-player escaping ban via merge. Security: superadmin-only endpoint, no source ID in response.

**Research flag:** `party_members` unique constraint conflict when source and target are in the same party — needs explicit resolution policy in planning. `admin_audit_log.actor_id` FK behavior on source-player delete — confirm `ON DELETE SET NULL` vs tombstone approach.

---

### Phase 5: GameKit.Lobby (New Package)

**Rationale:** New package with the most surface area in v2.0. Depends on Phase 3 (stable Matchmaking enqueue API with RegionName). Establishes the SignalR + Redis backplane pattern that Phase 6 Admin reuses.

**Delivers:**
- `GameKit.Lobby` NuGet package
- `lobbies` + `lobby_members` + `lobby_messages` tables; new migration; advisory lock live-verified (`SELECT hashtext('gamekit.lobby.migrations')::bigint`) as Wave 0 gate; exclusion list: 20 prior-package entities
- `LobbyHub : Hub` (SignalR; group `"lobby:{lobbyId}"`; `[Authorize(JwtBearerDefaults.AuthenticationScheme)]`; unauthenticated WS upgrade returns 401 integration test)
- Redis backplane from day one: `AddStackExchangeRedis(connectionString, opts => opts.Configuration.ChannelPrefix = RedisChannel.Literal("gamekit:signalr"))`
- `LobbyService.TryStartMatchmakingAsync` → `IPartyService.CreateAsync` → `IMatchmakingService.EnqueueAsync`
- Ready-check state machine; lobby lifecycle (`Open → ReadyChecking → InGame → Open` reset post-session via `ISessionLifecycleObserver`)
- `ILobbyMessageHandler` Scrutor-scanned extension point for operator chat intercept/moderation
- Ops guide + `docker-compose.yml` sticky-session documentation

**Open question to resolve in planning:** lobby_messages persistence — recommended: persist with 30-day retention cleanup (mirrors `MatchmakingRetentionCleanupService`). Must decide before schema is cut.

**Avoids:** SignalR sticky sessions (Pitfall 5): two-`TestServer` integration test + ops guide. Advisory lock collision (Pitfall 4): Wave 0 live-verify gate. Migration boundary (Pitfall 11): Lobby creates only its own tables. Admin/player hub isolation: `LobbyHub` JWT-only; admin hub cookie-only — separate hubs.

**Research flag:** Advisory lock key must be live-verified (Wave 0). Two-`TestServer` backplane integration test is novel test infrastructure — plan extra time.

---

### Phase 6: Admin Multi-Replica (SignalR Backplane + Rank-Adjust Fix)

**Rationale:** Operational polish. Reuses SignalR + Redis backplane pattern proven in Phase 5. The rank-adjust stub fix belongs here because it depends on Phase 2 Rankings schema being stable.

**Delivers:**
- `RedisErrorRateCounter` replacing `ErrorRateRingBuffer` hot path (aggregate error count via Redis time-bucketed `INCRBY`; `ErrorRateRingBuffer` retained for tests)
- `AdminEventHub : Hub` + `AdminLiveBroadcastService : BackgroundService` (Redis pub/sub `"gamekit:admin:events"` → `IHubContext<AdminEventHub>`)
- `GameKitAdminOptions.RedisConnectionString` (null = single-replica mode; backplane opt-in)
- Channel prefix: `RedisChannel.Literal("gamekit:admin")` (isolated from Lobby's `"gamekit:signalr"`)
- Fix Admin "Rank adjust" stub nav page (wire to existing `RankAdjustService` from Phase 2)
- Data Protection key-sharing documentation for operators (cookie auth across replicas)

**Avoids:** Admin/player hub CSRF isolation: `AdminEventHub` requires `GameKitAdmin` cookie scheme + antiforgery (mirrors v1 `CrossSchemeIsolationTests`). Per-replica error count misleading operators.

**Research flag:** None — same pattern as Phase 5 SignalR work.

---

### Phase Ordering Rationale

- Phase 1 first: zero risk, rating seam unblocks everything, auth packages are independent leaf nodes
- Phase 2 before Phase 4: account merge reads `player_ranks` schema — that schema must be frozen first
- Phase 3 before Phase 5: Lobby's `TryStartMatchmakingAsync` needs the stable `RegionName` enqueue API
- Phase 4 isolated: no downstream dependents; isolating limits blast radius if rework is needed
- Phase 5 before Phase 6: Admin reuses SignalR backplane pattern proven in Lobby
- Phase 6 last: operational polish, no feature gates blocked on it

### Research Flags

Phases needing deeper research during planning:
- **Phase 1 (Epic provider):** Custom `OAuthHandler<EpicOAuthOptions>` vs. any emerging aspnet-contrib package — resolve before coding. Expected: custom handler.
- **Phase 1 (Argon2 discriminator):** Confirm format-prefix detection sufficient; no migration needed.
- **Phase 4 (account merge):** `party_members` unique-constraint conflict path needs explicit business logic. `admin_audit_log.actor_id` FK behavior on source-player delete needs confirmation.
- **Phase 5 (lobby_messages):** Persist vs. ephemeral policy must be decided before schema is cut.

Phases with standard well-documented patterns (skip research-phase):
- **Phase 2 (rank decay + placement):** Glicko-2 inactivity formula specified; `BackgroundService` pattern is v1 precedent.
- **Phase 3 (regional pools + backfill):** PoolName column and Redis key structure already exist; backfill follows FlexMatch/Open Match patterns.
- **Phase 6 (Admin multi-replica):** Identical SignalR + Redis backplane pattern as Phase 5.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All 7 new package versions GA-verified on nuget.org 2026-06-05; all licenses confirmed GPL-compatible; Epic = no NuGet (expected, documented) |
| Features | HIGH | Standard industry patterns (Glicko-2, FlexMatch backfill, SignalR backplane); Apple SIWA quirks verified from official Apple docs + aspnet-contrib docs |
| Architecture | HIGH | All findings grounded in direct v1 codebase reads (file paths cited throughout ARCHITECTURE.md); `IPlayerRatingProvider` placement follows established `IPresenceProvider`/`IPostSessionCompleteHandler` null-object-port pattern |
| Pitfalls | HIGH | Apple ES256 expiry: verified from Apple Developer Portal + aspnet-contrib source. Account merge: extrapolated from existing `SerializationFailureRetry.cs`. Rating feedback loop: derived from v1 EloRange code behavior |

**Overall confidence: HIGH**

### Gaps to Address

- **Epic provider implementation:** Custom `OAuthHandler<EpicOAuthOptions>` vs. any library — resolve in Phase 1 planning before coding. Expected outcome: custom handler (EOS OAuth 2.0 is standard enough for `OAuthHandler<T>`).
- **Argon2 algorithm discriminator:** Format-prefix detection (`$argon2id$` vs. `$2a$`) expected to be sufficient — no `algorithm` column migration needed. Confirm in Phase 1 planning; fallback is a single `player_credentials` column migration in Auth.
- **lobby_messages persistence:** Recommended: persist with 30-day retention cleanup + `ILobbyMessageHandler`. Confirm in Phase 5 planning before schema is cut.
- **`party_members` unique constraint during account merge:** If source and target are both members of the same party, re-pointing `player_id` violates the unique constraint. Resolution strategy (abort merge? remove source member?) needs explicit business logic decision in Phase 4 planning.

---

## Sources

### Primary (HIGH confidence)
- `.planning/research/STACK.md` — all new package versions, licenses, TFMs, compatibility matrix; verified 2026-06-05
- `.planning/research/FEATURES.md` — feature landscape, complexity ratings, phase recommendations, dependency graph
- `.planning/research/ARCHITECTURE.md` — direct v1 codebase reads; `IPlayerRatingProvider` design; account merge transaction design; advisory lock keys
- `.planning/research/PITFALLS.md` — 11 pitfalls with prevention strategies and phase-to-pitfall mapping
- `.planning/PROJECT.md` — v2.0 scope, constraints, v1 Out-of-Scope reversals
- `src/GameKit.Matchmaking/Services/MatchmakingService.cs:201-204` — zero-rating hardcode confirmed
- `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs:52` — PoolName column confirmed existing
- `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` — `mm:queue:{ladderId}:{poolName}` confirmed
- `.planning/STATE.md` — v1 advisory lock keys (five values, all live-verified against Postgres 17.9)
- [NuGet: Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8](https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR.StackExchangeRedis)
- [MS Learn: Redis backplane for ASP.NET Core SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0)
- [Apple Developer: Creating a client secret](https://developer.apple.com/documentation/accountorganizationaldatasharing/creating-a-client-secret) — ES256 JWT 6-month max
- [aspnet-contrib: sign-in-with-apple.md](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/blob/dev/docs/sign-in-with-apple.md) — per-request secret generation
- [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) — Argon2id tuning minimums
- [Glickman: Glicko-2 System PDF](https://www.glicko.net/glicko/glicko2.pdf) — inactivity period RD inflation formula
- [AWS FlexMatch Backfill](https://docs.aws.amazon.com/gameliftservers/latest/flexmatchguide/match-backfill-client.html) — canonical backfill design

### Secondary (MEDIUM confidence)
- [Epic Online Services: Auth Web APIs](https://dev.epicgames.com/docs/web-api-ref/authentication) — OAuth 2.0 endpoints confirmed; no .NET library exists
- [gpluscb: So You Want to Use Glicko-2 for Your Game's Ratings](https://gist.github.com/gpluscb/302d6b71a8d0fe9f4350d45bc828f802) — practical Glicko-2 implementation advice
- [PlayFab: Use lobby and matchmaking together](https://learn.microsoft.com/en-us/gaming/playfab/multiplayer/lobby/lobby-and-matchmaking) — lobby/matchmaking composition model
- [Scott Brady: Sign in with Apple in ASP.NET Core](https://www.scottbrady.io/openid-connect/implementing-sign-in-with-apple-in-aspnet-core) — SIWA implementation details
- [ASO.dev: Apple Sign In Private Relay Incident (May 2025)](https://aso.dev/blog/apple-sign-in/) — userIdentifier stability incident documented

---

*Research completed: 2026-06-05*
*Ready for roadmap: yes*
