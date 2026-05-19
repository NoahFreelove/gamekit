# Phase 4: Rankings + Sessions Wiring + GDPR Export — Research

**Researched:** 2026-05-15
**Domain:** Skill-rating algorithms (Glicko-2), batched background work in ASP.NET Core, idempotent HTTP, GDPR data portability, per-package EF Core migrations.
**Confidence:** HIGH (decisions locked in CONTEXT.md narrow the unknowns to mechanical wiring; the only MEDIUM-confidence area is a Glickman-PDF licensing artifact and the exact Glicko-2 conversion-factor constants which need to ship as a regression fixture).

## Summary

Phase 4 ships `GameKit.Rankings` as the fourth NuGet package on the existing per-package-migration pattern (`__ef_migrations_rankings` history table, distinct advisory-lock key, `IModelBuilderExtension` + `RankingsDesignTimeDbContextFactory` + `RankingsMigrationHostedService` — three already-proven Phase-1/2/3 boilerplate components). It introduces six new tables (`ladders`, `player_ranks`, `season_rank_archive`, `ladder_seasons`, `service_tokens`, `pending_rating_updates`, `session_complete_idempotency`), a vendored Glicko-2 algorithm (~150 LOC port of MaartenStaa/glicko2-csharp under **BSD — NOT MIT — see Pitfalls §1**), a `BackgroundService`-driven ticker with Redis distributed lock leader election (`IDatabase.LockTake/LockExtend/LockRelease` from StackExchange.Redis), a state-conditional + idempotency-keyed session-complete endpoint in `GameKit.Core` (consumed via `IPostSessionCompleteHandler` port), a REPEATABLE-READ-snapshotted GDPR export endpoint, and a Phase-3-palette-lit admin rank-adjust endpoint.

No new NuGet dependencies. Every library Phase 4 needs is already pinned in `Directory.Packages.props` from Phases 1–3 (`StackExchange.Redis 2.8.41`, `FluentValidation 12.1.1`, `Microsoft.EntityFrameworkCore 10.0.6`, `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1`, `Spectre.Console.Cli 0.49.1`). Glicko-2 is vendored as in-house source per CLAUDE.md §2 — no NuGet package added.

**Primary recommendation:** Mirror the Phase-2 / Phase-3 migration + audit + builder scaffolding patterns line-for-line for the six new tables; vendor `MaartenStaa/glicko2-csharp`'s four `.cs` files (Rating, RatingCalculator, RatingPeriodResults, Result) under a GameKit-internal `Glicko2` namespace with a BSD-2-or-3-Clause attribution header (license verification needed before commit); and treat the session-complete endpoint as the only genuinely novel HTTP surface — everything else is variations on patterns already in `src/`.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Rating Period / Window (RANK-04/05/06)**
- **D-01:** Glicko-2 batch updates run on a time window via a `RankingsTickerService : BackgroundService`. The ticker checks each active ladder every 60 seconds; when an active ladder's rating period (default 1h) has elapsed since its last drain, the ticker applies `IRankingAlgorithm.Apply(state, batch)` against all `session_participants` rows that have a `Result` but no `RatingBefore`/`RatingAfter` yet for that ladder. One batch per ladder per period.
- **D-02:** Default `RatingPeriod = TimeSpan.FromHours(1)` per ladder. Overridable in the ladder's config JSONB.
- **D-03:** Multi-instance safety via **Redis distributed lock** (`SET NX PX`). Lock key: `gamekit:rankings:ticker:lease`. TTL = 90s. Self-renew mid-tick; on Redis disconnect, Polly v8 backs off; next instance picks up leadership on TTL expiry.
- **D-04:** On batch failure (algorithm throws / Postgres deadlock), the ticker rolls back the per-ladder transaction and logs an `OpenTelemetry`-friendly `ActivitySource` event under `GameKit.Rankings.Ticker`. Pending rows stay un-applied until the next tick.

**Session-Complete API Contract (RANK-11)**
- **D-05:** `POST /api/sessions/{id}/complete` requires a service-account bearer token. Distinct from the player JWT scheme. Player JWTs hitting this endpoint return 403.
- **D-06:** Service tokens minted via `dotnet gamekit service-token issue --name <name> [--expires <duration>]`. Raw bearer printed to stdout exactly once; only SHA-256 hash stored, in a new `service_tokens` table. Verbs: `issue`, `revoke <name>`, `list`. No web UI in v1.
- **D-07:** Endpoint is state-conditional via `UPDATE game_sessions SET state = 'completed', completed_at = @now WHERE id = @id AND state = 'active' RETURNING ...`. Zero rows updated → already-completed returns cached deltas (200), other state → 409.
- **D-08:** Mandatory `Idempotency-Key` header. `{session_id, idempotency_key}` dedup'd in `session_complete_idempotency` (columns: `session_id`, `idempotency_key text`, `response_hash text`, `created_at`) with **24h TTL**. Same key + same body → cached response. Same key + different body → 409 `idempotency_key_reused`. Cleanup `BackgroundService` deletes rows older than TTL nightly.
- **D-09:** Request body shape: `{ "participants": [ { "player_id": "uuid", "team": 0, "result": "win|loss|draw|forfeit", "score": 0 } ] }`. Validated by FluentValidation. Result enum mirrors `SessionResult`. Unknown player_id → 404. Missing participant → 400.
- **D-10:** New rate-limit policy `gamekit:sessions:complete` — 300 requests/min/service-token (burst configurable via `GameKitRankingsOptions`).

**Seasonal Reset (RANK-10)**
- **D-11:** Season end is admin-triggered only. Palette verb `end-season` (superadmin-only) opens confirmation dialog; writes `admin.ladder.end_season` audit row via `IAdminAuditWriter`. Single SERIALIZABLE transaction.
- **D-12:** Reset strategy is per-ladder (config JSONB picks one of three `SeasonResetPolicy` variants): `SoftRegress` (default, fields `RegressionFactor=0.5`, `RdCeiling=200`, `RdBump=50`), `HardReset`, `ArchiveOnly`.
- **D-13:** `season_rank_archive` table — columns: `id`, `ladder_id`, `season_id`, `player_id` (nullable), `rating`, `rating_deviation`, `volatility`, `wins`, `losses`, `draws`, `archived_at`. Composite index `(ladder_id, season_id, rating DESC)`.
- **D-14:** `ladder_seasons` table — columns: `id`, `ladder_id`, `season_number int`, `started_at`, `ended_at` (null while current), `ended_by_admin_id` (null until ended). Current season = row with `ended_at IS NULL`.

**GDPR Export (RANK-13)**
- **D-15:** `GET /api/players/{id}/export` returns single-blob `application/json` with fixed shape (player / identities / credentials_metadata / sessions / rating_history / exported_at). Password hashes, raw OAuth tokens, and refresh-token hashes NEVER included. Identities include only `external_id_hash`.
- **D-16:** Two endpoints, one handler: `/api/players/{id}/export` (player JWT, `{id}` must match `sub`) and `/admin/api/players/{id}/export` (admin cookie, Superadmin). Admin path writes `admin.player.gdpr_export` audit row.
- **D-17:** Handler opens a **REPEATABLE READ read-only transaction** at entry, reads every table inside it, commits at exit.
- **D-18:** Response size cap: 25 MB. Configurable via `GameKitRankingsOptions.GdprExport.MaxBytes`. Exceeding → `413 Payload Too Large`. Streaming path deferred.

**Manual Rank Adjustment (RANK-12)**
- **D-19:** `POST /admin/api/players/{id}/rank-adjust` — cookie scheme, Superadmin policy, antiforgery required. Body `{ ladder_id, new_rating, reason }`. FluentValidation: `reason` 3–512 chars; `new_rating` finite double bounded to `[100, 4000]` (configurable). Single SERIALIZABLE transaction: UPDATE `player_ranks` (lazy create if missing) + INSERT audit row via `IAdminAuditWriter` with action `admin.player.rank_adjust`.
- **D-20:** Manual rank-adjusts bypass the rating-period batch — take effect immediately, NOT replayed if participant later appears in a batched update.

**Library Boundaries & Wiring**
- **D-21:** `services.AddLadder("name", config)` is a build-time fluent API on `AddRankings()` builder. Per-ladder config fields: `DefaultRating=1500`, `DefaultRd=350`, `DefaultVolatility=0.06`, `RatingPeriod=1h`, `SeasonResetPolicy`. Row INSERTed into `ladders` at startup via `IHostedService` (idempotent by name).
- **D-22:** Session-complete endpoint lives in `GameKit.Core` (owns `game_sessions` + `session_participants`) but consumes `IPostSessionCompleteHandler` port. `GameKit.Rankings` registers an adapter that enqueues participants into `pending_rating_updates` for the next batch drain. Core has zero dependency on Rankings.
- **D-23:** `ILeaderboardService` with two methods: `TopAsync(ladderId, limit, ct)` (default 100) and `AroundAsync(ladderId, playerId, window, ct)` (default ±5). Hot-path index `idx_player_ranks_ladder_rating` on `(ladder_id, rating DESC)`. v1 ships the service surface; admin queries via `/admin/api/leaderboard` GET only.

### Claude's Discretion

CONTEXT.md does not enumerate an explicit Claude-discretion section — all 23 decisions are locked. Areas where the planner still chooses:
- **Exact column types** within the locked schemas (text length caps, NULLability of secondary columns, indexes beyond the ones explicitly named).
- **Migration timestamp** for `__ef_migrations_rankings` (follow precedent: `20260515000000_RankingsInitial`).
- **Advisory-lock key** for the Rankings package (compute as `SELECT hashtext('gamekit.rankings.migrations')::bigint`; verify on live Postgres via Testcontainers per AuthAdvisoryLockKeyTests precedent).
- **Polly v8 resilience pipeline shape** for the ticker's Redis reconnect path (D-03 says "Polly v8 backs off"; the planner picks decorrelated-jitter parameters).
- **`pending_rating_updates` columns** beyond `(session_id, player_id, ladder_id, claimed_at, applied_at)` — see §Open Questions Q2.
- **Idempotency canonicalization** rule for `response_hash` — see §Open Questions Q5.
- **CLI command surface details** (`Spectre.Console.Cli` settings classes, exit codes) — follow the `AdminCreateCommand` precedent.

### Deferred Ideas (OUT OF SCOPE)

- Real-time rating push (SignalR / WebSockets).
- Cross-ladder tournaments.
- Auto season-rollover ticker (`SeasonRolloverService`).
- Chunked / streaming GDPR export.
- Runtime ladder CRUD via admin UI.
- HTTP leaderboard exposure for player-facing surfaces.
- Multi-rating-system support beyond Glicko-2 default.
- Rotate JWT signing keys via admin UI (Phase 6).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RANK-01 | Ships as `GameKit.Rankings` NuGet package | §Standard Stack (no new NuGet refs); §Architecture: per-package csproj already exists empty at `src/GameKit.Rankings/`. |
| RANK-02 | `ladders` entity (name, algorithm, is_active, config JSONB) | §Schema sketch §Ladders; D-21 build-time `AddLadder` registers via `IHostedService` upsert by name. |
| RANK-03 | `player_ranks` per ladder; `rating` / `rating_deviation` / `volatility` = `double precision` (NOT `NUMERIC(8,2)`) | §Pitfall #13 carry-over; EF Core 10 mapping: `.HasColumnType("double precision")` or default `double` CLR type maps natively (see §Code Examples). |
| RANK-04 | `IRankingAlgorithm.Apply(state, batch)` batched-only interface | §Vendored Glicko-2; §Pitfall #1 (no per-match overload). |
| RANK-05 | Default `Glicko2Algorithm` vendored from MaartenStaa/glicko2-csharp | §Vendoring discipline; §Pitfall #1 (license verification BSD not MIT); §Code Examples — RatingCalculator.UpdateRatings shape. |
| RANK-06 | 1000-match convergence integration test | §Validation Architecture SC#1 — Glickman fixture from glicko.net PDF as the seed-row + 1000 synthetic matches against two known-true-skill populations. |
| RANK-07 | Lazy rank creation on first match (NOT registration) | §Architecture: handled inside `PendingRatingUpdatesAdapter.Enqueue` and `RankingsTickerService.DrainLadder` — upsert `player_ranks (player_id, ladder_id)` on first batch entry. |
| RANK-08 | Leaderboard `top-N` + `around-me` with `(ladder_id, rating DESC)` index | §ILeaderboardService; §Code Examples — keyset pagination on `rating DESC`. |
| RANK-09 | `services.AddLadder("name")` registration API | D-21; fluent builder on `IGameKitRankingsBuilder`. |
| RANK-10 | Seasonal leaderboard reset + archival | D-11/D-12/D-13/D-14; §Architecture — `EndSeasonService` runs a single SERIALIZABLE transaction that opens new `ladder_seasons` row + archives current `player_ranks` rows into `season_rank_archive` + applies `SeasonResetPolicy`. |
| RANK-11 | Session-complete: state-conditional + cached deltas + Idempotency-Key | D-07/D-08; §Architecture; §Code Examples — `UPDATE … WHERE state = 'active' RETURNING …`. |
| RANK-12 | Manual rank adjustment writes to `admin_audit_log` with before/after | D-19/D-20; §Code Examples — rank-adjust endpoint pattern (cookie scheme + Superadmin + antiforgery + `IAdminAuditWriter`). |
| RANK-13 | `GET /api/players/{id}/export` returns full PII bundle | D-15/D-16/D-17/D-18; §Code Examples — REPEATABLE READ transaction handler. |
| RANK-14 | Per-package migrations under `__ef_migrations_rankings` | §Per-package migration replay; §Pitfall #3 (always-exclude Core entities via `IModelCustomizer`); advisory-lock key live-verified per Phase-2 precedent. |
</phase_requirements>

<architectural_responsibility_map>
## Architectural Responsibility Map

Phase 4 is a server-only library phase — every capability runs in the API/Backend tier. No browser, no SSR frontend, no CDN, no edge. The "secondary tier" column maps each capability to the in-process subsystem it primarily exercises.

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| `GameKit.Rankings` NuGet package surface | API / Backend | — | Library is a server-side component; no client artifact ships. |
| Glicko-2 algorithm (vendored) | API / Backend | In-process computation | Pure deterministic math; no I/O. Lives in `GameKit.Rankings` assembly. |
| `RankingsTickerService` background work | API / Backend | Cache (Redis lock) + DB (per-ladder transaction) | `BackgroundService` runs in the same process as Kestrel; coordinates via Redis lock and writes via EF Core. |
| `POST /api/sessions/{id}/complete` | API / Backend | DB (state-conditional UPDATE) + DB (idempotency row) | HTTP endpoint, no UI. |
| `GET /api/players/{id}/export` | API / Backend | DB (REPEATABLE READ snapshot) | HTTP endpoint, no UI; emits single-blob JSON. |
| `POST /admin/api/players/{id}/rank-adjust` | API / Backend | DB (SERIALIZABLE tx) + Cookie-auth scheme (Phase 3) + Antiforgery filter (Phase 3) | Phase-3 admin cookie scheme, but logic and validation live in Phase 4 backend. |
| Palette-verb wiring (`rank-adjust`, `end-season`) | Frontend Server (SSR — Blazor Server) | API call from MainLayout.OpenDialog | Phase-3 Blazor Server admin shell hosts the dialogs; their `<form>` POSTs land at Phase-4 backend endpoints. Dialog components ship from `GameKit.Admin.UI`. |
| `service_tokens` storage + CLI minting | API / Backend (CLI) | DB | `dotnet gamekit service-token` runs out-of-process against the same Postgres; bearer middleware lives in-process. |
| `pending_rating_updates` queue | DB | API/Backend (writer) | Persistence-only — no in-memory cache; Postgres is the source of truth for "what needs draining." |
| `ladders` / `player_ranks` / `season_rank_archive` / `ladder_seasons` | DB / Storage | API / Backend (writer) | Owned tables; live in `gamekit` schema under Rankings' migration history. |

**Tier-correctness assertion for plan-checker:** The two new dialogs (`RankAdjustDialog`, `EndSeasonDialog`) MUST live in `src/GameKit.Admin.UI/Components/Dialogs/` — that's the Frontend-SSR tier. Their HTTP targets (`/admin/api/players/{id}/rank-adjust` and `/admin/api/ladders/{id}/end-season`) MUST live in `src/GameKit.Rankings/Http/` — backend tier. The palette-verb constants are already in `AdminCommandRegistry` (Phase 3); Phase 4 only un-comments the `rank-adjust` and `end-season` switch arms inside `MainLayout.OpenDialog` (`MainLayout.razor:130`).
</architectural_responsibility_map>

## Project Constraints (from CLAUDE.md)

The following CLAUDE.md directives apply to Phase 4 and MUST NOT be contradicted by any plan:

| Directive | How Phase 4 honors it |
|-----------|----------------------|
| **GPL — fully open-source. No proprietary deps, no telemetry, no phone-home.** | All packages used here are Apache-2.0 / MIT / BSD / EPL-2 (GPL-compatible). Glicko-2 vendoring keeps the dep graph clean. OpenTelemetry remains opt-in (D-04 logs via `ActivitySource` only — host wires the SDK if desired). [VERIFIED: Directory.Packages.props]. |
| **Self-hosted only. No cloud-service dependencies.** | Ticker uses local Redis + local Postgres only. CLI service-token mint runs locally. GDPR export streams from local DB. No outbound HTTP. |
| **.NET 10 LTS, ASP.NET Core 10, EF Core 10.0.6, Npgsql 10.0.1** | All pins already in `Directory.Packages.props` from Phases 1–3. `GameKit.Rankings.csproj` adds zero new `<PackageReference>` entries. [VERIFIED: Directory.Packages.props read 2026-05-15]. |
| **Postgres only for v1.** | All new tables go in `gamekit` schema. JSONB columns (`ladders.config`, `season_rank_archive.metadata` if any) use `HasColumnType("jsonb")`. [CITED: npgsql.org/efcore/mapping/json.html]. |
| **Redis for matchmaking + presence (Phase 4 ticker uses for leader election).** | `StackExchange.Redis 2.8.41` already pinned. `IDatabase.LockTake / LockExtend / LockRelease` is the canonical primitive (D-03). [CITED: redis.io distributed-locks pattern]. |
| **JWT via `Microsoft.AspNetCore.Authentication.JwtBearer` — already in use for player auth.** | Service-account bearer scheme is **NOT** JwtBearer — it is a custom `AuthenticationHandler<TOptions>` that does a SHA-256-hash table lookup against `service_tokens`. Player JWT scheme stays unchanged. See §Architecture §Service-token auth scheme. |
| **Refresh-token discipline: never store raw tokens — always SHA-256 hash; raw issued to client once.** | Mirrored exactly for service tokens (§Architecture §Service-token storage). |
| **Per-package migration boundaries: packages never modify Core tables in their migrations — only add new tables or FK references.** | Rankings migration ADDS the FK from `game_sessions.ladder_id → ladders.id`. The FK column already exists on Core's `game_sessions` table (Phase 1 reserved it); the Rankings migration introduces the constraint via `ALTER TABLE … ADD CONSTRAINT …` — not a table modification. See §Pitfalls §FK-from-Core-to-Rankings. |
| **Metadata JSONB columns: sparse, infrequently-written, non-relational data only.** | `ladders.config` is JSONB and meets this constraint (read at startup; written once per `AddLadder` call). Per-row `player_ranks` carries no JSONB — all fields are typed columns. |
| **xUnit + Testcontainers + Moq for integration tests against real Postgres + Redis.** | All Phase-4 integration tests reuse `tests/GameKit.TestFixtures/PostgresFixture.cs` + spin up a `Testcontainers.Redis` container per fixture (already pinned). |
| **XML doc comments on every public API.** | CS1591-as-error already enforced repo-wide via `Directory.Build.props`. |
| **In-house Glicko-2 vendoring — credit MaartenStaa in source header. Unit tests: ship Glickman's original worked example as regression fixture.** | §Vendored Glicko-2 spells this out. License header MUST cite BSD-2-Clause or BSD-3-Clause (we read both answers — see Pitfalls §1 — discrepancy MUST be resolved with a direct git-clone check before Glicko-2 source is committed). |
| **NOT MediatR / AutoMapper / Hangfire / Quartz / IdentityServer / OpenIddict / ASP.NET Core Identity / Jab / FluentValidation.AspNetCore.** | Phase 4 introduces no mediator, no auto-mapper, no scheduler library, no IdP scaffolding. Mapping is hand-rolled in the GDPR export handler; background work is `BackgroundService` + `PeriodicTimer`. |
| **MinVer-driven SemVer; sibling refs exact-pinned `[X.Y.Z]`.** | `GameKit.Rankings.csproj` will declare `ProjectReference` to `GameKit.Core` (for `IPostSessionCompleteHandler` port) and to `GameKit.Auth` only if needed (it isn't — see §Architecture §Dependency direction). |

## Standard Stack

### Core

Every package below is **already pinned** in `Directory.Packages.props` from Phases 1–3. Phase 4 adds **zero** new NuGet references — confirmed by `cat Directory.Packages.props` read 2026-05-15.

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.EntityFrameworkCore` | **10.0.6** | ORM + migrations | Existing per-package migration pattern (Phase 1 D-04). [VERIFIED: Directory.Packages.props]. |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **10.0.1** | Postgres provider — jsonb mapping, `IsolationLevel.RepeatableRead`, advisory locks | Required by `IsolationLevel` API + `HasColumnType("jsonb")` + Phase-1 `pg_advisory_lock` migration runner. [VERIFIED: Directory.Packages.props]. |
| `StackExchange.Redis` | **2.8.41** | Redis client for ticker distributed lock (D-03) | `IDatabase.LockTake / LockExtend / LockRelease` already wraps `SET NX PX` with a Lua-script-verified release. [CITED: stackoverflow / leapcell.io 2026 article]. |
| `FluentValidation` | **12.1.1** | Request DTO validation (session-complete + rank-adjust + end-season + GDPR export) | Phase-3 D-09 ban-reason validator is the precedent; rank-adjust reason is the parallel (3–512 chars). [VERIFIED: Directory.Packages.props]. |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.6 | Player JWT scheme (carried over from Phase 2) | Not used by service-token scheme; player JWT validates `Authorization: Bearer` for the GDPR-export player path. [VERIFIED: Directory.Packages.props]. |
| `Microsoft.AspNetCore.Antiforgery` | 10.0 (shared framework) | CSRF on `/admin/api/players/{id}/rank-adjust` | Phase-3 `AntiforgeryValidationFilter` (D-16) covers admin mutations. No new code; Rankings endpoints simply `.AddEndpointFilter<AntiforgeryValidationFilter>()`. [VERIFIED: src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs]. |
| `Spectre.Console.Cli` | 0.49.1 | `dotnet gamekit service-token …` verbs | Phase-1 CLI host (`src/GameKit.Cli/Program.cs`) registers verbs via `CommandApp.Configure(...)`; new branch `service-token` mirrors the existing `admin` branch. [VERIFIED: src/GameKit.Cli/Program.cs]. |
| `Polly` | 8.5.x (transitively via `Microsoft.Extensions.Http.Resilience`) | Redis reconnect / ticker backoff (D-03) | Pure `Polly.ResiliencePipelineBuilder` for non-HTTP work — wraps the `LockTake` retry loop with decorrelated jitter. [CITED: pollydocs.org]. Note: Polly is NOT directly pinned in `Directory.Packages.props` — it flows in transitively via `Microsoft.Extensions.Http.Resilience 10.5.0`. If a plan needs Polly types directly, add `PackageVersion Include="Polly" Version="8.5.x"` to `Directory.Packages.props` (verify exact version on nuget.org before pinning). |
| `System.Text.Json` | 10.0 (shared framework) | GDPR export serialization, idempotency `response_hash` canonicalization | Built-in. `JsonDocument` already used for jsonb columns. [VERIFIED: existing Player.Metadata: JsonDocument]. |

### Supporting (no new pin needed)

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Security.Cryptography.SHA256` (BCL) | net10.0 | Service-token hashing | Mirrors `RefreshTokenService.Sha256Hex` exactly. [VERIFIED: src/GameKit.Auth/Services/RefreshTokenService.cs:280-284]. |
| `Microsoft.Extensions.Hosting` | 10.0.0 | `BackgroundService` base class for `RankingsTickerService` + `IdempotencyCleanupService` + `StartupLadderUpserter` | [VERIFIED: Directory.Packages.props]. |
| `Microsoft.Extensions.DependencyInjection` (shared framework) | 10.0 | `TryAddEnumerable` for `IModelBuilderExtension`, `AddHostedService` | [VERIFIED: AuthBuilderExtensions.cs:59-60]. |
| `System.Diagnostics.ActivitySource` (BCL) | net10.0 | Opt-in observability (D-04) | Emit `ActivitySource("GameKit.Rankings.Ticker")` spans; consumer host wires `AddOpenTelemetry().AddSource("GameKit.*")` if desired. No hard OTel dep. |

### Alternatives Considered

| Instead of | Could Use | Tradeoff — but DO NOT pick this |
|------------|-----------|---------------------------------|
| In-house Glicko-2 vendor | `Glicko-2RankingSystem` NuGet | CLAUDE.md §2 explicitly rejects — unmaintained; 150 LOC doesn't warrant a dep. |
| `IDatabase.LockTake/LockExtend/LockRelease` | Raw `SET NX PX` via `db.ExecuteAsync` | Reinventing what `LockTake` already does with a Lua-script-verified release. Use the wrapper. [CITED: leapcell.io]. |
| REPEATABLE READ for GDPR export | SERIALIZABLE | Snapshot consistency is achieved by REPEATABLE READ on Postgres (MVCC); SERIALIZABLE adds predicate-locking overhead with no benefit for a read-only handler. D-17 locked. [CITED: postgresql.org/docs/current/transaction-iso.html — "consistent snapshot of the database at the moment the transaction begins"]. |
| Hand-roll JSON canonicalization for `response_hash` | `Org.Webpki.JsonCanonicalizer` (RFC 8785 / JCS) NuGet | Adding a new runtime dep for one feature when our request shape is fixed and shallow (3 fields per participant). See Open Questions Q5 — recommend "stable-sort top-level + structural-hash" rather than full JCS. |
| Per-match `IRankingAlgorithm.Apply` overload | "Just one batched method" | Pitfall #1 — silent Glicko-2 corruption. CONTEXT D-01/RANK-04 already lock batched-only. |
| BCL `System.Net.Http.Json` for service-token mint | Spectre.Console.Cli output | Out of scope — `dotnet gamekit service-token issue` writes to stdout only; no HTTP. |

**Installation:** None. Phase 4 adds no `PackageReference` to any csproj.

**Version verification (already done for repo-pinned packages):**
- `Microsoft.EntityFrameworkCore 10.0.6` — [VERIFIED: pinned in Directory.Packages.props 2026-04-15; live verified via `dotnet restore` runs in Phases 1–3].
- `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1` — [VERIFIED: pinned + live verified in Phases 1–3].
- `StackExchange.Redis 2.8.41` — [VERIFIED: pinned 2026-04-15].
- `FluentValidation 12.1.1` — [VERIFIED: pinned + live verified in Phases 2–3].
- `Spectre.Console.Cli 0.49.1` — [VERIFIED: pinned + actively used in `src/GameKit.Cli/`].

## Package Legitimacy Audit

**Determination: not applicable for Phase 4.**

Phase 4 introduces **zero new NuGet package references**. Every library it uses was pinned and audited in Phases 1–3. The slopcheck / npm-style legitimacy gate exists to catch hallucinated package names in fresh installs; Phase 4 has no fresh installs.

The single exception is the **vendored Glicko-2 source** from `MaartenStaa/glicko2-csharp`. That is not a NuGet package — it is four C# source files copy-pasted into `src/GameKit.Rankings/Glicko2/` under our own GPL header preserving the upstream BSD attribution. The audit obligation for vendored source is:

1. **Verify the upstream LICENSE** is GPL-compatible (BSD-2 and BSD-3 both are — Pitfall §1 captures the lingering ambiguity over which it actually is).
2. **Add an SPDX attribution comment** in each vendored file: `// Portions vendored from https://github.com/MaartenStaa/glicko2-csharp (BSD-{2|3}-Clause, Copyright (c) 2015 Maarten Staa).`
3. **Update `REUSE.toml`** to declare the vendored portion's license alongside our GPL header.

| Package | Registry | Disposition | Notes |
|---------|----------|-------------|-------|
| (no new packages) | — | — | Audit not applicable. Every pin is carried over from Phases 1–3. |
| `MaartenStaa/glicko2-csharp` (vendored — NOT a package ref) | github.com | Conditionally approved | License must be verified by a direct `git clone` before commit. Two web fetches disagreed (BSD-2 vs BSD-3). |

## Architecture Patterns

### System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         CONSUMER ASP.NET CORE APP                        │
│ ┌────────────────────────────────────────────────────────────────────┐  │
│ │  Kestrel HTTP pipeline                                              │  │
│ │  ┌─────────────────────────────────────────────────────────────┐   │  │
│ │  │  /api/sessions/{id}/complete                                 │   │  │
│ │  │  ─ Bearer: service-token scheme (Phase 4 new)               │   │  │
│ │  │  ─ FluentValidation filter (Phase 4 new)                     │   │  │
│ │  │  ─ Rate-limit: gamekit:sessions:complete (Phase 4 new)       │   │  │
│ │  │  → SessionCompleteHandler (GameKit.Core, Phase 4 new)        │   │  │
│ │  │      └── IPostSessionCompleteHandler.OnCompletedAsync(...)   │   │  │
│ │  │            (interface in Core; impl in Rankings)              │   │  │
│ │  └─────────────────────────────────────────────────────────────┘   │  │
│ │  ┌─────────────────────────────────────────────────────────────┐   │  │
│ │  │  GET /api/players/{id}/export   (player JWT)                 │   │  │
│ │  │  GET /admin/api/players/{id}/export (admin cookie+Superadmin)│   │  │
│ │  │  → GdprExportService (Phase 4 new)                           │   │  │
│ │  │      └── REPEATABLE READ tx ─→ 7 DbSet<T> reads ─→ commit    │   │  │
│ │  │      └── 25 MB cap check on serialized output                 │   │  │
│ │  └─────────────────────────────────────────────────────────────┘   │  │
│ │  ┌─────────────────────────────────────────────────────────────┐   │  │
│ │  │  POST /admin/api/players/{id}/rank-adjust  (admin cookie)    │   │  │
│ │  │  ─ AntiforgeryValidationFilter (Phase 3, reused)             │   │  │
│ │  │  ─ Superadmin policy (Phase 3, reused)                       │   │  │
│ │  │  → RankAdjustService (Phase 4 new)                           │   │  │
│ │  │      └── SERIALIZABLE tx: UPDATE player_ranks                │   │  │
│ │  │                          + INSERT admin_audit_log (via Phase-3 IAdminAuditWriter) │  │
│ │  └─────────────────────────────────────────────────────────────┘   │  │
│ │  ┌─────────────────────────────────────────────────────────────┐   │  │
│ │  │  POST /admin/api/ladders/{id}/end-season  (admin cookie)     │   │  │
│ │  │  ─ AntiforgeryValidationFilter + Superadmin                  │   │  │
│ │  │  → EndSeasonService (Phase 4 new)                            │   │  │
│ │  └─────────────────────────────────────────────────────────────┘   │  │
│ └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│ ┌────────────────────────────────────────────────────────────────────┐  │
│ │  Background services                                                │  │
│ │  ┌─────────────────────────────────────────────────────────────┐   │  │
│ │  │ RankingsTickerService : BackgroundService                    │   │  │
│ │  │   loop every 60s:                                            │   │  │
│ │  │     ┌─ Redis: IDatabase.LockTake("gamekit:rankings:ticker:lease", 90s)  │   │  │
│ │  │     │   (Polly v8 retry on transient Redis failure)         │   │  │
│ │  │     ├─ for each active ladder:                              │   │  │
│ │  │     │     SELECT player_id, ladder_id, score, result        │   │  │
│ │  │     │     FROM pending_rating_updates                       │   │  │
│ │  │     │     WHERE ladder_id = @id AND applied_at IS NULL      │   │  │
│ │  │     │     and ladder.last_drained_at + RatingPeriod ≤ now() │   │  │
│ │  │     │   → IRankingAlgorithm.Apply(state, batch)             │   │  │
│ │  │     │   → UPDATE player_ranks SET … in same tx              │   │  │
│ │  │     │   → UPDATE session_participants SET rating_before/after│   │  │
│ │  │     │   → UPDATE pending_rating_updates SET applied_at = now()│  │  │
│ │  │     └─ Redis: LockRelease                                    │   │  │
│ │  └─────────────────────────────────────────────────────────────┘   │  │
│ │  ┌─────────────────────────────────────────────────────────────┐   │  │
│ │  │ IdempotencyCleanupService : BackgroundService                │   │  │
│ │  │   nightly: DELETE FROM session_complete_idempotency          │   │  │
│ │  │            WHERE created_at < now() - 24h                    │   │  │
│ │  └─────────────────────────────────────────────────────────────┘   │  │
│ │  ┌─────────────────────────────────────────────────────────────┐   │  │
│ │  │ StartupLadderUpserter : IHostedService                       │   │  │
│ │  │   on app start: upsert AddLadder() rows by name              │   │  │
│ │  └─────────────────────────────────────────────────────────────┘   │  │
│ │  ┌─────────────────────────────────────────────────────────────┐   │  │
│ │  │ RankingsMigrationHostedService : IHostedService              │   │  │
│ │  │   on app start: MigrateWithLockAsync(__ef_migrations_rankings)│  │  │
│ │  └─────────────────────────────────────────────────────────────┘   │  │
│ └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│ ┌────────────────────────────────────────────────────────────────────┐  │
│ │  dotnet gamekit service-token  {issue|revoke|list}  (CLI host)     │  │
│ │   → reads conn-string env, mints random 32-byte secret             │  │
│ │   → SHA-256-hashes, stores in service_tokens, prints raw to stdout │  │
│ └────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
                                  │                          │
                                  ▼                          ▼
           ┌──────────────────────────────────┐    ┌─────────────────────┐
           │ Postgres 17.9 (gamekit schema)   │    │ Redis 8.6.2          │
           │  Phase 4 NEW tables:              │    │  Phase 4 NEW keys:   │
           │  - ladders                        │    │  - gamekit:rankings: │
           │  - player_ranks                   │    │    ticker:lease      │
           │  - ladder_seasons                 │    │                       │
           │  - season_rank_archive            │    │                       │
           │  - service_tokens                 │    │                       │
           │  - pending_rating_updates         │    │                       │
           │  - session_complete_idempotency   │    │                       │
           │  __ef_migrations_rankings (HIST)  │    │                       │
           └──────────────────────────────────┘    └─────────────────────┘
```

### Recommended Project Structure

Modeled on the proven Phase-2 / Phase-3 layouts:

```
src/GameKit.Rankings/
├── GameKit.Rankings.csproj          # net10.0; ProjectReference Core only
├── AssemblyInfo.cs
├── GameKitRankingsOptions.cs        # root options
├── Builder/
│   ├── IGameKitRankingsBuilder.cs   # fluent ladder registration surface
│   ├── RankingsBuilderExtensions.cs # AddRankings + AddLadder
│   └── RankingsApplicationBuilderExtensions.cs  # MapRankings, MapSessionComplete
├── Data/
│   ├── RankingsMigrationConstants.cs        # history table name + advisory lock key
│   ├── RankingsDesignTimeDbContextFactory.cs # dotnet ef tooling
│   ├── RankingsMigrationHostedService.cs    # IHostedService — apply on startup
│   ├── RankingsModelBuilderExtension.cs     # IModelBuilderExtension impl
│   └── Configurations/
│       ├── LadderConfiguration.cs
│       ├── PlayerRankConfiguration.cs
│       ├── LadderSeasonConfiguration.cs
│       ├── SeasonRankArchiveConfiguration.cs
│       ├── ServiceTokenConfiguration.cs
│       ├── PendingRatingUpdateConfiguration.cs
│       └── SessionCompleteIdempotencyConfiguration.cs
├── Entities/
│   ├── Ladder.cs
│   ├── PlayerRank.cs
│   ├── LadderSeason.cs
│   ├── SeasonRankArchive.cs
│   ├── ServiceToken.cs
│   ├── PendingRatingUpdate.cs
│   ├── SessionCompleteIdempotency.cs
│   └── SeasonResetPolicy.cs           # enum: SoftRegress / HardReset / ArchiveOnly
├── Glicko2/                            # vendored ~150 LOC
│   ├── Rating.cs                       # SPDX-License-Identifier: GPL-3.0-or-later
│   ├── RatingCalculator.cs             # + "// Portions BSD-{2|3}-Clause, MaartenStaa 2015"
│   ├── RatingPeriodResults.cs
│   └── Result.cs
├── Algorithms/
│   ├── IRankingAlgorithm.cs           # Apply(state, batch) — RANK-04
│   └── Glicko2Algorithm.cs            # IRankingAlgorithm impl wrapping Glicko2/RatingCalculator
├── Services/
│   ├── RankingsTickerService.cs       # BackgroundService — D-01 / D-03
│   ├── RankingsTickerLeaseHelper.cs   # encapsulates LockTake/Extend/Release
│   ├── IPostSessionCompleteHandler.cs # PORT — interface in Core, impl here
│   │                                    (or define interface in Core; just impl here)
│   ├── PendingRatingUpdatesAdapter.cs # IPostSessionCompleteHandler impl
│   ├── ILeaderboardService.cs         # TopAsync + AroundAsync
│   ├── LeaderboardService.cs
│   ├── IEndSeasonService.cs           # admin verb backend
│   ├── EndSeasonService.cs
│   ├── IRankAdjustService.cs          # admin rank-adjust backend
│   ├── RankAdjustService.cs
│   ├── IGdprExportService.cs          # REPEATABLE READ handler
│   ├── GdprExportService.cs
│   ├── IServiceTokenService.cs        # CLI + middleware shared
│   ├── ServiceTokenService.cs
│   └── IdempotencyCleanupService.cs   # BackgroundService — nightly cleanup
├── Authentication/
│   ├── ServiceTokenAuthenticationHandler.cs # custom AuthenticationHandler<TOptions>
│   ├── ServiceTokenAuthenticationOptions.cs
│   ├── ServiceTokenAuthenticationDefaults.cs # const scheme name "GameKitServiceToken"
│   └── ServiceTokenAuthorizationPolicy.cs
├── Http/
│   ├── RankingsEndpoints.cs           # MapPost /admin/api/players/{id}/rank-adjust + /admin/api/ladders/{id}/end-season + /admin/api/leaderboard + /admin/api/players/{id}/export + /api/players/{id}/export
│   ├── SessionCompleteEndpoint.cs     # actually lives in Core/Http per D-22
│   ├── Contracts/
│   │   ├── SessionCompleteRequest.cs
│   │   ├── SessionCompleteResponse.cs
│   │   ├── RankAdjustRequest.cs
│   │   ├── EndSeasonRequest.cs
│   │   ├── GdprExportResponse.cs
│   │   └── LeaderboardRowDto.cs
│   ├── Validators/
│   │   ├── SessionCompleteRequestValidator.cs
│   │   ├── RankAdjustRequestValidator.cs
│   │   └── EndSeasonRequestValidator.cs
│   ├── EndpointFilters/
│   │   ├── IdempotencyKeyEndpointFilter.cs  # dedup via session_complete_idempotency
│   │   └── ResponseSizeCapFilter.cs         # 25 MB guard for GDPR export
│   └── RateLimiting/
│       └── RankingsRateLimitRegistrations.cs  # gamekit:sessions:complete
├── Json/
│   └── CanonicalJsonHasher.cs        # response_hash computation (see Open Q5)
├── Migrations/
│   ├── 20260515000000_RankingsInitial.cs
│   ├── 20260515000000_RankingsInitial.Designer.cs
│   └── GameKitDbContextModelSnapshot.cs

src/GameKit.Core/Http/                # session-complete endpoint lives here per D-22
└── SessionCompleteEndpoint.cs        # NEW — calls IPostSessionCompleteHandler if registered

src/GameKit.Core/Services/             # NEW interface in Core
└── IPostSessionCompleteHandler.cs    # port — Rankings supplies the impl

src/GameKit.Cli/Commands/              # CLI verbs — Phase 4 new
├── ServiceTokenIssueCommand.cs
├── ServiceTokenRevokeCommand.cs
└── ServiceTokenListCommand.cs

src/GameKit.Admin.UI/                  # Phase-3 surface, Phase-4 lights up two switch arms
├── Components/Dialogs/
│   ├── RankAdjustDialog.razor        # NEW — opens from MainLayout.OpenDialog "rank-adjust"
│   └── EndSeasonDialog.razor         # NEW — opens from "end-season"
├── Components/Layout/
│   └── MainLayout.razor              # edit lines 123-132 to wire the two new dialog types
├── Services/
│   ├── AdminAuditActions.cs          # ADD const string LadderEndSeason = "admin.ladder.end_season";
│   └── AuditSentenceTemplates.cs     # ADD a LadderEndSeason template
└── Services/AdminCommandRegistry.cs  # ADD `new("end-season", "End ladder season", "actions",
                                      #          RequiresSuperadmin: true, RequiresTarget: true)`

tests/GameKit.Rankings.Tests/          # unit (in-memory model + Glickman fixture)
tests/GameKit.Rankings.Integration.Tests/   # Testcontainers Postgres + Redis
```

### Pattern 1: Per-package migration replay

**What:** Mirror `AuthDesignTimeDbContextFactory` + `AuthMigrationHostedService` + `AuthModelBuilderExtension` line-for-line for Rankings.

**When to use:** Whenever a sibling package owns its own tables.

**Example (cribbed from `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs`):**

```csharp
// Source: src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs:82-117 (adapted for Rankings)
// SPDX-License-Identifier: GPL-3.0-or-later
public sealed class RankingsMigrationModelCustomizer : RelationalModelCustomizer
{
    public RankingsMigrationModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // Apply Rankings entity configurations directly — bypass DI because the migration-context
        // factory path does not wire app services into customizer constructor injection.
        modelBuilder.ApplyConfiguration(new LadderConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerRankConfiguration());
        modelBuilder.ApplyConfiguration(new LadderSeasonConfiguration());
        modelBuilder.ApplyConfiguration(new SeasonRankArchiveConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceTokenConfiguration());
        modelBuilder.ApplyConfiguration(new PendingRatingUpdateConfiguration());
        modelBuilder.ApplyConfiguration(new SessionCompleteIdempotencyConfiguration());

        // Exclude every Core entity AND every Auth/Admin entity from the Rankings migration.
        // Auth/Admin tables are owned by their packages' migrations; we must NOT re-emit them.
        // The FK from game_sessions.ladder_id → ladders.id is added via a raw `migrationBuilder.Sql(
        //   "ALTER TABLE gamekit.game_sessions ADD CONSTRAINT fk_game_sessions_ladders FOREIGN KEY ...")`
        // call inside the Rankings InitialCreate Up() method (NOT via the model fluent API — see Pitfall §FK).
        var excluded = new[]
        {
            typeof(GameKit.Core.Entities.Player),
            typeof(GameKit.Core.Entities.GameSession),
            typeof(GameKit.Core.Entities.SessionParticipant),
            typeof(GameKit.Core.Entities.AdminAuditLog),
            // Auth entities — only if Auth is project-referenced. If Rankings has no ProjectReference to Auth,
            // these typeof() calls do not compile, so omit.
            // Admin entities — same caveat.
        };
        foreach (var t in excluded)
        {
            var e = modelBuilder.Model.FindEntityType(t);
            if (e is null) continue;
            modelBuilder.Entity(t).ToTable(e.GetTableName()!, e.GetSchema(), x => x.ExcludeFromMigrations());
        }
    }
}
```

### Pattern 2: Service-token authentication scheme

**What:** A custom `AuthenticationHandler<TOptions>` that reads `Authorization: Bearer <token>`, SHA-256-hashes the token, looks it up in `service_tokens`, and produces a claims-principal with role `service-account`. Player JWT scheme is unaffected.

**When to use:** For the session-complete endpoint and any future "trusted-server-only" surface.

**Example:**

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
public sealed class ServiceTokenAuthenticationHandler
    : AuthenticationHandler<ServiceTokenAuthenticationOptions>
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;

    public ServiceTokenAuthenticationHandler(
        IOptionsMonitor<ServiceTokenAuthenticationOptions> opts,
        ILoggerFactory log, UrlEncoder enc,
        GameKitDbContext ctx, IClock clock)
        : base(opts, log, enc)
    {
        _ctx = ctx;
        _clock = clock;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var auth)) return AuthenticateResult.NoResult();
        var raw = auth.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var token = raw.AsSpan("Bearer ".Length).TrimStart().ToString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        var row = await _ctx.Set<ServiceToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash)
            .ConfigureAwait(false);
        if (row is null || row.RevokedAt is not null || (row.ExpiresAt is { } exp && exp < _clock.UtcNow))
            return AuthenticateResult.Fail("invalid_service_token");

        // Fire-and-forget last-used update (don't block the auth path).
        _ = Task.Run(async () => await _ctx.Set<ServiceToken>()
            .Where(t => t.Id == row.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.LastUsedAt, _clock.UtcNow))
            .ConfigureAwait(false));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, row.Id.ToString()),
            new Claim(ClaimTypes.Name, row.Name),
            new Claim(ClaimTypes.Role, "service-account"),
        }, Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
```

Registered alongside the player JwtBearer scheme so it does not interfere:

```csharp
services.AddAuthentication() // already added by AddAuth() — additive
    .AddScheme<ServiceTokenAuthenticationOptions, ServiceTokenAuthenticationHandler>(
        ServiceTokenAuthenticationDefaults.SchemeName, _ => { });

// Authorization policy:
services.AddAuthorization(o =>
    o.AddPolicy("RequiresServiceToken", p =>
        p.AddAuthenticationSchemes(ServiceTokenAuthenticationDefaults.SchemeName)
         .RequireAuthenticatedUser()
         .RequireRole("service-account")));
```

### Pattern 3: REPEATABLE READ read-only handler (GDPR export)

**What:** Open a `RepeatableRead` EF transaction, run all reads, commit at exit. Postgres MVCC supplies a point-in-time snapshot without blocking writers.

**When to use:** Any handler that must produce a consistent view across multiple tables.

**Example:**

```csharp
// Source pattern: D-17 + postgresql.org/docs transaction-iso §13.2.2 +
//                 src/GameKit.Auth/Services/RefreshTokenService.cs:99-101 (ReadCommitted variant)
public async Task<GdprExportResponse> ExportAsync(Guid playerId, CancellationToken ct)
{
    await using var tx = await _ctx.Database
        .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);

    // Optional but recommended for read-only handlers — tells Postgres the snapshot is
    // guaranteed read-only, eliminating predicate-locking overhead.
    await _ctx.Database
        .ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct).ConfigureAwait(false);

    var player = await _ctx.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, ct);
    if (player is null) { await tx.CommitAsync(ct); return null!; }  // caller maps to 404

    var identities  = await _ctx.Set<PlayerIdentity>().AsNoTracking()
        .Where(i => i.PlayerId == playerId).ToListAsync(ct);
    var credentials = await _ctx.Set<PlayerCredential>().AsNoTracking()
        .Where(c => c.PlayerId == playerId).Select(c => new { c.CreatedAt, c.UpdatedAt }).ToListAsync(ct);
    var sessions    = await _ctx.SessionParticipants.AsNoTracking()
        .Where(sp => sp.PlayerId == playerId)
        .Join(_ctx.GameSessions, sp => sp.SessionId, gs => gs.Id, (sp, gs) => new {
            session_id = gs.Id, ladder_id = gs.LadderId, sp.Team, sp.Result,
            rating_before = sp.RatingBefore, rating_after = sp.RatingAfter,
            completed_at = gs.CompletedAt
        }).ToListAsync(ct);
    var ratings     = await _ctx.Set<PlayerRank>().AsNoTracking()
        .Where(r => r.PlayerId == playerId).ToListAsync(ct);

    await tx.CommitAsync(ct).ConfigureAwait(false);

    var dto = new GdprExportResponse(player, identities, credentials, sessions, ratings, _clock.UtcNow);
    var json = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
    if (json.Length > _opts.GdprExport.MaxBytes)
        throw new PayloadTooLargeException(json.Length, _opts.GdprExport.MaxBytes);
    return dto;
}
```

### Pattern 4: State-conditional + idempotency-keyed session-complete

**What:** A `RETURNING`-clause `UPDATE` with `WHERE state = 'active'` distinguishes "first writer wins" from "already completed". The idempotency table is consulted **before** the UPDATE and **populated after**, both inside the same Postgres transaction.

**When to use:** Whenever a mutation must be replayable safely under client retries.

**Example (the canonical session-complete flow):**

```csharp
public async Task<SessionCompleteResponse> CompleteAsync(
    Guid sessionId, string idempotencyKey, SessionCompleteRequest req, CancellationToken ct)
{
    var requestHash = CanonicalJsonHasher.Sha256OfCanonicalJson(req); // see Open Q5

    await using var tx = await _ctx.Database
        .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);

    // 1. Idempotency dedup (D-08).
    var prior = await _ctx.Set<SessionCompleteIdempotency>().AsNoTracking()
        .FirstOrDefaultAsync(i => i.SessionId == sessionId && i.IdempotencyKey == idempotencyKey, ct);
    if (prior is not null)
    {
        if (prior.ResponseHash != requestHash)
            throw new IdempotencyKeyReusedException();  // 409 idempotency_key_reused
        // Same key + same body → return cached response (read it from session_participants).
        var cached = await ReadCachedDeltas(sessionId, ct);
        await tx.CommitAsync(ct);
        return cached;
    }

    // 2. State-conditional UPDATE (D-07). Zero rows updated → 409 or 200-cached.
    var now = _clock.UtcNow;
    var affected = await _ctx.GameSessions
        .Where(s => s.Id == sessionId && s.State == GameSessionState.Active)
        .ExecuteUpdateAsync(u => u
            .SetProperty(s => s.State, GameSessionState.Completed)
            .SetProperty(s => s.CompletedAt, now), ct);

    if (affected == 0)
    {
        var current = await _ctx.GameSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (current is null) throw new SessionNotFoundException();
        if (current.State == GameSessionState.Completed) {
            var cached = await ReadCachedDeltas(sessionId, ct);
            await tx.CommitAsync(ct);
            return cached;
        }
        throw new SessionConflictException(current.State);  // 409 invalid_session_state
    }

    // 3. Snapshot rating deltas onto session_participants (rating_before from current player_ranks,
    //    rating_after = null at this point — filled later by the ticker; immediately render 0 deltas).
    //    Per RANK-03 / SC#3: rating_before MUST be populated NOW; rating_after + delta land on next tick.
    foreach (var p in req.Participants)
    {
        var rb = await _ctx.Set<PlayerRank>().AsNoTracking()
            .Where(r => r.PlayerId == p.PlayerId && r.LadderId == GetLadderId(sessionId))
            .Select(r => (double?)r.Rating).FirstOrDefaultAsync(ct);
        await _ctx.SessionParticipants
            .Where(sp => sp.SessionId == sessionId && sp.PlayerId == p.PlayerId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(sp => sp.Result, p.Result)
                .SetProperty(sp => sp.Score, p.Score)
                .SetProperty(sp => sp.RatingBefore, rb), ct);
    }

    // 4. Enqueue for the next batched rating-period drain via the port (D-22).
    await _postCompleteHandler.OnCompletedAsync(sessionId, ct);

    // 5. Persist the idempotency dedup row.
    _ctx.Set<SessionCompleteIdempotency>().Add(new SessionCompleteIdempotency {
        SessionId = sessionId, IdempotencyKey = idempotencyKey,
        ResponseHash = requestHash, CreatedAt = now });
    await _ctx.SaveChangesAsync(ct);

    await tx.CommitAsync(ct);
    return await ReadCachedDeltas(sessionId, ct);
}
```

### Pattern 5: Redis distributed lock for ticker leader election

**What:** `IDatabase.LockTake` / `LockExtend` / `LockRelease` on a single Redis key with a 90s TTL self-renewed mid-tick.

**When to use:** Multi-replica safety for any single-leader background job.

**Example:**

```csharp
// Source: redis.io distributed-locks pattern + StackExchange.Redis IDatabase
public async Task<bool> TryAcquireLeaseAsync(string key, string instanceId, TimeSpan ttl, CancellationToken ct)
{
    return await _redis.GetDatabase().LockTakeAsync(key, instanceId, ttl).ConfigureAwait(false);
}

public async Task<bool> RenewLeaseAsync(string key, string instanceId, TimeSpan ttl, CancellationToken ct)
{
    return await _redis.GetDatabase().LockExtendAsync(key, instanceId, ttl).ConfigureAwait(false);
}

public async Task ReleaseLeaseAsync(string key, string instanceId, CancellationToken ct)
{
    await _redis.GetDatabase().LockReleaseAsync(key, instanceId).ConfigureAwait(false);  // Lua-script-verified
}
```

The instance value must be unique per process (e.g. `$"{Environment.MachineName}:{Guid.NewGuid()}"`); the Lua-script-verified release ensures we never delete another instance's lock.

### Anti-Patterns to Avoid

- **Per-match Glicko-2 call.** Even one `algorithm.UpdateRatings(singleResult)` invocation silently re-shrinks RD on every match, ruining convergence. CONTEXT D-01 forbids — Pitfall #1 owns the test that catches it.
- **`new HttpClient()` for service-token validation.** No HTTP at all — service tokens are an in-process DB lookup. (Listed because Phases 1–2 had to defend against `naked new HttpClient()`; the equivalent landmine here is "calling Steam/Discord for a token", which makes no sense for this surface but is worth nailing down.)
- **Modifying Core's `game_sessions` schema from Rankings' migration.** The FK constraint `fk_game_sessions_ladders` is added via raw SQL inside the Rankings `Up()` method targeting the existing column. Do not re-declare the column. (Pitfall §FK.)
- **`UPDATE … SET rating = …` without a transaction wrapping the audit-log write.** RANK-12 SC#6 requires atomicity. Use SERIALIZABLE (D-19) per the ban-mutation precedent (`PlayerBanService` from Phase 3).
- **Reading every page of the GDPR export into memory and counting bytes.** The 25 MB cap (D-18) must trip BEFORE serialization completes for an over-cap payload. A streaming hash + counter approach is overkill; a single `JsonSerializer.SerializeToUtf8Bytes(...)` + `.Length` check matches the locked single-blob contract.
- **Storing service-token raw text anywhere.** Mirror `RefreshTokenService.GenerateRaw + Sha256Hex` exactly — print to stdout once, store hash only.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Glicko-2 from Glickman's paper directly | A from-scratch C# port | Vendor MaartenStaa's port; ship a 4-file copy under our header | Glickman's worked example is the canonical regression fixture; MaartenStaa's port already passes it; ~150 LOC. |
| Redis `SET NX PX` with manual Lua release | Raw `db.StringSetAsync(k, v, ttl, When.NotExists)` + custom Lua release | `IDatabase.LockTake / LockExtend / LockRelease` | Built-in StackExchange.Redis wrapper, Lua-script-verified safe release. |
| Custom JSON-canonicalization for `response_hash` | Newtonsoft / hand-rolled sort | `JsonSerializer.SerializeToUtf8Bytes` with `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DictionaryKeyPolicy = JsonNamingPolicy.CamelCase }` + structural sort of the `participants[]` by `player_id` BEFORE hash. See Open Q5. | RFC 8785 (JCS) is the gold standard, but adds a runtime dep for a marginal benefit on our shallow body. |
| `BackgroundService` retry / jitter loop | hand-rolled `while(!ct.IsCancellationRequested) { try { … } catch { await Task.Delay(...) } }` | `Polly.ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { BackoffType = DelayBackoffType.Exponential, UseJitter = true })` | Polly v8 has built-in decorrelated jitter; matches CLAUDE.md §7 stack pick. |
| Validation of request DTOs | Inline `if (...) throw new ArgumentException(...)` | `FluentValidation` validator + Phase-3 `ValidationEndpointFilter<TRequest>` | Pattern already established in Phases 2 and 3 for ban/unban + create-admin. |
| CSRF on admin endpoints | New filter | `AntiforgeryValidationFilter` (Phase 3) | Already exists. Just `.AddEndpointFilter<AntiforgeryValidationFilter>()`. |
| Audit-log writes | Manual `_ctx.AdminAuditLog.Add(...)` | `IAdminAuditWriter.WriteAsync(...)` (Phase 3 D-17) | Already exists. Scoped service; rides the caller's transaction. |
| Migration runner | Hand-rolled `Database.Migrate()` | `MigrationRunner.MigrateWithLockAsync(ctx, RankingsMigrationConstants.AdvisoryLockKey, ct)` | Phase-1 advisory-lock wrapper; per-package lock key prevents cross-package deadlock. |
| Spectre.Console CLI password input | `Console.ReadLine()` | `AdminCreateCommand.ReadPasswordMasked()` pattern (Phase 3) | Already established — `Console.ReadKey(intercept: true)` for service-token names (no password, but match the look-and-feel). |

**Key insight:** Phase 4 is mostly a *composition* phase. Every architectural building block — per-package migrations, audit writers, FluentValidation filters, antiforgery, advisory locks, rate-limit registration, Spectre.Console verbs — already exists. The only genuinely new abstractions are the `IRankingAlgorithm` strategy + the vendored Glicko-2 + the service-token bearer scheme + the REPEATABLE-READ-transaction GDPR handler.

## Runtime State Inventory

> Phase 4 is a feature-addition phase, NOT a rename / refactor / migration phase. This section is included for completeness because the CONTEXT.md introduces new operator-facing state (service tokens, ladders) that the planner must remember to handle.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | The seven new tables (`ladders`, `player_ranks`, `season_rank_archive`, `ladder_seasons`, `service_tokens`, `pending_rating_updates`, `session_complete_idempotency`) plus the new `__ef_migrations_rankings` history row. Existing `game_sessions.ladder_id` column gets its FK constraint populated retroactively (no data backfill needed — existing rows are NULL until a ladder is registered). | Per-package migration. No data migration script — all new tables start empty; existing FK column already nullable. |
| Live service config | Service tokens stored in Postgres (NOT in env vars or in a `.env` file). Ladders stored in Postgres (registered via `AddLadder` at startup, upserted by name). | Operators must remember to mint service tokens after first deploy. README + DIST guide note for Phase 6. |
| OS-registered state | None. Phase 4 introduces no OS-level state (no Task Scheduler tasks, no systemd units, no launchd plists). | None. |
| Secrets/env vars | A new optional env var `GAMEKIT_SERVICE_TOKEN_NAME` may be useful for non-interactive `dotnet gamekit service-token issue` calls. No existing env var renames. | Document in Phase-6 ops guide. |
| Build artifacts | `src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs` is regenerated by `dotnet ef migrations add`. Sibling packages' snapshots are NOT touched (per-package isolation per Pitfall §3). | Verify no Core/Auth/Admin snapshot files mutate when Rankings migration is generated. |

**Verified clean for ALL categories:** Phase 4 does not rename any existing string, table, or env var. There is no concern about stale references.

## Common Pitfalls

### Pitfall 1: MaartenStaa license is **BSD-2/BSD-3-Clause**, not MIT

**What goes wrong:** CLAUDE.md and CONTEXT.md both say `MaartenStaa/glicko2-csharp (MIT, GPL-compatible)`. **Two independent web fetches of the upstream LICENSE file returned BSD-2-Clause and BSD-3-Clause respectively.** Both are GPL-compatible per the FSF license list, but the attribution requirements differ slightly (BSD-3 adds the non-endorsement clause). If we vendor under the wrong attribution we expose ourselves to a license-compliance audit failure.

**Why it happens:** WebFetch summarizes content; the upstream README and LICENSE file may say different things. Training data may have remembered the project as MIT because the npm `glicko2` package is MIT.

**How to avoid:** Before committing any vendored Glicko-2 source, the planner MUST add a task: **`git clone https://github.com/MaartenStaa/glicko2-csharp && cat LICENSE`** and pin the exact verbatim license text into a header comment + into `REUSE.toml`. Treat CLAUDE.md's "MIT" line as `[ASSUMED]` until verified.

**Warning signs:** A REUSE.toml or SPDX header reading `MIT` for any file under `src/GameKit.Rankings/Glicko2/`.

### Pitfall 2: MaartenStaa's default τ (`tau`) is **0.75, NOT 0.5**

**What goes wrong:** Glickman's worked example in the official PDF uses τ = 0.5. The MaartenStaa default in `RatingCalculator()` (no-arg constructor) is **τ = 0.75**. If the planner ships the convergence fixture (RANK-06 / SC#1) using the no-arg constructor, the test will compare against τ = 0.75 outputs and the regression-fixture numbers (`new σ' = 0.05999`) WILL DRIFT.

**Why it happens:** Library defaults are not always paper defaults.

**How to avoid:** The `Glicko2Algorithm` wrapper MUST construct the `RatingCalculator` with `new RatingCalculator(initVolatility: 0.06, tau: 0.5)` to match Glickman's example. The 0.5 → 0.75 gap is also documented as a tuning knob for operators — exposed via `GameKitRankingsOptions.Glicko2.Tau`.

**Warning signs:** A `new RatingCalculator()` call (no args) inside `Glicko2Algorithm`. Test failure in `Glicko2ConvergenceTests.Glickman_Worked_Example_Matches` where the new σ' is ~0.06000 instead of 0.05999.

### Pitfall 3: EF Core caches the runtime model GLOBALLY per `DbContext` type across every service provider in the process

**What goes wrong:** When the test harness spins up multiple `WebApplicationFactory` instances in the same process — one with `AddGameKit().AddRankings()` and one with just `AddGameKit()` — EF Core's model cache key does NOT include the application service provider. The first context built without the Rankings extension caches a Rankings-less model; subsequent contexts in the same process reuse it and crash on `Cannot create a DbSet for 'PlayerRank'`.

**Why it happens:** Documented in the Phase-3 `AdminCreateCommand.AdminCliModelCustomizer` rationale comments (lines 105-122). Phase 4 hits the exact same issue with `PlayerRank` / `Ladder` etc.

**How to avoid:** Mirror the Phase-3 `AdminCliModelCustomizer` pattern: any test harness or CLI command that constructs a `GameKitDbContext` outside of `AddGameKit().AddRankings()` MUST use a `RankingsTestModelCustomizer : RelationalModelCustomizer` that applies the Rankings entity configurations directly (no DI), and replace it via `.ReplaceService<IModelCustomizer, RankingsTestModelCustomizer>()`.

**Warning signs:** Any test that runs `new GameKitDbContext(new DbContextOptionsBuilder<GameKitDbContext>().UseNpgsql(...).Options)` without a model-customizer replacement.

### Pitfall 4: Adding the `game_sessions.ladder_id → ladders.id` FK in EF Core's model is impossible at runtime but possible at design-time

**What goes wrong:** The Rankings `IModelBuilderExtension.ApplyTo(modelBuilder)` cannot add a `HasOne<Ladder>().WithMany().HasForeignKey(s => s.LadderId)` because the `GameSession` entity is owned by Core's `OnModelCreating` and Core does NOT reference `Ladder` (correct — Core has no dep on Rankings, per D-22). If Rankings tries to add the FK fluently, it adds it to its own per-package snapshot only — the runtime model has it but the per-package migration does not contain the `ALTER TABLE … ADD CONSTRAINT …` statement.

**Why it happens:** Each package's migration only sees that package's entities. Per-package boundary works for tables but is subtle for cross-package FKs.

**How to avoid:** The Rankings `RankingsInitial` migration's `Up()` method MUST add the FK via raw SQL:

```csharp
migrationBuilder.Sql(@"
    ALTER TABLE gamekit.game_sessions
    ADD CONSTRAINT fk_game_sessions_ladders
    FOREIGN KEY (ladder_id) REFERENCES gamekit.ladders(id)
    ON DELETE SET NULL;");
```

And `Down()` does the symmetric `DROP CONSTRAINT`. This is the "packages never modify Core tables" rule's ONLY documented carve-out — adding an FK to an already-existing nullable column does not change the column itself.

**Warning signs:** A `HasOne<Ladder>()` call inside `GameSessionConfiguration` (which lives in Core) — that would be a CLAUDE.md migration-boundary violation.

### Pitfall 5: REPEATABLE READ on Npgsql does NOT auto-promote a `SELECT` to a deferred snapshot

**What goes wrong:** Postgres `REPEATABLE READ` takes its snapshot on the FIRST `SELECT` statement after `BEGIN`, NOT on `BEGIN` itself. If our GDPR-export handler opens the transaction, then does a slow auth check (against a different DB connection!) before the first SELECT, the snapshot floats forward. For a single-handler call this is harmless, but it surprises operators who instrument their handlers.

**Why it happens:** Postgres docs §13.2.2 — "The transaction sees a snapshot of the database as of the moment the FIRST query of the transaction begins."

**How to avoid:** Use the `BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY` (single statement) at handler entry, and run the first `SELECT` immediately. The EF Core `BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)` + `ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct)` pattern shown in §Code Examples §Pattern 3 achieves this.

**Warning signs:** A `BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)` call followed by ANY work that does not access the DB before the next `SELECT`.

### Pitfall 6: `LockExtend` can fail silently if the lock has already expired

**What goes wrong:** If the ticker pauses for >90s (GC, debugger break, swap thrash), the lock expires; `LockExtend` returns `false` instead of true; if the ticker doesn't check the return value, it continues operating without the lock and another replica can race in.

**Why it happens:** StackExchange.Redis returns a bool for `LockExtend`; ignoring it is silent.

**How to avoid:** Wrap the per-tick body in `if (!await db.LockExtendAsync(key, val, ttl)) { logger.LogWarning("ticker lease lost mid-tick"); break; }` and bail out of the current iteration.

**Warning signs:** A bare `await db.LockExtendAsync(...)` discarding the return value.

### Pitfall 7: `Player.PlayerId IS NULL` rows from GDPR cascade can leak into the export

**What goes wrong:** `SessionParticipant.PlayerId` is nullable (Phase 1 D-13). When player A is GDPR-deleted, their FK is nulled on every `SessionParticipant` row. If the GDPR export for player B reads `WHERE PlayerId = B.Id` correctly, it doesn't leak — but if any join condition uses `LEFT JOIN` and forgets the WHERE, it does.

**Why it happens:** Documented in the Phase-4 CONTEXT carrying-forward block.

**How to avoid:** Every read in `GdprExportService.ExportAsync` MUST be filtered by `WHERE … PlayerId == playerId` (NOT `WHERE … OR PlayerId IS NULL`). Integration test: create a deleted-opponent session, run the export for player B, assert no row has a NULL `player_id` field.

**Warning signs:** Any `.Where(...)` clause that uses `??` or `OR` against `PlayerId`.

### Pitfall 8: `Idempotency-Key` body-hash comparison fails on whitespace / key-ordering differences

**What goes wrong:** Two identical payloads serialized by two different JSON libraries (operator's Go client vs. their Node client) produce different byte sequences (key order, whitespace, scientific-notation numbers). A naïve `SHA256(rawBytes)` flags them as "different body" → 409 idempotency_key_reused on a legitimate retry.

**Why it happens:** Stripe's docs explicitly warn — "compute the request hash over the raw body" is the antipattern. The canonical approach is JCS (RFC 8785).

**How to avoid:** Adopt a structural canonicalization rule (Open Q5): deserialize the body into the DTO, re-serialize via `JsonSerializer.SerializeToUtf8Bytes(dto, options)` with sorted dictionary keys and a stable participant-array sort by `player_id`, then SHA-256-hash that. Document this in `CanonicalJsonHasher.cs`.

**Warning signs:** A `SHA256.HashData(rawBodyBytes)` call against the unparsed HTTP body.

### Pitfall 9: Vendored Glicko-2 mathematical instability under deflation

**What goes wrong:** When a high-rated player loses many games against low-rated opponents, the volatility update can diverge if the convergence tolerance ε is too loose. Glickman's paper specifies ε = 0.000001; MaartenStaa's port defaults to this — but a custom replacement implementation may not.

**Why it happens:** The `IRankingAlgorithm` swap point invites custom impls; an under-tested replacement can corrupt the live `player_ranks` table.

**How to avoid:** The `IRankingAlgorithm` interface XML doc MUST state: "Implementations are responsible for numerical stability under all input distributions. The default `Glicko2Algorithm` uses Glickman's ε = 0.000001 convergence tolerance and has been validated against the published worked example." The 1000-match convergence test (SC#1) gates this for the default impl; replacers must pass an equivalent contract test (Pattern Mapper hint).

**Warning signs:** A custom `IRankingAlgorithm` impl shipped without a corresponding convergence test in the consuming game's test suite.

### Pitfall 10: Service-token lookup turns the auth handler into a hot DB read

**What goes wrong:** Every `POST /api/sessions/{id}/complete` triggers a `SELECT … FROM service_tokens WHERE token_hash = …`. At 300 req/min/token-fleet, the DB connection pool can saturate.

**Why it happens:** No caching layer.

**How to avoid:** **For v1**, accept the DB hit — the rate-limit (300/min/token) keeps the load tractable on a single Postgres. Document `IMemoryCache`-backed validation as a v2 optimization (5-min sliding cache of `token_hash → row`). The cache is NOT in scope for Phase 4 per CONTEXT (no decision authorizes it).

**Warning signs:** Operators reporting connection-pool warnings under load. Add a TODO in `ServiceTokenAuthenticationHandler` pointing at the v2 optimization.

### Pitfall 11: Migrations advisory-lock key collision across packages

**What goes wrong:** If `RankingsMigrationConstants.AdvisoryLockKey` accidentally equals Core's, Auth's, or Admin's key, two replicas applying their respective migrations concurrently deadlock.

**Why it happens:** Computing `hashtext('gamekit.rankings.migrations')::bigint` LOCALLY can produce a different value than Postgres computes (Postgres uses `crc32c`-based hashtext; depending on `LC_*` collations and Postgres version, the value can vary). Phase 2 caught this and added a live-verification test.

**How to avoid:** Add `RankingsAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation` — Testcontainers Postgres 17.9, run `SELECT hashtext('gamekit.rankings.migrations')::bigint`, assert equality with `RankingsMigrationConstants.AdvisoryLockKey`. Also add `RankingsAdvisoryLockKeyTests.RankingsKey_Is_Distinct_From_Core_Auth_Admin_Keys`.

**Warning signs:** A pinned advisory-lock key value that has NOT been live-verified against a Testcontainers Postgres in CI.

### Pitfall 12: `SessionParticipant.PlayerId` nullable + `pending_rating_updates` FK

**What goes wrong:** If `pending_rating_updates.player_id` is declared `NOT NULL`, a GDPR-delete during a pending drain orphans the row → next drain crashes on `Cannot insert null player_id into …`.

**Why it happens:** Two design instincts collide: "always FK-enforce" and "GDPR sets player_id to NULL."

**How to avoid:** `pending_rating_updates.player_id` MUST be NULLABLE with `ON DELETE SET NULL`. The ticker's drain query MUST skip `WHERE player_id IS NULL` rows (they correspond to GDPR-deleted players whose pending update is meaningless). Document this in the table comment.

**Warning signs:** A `player_id uuid NOT NULL` column declaration on the `pending_rating_updates` migration.

### Pitfall 13: (carry-over from CONTEXT) Rating columns as `NUMERIC(8,2)` instead of `double precision`

**What goes wrong:** RANK-03 mandates `double precision`. EF Core 10 maps `double` CLR to `double precision` natively, so this is the default — but if a planner writes `.HasColumnType("numeric(8,2)")` for "money-style stable storage", Glicko-2's internal calculations (which require ε = 0.000001 precision) round-trip with precision loss.

**Why it happens:** Habit. Money-style code expects `decimal`.

**How to avoid:** Schema-introspection test (SC#3) — `SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'gamekit' AND table_name = 'player_ranks' AND column_name IN ('rating','rating_deviation','volatility')` and assert `data_type = 'double precision'`. CONTEXT D-13 anchors this to the SessionParticipant rating columns too (already `double?`).

**Warning signs:** A `.HasColumnType("numeric…")` call on any Rankings rating column.

## Code Examples

### Schema sketch — Ladders + PlayerRank + SeasonRankArchive + LadderSeasons

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// src/GameKit.Rankings/Entities/Ladder.cs
public sealed class Ladder
{
    public Guid Id { get; set; }                        // UUIDv7
    public required string Name { get; set; }           // unique by name (citext)
    public required string Algorithm { get; set; }      // e.g. "glicko2"
    public bool IsActive { get; set; }
    public JsonDocument? Config { get; set; }           // jsonb
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastDrainedAt { get; set; }  // ticker uses this to know whether RatingPeriod elapsed
}

// src/GameKit.Rankings/Entities/PlayerRank.cs
public sealed class PlayerRank
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid LadderId { get; set; }
    public double Rating { get; set; }                  // double precision (RANK-03)
    public double RatingDeviation { get; set; }
    public double Volatility { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public DateTimeOffset? LastMatchAt { get; set; }
    // Unique constraint (player_id, ladder_id); index (ladder_id, rating DESC) for leaderboard.
}

// src/GameKit.Rankings/Entities/ServiceToken.cs
public sealed class ServiceToken
{
    public Guid Id { get; set; }
    public required string Name { get; set; }           // UNIQUE
    public required string TokenHash { get; set; }      // SHA-256 hex (64 chars)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }      // null = never
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
```

### Configuring `double precision` (RANK-03)

```csharp
// src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs
internal sealed class PlayerRankConfiguration : IEntityTypeConfiguration<PlayerRank>
{
    public void Configure(EntityTypeBuilder<PlayerRank> b)
    {
        b.ToTable("player_ranks");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();

        // EF Core 10 + Npgsql maps `double` CLR → `double precision` natively. The explicit
        // .HasColumnType("double precision") call documents intent and is asserted by the
        // schema-introspection test (SC#3).
        b.Property(r => r.Rating).IsRequired().HasColumnType("double precision");
        b.Property(r => r.RatingDeviation).IsRequired().HasColumnType("double precision");
        b.Property(r => r.Volatility).IsRequired().HasColumnType("double precision");

        b.HasIndex(r => new { r.PlayerId, r.LadderId }).IsUnique();
        b.HasIndex(r => new { r.LadderId, r.Rating })
            .HasDatabaseName("idx_player_ranks_ladder_rating")
            .IsDescending(false, true);   // (ladder_id ASC, rating DESC)

        b.HasOne<Player>().WithMany().HasForeignKey(r => r.PlayerId).OnDelete(DeleteBehavior.Cascade);
        // Ladder FK lives in this migration's package — clean fluent declaration:
        b.HasOne<Ladder>().WithMany().HasForeignKey(r => r.LadderId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

### Glicko-2 vendoring header pattern

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// Portions of this file are vendored from
//   https://github.com/MaartenStaa/glicko2-csharp
// Copyright (c) 2015 Maarten Staa, licensed under BSD-{2|3}-Clause.   <-- VERIFY before commit
// Original license text reproduced at REUSE.toml; modifications are GPL-3.0-or-later.
//
// Differences from upstream:
// - Namespace changed to GameKit.Rankings.Glicko2
// - Visibility narrowed to internal (external callers go through IRankingAlgorithm)
// - Updated to net10.0 (no API change)
// - Default tau aligned with Glickman's worked example (0.5) for test-fixture parity
//   when constructed via the no-arg overload. Upstream default is 0.75.
using System.Collections.Generic;
namespace GameKit.Rankings.Glicko2;
internal sealed class RatingCalculator
{
    // ... vendored body ...
}
```

### Glickman's worked-example regression fixture (RANK-06 / SC#1 anchor)

```csharp
// tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs
public sealed class Glicko2WorkedExampleTests
{
    /// <summary>Glickman 2012 §3.1, https://glicko.net/glicko/glicko2.pdf — verbatim numerics.</summary>
    [Fact]
    public void Glickman_Worked_Example_Matches_Within_Tolerance()
    {
        var calc = new RatingCalculator(initVolatility: 0.06, tau: 0.5);
        var player = new Rating("player", calc, initRating: 1500, initRd: 200, initVol: 0.06);
        var opp1 = new Rating("opp1", calc, 1400, 30, 0.06);
        var opp2 = new Rating("opp2", calc, 1550, 100, 0.06);
        var opp3 = new Rating("opp3", calc, 1700, 300, 0.06);

        var results = new RatingPeriodResults();
        results.AddResult(player, opp1);    // win
        results.AddResult(opp2, player);    // loss (player loses)
        results.AddResult(opp3, player);    // loss

        calc.UpdateRatings(results);

        // Glickman's published outputs (rounded to 4 decimal places per the PDF):
        Assert.Equal(1464.05, player.GetRating(), 1);
        Assert.Equal(151.52,  player.GetRatingDeviation(), 1);
        Assert.Equal(0.05999, player.GetVolatility(), 4);
    }
}
```

### Service-token CLI mint (mirrors `AdminCreateCommand`)

```csharp
// src/GameKit.Cli/Commands/ServiceTokenIssueCommand.cs
internal sealed class ServiceTokenIssueCommand : AsyncCommand<ServiceTokenIssueCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-n|--name <NAME>")] public string? Name { get; init; }
        [CommandOption("--expires <DURATION>")] public string? Expires { get; init; } // ISO 8601 duration, optional
        [CommandOption("-c|--connection-string <CONN>")] public string? ConnectionString { get; init; }
    }
    public override async Task<int> ExecuteAsync(CommandContext _, Settings s)
    {
        var conn = s.ConnectionString ?? Environment.GetEnvironmentVariable("GAMEKIT_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn)) return Fail("No connection string.");

        var name = s.Name ?? (Console.IsInputRedirected ? "" : AnsiConsole.Ask<string>("Token name:"));
        if (string.IsNullOrWhiteSpace(name)) return Fail("--name is required.");

        // Mint random 32-byte secret, base64url-encoded — mirrors RefreshTokenService.GenerateRaw().
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

        var dbOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(conn!)
            .ReplaceService<IModelCustomizer, RankingsCliModelCustomizer>()
            .Options;
        await using var ctx = new GameKitDbContext(dbOpts);

        ctx.Set<ServiceToken>().Add(new ServiceToken {
            Id = new UuidV7IdGenerator().NewId(),
            Name = name,
            TokenHash = hash,
            CreatedAt = new SystemClock().UtcNow,
            ExpiresAt = ParseExpiry(s.Expires),
        });
        try { await ctx.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (TryFindUniqueViolation(ex))
        { return Fail($"Service-token name '{name}' already exists."); }

        AnsiConsole.MarkupLine($"[green]Token issued[/]");
        AnsiConsole.MarkupLine($"  Name: [bold]{name}[/]");
        AnsiConsole.MarkupLine($"  Token (store this NOW — it is shown ONLY once):");
        AnsiConsole.MarkupLine($"  [bold yellow]{raw}[/]");
        AnsiConsole.MarkupLine($"  Hash prefix: [dim]{hash[..8]}...[/]");
        return 0;
    }
    // ... helper methods omitted ...
}
```

### Lighting up the rank-adjust palette verb (Phase-3 carry-over)

```csharp
// src/GameKit.Admin.UI/Components/Layout/MainLayout.razor — edit lines 123-132
var dialogType = commandId switch
{
    "ban"          => typeof(BanPlayerDialog),
    "unban"        => typeof(UnbanPlayerDialog),
    "gdpr-delete"  => typeof(GdprDeleteDialog),
    "create-admin" => typeof(CreateAdminDialog),
    "delete-admin" => typeof(DeleteAdminDialog),
    "rank-adjust"  => typeof(RankAdjustDialog),   // NEW (Phase 4)
    "end-season"   => typeof(EndSeasonDialog),    // NEW (Phase 4)
    // rotate-signing-key intentionally still null (Phase 6 territory)
    _ => null
};
```

And add to `AdminCommandRegistry.AllCommands`:

```csharp
new("end-season", "End ladder season", "actions",
    RequiresSuperadmin: true, RequiresTarget: true),  // target = ladder id
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `IDbConnection.BeginTransaction` raw ADO.NET | `_ctx.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)` | EF Core 3.0+ | EF Core 10 fully supports Npgsql + repeatable-read with `await using` + `CommitAsync`/`RollbackAsync`. |
| Manual `SET NX PX` Lua script for distributed lock | `IDatabase.LockTake / LockExtend / LockRelease` | StackExchange.Redis 1.x → 2.x | Lua-script-verified release ships built-in. |
| MediatR for cross-cutting in libraries | Plain ports (`IPostSessionCompleteHandler`) | MediatR 13 (July 2025) went RPL-1.5 dual-license | CLAUDE.md §STACK forbids. Phase 4 stays plain. |
| `Microsoft.AspNetCore.Identity` for service-account auth | Custom `AuthenticationHandler<TOptions>` | CLAUDE.md §STACK forbids ASP.NET Core Identity | Lightweight custom handler matches the project's "no Identity" stance. |
| Hand-rolled retry loop in `BackgroundService` | Polly v8 `ResiliencePipelineBuilder().AddRetry(...)` | Polly 8.0 (2023) | Decorrelated jitter is built-in. |
| `Newtonsoft.Json.Linq.JObject.DeepEquals` for body comparison | `JsonSerializer.SerializeToUtf8Bytes` + structural sort + SHA-256 | `System.Text.Json` matured in .NET 8 | Built-in; no Newtonsoft dep needed. |

**Deprecated/outdated:**
- `EF Core 9.x` snapshot files: do NOT use as a reference — Phase 4 generates fresh `Migrations/GameKitDbContextModelSnapshot.cs` under EF Core 10. The pattern of per-package snapshot files is unchanged.
- `FluentValidation.AspNetCore` (deprecated since v11): NOT in our stack. We use `FluentValidation` + `FluentValidation.DependencyInjectionExtensions` + explicit `IValidator<T>` injection (CLAUDE.md §STACK).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | MaartenStaa's `glicko2-csharp` is licensed under BSD-2-Clause or BSD-3-Clause (NOT MIT as CLAUDE.md / CONTEXT.md state). | Pitfall §1, Vendoring header | Wrong attribution → license-compliance audit failure. **MUST be verified by `git clone && cat LICENSE` before vendored source is committed.** |
| A2 | MaartenStaa's `RatingCalculator()` no-arg constructor uses `tau = 0.75`; Glickman's PDF worked example uses `tau = 0.5`. The Phase-4 `Glicko2Algorithm` MUST pass `tau: 0.5` to match the regression fixture. | Pitfall §2, Glickman fixture example | If wrong, the convergence test (SC#1) fails or — worse — passes against the wrong target value. |
| A3 | `IDatabase.LockTake / LockExtend / LockRelease` from StackExchange.Redis 2.8.41 correctly implements the SET NX PX pattern with a Lua-script-verified release. | Pattern 5 | If the library's lock semantics regressed since the cited 2026 articles, leader election double-fires. Mitigation: integration test that asserts only one replica claims the lease in a chaos-test scenario. |
| A4 | Postgres 17.9 + Npgsql 10.0.1 + EF Core 10.0.6 honor `IsolationLevel.RepeatableRead` by issuing `SET TRANSACTION ISOLATION LEVEL REPEATABLE READ` on `BEGIN`. | Pattern 3, Pitfall §5 | If wrong, GDPR export reads see writer-induced inconsistency between table reads. Mitigation: integration test that opens a parallel writer mid-export and asserts the export still reflects the pre-write snapshot. |
| A5 | The proposed canonicalization rule for `response_hash` (parse → sort participants by `player_id` → re-serialize with `JsonSerializer` + `JsonNamingPolicy.CamelCase`) is deterministic across .NET 10 versions. | Open Q5 | If `JsonSerializer` changes its default property ordering policy in a future patch (.NET historically has not), idempotency dedup breaks under a runtime upgrade. Mitigation: comment in `CanonicalJsonHasher.cs` warning maintainers. |
| A6 | Adding the `fk_game_sessions_ladders` FK constraint via raw SQL in the Rankings migration does NOT violate the CLAUDE.md "packages never modify Core tables" rule, because the column itself was reserved in Phase 1 and the constraint is the ONLY mutation. | Pitfall §4 | If a future reviewer disputes this carve-out, the Rankings package must drop the FK and rely on application-level integrity (orphan ladder_ids allowed). Acceptable degradation. |
| A7 | `pending_rating_updates.player_id` MUST be NULLABLE for GDPR-cascade safety. | Pitfall §12 | If declared NOT NULL, GDPR-delete crashes a future ticker tick. |
| A8 | The 25 MB GDPR-export cap (D-18) is enforced post-serialize via `JsonSerializer.SerializeToUtf8Bytes` `.Length`, NOT pre-serialize via row-count estimation. | Pattern 3 | If a player has truly enormous data, the handler allocates the full byte buffer before failing — memory pressure for one request. Acceptable for v1; streaming path deferred. |

## Open Questions

### Q1 — Glicko-2 vendoring license: BSD-2 or BSD-3?

- **What we know:** Upstream is definitely BSD, not MIT. Two web fetches of the LICENSE file disagreed on the exact variant.
- **What's unclear:** Which clause set we copy into our REUSE.toml + per-file header.
- **Recommendation:** Plan a Wave 0 task: `git clone https://github.com/MaartenStaa/glicko2-csharp && cat LICENSE` once. Pin the verbatim text. Treat A1 as confirmed by that read.

### Q2 — `pending_rating_updates` exact column shape

- **What we know:** It's the queue the session-complete handler enqueues into and the ticker drains. Player_id must be nullable (Pitfall §12). Must support fast per-ladder filtering.
- **What's unclear:** Whether the row references `session_participants.id` (rich) or just `(session_id, player_id, ladder_id, result, score, claimed_at, applied_at)` (denormalized). Index strategy.
- **Recommendation:** Denormalized — easier to drain in batches without `JOIN`s, easier to clean up after a successful drain. Columns:
  ```
  id uuid PK,
  session_id uuid NOT NULL,
  player_id uuid NULL,  -- ON DELETE SET NULL via FK to players
  ladder_id uuid NOT NULL,  -- FK to ladders
  result text NOT NULL,  -- 'win' | 'loss' | 'draw' | 'forfeit'
  score int NULL,
  enqueued_at timestamptz NOT NULL,
  claimed_at timestamptz NULL,  -- set when the ticker leases a batch
  applied_at timestamptz NULL,  -- set when the algorithm has been applied
  ```
  Indexes:
  ```
  CREATE INDEX idx_pending_rating_updates_ladder_pending
    ON gamekit.pending_rating_updates (ladder_id, enqueued_at)
    WHERE applied_at IS NULL;
  ```
  After a successful drain, rows STAY (don't `DELETE`) so the audit trail survives — cleaned up by `IdempotencyCleanupService` after a configurable retention (default 30 days).
- **Planner decision required.**

### Q3 — Where does the service-token bearer-validation middleware live?

- **What we know:** D-22 says the session-complete endpoint lives in `GameKit.Core`. The auth scheme is a Phase-4 invention.
- **What's unclear:** Whether `ServiceTokenAuthenticationHandler` lives in `GameKit.Rankings` (with Core having NO reference to it) or in `GameKit.Core` (so the endpoint in Core can wire `RequireAuthorization(...)` against a scheme declared in the same assembly).
- **Recommendation:** **Lives in `GameKit.Rankings`.** Core remains zero-dep on Rankings (per D-22). The session-complete endpoint in Core uses `RequireAuthorization()` with a policy name string (`"GameKitServiceToken"`); the policy itself is registered by Rankings' `AddRankings()`. If a consumer installs Core but NOT Rankings, the session-complete endpoint refuses all traffic (which is the correct degraded behavior — no rating system → no completed sessions to record).
- **Planner decision required.**

### Q4 — Antiforgery on `/admin/api/players/{id}/rank-adjust`

- **What we know:** Phase 3's `AntiforgeryValidationFilter` covers all `/admin/api/*` mutations registered with the filter.
- **What's unclear:** Whether the existing filter "just works" for endpoints registered by `GameKit.Rankings` (since the antiforgery service is registered in `GameKit.Admin.UI`'s `UseGameKitAdmin`).
- **Recommendation:** Confirmed working — `IAntiforgery` is registered globally by `AddAntiforgery()` and resolved via `RequestServices`. The Phase-3 filter is a `Microsoft.AspNetCore.Http.IEndpointFilter` that resolves `IAntiforgery` per-request; nothing about it ties it to the `GameKit.Admin.UI` assembly. Rankings simply references the filter type and applies it.
- **Open consideration:** Should Rankings depend on `GameKit.Admin.UI` to reuse the filter type? **No** — duplicate the 30-line filter into `src/GameKit.Rankings/Http/EndpointFilters/AntiforgeryValidationFilter.cs` (DRY-violation accepted to preserve the package boundary). The filter is trivially small.
- **Planner decision required.**

### Q5 — Idempotency canonicalization rule

- **What we know:** D-08 says "duplicate key with same body → cached response, duplicate key with different body → 409." Stripe's published guidance is to canonicalize. RFC 8785 (JCS) is the gold standard.
- **What's unclear:** Whether to ship a full JCS implementation, a hand-rolled stable-sort + minified JSON, or accept naïve `SHA256(rawBody)`.
- **Recommendation:** **Hand-rolled stable-sort.** Our request body has exactly three top-level shapes (`participants[]` of `{ player_id, team, result, score }`). The canonical form is:
  1. Parse to `SessionCompleteRequest` DTO.
  2. Sort `participants` array by `player_id` (Guid string comparison).
  3. `JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false })`.
  4. `SHA-256` of the resulting byte array. Hex-lowercase encoding.

  Document the rule in `CanonicalJsonHasher.cs` XML comments AND in the API contract docs so consumers know what hash their idempotency-key body must canonicalize to. **Adopt RFC 8785 / `Org.Webpki.JsonCanonicalizer` in v2 only if a consumer reports a real interop bug.**

- **Planner decision required.**

### Q6 — `IPostSessionCompleteHandler` definition site

- **What we know:** D-22 says the interface is a port; Core defines it, Rankings supplies the impl.
- **What's unclear:** Trivial — confirms that the interface file `src/GameKit.Core/Services/IPostSessionCompleteHandler.cs` is part of Phase 4 (NOT Phase 1 retroactively, even though it lives in Core).
- **Recommendation:** Add the interface file as part of Phase 4 plans (most likely the same plan that adds the session-complete endpoint). Core has zero impl for it; if no `IPostSessionCompleteHandler` is registered, the endpoint records the session-complete + caches the deltas (rating_before from a NULL pre-existing rank → 0 delta) and skips the enqueue. This means a Core-only install can complete sessions without ratings — which is the intended degraded mode.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Postgres 17.9 | All Phase-4 tables + REPEATABLE READ + advisory lock | ✓ (docker-compose, Testcontainers) | 17.9 (matches `docker-compose.yml`) | — |
| Redis 8.6.2 | Rankings ticker distributed lock (D-03) | ✓ (docker-compose, Testcontainers) | 8.6.2 (matches `docker-compose.yml`) | None — the ticker MUST have Redis. Document in README that Rankings requires Redis; without it, `AddRankings()` throws at startup. |
| .NET 10 SDK (10.0.106) | All compilation | ✓ (`global.json` pinned) | 10.0.106 | — |
| `dotnet ef` CLI | Migration generation | ✓ (already used in Phases 1–3) | 10.0.6 | — |
| `git` (for vendoring) | One-time clone of MaartenStaa to vendor source | ✓ (assumed; project is git-managed) | any modern | If absent, manually download the four `.cs` files from GitHub web UI and verify license. |
| Testcontainers.PostgreSql 4.11.0 | Integration tests | ✓ (pinned) | 4.11.0 | — |
| Testcontainers.Redis 4.11.0 | Integration tests (NEW — ticker tests need a real Redis) | ✓ (pinned) | 4.11.0 | — |

**Missing dependencies with no fallback:** None.
**Missing dependencies with fallback:** None.

## Validation Architecture

Phase 4 inherits the project's xUnit + Testcontainers + Moq stack. `workflow.nyquist_validation` is `true` in `.planning/config.json` — this section is required.

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config file | `tests/Directory.Build.props` (already exists, drives every test csproj) |
| Quick run command | `dotnet test --no-build --filter "FullyQualifiedName~GameKit.Rankings.Tests"` |
| Full suite command | `dotnet test` |
| Live-DB integration command | `dotnet test --filter "FullyQualifiedName~Integration"` (requires Docker running) |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| RANK-01 | Package ships at `src/GameKit.Rankings/GameKit.Rankings.csproj` with net10.0 TFM | smoke (csproj load) | `dotnet build src/GameKit.Rankings` | ❌ Wave 0 (csproj currently empty) |
| RANK-02 | `ladders` table exists | schema introspection | `pytest`-equivalent xUnit `SchemaTypeAssertions.Ladders_Table_Exists` | ❌ Wave 0 |
| RANK-03 | rating columns are `double precision` | schema introspection (SC#3) | `SchemaTypeAssertions.Rating_Columns_Are_DoublePrecision` | ❌ Wave 0 |
| RANK-04 | `IRankingAlgorithm.Apply(state, batch)` interface — batched only | unit (reflection check) | `Glicko2AlgorithmContractTests.IRankingAlgorithm_Has_Only_Apply_Batch_Method` | ❌ Wave 0 |
| RANK-05 | Default `Glicko2Algorithm` produces Glickman's worked-example numbers | unit | `Glicko2WorkedExampleTests.Glickman_Worked_Example_Matches_Within_Tolerance` | ❌ Wave 0 |
| RANK-06 | 1000-match convergence (SC#1) | integration | `Glicko2ConvergenceTests.Two_Populations_Converge_Within_Tolerance` | ❌ Wave 0 |
| RANK-07 | Lazy rank creation on first match | integration | `LazyRankCreationTests.Rank_Row_Created_On_First_Match_Drain` | ❌ Wave 0 |
| RANK-08 | Leaderboard top-N + around-me | integration | `LeaderboardServiceTests.TopAsync_Returns_Sorted_By_Rating_Desc`, `LeaderboardServiceTests.AroundAsync_Returns_Window_Centered_On_Player` | ❌ Wave 0 |
| RANK-09 | `AddLadder("name", config)` upserts at startup | integration | `LadderUpsertOnStartupTests.AddLadder_Inserts_Row_Idempotently` | ❌ Wave 0 |
| RANK-10 | Seasonal reset + archival (SC#4) | integration | `SeasonArchiveLeaderboardTests.Archive_Preserves_Previous_Season_TopN`, `SeasonArchiveLeaderboardTests.SoftRegress_Reduces_Rating_Toward_Default` | ❌ Wave 0 |
| RANK-11 | Session-complete 5× retry → exactly one delta (SC#2) | integration | `SessionCompleteIdempotencyTests.Retry_Five_Times_Applies_Delta_Once` | ❌ Wave 0 |
| RANK-12 | Rank-adjust audit atomicity (SC#6) | integration | `AdminRankAdjustTransactionTests.UpdateAndAudit_RollBack_Together_On_Failure` | ❌ Wave 0 |
| RANK-13 | GDPR export contract (SC#5) | contract | `GdprExportContractTests.Response_Has_All_Documented_Top_Level_Keys` | ❌ Wave 0 |
| RANK-14 | Per-package migration under `__ef_migrations_rankings` | integration | `RankingsMigrationDeterminismTests.Apply_Then_ReApply_Produces_No_Diff`, `RankingsAdvisoryLockKeyTests.RankingsKey_Is_Distinct_From_Core_Auth_Admin_Keys` | ❌ Wave 0 |

### ROADMAP Success Criteria → Test Anchors

| SC | What must be TRUE | Test Class | Fixture |
|----|--------------------|-----------|---------|
| SC#1 | 1000-match convergence within Glickman tolerance | `Glicko2ConvergenceTests` (integration) — seeds two populations of 50 players each with known true skill (1500 ± σ), simulates 1000 paired matches with outcomes weighted by true-skill delta, runs 100 rating periods through `RankingsTickerService`, asserts mean-rating of each population is within 50 points of true skill. | Glickman fixture from glicko.net PDF + RNG seeded with `Random(42)` for determinism. |
| SC#2 | `/sessions/{id}/complete` 5× retry → exactly one rating delta | `SessionCompleteIdempotencyTests` (integration) — WebApplicationFactory + Testcontainers Postgres; mints a service token; POSTs 5 times with same `Idempotency-Key`; asserts exactly one `pending_rating_updates` row enqueued and exactly one `session_participants` row has non-null `rating_before`. | `SessionCompleteFixture` (new — extends `PostgresFixture` + spins up a Redis container). |
| SC#3 | Rating columns are `double precision`; before/after/delta snapshotted | `SchemaTypeAssertions` (integration) — queries `information_schema.columns` via raw `NpgsqlCommand`, asserts `data_type = 'double precision'` for the six columns: `player_ranks.rating`, `player_ranks.rating_deviation`, `player_ranks.volatility`, `session_participants.rating_before`, `session_participants.rating_after`, `session_participants.rating_delta`. | `PostgresFixture`. |
| SC#4 | Seasonal archive preserves prior-season top-N + around-me | `SeasonArchiveLeaderboardTests` (integration) — seeds 10 players with known ratings on ladder L; triggers `EndSeasonService.EndAsync(L)`; asserts `season_rank_archive` has 10 rows with the pre-end values; asserts `ILeaderboardService.TopAsync` on the **archived** season returns the same ordering. | `PostgresFixture` + admin auth helper. |
| SC#5 | `/export` returns documented JSON bundle | `GdprExportContractTests` (integration) — seeds a player with identities + credentials + sessions + ratings; GETs `/api/players/{id}/export`; asserts top-level keys exactly `{ player, identities, credentials_metadata, sessions, rating_history, exported_at }`; asserts no `password_hash` field anywhere; asserts identities use `external_id_hash` not raw external_id; asserts response is ≤ 25 MB. | `SessionCompleteFixture` (reused). |
| SC#6 | Admin rank-adjust writes before/after atomically | `AdminRankAdjustTransactionTests` (integration) — registers a faulty `IAdminAuditWriter` that throws after the UPDATE; calls `RankAdjustService.AdjustAsync`; asserts the UPDATE was rolled back and `player_ranks.rating` is still the original value. | `AdminIntegrationFixture` (Phase 3) extended for Rankings. |

### Sampling Rate

- **Per task commit:** `dotnet test --filter "FullyQualifiedName~GameKit.Rankings.Tests"` (unit-only; ~1s wall time once written).
- **Per wave merge:** `dotnet test` full suite (Phase 4 unit + integration; ~3-5min wall time with Testcontainers cold start).
- **Phase gate:** Full suite green before `/gsd:verify-work`. All six SC anchors must show a green test.

### Wave 0 Gaps

- [ ] `tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj` — unit test project (does not exist).
- [ ] `tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj` — integration test project (does not exist).
- [ ] `tests/GameKit.TestFixtures/RankingsFixture.cs` — composes `PostgresFixture` + new `RedisFixture`.
- [ ] `tests/GameKit.TestFixtures/RedisFixture.cs` — Testcontainers.Redis fixture (new — Phase 5 will reuse).
- [ ] Glickman's worked-example seed data in `tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json` (deterministic input + expected output).
- [ ] `src/GameKit.Rankings/GameKit.Rankings.csproj` — populated csproj (currently empty stub from Phase 1).

*(All test projects need to be created; no existing test infrastructure covers Rankings.)*

## Security Domain

`security_enforcement` is not explicitly set to `false` in `.planning/config.json` — treat as enabled.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes | Service-token bearer scheme (custom `AuthenticationHandler<TOptions>` against SHA-256-hashed `service_tokens` table). Player JWT scheme (Phase 2) reused for GDPR-export player path. Admin cookie scheme (Phase 3) reused for rank-adjust + end-season + GDPR-export admin path. |
| V3 Session Management | yes | Phase-2 refresh-token discipline already covers player sessions; service tokens are long-lived (no rotation) per CONTEXT D-06 — operators rotate manually via CLI revoke + issue. |
| V4 Access Control | yes | `RequireAuthorization(AdminPolicies.Superadmin)` on rank-adjust + end-season + admin GDPR export. `RequireAuthorization("GameKitServiceToken")` on session-complete. `{id}` claim match on player GDPR export. |
| V5 Input Validation | yes | FluentValidation 12.1.1 validators for every request DTO: `SessionCompleteRequestValidator`, `RankAdjustRequestValidator`, `EndSeasonRequestValidator`. `result` enum constrained to `win|loss|draw|forfeit`. `new_rating` bounded `[100, 4000]`. `reason` length `[3, 512]`. |
| V6 Cryptography | yes | SHA-256 for service-token storage (never raw). `RandomNumberGenerator.Fill(Span<byte>)` for raw-token generation. Never roll-our-own. |
| V7 Error Handling | yes | All exceptions converted to ProblemDetails 4xx/5xx; never leak stack traces; idempotency-key reuse maps to 409, not 500. |
| V8 Data Protection | yes | GDPR export NEVER includes `password_hash`, `refresh_token.TokenHash`, or raw external_ids. Identities use `external_id_hash` only. 25 MB response cap prevents DoS-by-export. |
| V9 Communications | not directly | TLS handled by host (operator's reverse proxy or `app.UseHttpsRedirection()`). |
| V13 API + Web Services | yes | Idempotency-Key header per Stripe convention; rate-limit `gamekit:sessions:complete` 300/min/token; antiforgery on admin mutations. |

### Known Threat Patterns for ASP.NET Core 10 / EF Core 10 / Postgres

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| SQL injection via session-complete `Idempotency-Key` header | Tampering | EF Core parameterizes; no raw SQL on this path. The raw-SQL FK constraint in the migration is a one-shot DDL, not user-influenced. |
| Service-token theft from disk / env var | Spoofing | Tokens never echoed back via API; SHA-256 hashed at rest. Operators must transport the raw token securely (out-of-band — CLI prints once). |
| Replay of completed session-complete request | Tampering | `Idempotency-Key` + state-conditional UPDATE — duplicates are cache-served, never re-applied (D-07, D-08). |
| GDPR-export DoS (request huge player payload) | Denial of Service | 25 MB response cap (D-18). Rate-limit per JWT subject (TBD by planner — not in CONTEXT, recommend follow Phase-2 refresh limit of 60/min). |
| Cross-player GDPR export | Information Disclosure | Player path enforces `{id} == jwt.sub`; admin path enforces Superadmin policy + audit-log row. |
| Rank-adjust without audit row | Repudiation | Single SERIALIZABLE transaction wraps UPDATE + `IAdminAuditWriter.WriteAsync` — Phase-3 D-17 pattern. Atomicity test (SC#6) anchors. |
| Ticker double-firing under network partition | Tampering (rating corruption) | Redis distributed lock with 90s TTL (D-03); `LockExtend` failure check (Pitfall §6). |
| Glicko-2 input crafted to cause infinite-loop in volatility convergence | Denial of Service | MaartenStaa's port uses Glickman's ε = 0.000001 with bounded iteration (max ~30 iters per player); custom `IRankingAlgorithm` impls inherit a documented contract requirement. |
| Service-token brute-force | Spoofing | 32 bytes of CSRNG entropy = 256 bits; rate-limit 300/min/token; no enumeration endpoint. |
| Idempotency-Key reuse across sessions | Information Disclosure (potentially) | The dedup key is `{session_id, idempotency_key}` — same idempotency_key on a different session_id is independent. No cross-session correlation. |

## Sources

### Primary (HIGH confidence)
- `/home/noah/Desktop/projects/gamekit/CLAUDE.md` — project-wide invariants (read 2026-05-15).
- `/home/noah/Desktop/projects/gamekit/.planning/REQUIREMENTS.md` — RANK-01 through RANK-14 verbatim (read 2026-05-15).
- `/home/noah/Desktop/projects/gamekit/.planning/ROADMAP.md` — Phase 4 goal + 6 Success Criteria.
- `/home/noah/Desktop/projects/gamekit/.planning/phases/04-rankings-sessions-gdpr/04-CONTEXT.md` — 23 user-locked decisions.
- `/home/noah/Desktop/projects/gamekit/Directory.Packages.props` — current package pins (read 2026-05-15).
- `src/GameKit.Core/Entities/GameSession.cs` — already has `LadderId Guid?` reserved for Phase 4 (lines 21-22).
- `src/GameKit.Core/Entities/SessionParticipant.cs` — already has `RatingBefore/RatingAfter/RatingDelta double?` (lines 39-45).
- `src/GameKit.Auth/Services/RefreshTokenService.cs:280-291` — `Sha256Hex` + `GenerateRaw` patterns to mirror for service tokens.
- `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` — full per-package migration pattern.
- `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` — IHostedService pattern for Rankings migration.
- `src/GameKit.Admin.UI/Services/AdminAuditActions.cs` — `PlayerRankAdjust = "admin.player.rank_adjust"` already exists; new `LadderEndSeason` constant lands here.
- `src/GameKit.Admin.UI/Services/IAdminAuditWriter.cs` + `AdminAuditWriter.cs` — interface contract to reuse for rank-adjust + end-season + admin GDPR export.
- `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs` — palette verb registry; `rank-adjust` already registered (line 39), `end-season` needs adding.
- `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor:123-132` — switch arm where rank-adjust + end-season dialogs get wired.
- `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs` — Phase-3 CSRF filter to reuse (or DRY-clone — see Q4).
- `src/GameKit.Cli/Program.cs` + `src/GameKit.Cli/Commands/AdminCreateCommand.cs` — Spectre.Console.Cli pattern for `service-token` verbs.
- `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs` — fixed-window rate-limit pattern for `gamekit:sessions:complete`.
- [https://glicko.net/glicko/glicko2.pdf](https://glicko.net/glicko/glicko2.pdf) — Glickman 2012, the canonical Glicko-2 spec + worked example.

### Secondary (MEDIUM confidence — WebSearch verified against an authoritative anchor)
- [https://github.com/MaartenStaa/glicko2-csharp](https://github.com/MaartenStaa/glicko2-csharp) — vendor source. Repository structure (`Rating.cs`, `RatingCalculator.cs`, `RatingPeriodResults.cs`, `Result.cs`). Default constants extracted from `RatingCalculator.cs` (tau=0.75 default, NOT Glickman's 0.5 — Pitfall §2). **License variant (BSD-2 vs BSD-3) MUST be re-verified locally — see Pitfall §1.**
- [https://redis.io/docs/latest/develop/clients/patterns/distributed-locks/](https://redis.io/docs/latest/develop/clients/patterns/distributed-locks/) — `SET NX PX` algorithm + Lua-script-verified release.
- [https://leapcell.io/blog/implementing-distributed-locks-with-redis-delving-into-setnx-redlock-and-their-controversies](https://leapcell.io/blog/implementing-distributed-locks-with-redis-delving-into-setnx-redlock-and-their-controversies) — StackExchange.Redis `LockTake / LockExtend / LockRelease` wrapper analysis.
- [https://www.postgresql.org/docs/current/transaction-iso.html](https://www.postgresql.org/docs/current/transaction-iso.html) — official Postgres docs §13.2.2 on REPEATABLE READ snapshot semantics. Pitfall §5 anchor.
- [https://www.postgresql.org/docs/current/sql-set-transaction.html](https://www.postgresql.org/docs/current/sql-set-transaction.html) — `SET TRANSACTION READ ONLY` for the GDPR export.
- [https://docs.stripe.com/api/idempotent_requests](https://docs.stripe.com/api/idempotent_requests) — 24h TTL + body-hash precedent. Pitfall §8.
- [https://stripe.com/blog/idempotency](https://stripe.com/blog/idempotency) — body canonicalization vs raw-bytes hash.
- [https://www.rfc-editor.org/rfc/rfc8785](https://www.rfc-editor.org/rfc/rfc8785) — JCS spec (informational reference for Open Q5).
- [https://www.npgsql.org/efcore/mapping/json.html](https://www.npgsql.org/efcore/mapping/json.html) — JsonDocument / jsonb mapping in EF Core 10 + Npgsql 10.0.1.
- [https://www.pollydocs.org/strategies/retry.html](https://www.pollydocs.org/strategies/retry.html) — Polly v8 `ResiliencePipelineBuilder.AddRetry` with `UseJitter = true`.

### Tertiary (LOW confidence — single WebSearch, marked for validation)
- The exact MudBlazor dialog component shape for `RankAdjustDialog.razor` / `EndSeasonDialog.razor` — recommend looking at `BanPlayerDialog.razor` (Phase 3) for the canonical pattern. Not researched in depth here because the dialog wiring is mechanical pattern-following, not a research question.

## Metadata

**Confidence breakdown:**
- **Standard stack:** HIGH — every dependency already pinned + already exercised in earlier phases.
- **Per-package migration:** HIGH — Phase-1 and Phase-2 ran this twice; Phase-3 ran it once more for `__ef_migrations_admin`. Pattern is mature.
- **Glicko-2 vendoring:** MEDIUM — algorithm + library are well-understood, but BSD-2/BSD-3 license disambiguation pending. Mitigation: Wave 0 license-verify task.
- **Service-token auth scheme:** HIGH — mirrors Phase-2 refresh-token discipline verbatim; SHA-256 + random 32 bytes is textbook.
- **Idempotency canonicalization:** MEDIUM — Open Q5 needs planner decision; the recommendation (stable-sort + STJ) is operationally sound but not RFC-8785-compliant.
- **REPEATABLE READ for GDPR export:** HIGH — `IsolationLevel.RepeatableRead` is standard EF Core; Npgsql honors it via Postgres MVCC.
- **Redis distributed lock:** HIGH — `IDatabase.LockTake` is the documented primitive; Polly v8 wraps the retry.
- **Ticker safety + leader election:** HIGH conceptually, MEDIUM on the exact `LockExtend` cadence — recommend the planner pick "every 30s mid-tick" so a 90s TTL gives two renewal chances before expiry.
- **Pitfalls catalogue:** HIGH — every entry is anchored in either a Phase-1/2/3 lesson, a CONTEXT.md decision, or an official-source citation.

**Research date:** 2026-05-15.
**Valid until:** 2026-06-14 (30 days for stable stack); license-verification subtask of Pitfall §1 MUST close before any Glicko-2 source is committed.

---

## RESEARCH COMPLETE

This RESEARCH.md is consumed by the Phase-4 planner. The planner can produce 6–8 plans against the established Phase-2 / Phase-3 plan cadence:

1. **04-01:** Wave 0 — `GameKit.Rankings.Tests` + `GameKit.Rankings.Integration.Tests` csprojs; `RankingsFixture` + `RedisFixture` test infrastructure; Glickman fixture JSON; `RankingsTestModelCustomizer` (mirror of `AdminCliModelCustomizer`); license-verify task for MaartenStaa Glicko-2 (Pitfall §1 close).
2. **04-02:** Entities + EF configurations + `RankingsModelBuilderExtension` + `RankingsDesignTimeDbContextFactory` + `RankingsInitialCreate` migration + `RankingsMigrationHostedService` + `RankingsAdvisoryLockKeyTests` live-verification + raw-SQL FK to `game_sessions.ladder_id`.
3. **04-03:** Vendored Glicko-2 source under GameKit-internal namespace + `IRankingAlgorithm.Apply(state, batch)` contract + `Glicko2Algorithm` adapter + Glickman worked-example regression test + RANK-04/05/06 contract tests.
4. **04-04:** `GameKitRankingsOptions` + `IGameKitRankingsBuilder.AddLadder(...)` + `StartupLadderUpserter : IHostedService` + `services.AddRankings(...)` fluent registration + `ServiceTokenAuthenticationHandler` + `service-token` CLI verbs (`issue`/`revoke`/`list`).
5. **04-05:** Session-complete endpoint in `GameKit.Core/Http` + `IPostSessionCompleteHandler` port + `PendingRatingUpdatesAdapter` impl in Rankings + `CanonicalJsonHasher` + `IdempotencyKeyEndpointFilter` + `pending_rating_updates` + `session_complete_idempotency` tables + rate-limit policy `gamekit:sessions:complete` + `SessionCompleteIdempotencyTests` (SC#2).
6. **04-06:** `RankingsTickerService : BackgroundService` + Redis lock leader election + per-ladder batch drain + `IdempotencyCleanupService` + 1000-match convergence test (SC#1).
7. **04-07:** `EndSeasonService` (SoftRegress / HardReset / ArchiveOnly) + `LadderSeasons` + `SeasonRankArchive` + `ILeaderboardService` (TopAsync + AroundAsync) + admin `end-season` palette verb wiring (`MainLayout.OpenDialog` switch arm + `EndSeasonDialog.razor` + `LadderEndSeason` audit action + sentence template) + `SeasonArchiveLeaderboardTests` (SC#4).
8. **04-08:** GDPR export REPEATABLE READ handler + player & admin endpoints + 25 MB cap + `GdprExportContractTests` (SC#5) + `RankAdjustService` SERIALIZABLE tx + admin endpoint + `RankAdjustDialog.razor` wiring (`MainLayout.OpenDialog` switch arm) + `AdminRankAdjustTransactionTests` (SC#6) + sample-app boot integration.

**Open questions for planner to lock:** Q1 (Glicko-2 license — Wave 0 verify task), Q2 (`pending_rating_updates` columns — see recommended denormalized shape), Q3 (service-token handler lives in Rankings), Q4 (duplicate the antiforgery filter into Rankings to preserve package boundary), Q5 (stable-sort + STJ for response_hash, not full RFC 8785), Q6 (`IPostSessionCompleteHandler` added in Phase 4, not retroactively in Phase 1).

**Sources cited above; the planner reads this document end-to-end and creates per-plan files under `.planning/phases/04-rankings-sessions-gdpr/04-NN-PLAN.md`.**
