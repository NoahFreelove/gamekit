# Requirements: GameKit

**Defined:** 2026-04-15
**Core Value:** A .NET-native, composable, extensible, fully self-hosted game services backend where every algorithm and strategy is an interface the developer can replace — install only what you need, own the rest, depend on no cloud service.
**License:** GPL
**Runtime:** .NET 10 LTS (released 2026-04-14)

## v1 Requirements

### Foundation (Core Package)

- [x] **CORE-01**: Library ships as `GameKit.Core` NuGet package targeting `net10.0`
- [x] **CORE-02**: Single fully-owned `GameKitDbContext` registered in DI (not a base class)
- [x] **CORE-03**: All GameKit tables live in dedicated `gamekit` Postgres schema (`HasDefaultSchema("gamekit")`)
- [x] **CORE-04**: Sibling packages extend the model via `IModelBuilderExtension` discovered through DI and applied via a singleton `IModelCustomizer` replacement
- [x] **CORE-05**: Fluent registration API: `services.AddGameKit(opts).AddAuth(...)...` returning `IGameKitBuilder`
- [x] **CORE-06**: `players` entity (id, created_at, last_seen_at, is_banned, banned_at, ban_reason, metadata JSONB, deleted_at)
- [x] **CORE-07**: `game_sessions` entity with state machine (pending → active → completed/cancelled/abandoned) + ladder_id + timestamps + metadata JSONB
- [x] **CORE-08**: `session_participants` entity with team, result, score, rating_before/after/delta snapshots
- [x] **CORE-09**: `admin_audit_log` table (actor, action, target, before/after JSON, timestamp)
- [x] **CORE-10**: `IPresenceProvider` interface defined in Core (no impl in this package)
- [x] **CORE-11**: `ICurrentPlayer` accessor; `IClock`, `IIdGenerator` abstractions for testability
- [x] **CORE-12**: Rate-limiting middleware helpers exposed for sibling packages (built on `Microsoft.AspNetCore.RateLimiting`)
- [x] **CORE-13**: `app.UseGameKit()` middleware that runs migrations on startup (with opt-out)
- [x] **CORE-14**: Per-package migrations infrastructure proven on Core (`MigrationsAssembly`, `__ef_migrations_core` history table, per-package `IDesignTimeDbContextFactory`)
- [x] **CORE-15**: All public APIs have XML doc comments (CS1591 enforced as error)
- [x] **CORE-16**: GDPR delete service: nulls PII, sets `deleted_at`, cascades identities/credentials, sets opponent FKs to NULL
- [x] **CORE-17**: `metadata` JSONB constraint documented (sparse, infrequently-written, non-relational only)

### Authentication (Auth Package)

- [ ] **AUTH-01**: Library ships as `GameKit.Auth` NuGet package
- [ ] **AUTH-02**: `player_identities` entity (provider, external_id, display_name, avatar_url, metadata, timestamps) with unique `(provider, external_id)` constraint
- [ ] **AUTH-03**: `player_credentials` entity (player_id PK, password_hash, updated_at) — separate from identities
- [ ] **AUTH-04**: `refresh_tokens` entity with hashed token (SHA-256), issued_at, expires_at, revoked_at, `replaced_by` chain
- [ ] **AUTH-05**: `IOAuthProvider` interface — pluggable
- [ ] **AUTH-06**: Steam OAuth provider (in-house OpenID 2.0, server-side `check_authentication` round-trip)
- [ ] **AUTH-07**: Discord OAuth provider (`identify` scope only, no scope creep)
- [ ] **AUTH-08**: Guest provider (anonymous account creation)
- [ ] **AUTH-09**: Username/Password provider with BCrypt.Net-Next password hashing
- [ ] **AUTH-10**: JWT issuance via `Microsoft.AspNetCore.Authentication.JwtBearer` with configurable issuer/audience/secret/lifetimes
- [ ] **AUTH-11**: Refresh token rotation with reuse-attack detection (using `replaced_by` chain to revoke entire family)
- [ ] **AUTH-12**: Reuse-interval grace window (30–60s) with client-fingerprint check to prevent mobile-resume false positives
- [ ] **AUTH-13**: Guest → real account upgrade in a SERIALIZABLE transaction, protected by unique constraint
- [ ] **AUTH-14**: Identity link/switch challenge policy (explicit user choice when login matches existing player)
- [ ] **AUTH-15**: Rate limits applied to `/auth/login`, `/auth/refresh`, `/auth/register`
- [ ] **AUTH-16**: `IPasswordHasher` interface allowing future Argon2 sibling package without breaking change

### Admin UI (Admin.UI Package)

- [ ] **ADMIN-01**: Library ships as `GameKit.Admin.UI` package — Blazor Server in a Razor Class Library
- [ ] **ADMIN-02**: Mountable at configurable path via `app.MapGameKitAdmin("/admin")`
- [ ] **ADMIN-03**: Default-deny route policy: returns 404 (not 401) on unauth in Production; startup assertion fails fast if mounted with no role configured
- [ ] **ADMIN-04**: Separate auth scheme from player JWT (HTTP Basic or admin-token by default; pluggable)
- [ ] **ADMIN-05**: Player search (by id, display name, identity)
- [ ] **ADMIN-06**: Player ban/unban with mandatory reason — writes to `admin_audit_log`
- [ ] **ADMIN-07**: Manual rank adjustment UI (functional once Rankings package present)
- [ ] **ADMIN-08**: Match history viewer
- [ ] **ADMIN-09**: Live matchmaking queue depth panel (functional once Matchmaking present)
- [ ] **ADMIN-10**: Health panel: Postgres connectivity, Redis connectivity, recent error rate
- [ ] **ADMIN-11**: First-admin bootstrap CLI (no admin → cannot mount in Production until one exists)
- [ ] **ADMIN-12**: CSP headers + anti-CSRF token enforcement on all mutations

### Rankings + Sessions Wiring + GDPR Export (Rankings Package)

- [ ] **RANK-01**: Library ships as `GameKit.Rankings` NuGet package
- [ ] **RANK-02**: `ladders` entity (name, algorithm, is_active, config JSONB)
- [ ] **RANK-03**: `player_ranks` entity per ladder with rating, rating_deviation, volatility, wins/losses/draws, last_match_at — rating columns stored as `double precision` (NOT `NUMERIC(8,2)`)
- [ ] **RANK-04**: `IRankingAlgorithm.Apply(state, batch)` interface — batched (NOT per-match) to prevent silent Glicko-2 corruption
- [ ] **RANK-05**: Default `Glicko2Algorithm` vendored from MaartenStaa/glicko2-csharp (MIT, GPL-compatible)
- [ ] **RANK-06**: 1000-match convergence integration test for Glicko-2 implementation
- [ ] **RANK-07**: Rank records created lazily on first match (NOT on player registration)
- [ ] **RANK-08**: Leaderboard queries: top-N AND around-me, sorted by rating DESC; `(ladder_id, rating DESC)` index hot-path
- [ ] **RANK-09**: `AddLadder("name")` registration API
- [ ] **RANK-10**: Seasonal leaderboard reset + archival (rating snapshots preserved, current rank reset per season config)
- [ ] **RANK-11**: Session-complete endpoint: `POST /api/sessions/{id}/complete` — idempotent via state-conditional UPDATE + cached rating deltas + `Idempotency-Key` header support
- [ ] **RANK-12**: Manual rank adjustment writes to `admin_audit_log` with before/after rating
- [ ] **RANK-13**: GDPR export endpoint: `GET /api/players/{id}/export` returns all PII + identities + sessions + ratings as JSON
- [ ] **RANK-14**: Per-package migrations targeting `gamekit` schema with `__ef_migrations_rankings` history table

### Matchmaking + Parties (Matchmaking Package)

- [ ] **MATCH-01**: Library ships as `GameKit.Matchmaking` NuGet package
- [ ] **MATCH-02**: `matchmaking_tickets` entity (status: queued/matched/cancelled/expired) — Postgres async-write for analytics only
- [ ] **MATCH-03**: `party_members` entity supporting 1-N players per ticket from v1 (model not widenable later)
- [ ] **MATCH-04**: Redis is the source of truth for live queue (sorted sets per pool, server-owned leases, NOT bare TTLs)
- [ ] **MATCH-05**: Atomic ticket claim via Redis WATCH/MULTI to prevent double-matching
- [ ] **MATCH-06**: Reconciliation worker (every 30s) + startup sweep — claims abandoned tickets/pending sessions from Postgres, never rehydrates Redis from Postgres
- [ ] **MATCH-07**: Matchmaker runs as `BackgroundService` + `PeriodicTimer` + Polly retry (NOT Hangfire/Quartz)
- [ ] **MATCH-08**: Leader election via Redis distributed lock so multiple replicas don't double-match
- [ ] **MATCH-09**: `IMatchmakingStrategy.Match(Party, candidates)` interface — party-aware from v1 (NOT flat ratings)
- [ ] **MATCH-10**: Default `EloRangeMatchmakingStrategy` with time-based bracket flex (e.g. ±100 → ±500 over 40s)
- [ ] **MATCH-11**: Per-player rate limit on enqueue (no DoS via spam tickets)
- [ ] **MATCH-12**: Chaos test: kill app mid-match → no duplicate sessions, no ghost tickets
- [ ] **MATCH-13**: Load test as phase gate (1k concurrent tickets sustained for 10 min)
- [ ] **MATCH-14**: Admin UI queue-depth + health panels wired to Redis live state
- [ ] **MATCH-15**: Per-package migrations targeting `gamekit` schema with `__ef_migrations_matchmaking` history table

### Presence + OpenAPI + Distribution (Presence Package + Polish)

- [ ] **PRES-01**: Library ships as `GameKit.Presence` NuGet package — Redis-only (no EF entities)
- [ ] **PRES-02**: Implements `Core.IPresenceProvider`
- [ ] **PRES-03**: Heartbeat endpoint: client posts liveness; expires via Redis TTL
- [ ] **PRES-04**: Status states: online / offline / in-match
- [ ] **PRES-05**: Abandonment grace period (game-server-authoritative — server reports the abandonment, presence does not infer)
- [ ] **PRES-06**: Admin UI presence panel (top-N online, per-player status)
- [ ] **OPEN-01**: OpenAPI spec generated by `Microsoft.AspNetCore.OpenApi` covering all GameKit HTTP endpoints
- [x] **DIST-01**: `docker-compose.yml` at repo root with Postgres + Redis matching `SampleGame` connection strings; Redis configured with `--appendonly yes --appendfsync everysec`; THREE Postgres roles (`gamekit_owner`, `gamekit_app`, `gamekit_reader`)
- [ ] **DIST-02**: Integration test asserts `gamekit_reader` cannot INSERT into `gamekit.sessions`
- [ ] **DIST-03**: `SampleGame` reference application using all packages, demonstrating `gamekit_reader` from the game-server side
- [ ] **DIST-04**: `GameKit.Template` NuGet template package: `dotnet new gamekit` wraps SampleGame
- [ ] **DIST-05**: Production-readiness ops guide (bare-metal, container, air-gapped deployment recipes)
- [ ] **DIST-06**: All public APIs have XML doc comments — CS1591 enforced as error across all packages

### Cross-Cutting (Tooling, License, Operational Discipline)

- [x] **OPS-01**: GPL LICENSE file at repo root + per-source-file headers + CI check
- [x] **OPS-02**: Repo-wide `Directory.Build.props` with MinVer + SourceLink + nullable enable + warnings-as-errors + CS1591-as-error
- [x] **OPS-03**: Central Package Management (`Directory.Packages.props`) — versions consistent across all packages
- [ ] **OPS-04**: Coordinated SemVer release train: all 6 packages stamp the same MinVer-derived version per release; sibling refs exact-pinned `[X.Y.Z]`
- [ ] **OPS-05**: Runtime startup assertion: all GameKit packages report matching `GameKitVersion` constant; fail-fast on mismatch
- [ ] **OPS-06**: CI clean-install integration test: install all 6 packages onto empty Postgres, run `Database.Migrate()`, assert no model snapshot drift
- [ ] **OPS-07**: Runtime guard test: assert library performs zero outbound HTTP except via configured providers (no telemetry, no phone-home)
- [x] **OPS-08**: All integration tests use Testcontainers for Postgres + Redis (no shared-state fixtures, no skip-if-no-docker fallbacks)
- [x] **OPS-09**: Cross-schema FK direction enforced in code review/docs: only `public` → `gamekit` allowed
- [x] **OPS-10**: README explicitly enumerates anti-features (no AI, no cloud, no telemetry, no hosting, etc.) to set scope expectations

## v2 Requirements

Deferred to a future release.

### Auth
- **AUTH-V2-01**: Argon2 password hasher in `GameKit.Auth.Argon2` opt-in sibling package (Isopoh)
- **AUTH-V2-02**: Additional OAuth providers as opt-in sibling packages: Google, Apple, Epic
- **AUTH-V2-03**: Account merge (combining two distinct player_ids into one)

### Rankings / Matchmaking
- **RANK-V2-01**: Configurable rank decay for inactive top-tier players
- **RANK-V2-02**: Placement matches (initial high-RD games)
- **MATCH-V2-01**: Richer parties (lobby package: ready-checks, in-lobby chat, persistent groups)
- **MATCH-V2-02**: Backfill into in-progress sessions
- **MATCH-V2-03**: Regional matchmaking pools as first-class concept (currently expressible via `metadata`)

### Admin
- **ADMIN-V2-01**: Multi-replica Admin UI with SignalR backplane

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Game server hosting / orchestration | Use Agones, Multiplay, custom |
| Real-time game communication (netcode) | Use Mirror, Fish-Net, WebSockets, custom |
| Inventory, economy, progression systems | Game dev's companion tables, FK into `gamekit.players(id)` |
| Analytics pipeline | Operator brings their own (ClickHouse, BigQuery, etc.) |
| DDoS mitigation | Network/edge concern |
| Game-specific anti-cheat | Engine/game concern |
| Billing / entitlements | Storefronts (Steam, Epic, etc.) own this |
| Polyglot runtime / scripting VM | Explicitly C# all the way down |
| **AI / LLM integrations of any kind** | GPL self-hosted commitment; no AI moderation, matchmaking, content gen, telemetry analysis |
| **Cloud-only / SaaS dependencies** | No managed-service requirements; library must run air-gapped |
| **Hosted / paid components** | All functionality is GPL and free, always; no upsell tier |
| **Telemetry / phone-home** | Library does not collect or transmit usage data |
| Real-time chat | Out of scope for this library; use SignalR / Mirror / engine netcode |
| Voice chat | Out of scope; vendor-specific (Vivox, etc.) |
| Achievements | Platform-native (Steam/PSN/Xbox); document hook pattern only |
| Tournaments | Build atop ladders; defer to v2 if ladders insufficient |
| Friends graph (`GameKit.Social`) | Defer to v1.x sibling package |
| MySQL / SQL Server providers | Postgres-only for v1; provider abstraction exists for v2+ |
| Parental controls | Operator/storefront responsibility |
| Server browser | Implies hosting; out of scope |
| Multi-region as first-class axis | Deployment topology concern; ticket `metadata.region` is the v1 escape hatch |
| Account merge (combine 2 players) | Hard problem; explicit policy in v1 is "link or switch", not merge |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CORE-01..17 | Phase 1 | Pending |
| OPS-01, OPS-02, OPS-03, OPS-06, OPS-07, OPS-08, OPS-09, OPS-10 | Phase 1 | Pending |
| DIST-01 (initial: Postgres/Redis + roles) | Phase 1 | Pending |
| AUTH-01..16 | Phase 2 | Pending |
| ADMIN-01..12 | Phase 3 | Pending |
| RANK-01..14 | Phase 4 | Pending |
| MATCH-01..15 | Phase 5 | Pending |
| PRES-01..06 | Phase 6 | Pending |
| OPEN-01 | Phase 6 | Pending |
| DIST-02..06 | Phase 6 | Pending |
| OPS-04, OPS-05 | Phase 6 (final release-train wiring) | Pending |

**Coverage:**
- v1 requirements: 92 total
- Mapped to phases: 92
- Unmapped: 0 ✓

---
*Requirements defined: 2026-04-15*
*Last updated: 2026-04-15 after initial definition*
