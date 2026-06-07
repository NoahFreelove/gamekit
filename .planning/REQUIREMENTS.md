# Requirements: GameKit — v2.0 (Expansion: Providers, Lobby & Rating-Aware Play)

**Defined:** 2026-06-05
**Core Value:** A .NET-native, composable, extensible, fully self-hosted game services backend where every algorithm and strategy is an interface the developer can replace — install only what you need, own the rest, depend on no cloud service.
**License:** GPL · **Runtime:** .NET 10 LTS · **Infra:** Postgres + Redis only (zero-cloud)

> Continues v1.0 (92/92 shipped — archived at `.planning/milestones/v1.0-REQUIREMENTS.md`). REQ-IDs continue from each category's v1.0 high-water mark. Research basis: `.planning/research/SUMMARY.md`.

## v2.0 Requirements

### Core — Rating Seam

- [x] **CORE-18**: `IPlayerRatingProvider` optional-port interface defined in `GameKit.Core` (null-object default returns rating=0 / default RD) — consumed by Matchmaking **without** a hard compile-time dependency on Rankings (mirrors `IPresenceProvider` pattern)

### Auth — Argon2 Hasher

- [x] **AUTH-17**: `GameKit.Auth.Argon2` opt-in sibling package provides `Argon2idPasswordHasher : IPasswordHasher` using Isopoh.Cryptography.Argon2 (tuned m=64 MiB, t=3, p=1)
- [x] **AUTH-18**: Transparent BCrypt→Argon2 migration — rehash-on-verify via hash-format detection (`$2a$` vs `$argon2id$`); no forced password reset

### Auth — OAuth Providers

- [x] **AUTH-19**: Google OAuth provider as opt-in sibling package `GameKit.Auth.Google` (Microsoft.AspNetCore.Authentication.Google) implementing `IOAuthProvider`
- [x] **AUTH-20**: Apple Sign-In provider `GameKit.Auth.Apple` — ES256 client-secret generated per token exchange; `sub` is the canonical identity key; name/email persisted on **first** login only; private-relay email stored as-is
- [x] **AUTH-21**: Epic OAuth provider `GameKit.Auth.Epic` — custom `OAuthHandler` against Epic OAuth 2.0 endpoints (no maintained NuGet package; zero new dep)
- [x] **AUTH-22**: All new providers integrate with existing `IOAuthProvider` + identity-linking under the `(provider, external_id)` uniqueness contract (no scope creep; `identify`-equivalent minimal scopes)

### Auth — Account Merge

- [x] **AUTH-23**: Account merge combines two distinct `player_id`s into one via a single SERIALIZABLE transaction + advisory lock, re-homing FK references across `player_identities`, `player_credentials`, `refresh_tokens`, `player_ranks`, `matchmaking_tickets`, `party_members`, `session_participants`, `admin_audit_log`
- [x] **AUTH-24**: `account_merges` idempotency/history table (statuses `pending` / `committed` / `redis_cleaned`) enabling crash-and-resume; merge is idempotent under retry
- [x] **AUTH-25**: Merge conflict policy — `player_ranks`: keep higher-rated row per ladder (sum W/L/D, max RD); revoke ALL secondary-account refresh tokens; tombstone secondary `player_id` with `merged_into_player_id`; explicit banned-player merge policy
- [x] **AUTH-26**: Merge recorded in `admin_audit_log` (actor, before/after JSON); audit FK behavior `ON DELETE SET NULL` so tombstoning never orphans audit history

### Rankings — Decay, Placement, Rating Source

- [x] **RANK-15**: Configurable rank decay for inactive players above a rating threshold — implemented as Glicko-2 **RD inflation** (the "no games played" period update), not arbitrary point loss; applied by a leader-elected `BackgroundService`
- [x] **RANK-16**: Placement matches — initial high-RD calibration games; visible rank hidden until N placements complete (configurable)
- [x] **RANK-17**: `RankingsRatingSource : IPlayerRatingProvider` in `GameKit.Rankings`, opt-in via `.WithRatingsFrom<RankingsRatingSource>()`; default remains the Core null-object (preserves no-hard-dep rule)

### Matchmaking — Rating-Aware, Regional, Backfill

- [x] **MATCH-16**: Rating-aware EloRange — strategy reads real ratings via `IPlayerRatingProvider`, cached into the Redis ticket hash at enqueue (`MatchmakingService.EnqueueAsync`), replacing the v1 hardcoded rating=0
- [x] **MATCH-17**: Anti-feedback-loop guardrails `MaxBracketWidth` + `MinPoolDepthBeforeBracketExpansion` ship **simultaneously** with MATCH-16 (not a follow-up) — prevents new high-RD players funnelling into top-rated matches on sparse pools
- [x] **MATCH-18**: Regional matchmaking pools as a first-class concept — `AllowedRegions` config + region-validated enqueue partitioning the existing `mm:queue:{ladderId}:{poolName}` Redis keys (no schema migration; `PoolName` already exists)
- [x] **MATCH-19**: Backfill — fill vacated slots in in-progress sessions; participation-fraction / abandonment accounting guard ships in the same unit

### Lobby — New Package

- [x] **LOBBY-01**: `GameKit.Lobby` ships as a new NuGet package (net10.0) with its own per-package migration — distinct **live-verified** advisory-lock key, `__ef_migrations_lobby` history table, `IDesignTimeDbContextFactory`, `ExcludeFromMigrations` on all prior packages (never mutates Core tables)
- [x] **LOBBY-02**: Lobby data model — `lobbies` + `lobby_members` + ready-state; persistent groups survive across sessions
- [x] **LOBBY-03**: Ready-check flow — members mark ready; lobby transitions when all members ready
- [x] **LOBBY-04**: In-lobby chat via SignalR groups — **ephemeral only**, no message persistence (documented anti-feature: no chat log storage, GDPR/moderation out of scope)
- [x] **LOBBY-05**: Lobby → Matchmaking integration — a ready lobby submits a party ticket (`lobby_id` FK on `matchmaking_tickets`); an `IMatchFoundHandler` transitions lobby state on match-found
- [x] **LOBBY-06**: Lobby SignalR hub is `[Authorize]`-gated (player JWT) and runs on a Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`, `ChannelPrefix = "GameKit"`)

### Admin — Multi-Replica

- [x] **ADMIN-13**: Multi-replica Admin UI — SignalR + **Redis** backplane (never Azure SignalR); sticky-session requirement documented for operators
- [x] **ADMIN-14**: Replace the in-memory `ErrorRateRingBuffer` with a Redis-backed error-rate counter (`INCRBY` on time-bucketed keys) so the health panel is correct across replicas
- [x] **ADMIN-15**: Replace the dead "Rank adjust" stub nav page (`/admin/rankings/adjust`) with the working flow (wires the existing `IRankAdjustService`); Admin hub uses a distinct hub + `[Authorize]` policy from the Lobby hub

### Distribution & Ops

- [x] **DIST-07**: New v2 packages (`GameKit.Auth.Argon2`, `.Google`, `.Apple`, `.Epic`, `GameKit.Lobby`) join the coordinated MinVer release train — same version, exact-pinned `[X.Y.Z]` sibling refs; the runtime version-assertion hosted service covers them
- [x] **OPS-11**: Advisory-lock live-verify gate — every new package's migration advisory-lock key is verified pairwise-distinct from the existing five (Core 1800940027, Auth -298890956, Admin -2101739634, Rankings -156812172, Matchmaking 388956820) via Testcontainers before integration tests run (Wave 0 RED→GREEN)

## Future Requirements (deferred beyond v2.0)

- Friends graph (`GameKit.Social`) sibling package
- MySQL / SQL Server EF providers (provider abstraction exists)
- Tournaments built atop ladders
- Persistent / moderated chat (if a consumer credibly needs it — currently an anti-feature)

## Out of Scope

Carried from v1.0 (unchanged) — see `.planning/PROJECT.md` § Out of Scope. Highlights: **no AI/LLM integrations**, **no cloud/SaaS dependencies** (SignalR backplane is Redis, not Azure SignalR), **no telemetry/phone-home**, no game-server hosting/netcode/voice, no inventory/economy/billing.

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CORE-18 | Phase 7 | Complete |
| AUTH-17 | Phase 7 | Complete |
| AUTH-18 | Phase 7 | Complete |
| AUTH-19 | Phase 7 | Complete |
| AUTH-20 | Phase 7 | Complete |
| AUTH-21 | Phase 7 | Complete |
| AUTH-22 | Phase 7 | Complete |
| RANK-15 | Phase 8 | Complete |
| RANK-16 | Phase 8 | Complete |
| RANK-17 | Phase 8 | Complete |
| MATCH-16 | Phase 8 | Complete |
| MATCH-17 | Phase 8 | Complete |
| MATCH-18 | Phase 9 | Complete |
| MATCH-19 | Phase 9 | Complete |
| AUTH-23 | Phase 10 | Complete |
| AUTH-24 | Phase 10 | Complete |
| AUTH-25 | Phase 10 | Complete |
| AUTH-26 | Phase 10 | Complete |
| LOBBY-01 | Phase 11 | Complete |
| LOBBY-02 | Phase 11 | Complete |
| LOBBY-03 | Phase 11 | Complete |
| LOBBY-04 | Phase 11 | Complete |
| LOBBY-05 | Phase 11 | Complete |
| LOBBY-06 | Phase 11 | Complete |
| OPS-11 | Phase 11 | Complete |
| ADMIN-13 | Phase 12 | Complete |
| ADMIN-14 | Phase 12 | Complete |
| ADMIN-15 | Phase 12 | Complete |
| DIST-07 | Phase 12 | Complete |

**Coverage:**
- v2.0 requirements: 29 total
- Mapped to phases: 29/29
- Unmapped: 0

---
*Requirements defined: 2026-06-05 — milestone v2.0*
*Traceability filled: 2026-06-05 — roadmap created*
