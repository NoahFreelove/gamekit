# ROADMAP: GameKit

**Project:** GameKit — GPL, self-hostable, composable .NET 10 game-services library

## Milestones

- ✅ **v1.0 — Initial 6-Phase Build-Out** (2026-04-15 → 2026-05-26, shipped 2026-05-30) — 7 NuGet packages (Core, Auth, Rankings, Matchmaking, Presence, Admin.UI, OpenApi) + CLI + template; full auth, rankings (Glicko-2), crash-safe matchmaking, Blazor admin UI, presence, OpenAPI, and a 9-file ops guide. 92/92 requirements. → [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)
- 🔄 **v2.0 — Expansion: Providers, Lobby & Rating-Aware Play** (in progress, started 2026-06-05) — Argon2 + Google/Apple/Epic OAuth, rating-aware matchmaking, rank decay + placement, regional pools, backfill, GameKit.Lobby, account merge, multi-replica Admin UI. 29 requirements. Phases 7–12.

---

## v2.0 Phases

### Summary Checklist

- [x] **Phase 7: Core Rating Seam + Stateless Auth Packages** — `IPlayerRatingProvider` seam in Core + all four new auth packages (Argon2, Google, Apple, Epic); zero migrations, parallelizable; unblocks all rating-aware work downstream. (completed 2026-06-05)
- [x] **Phase 8: Rankings Depth + Rating-Aware Matchmaking** — Rank decay (RD inflation), placement matches, `RankingsRatingSource`; freeze `player_ranks` schema before account merge reads it; simultaneously ships MATCH-16 (real ratings) + MATCH-17 (guardrails). (completed 2026-06-06)
- [x] **Phase 9: Regional Matchmaking Pools + Backfill** — First-class `RegionName` on enqueue (no migration), cross-region fallback, backfill ticket type with `ParticipationFraction` guard; stabilises the Matchmaking enqueue API before Lobby depends on it. (completed 2026-06-06)
- [x] **Phase 10: Account Merge (Isolated High-Risk)** — SERIALIZABLE transaction over 8+ FK tables; `account_merges` idempotency table first; crash-resume state machine; superadmin-only; depends on frozen `player_ranks` from Phase 8. (completed 2026-06-06)
- [x] **Phase 11: GameKit.Lobby (New Package)** — `lobbies` + `lobby_members` tables (chat is ephemeral — NOT persisted, per LOBBY-04); advisory-lock live-verify gate (Wave 0); SignalR + Redis backplane from day one; Lobby→Matchmaking party integration; establishes the SignalR pattern reused in Phase 12. (completed 2026-06-07)
- [x] **Phase 12: Admin Multi-Replica + Distribution Close-Out** — `RedisErrorRateCounter` replaces in-memory ring buffer; `AdminEventHub` + Redis backplane; fix Rank-adjust stub; five new packages join the MinVer release train (DIST-07). (completed 2026-06-07)

---

## Phase Details

### Phase 7: Core Rating Seam + Stateless Auth Packages
**Goal**: The codebase gains the rating-provider seam and four new auth packages; every rating-aware feature is unblocked; no database migrations are needed.
**Depends on**: Nothing (zero-migration, independent leaf nodes)
**Requirements**: CORE-18, AUTH-17, AUTH-18, AUTH-19, AUTH-20, AUTH-21, AUTH-22
**Success Criteria** (what must be TRUE):
  1. A developer who installs only `GameKit.Matchmaking` (without `GameKit.Rankings`) gets v1 zero-rating fallback behaviour unchanged — no compile errors, no runtime exceptions.
  2. A developer who installs `GameKit.Rankings` + `GameKit.Matchmaking` gets real Glicko-2 ratings flowing into `EloRangeMatchmakingStrategy` bracket comparisons — no additional configuration required.
  3. A developer who installs `GameKit.Auth.Argon2` and calls `AddAuth().UseArgon2()` can log in: existing BCrypt-hashed passwords are transparently rehashed to Argon2id on successful login; no password reset is required.
  4. A developer can install any of `GameKit.Auth.Google`, `GameKit.Auth.Apple`, `GameKit.Auth.Epic` as standalone packages; each provider registers its `IOAuthProvider` via Scrutor and creates a `player_identities` row on first login using the `(provider, external_id)` uniqueness contract.
  5. The Apple provider generates a fresh ES256 client secret per token exchange (`GenerateClientSecret = true`); an integration test asserts the Apple `sub` (not email) is stored as `external_id`.

> Note: SC#2 (real Glicko-2 ratings flowing into `EloRangeMatchmakingStrategy`) requires the Phase 8 consumption wiring in `MatchmakingService.EnqueueAsync`. Phase 7 ships only the `IPlayerRatingProvider` seam + null-object default that makes SC#2 reachable (per 07-CONTEXT.md deferred scope). The seam, all four auth packages, and the BCrypt→Argon2 rehash (SC#1/#3/#4/#5) land in Phase 7.

**Plans**: 6 plans (2 waves)
- [x] 07-01-PLAN.md — Core `IPlayerRatingProvider` seam + null-object default (CORE-18)
- [x] 07-02-PLAN.md — `IPasswordHasher.NeedsRehash` + `GameKit.Auth.Argon2` package + shared CPM/sln infra (AUTH-17, AUTH-18)
- [x] 07-03-PLAN.md — `GameKit.Auth.Google` provider package (AUTH-19, AUTH-22)
- [x] 07-04-PLAN.md — `GameKit.Auth.Apple` provider package, ES256/sub (AUTH-20, AUTH-22)
- [x] 07-05-PLAN.md — `GameKit.Auth.Epic` custom-handler provider package (AUTH-21, AUTH-22)
- [x] 07-06-PLAN.md — BCrypt→Argon2 rehash-on-verify wiring + Testcontainers proof (AUTH-18)

### Phase 8: Rankings Depth + Rating-Aware Matchmaking
**Goal**: The `player_ranks` schema reaches its final v2.0 shape (decay + placement columns added), real ratings flow into the matchmaking bracket, and guardrails ship alongside the rating wire — no feedback-loop risk.
**Depends on**: Phase 7 (IPlayerRatingProvider seam must exist before RankingsRatingSource implements it; real ratings flowing into matchmaking require the Phase 7 seam)
**Requirements**: RANK-15, RANK-16, RANK-17, MATCH-16, MATCH-17
**Success Criteria** (what must be TRUE):
  1. An inactive player whose rating exceeds the configured threshold sees their RD inflated (not their rating reduced) after the decay `BackgroundService` runs — a unit test using Glickman's inactivity formula confirms RD increases and rating stays constant.
  2. A new player entering placement matches has their visible rank hidden in API responses until N configurable games complete; session-complete decrements `placement_matches_remaining` atomically.
  3. A developer calling `.WithRatingsFrom<RankingsRatingSource>()` gets real Glicko-2 ratings injected into the matchmaking queue at enqueue time; a developer who omits the call gets the v1 zero-rating fallback.
  4. The `MaxBracketWidth` cap and `MinPoolDepthBeforeBracketExpansion` guardrails are enforced simultaneously with real-rating injection — an integration test confirms bracket expansion stops at `MaxBracketWidth` regardless of pool depth.
  5. `player_ranks` schema is finalized: Rankings migrations add `last_decay_at`, `placement_matches_remaining`, and `is_in_placement` columns; no further structural changes to `player_ranks` will be made in later phases.
**Plans**: 4 plans (3 waves)
- [x] 08-01-PLAN.md — Schema freeze migration + decay/placement options surface + visible-rank hiding + Glickman inactivity unit test (RANK-15, RANK-16) [wave 1]
- [x] 08-02-PLAN.md — Leader-elected RankDecayBackgroundService (RD inflation, dedicated decay lock key) + integration test (RANK-15) [wave 2]
- [x] 08-03-PLAN.md — Atomic placement decrement + RankingsRatingSource + `.WithRatingsFrom<>()` override + tests (RANK-16, RANK-17) [wave 2]
- [x] 08-04-PLAN.md — MATCH-16 rating-aware enqueue + MATCH-17 guardrails (same unit) + cross-package SC#3/SC#4 test (MATCH-16, MATCH-17) [wave 3]
**UI hint**: yes

### Phase 9: Regional Matchmaking Pools + Backfill
**Goal**: Regional matchmaking pools are a first-class concept (no schema migration needed), and backfill into in-progress sessions ships with the participation-fraction guard in the same unit.
**Depends on**: Phase 8 (Matchmaking enqueue path was modified for real ratings in Phase 8; Phase 9 extends the same enqueue path with RegionName; backfill ticket type reads `ParticipationFraction` which is a new column requiring Phase 8's migration pass to be stable first)
**Requirements**: MATCH-18, MATCH-19
**Success Criteria** (what must be TRUE):
  1. A developer configuring `AllowedRegions = ["us-east", "eu-west"]` on a ladder sees enqueue requests with a mismatched or missing `RegionName` rejected with a validation error; a `RegionName = null` request routes to the `"default"` pool (backwards-compatible v1 behaviour).
  2. The Redis queue key for a regional pool is `mm:queue:{ladderId}:{regionName}` and is distinct from the default `mm:queue:{ladderId}:default`; the ticker's existing pool-scan glob picks up both keys without any ticker code changes.
  3. A `POST /api/matchmaking/backfill` request creates a `backfill`-typed ticket; the backfill ticket is processed at higher priority than normal tickets.
  4. A backfill player whose `ParticipationFraction` falls below the configured minimum does not receive a rating change — an integration test confirms the `IRankingAlgorithm.Apply` guard fires correctly.
**Plans**: 4 plans (3 waves)
- [x] 09-01-PLAN.md — Data + config foundation: migration 20260520000000 (TicketType + ParticipationFraction), MatchmakingTicketType enum, AllowedRegions + MinParticipationFractionForRating config + builder validation, Wave 0 test scaffolds (MATCH-18, MATCH-19) [wave 1]
- [x] 09-02-PLAN.md — MATCH-18 regional pool routing: RegionName validation + region→pool Redis keys + ticker pool enumeration (MATCH-18) [wave 2]
- [x] 09-03-PLAN.md — MATCH-19 backfill endpoint: POST /api/matchmaking/backfill + BackfillService (Redis score 0 priority) (MATCH-19) [wave 3]
- [x] 09-04-PLAN.md — MATCH-19 participation-fraction guard in Rankings PendingRatingUpdatesAdapter (JSONB-config threshold) (MATCH-19) [wave 2]

### Phase 10: Account Merge (Isolated High-Risk)
**Goal**: Two distinct `player_id`s can be irreversibly merged via a SERIALIZABLE transaction with an idempotency table that enables crash-and-resume; the operation is superadmin-only and fully audited.
**Depends on**: Phase 8 (the `player_ranks` merge strategy reads `player_ranks.rating` to determine which rank row to keep; that schema must be finalized and frozen before this phase modifies it), Phase 7 (new provider identity rows from Google/Apple/Epic must be covered by the merge FK re-pointing logic)
**Requirements**: AUTH-23, AUTH-24, AUTH-25, AUTH-26
**Success Criteria** (what must be TRUE):
  1. A process killed mid-merge can be resumed: the `account_merges` table state machine (`pending → committed → redis_cleaned`) allows an identical re-request to pick up from the last committed checkpoint rather than starting over or producing a duplicate.
  2. After a successful merge, the source player's `player_identities`, `player_credentials`, and `session_participants` rows all reference the target `player_id`; all source refresh tokens are revoked; the source `players` row is soft-deleted with a `merged_into_player_id` tombstone.
  3. Rank conflict resolution follows the "keep higher-rated row per ladder" policy: a player with a higher source rating ends up with source's rating after merge; wins/losses/draws are summed across both accounts.
  4. The merge is recorded in `admin_audit_log` with before/after JSON; the `actor_id` FK uses `ON DELETE SET NULL` so tombstoning the source player never orphans the audit history.
  5. The merge endpoint requires the `gamekit.admin.superadmin` policy; the API response never includes the source `player_id`.
**Plans**: 4 plans (3 waves)
- [x] 10-01-PLAN.md — Core schema: `merged_into_player_id` + `deleted_at` on players (self-FK SET NULL) + `admin_audit_log.actor_id` FK ON DELETE SET NULL; two deterministic Core migrations (AUTH-23, AUTH-26) [wave 1]
- [x] 10-02-PLAN.md — Auth data layer: `account_merges` state-machine table + `AccountMerge` entity/config/migration + result/conflict types + cross-package integration test scaffold + IVT grants (AUTH-24) [wave 1]
- [x] 10-03-PLAN.md — `AccountMergeService`: SERIALIZABLE FK surgery (all tables) + rank conflict + token revoke + tombstone + crash-resume ladder + direct audit write + guards + DI (AUTH-23, AUTH-24, AUTH-25, AUTH-26) [wave 2]
- [x] 10-04-PLAN.md — Superadmin `POST /players/merge` endpoint (antiforgery + validator + rate limit, no source-id leak) + SC#1–#5 Testcontainers suite (AUTH-23, AUTH-24, AUTH-25, AUTH-26) [wave 3]

### Phase 11: GameKit.Lobby (New Package)
**Goal**: A new `GameKit.Lobby` NuGet package delivers ready-checks, ephemeral in-lobby chat, and persistent groups — group membership (`lobbies` + `lobby_members`) is backed by Postgres tables; chat messages are relayed live via a SignalR hub on a Redis backplane and are NEVER persisted (LOBBY-04 anti-feature).
**Depends on**: Phase 9 (Lobby's `TryStartMatchmakingAsync` calls `IMatchmakingService.EnqueueAsync` with a `RegionName`; that API must be stable — i.e., regional pool support must be present — before Lobby integrates against it)
**Requirements**: LOBBY-01, LOBBY-02, LOBBY-03, LOBBY-04, LOBBY-05, LOBBY-06, OPS-11
**Success Criteria** (what must be TRUE):
  1. The `GameKit.Lobby` advisory-lock key (`hashtext('gamekit.lobby.migrations')::bigint`) is live-verified pairwise-distinct from all five existing package keys in a Testcontainers Wave 0 test before any other integration tests run.
  2. A player JWT authenticates a WebSocket upgrade to `/hubs/lobby`; an unauthenticated upgrade attempt returns HTTP 401 before the WebSocket handshake completes — verified by an integration test using two `TestServer` instances sharing a Redis backplane.
  3. When all `lobby_members.ready = true`, `LobbyService.TryStartMatchmakingAsync` submits a party ticket to `IMatchmakingService.EnqueueAsync` and the lobby state transitions from `ReadyChecking` to `InGame`; the transition is observable via the SignalR group broadcast.
  4. A chat message sent via the hub reaches all connected members in the same lobby group in real time; chat is ephemeral — an integration test asserts NO chat-message table exists and nothing is written to Postgres on send (LOBBY-04 anti-feature: no chat log storage).
  5. A SignalR message broadcast from `LobbyHub` instance A reaches a client connected to `LobbyHub` instance B when both are connected to the same Redis backplane — verified by a two-`TestServer` integration test.
**Plans**: 4 plans (4 waves — linear; all four touch the shared GameKit.Lobby project)
- [x] 11-01-PLAN.md — Wave 0: package + test-project skeleton, CPM pins, live-verified advisory-lock key distinctness gate (OPS-11, LOBBY-01)
- [x] 11-02-PLAN.md — Lobby data model + per-package migration (lobbies + lobby_members, 20-entity exclusion, no chat table) (LOBBY-01, LOBBY-02)
- [x] 11-03-PLAN.md — SignalR LobbyHub + Redis backplane + JWT WS auth + LobbyService ready-check + ephemeral chat relay (LOBBY-02, LOBBY-03, LOBBY-04, LOBBY-06)
- [x] 11-04-PLAN.md — Lobby→Matchmaking party integration + two-TestServer SC suite (SC#2/#3/#4/#5) (LOBBY-03, LOBBY-04, LOBBY-05, LOBBY-06)
**UI hint**: yes

### Phase 12: Admin Multi-Replica + Distribution Close-Out
**Goal**: The Admin UI is correct across multiple replicas (Redis-backed error counter, SignalR backplane, Data Protection key sharing documented), the dead Rank-adjust stub is fixed, and all five new packages join the coordinated MinVer release train.
**Depends on**: Phase 11 (the `AdminEventHub` reuses the SignalR + Redis backplane pattern proven in Lobby; Phase 8 must be complete so the wired `RankAdjustService` has the finalized Rankings schema it writes to)
**Requirements**: ADMIN-13, ADMIN-14, ADMIN-15, DIST-07
**Success Criteria** (what must be TRUE):
  1. The health panel "recent error rate" tile shows the aggregate error count across all replicas: an error logged on replica A increments the count visible on replica B — verified by writing to `RedisErrorRateCounter` in one test context and asserting from another.
  2. An `AdminEventHub` SignalR message published via Redis Pub/Sub channel `"gamekit:admin:events"` reaches all connected admin sessions regardless of which replica they are connected to; the `AdminLiveBroadcastService` `BackgroundService` is responsible for the relay.
  3. A developer navigating to `/admin/rankings/adjust` reaches a functional rank-adjustment UI that calls the existing `IRankAdjustService` and produces an `admin_audit_log` row — the dead stub page is replaced.
  4. All five new packages (`GameKit.Auth.Argon2`, `GameKit.Auth.Google`, `GameKit.Auth.Apple`, `GameKit.Auth.Epic`, `GameKit.Lobby`) are present in the MinVer release train: they share the same version as all other GameKit packages, carry exact-pinned `[X.Y.Z]` sibling refs, and are covered by the `GameKitVersionAssertionHostedService` mismatch check.
**Plans**: 4 plans (2 waves)
- [x] 12-01-PLAN.md — DIST-07 version-train close-out: extend OPS04 version-coherence test to all 12 packages + 5 ProjectReferences (test-only, zero prod code) (DIST-07) [wave 1]
- [x] 12-02-PLAN.md — ADMIN-15 rank-adjust page: replace dead stub with player-search + existing RankAdjustDialog launch + SC#3 audit-row integration test (ADMIN-15) [wave 1]
- [x] 12-03-PLAN.md — ADMIN-14 RedisErrorRateCounter (additive INCRBY bucketed counter) + LogErrorCounter dual-write + async HealthProbe aggregate + two-host cross-replica test (ADMIN-14) [wave 1]
- [x] 12-04-PLAN.md — ADMIN-13 AdminEventHub (cookie-scheme) + Redis backplane + AdminLiveBroadcastService relay + multi-replica ops guide + cookie-auth/cross-replica test (ADMIN-13) [wave 2]

---

## Progress Table

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 7. Core Rating Seam + Stateless Auth Packages | 6/6 | Complete   | 2026-06-05 |
| 8. Rankings Depth + Rating-Aware Matchmaking | 4/4 | Complete   | 2026-06-06 |
| 9. Regional Matchmaking Pools + Backfill | 4/4 | Complete   | 2026-06-06 |
| 10. Account Merge | 4/4 | Complete   | 2026-06-06 |
| 11. GameKit.Lobby | 4/4 | Complete   | 2026-06-07 |
| 12. Admin Multi-Replica + Distribution Close-Out | 4/4 | Complete   | 2026-06-07 |

---

## v1.0 Archive Reference

v1.0 roadmap detail: [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)

Phases delivered in v1.0: 01 Foundation, 02 Auth, 03 Admin UI, 03.1 Admin Redesign, 04 Rankings+Sessions+GDPR, 05 Matchmaking, 06 Presence+OpenAPI+Distribution. Phase numbers 7–12 continue from that sequence.

---
*v2.0 roadmap created: 2026-06-05. 29/29 requirements mapped.*
