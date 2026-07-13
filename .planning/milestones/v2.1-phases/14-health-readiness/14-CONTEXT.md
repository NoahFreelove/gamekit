# Phase 14: Health & Readiness - Context

**Gathered:** 2026-06-14
**Status:** Ready for planning
**Mode:** `--auto` (gray areas auto-resolved to recommended defaults; review before planning)

<domain>
## Phase Boundary

Deliver Kubernetes-correct **liveness vs readiness** for every GameKit deployment:
- `AddGameKitHealthChecks()` + `MapGameKitHealth()` in `GameKit.Core` exposing separate
  `/health/live` and `/health/ready` endpoints with orchestrator-compatible JSON.
- **Liveness = process-only** (never touches Postgres/Redis — survives a DB/Redis blip).
- **Readiness = dependency-gated**: Postgres (`SELECT 1`), Redis (`PING`, when configured),
  and a per-package "all migrations applied" check via a new `IMigrationReadinessReporter`.
- **Three-state** health (`Healthy`/`Degraded`/`Unhealthy`) where a follower replica (not
  holding the matchmaking leader lock) reports `Degraded`, not `Unhealthy`.
- **Leader-lock probe** reports which replica holds the matchmaking lock + TTL remaining.
- **PII/infra-safe payloads** — component name + status + human description only.
- **Admin.UI delegation** — the existing health panel + `HealthProbeService` consume Core's
  `HealthCheckService` instead of duplicating the Postgres/Redis probe logic.

Maps to HLTH-01, HLTH-02, HLTH-03, HLTH-04, HLTH-05, HLTH-06.

**Out of scope:** per-package OTel spans/metrics (Phase 15); fleet-wide multi-replica
correctness under leader churn / SIGTERM drain / request storms (Phase 16); K8s manifest
YAML examples + probe-tuning docs (docs phase / DOCS-04). The "six migration reporters"
are Core + Auth + Admin.UI + Rankings + Matchmaking + Lobby — **Presence has no migrations**,
so it gets no reporter.

</domain>

<decisions>
## Implementation Decisions

### Health-check framework (HLTH-01, HLTH-03)
- **D-01: Built-in ASP.NET Core HealthChecks — not Xabaril, not hand-rolled.** Use
  `Microsoft.Extensions.Diagnostics.HealthChecks` (`IHealthCheck`, `AddHealthChecks()`,
  `MapHealthChecks()`) — it ships in the `Microsoft.AspNetCore.App` shared framework, so
  **zero new NuGet pin** (honors "install only what you need"). The framework's
  `HealthStatus.Healthy/Degraded/Unhealthy` maps 1:1 to HLTH-03's three states, and
  `HealthCheckService` is the single source of truth HLTH-06 names explicitly. The community
  `AspNetCore.HealthChecks.*` (Xabaril) packages are rejected — extra third-party deps for
  what the BCL already provides.
- **D-02: New Core surface.** `AddGameKitHealthChecks()` registers the Postgres + migration
  aggregate checks and returns the `IHealthChecksBuilder` so sibling packages can add their
  own checks (mirrors the opt-in shape of `AddGameKitObservability()`). `MapGameKitHealth()`
  (extension on `IEndpointRouteBuilder`) maps `/health/live` + `/health/ready`. Both
  endpoints are **anonymous, excluded from OpenAPI, and bypass rate limiting** — orchestrator
  probes must never be throttled or authenticated.

### Live vs ready separation (HLTH-01 — success criterion #1)
- **D-03: Tag-based filtering on one registration.** `/health/live` maps with
  `Predicate = _ => false` → **no checks execute**, returns 200 whenever the process is alive
  (satisfies "`/health/live` returns 200 even when Postgres is stopped"). `/health/ready`
  maps with `Predicate = c => c.Tags.Contains("ready")`; every dependency check is tagged
  `"ready"`. One registration, two filtered endpoints — the idiomatic ASP.NET Core pattern.
- **D-04: HTTP status mapping.** `Healthy` → 200, `Degraded` → **200** (stays in rotation),
  `Unhealthy` → 503. The Degraded→200 rule is what makes the leader-lock and Redis-blip
  stories work without draining the pod.

### Migration readiness (HLTH-02 — success criterion #1)
- **D-05: `IMigrationReadinessReporter` in `GameKit.Core`.** One implementation per migration
  site — **six total**: Core, Auth, Admin.UI, Rankings, Matchmaking, Lobby. Each registered
  as an enumerable singleton (`services.AddSingleton<IMigrationReadinessReporter, …>()`) by
  its package's existing `Add*` builder, alongside the existing `*MigrationHostedService`.
- **D-06: Single Core aggregate check `"migrations"` (tagged `ready`).** Resolves
  `IEnumerable<IMigrationReadinessReporter>`; returns `Unhealthy` (→503) while ANY reports
  pending, flips to `Healthy` once all six report applied (satisfies "503 while any package's
  migrations are pending, then 200 once all six report ready").
- **D-07: Reporters check pending migrations, not their own apply.** Each reporter decides
  readiness by `db.Database.GetPendingMigrationsAsync()` being empty against its own package's
  migration-history table — **not** by remembering whether its own HostedService applied
  them. This is correct under both `AutoMigrate=true` (apply → pass) and `AutoMigrate=false`
  (operator runs `gamekit migrate` out-of-band; the pod gates until that lands). Once a
  reporter observes "all applied" it **latches** that result (migrations never become
  un-applied at runtime) so steady-state probes don't re-query Postgres on every poll.

### Dependency gating on Core-only installs (HLTH-02 — success criterion #2)
- **D-08: Postgres is always a hard gate.** The Postgres readiness check (`SELECT 1`, ~2s
  command timeout) is always registered + tagged `ready`; failure → `Unhealthy` (503).
- **D-09: Redis gate is conditional on Redis being installed.** Register the Redis `PING`
  readiness check **only when an `IConnectionMultiplexer` is present in DI** (matchmaking /
  presence / lobby installed). On a Core-only install Redis is absent from the ready set, so
  its absence never blocks readiness (satisfies criterion #2). When Redis **is** configured, a
  failed `PING` → `Unhealthy` (503), per HLTH-02's "a replica stays out of rotation until
  every dependency … passes." (The leader-lock check, D-10, is the **only** Redis-touching
  check that is Degraded-rather-than-Unhealthy.) ⚠ See Deferred: shared-Redis fleet-drain is a
  Phase-16 concern, not re-litigated here.

### Leader-lock probe (HLTH-03, HLTH-04 — success criterion #3)
- **D-10: Ships in `GameKit.Matchmaking`, self-registers, Degraded-only.** A dedicated
  `"matchmaking-leader"` readiness check lives in `GameKit.Matchmaking` (NOT Core — preserves
  the no-reverse-dependency architecture) and adds itself to the shared health-checks builder
  when matchmaking is installed. Reports `Healthy` when this replica holds the lock,
  `Degraded` (never `Unhealthy`) when it does not — so a follower stays in rotation
  (criterion #3: "Degraded, not Unhealthy").
- **D-11: Non-acquiring read of holder + TTL.** The probe must read the lock **without taking
  it**. Add a read-only query to the lease surface — e.g. `IMatchmakerLease.QueryLeaseAsync()`
  returning `{ holder InstanceId, TTL remaining }` via Redis `GET` + `PTTL` (today
  `RedisMatchmakerLease` exposes only `TryAcquireLeaseAsync`/`ReleaseLeaseAsync`). The check's
  structured data carries holder id + TTL so operators see which replica leads and how long
  the lease lasts (criterion #3).

### Payload PII / infra-safety (HLTH-05 — success criterion #4)
- **D-12: Custom `ResponseWriter`, whitelist fields only.** Both endpoints emit
  `{ status, checks: [ { name, status, description } ] }` and **nothing else** — explicitly
  omit `HealthReportEntry.Exception`, `.Data`, `.Tags`. The default writer would serialize
  exception text (Npgsql exceptions embed `host:port`) — forbidden. Each check authors a
  human-readable, infra-free `description` (e.g. "database unreachable", "redis ping failed",
  "3 of 6 migration sets pending") — never a raw exception, connection string, host, or
  credential.
- **D-13: Replica identity ≠ infrastructure host.** The leader-lock check surfaces the lease
  `InstanceId` (`MachineName:Guid`, typically the K8s pod name) because HLTH-04 *requires*
  identifying the replica. This is the replica's **own self-id**, categorically different from
  the *dependency hosts/connection strings* HLTH-05 protects — flagged here so the
  security-phase auditor reads it as intentional, not a contradiction.
- **D-14: Dedicated leak test.** A test asserts no health response body contains
  connection-string fragments, the configured Postgres/Redis host substrings, ports, or
  `Password=`/`Host=` patterns. Phase 13's PII Roslyn analyzer guards span **tags**, not
  health **payloads** — this is a separate runtime/test guard.

### Admin.UI delegation (HLTH-06 — success criterion #5)
- **D-15: `HealthProbeService` becomes a thin adapter over `HealthCheckService`.** Refactor it
  to call `HealthCheckService.CheckHealthAsync()` and project the entries into the existing
  `HealthReport`/`HealthTile` view contract — **delete** the duplicated `ProbePostgresAsync` /
  `ProbeRedisAsync` (`SELECT 1` + `PingAsync` now live once in Core). Status map:
  `Healthy→"OK"`, `Degraded→"Degraded"`, `Unhealthy→"Down"`; tile `Detail` from the check
  description, latency from check duration.
- **D-16: Keep the Admin-only error-rate tile local.** "Error rate" (from
  `ErrorRateRingBuffer` / `IRedisErrorRateCounter`) is an Admin observability gauge, **not** a
  K8s readiness dependency — it does NOT join `/health/ready`. The Admin Health page composes
  two Core-sourced tiles (Postgres, Redis) + the Admin-local error-rate tile.

### Claude's Discretion
- Exact namespace/file layout for the new Core health types (`GameKit.Core.Health` vs
  `.Data`); whether `AddGameKitHealthChecks()` extends `IGameKitBuilder` or `IServiceCollection`;
  the precise check-name strings and tag constant; whether the leader read uses
  `LockQueryAsync` vs raw `GET`+`PTTL`; the migration-latch caching mechanism; and how sibling
  packages discover the shared `IHealthChecksBuilder` (explicit `Add*HealthChecks()` inside
  each package's `Add*` builder vs a Scrutor scan). Researcher/planner decide.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase scope & requirements
- `.planning/ROADMAP.md` — Phase 14 section (goal + 5 success criteria)
- `.planning/REQUIREMENTS.md` — HLTH-01 … HLTH-06 (lines 28–33)
- `.planning/PROJECT.md` — v2.1 "Operability & Hardening" milestone goal ("health & readiness
  … liveness vs. readiness endpoints with Postgres/Redis/migration dependency probes +
  startup-gating") and the "not yet public" north star
- `.planning/phases/13-observability-foundations/13-CONTEXT.md` — prior phase; the opt-in
  `AddGameKitObservability()` shape that `AddGameKitHealthChecks()` should mirror, and the
  PII-gate scope boundary (analyzer guards span tags, not health payloads)

### Code refactored / extended this phase
- `src/GameKit.Admin.UI/Services/HealthProbeService.cs` — the duplicated three-probe logic to
  replace with delegation to `HealthCheckService` (D-15); current `OK`/`Degraded`/`Down` tile
  model to preserve as the view contract
- `src/GameKit.Admin.UI/Services/IHealthProbeService.cs` — the interface the adapter keeps
- `src/GameKit.Admin.UI/Http/Contracts/HealthReport.cs` — `HealthReport` + `HealthTile` view
  records consumed by the Blazor panel
- `src/GameKit.Admin.UI/Components/Pages/Health.razor` + `Components/Shared/HealthTileView.razor`
  — the panel that renders the tiles (HLTH-06 criterion #5)

### Core integration points
- `src/GameKit.Core/Builder/IGameKitBuilder.cs` — builder the new `AddGameKitHealthChecks()`
  extends (`Services` + `Options`)
- `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` — `UseGameKit()` applies
  Core migrations synchronously before Kestrel; `MapGameKit()` is the sibling-map pattern that
  `MapGameKitHealth()` mirrors
- `src/GameKit.Core/Data/MigrationRunner.cs` — advisory-lock migrate flow; readiness reporters
  query pending migrations against the same per-package history tables it migrates
- `src/GameKit.Core/Data/GameKitMigrationConstants.cs` + each package's
  `*MigrationConstants.cs` (Auth/Admin.UI/Rankings/Matchmaking/Lobby) — per-package
  `MigrationsHistoryTable` + schema the six reporters target

### Migration hosted services (the six reporter hosts)
- `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` (Core, via `UseGameKit`)
- `src/GameKit.Auth/Data/AuthMigrationHostedService.cs`
- `src/GameKit.Admin.UI/Data/AdminMigrationHostedService.cs`
- `src/GameKit.Rankings/Data/RankingsMigrationHostedService.cs`
- `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs`
- `src/GameKit.Lobby/Data/LobbyMigrationHostedService.cs`

### Leader-lock surface
- `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` +
  `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` — add the non-acquiring
  `QueryLeaseAsync()` here; `InstanceId` (`MachineName:Guid`) is the holder identity (D-11/D-13)
- `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` (`MatcherLock`) +
  `GameKitMatchmakingOptions.Ticker` (`LockKey`, `LockTtlSeconds`) — lock key + TTL the probe reads

### Wiring reference
- `samples/TicTacToeDuel/Program.cs` — where `AddGameKitHealthChecks()` and `MapGameKitHealth()`
  get wired; strict middleware order (`UseRouting → UseRateLimiter → UseGameKitAuth →
  UseGameKit → UseGameKitAdmin`); health endpoints must sit outside auth + rate-limit

### Deps
- `Directory.Packages.props` — confirm NO health-check pin is added (built-in shared-framework
  HealthChecks per D-01)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`HealthProbeService` probe bodies** — `ProbePostgresAsync` (`SELECT 1`, 2s `CommandTimeout`,
  exception→Down) and `ProbeRedisAsync` (`IDatabase.PingAsync`, null-multiplexer→"not
  configured") are the exact probe logic to lift into Core `IHealthCheck`s, then delete from
  Admin.UI (D-15). The `IConnectionMultiplexer? redis = null` optional-dependency pattern is
  the precedent for D-09's "register Redis check only when configured."
- **`MigrationRunner.MigrateWithLockAsync` + per-package `*MigrationConstants`** — give each
  reporter the history-table name + schema to query `GetPendingMigrationsAsync()` against.
- **`RedisMatchmakerLease.InstanceId`** (`MachineName:Guid`) — the fencing-token identity the
  leader probe surfaces; the Lua-verified `LockTake/LockRelease` pattern is the model for the
  read-only `QueryLeaseAsync` addition.

### Established Patterns
- **Opt-in registration mirroring `AddGameKitObservability()`** — Core exposes an `Add*`
  extension that registers names/checks without forcing anything on consumers who skip it.
- **Composable, no reverse dependencies** — Core never references Matchmaking. The leader-lock
  check therefore ships in Matchmaking and self-registers into the shared builder (D-10), the
  same way sibling packages extend `IGameKitBuilder` and add their own `Map*` endpoints.
- **Per-package migration boundary (PITFALLS #3)** — each package owns its own migrations +
  history table + advisory-lock key; the reporter design follows this one-reporter-per-package
  grain (six reporters, Presence excluded).
- **`*MigrationHostedService` as `IHostedService`** — the natural home to also register/flip
  each package's `IMigrationReadinessReporter`.

### Integration Points
- `AddGameKitHealthChecks()` on the Core builder; `MapGameKitHealth()` on
  `IEndpointRouteBuilder`, wired in `samples/TicTacToeDuel/Program.cs` outside auth + rate
  limiting.
- Admin.UI `HealthProbeService` → resolves and delegates to Core `HealthCheckService`.
- Matchmaking package self-registers its leader-lock check into the shared
  `IHealthChecksBuilder` from its `AddMatchmaking` builder.

</code_context>

<specifics>
## Specific Ideas

- **Liveness is process-only** — `/health/live` runs zero checks (`Predicate = _ => false`),
  so a stopped Postgres or a Redis blip never fails liveness (criterion #1, verbatim).
- **Degraded → HTTP 200** is the load-bearing rule: a follower replica (no leader lock) and a
  Redis-blip-on-the-leader-probe both stay in the load-balancer rotation.
- **Six reporters, not seven** — Core, Auth, Admin.UI, Rankings, Matchmaking, Lobby. Presence
  has no `*MigrationHostedService` / migrations, so it contributes no reporter. If Presence
  ever gains a migration, add a seventh reporter then.
- **Health payloads are anonymous** — probes are unauthenticated, so the payload is the
  attack surface for HLTH-05. Hence the whitelist `ResponseWriter` (D-12) + leak test (D-14).

</specifics>

<deferred>
## Deferred Ideas

- **Shared-Redis fleet-drain tradeoff (→ Phase 16, Multi-Replica Hardening).** D-09 follows
  HLTH-02 literally: a configured-but-unreachable Redis flips readiness to 503. Across a fleet
  sharing one Redis, a Redis outage would drain every replica at once (including the
  Postgres-only auth/session traffic that could still serve). Whether to soften Redis-down to
  `Degraded` for non-matchmaking replicas is a multi-replica-correctness decision — left to
  Phase 16, not re-litigated here.
- **K8s manifest + probe-tuning docs (→ docs phase / DOCS-04).** Example
  `livenessProbe`/`readinessProbe`/`startupProbe` YAML, and the `startupProbe` recommendation
  for deployments with slow migration sets, belong in the upgrade/ops docs, not this phase's
  code.
- **A combined `/health` aggregate endpoint** — not required; `/health/live` + `/health/ready`
  cover the orchestrator contract. Out of scope.
- **Per-package OTel spans/metrics (→ Phase 15)** and **leader-churn / SIGTERM-drain /
  request-storm correctness (→ Phase 16)** — explicitly downstream.

### Reviewed Todos (not folded)
None — `todo.match-phase 14` returned zero matches.

</deferred>

---

*Phase: 14-health-readiness*
*Context gathered: 2026-06-14*
