# Architecture Patterns

**Domain:** v2.1 — Operability & Hardening integration into existing GameKit v2.0 codebase.
**Researched:** 2026-06-08
**Confidence:** HIGH — all claims grounded in verified source files; no external sources needed.

---

## Existing Architecture (baseline — do not redesign)

### Package dependency graph (current, v2.0 shipped)

```
GameKit.Core                     (IPlayerRatingProvider, IClock, IIdGenerator, IModelBuilderExtension)
    └─ GameKit.Auth              (IOAuthProvider, IPasswordHasher; migration -298890956)
        ├─ GameKit.Auth.Argon2   (stateless sibling; no migration)
        ├─ GameKit.Auth.Google   (stateless sibling; no migration)
        ├─ GameKit.Auth.Apple    (stateless sibling; no migration)
        ├─ GameKit.Auth.Epic     (stateless sibling; no migration)
        └─ GameKit.Admin.UI      (Blazor Server; cookie auth "GameKitAdmin"; migration -2101739634)
GameKit.Rankings                 (IPlayerRatingProvider impl; migration -156812172)
    └─ GameKit.Matchmaking       (MatchmakerLeaseHelper; ticker+reconciler; migration 388956820)
GameKit.Presence                 (stateless; IConnectionMultiplexer; ISessionLifecycleObserver)
GameKit.Lobby                    (SignalR hub; Redis backplane; migration 12178347)
GameKit.OpenApi                  (stateless docs shim)
GameKit.Cli                      (Spectre.Console; dev tooling)
```

Runtime dep direction: downward only. Design-time-only ProjectReferences (for migration exclusion lists) never create runtime coupling.

### Telemetry seams already present (v2.0)

These are NOT hypothetical — they are in the actual code:

| Package | Source/Meter | Type | Opt-in key |
|---------|-------------|------|------------|
| `GameKit.Matchmaking` | `MatchmakingActivitySource` | `ActivitySource("GameKit.Matchmaking.Ticker")` | `AddSource("GameKit.Matchmaking.Ticker")` |
| `GameKit.Matchmaking` | `MatchmakingMeter` | `Meter("GameKit.Matchmaking")` | `AddMeter("GameKit.Matchmaking")` |
| `GameKit.Rankings` | inline `_activitySource` in `RankingsTickerService` | `ActivitySource("GameKit.Rankings.Ticker")` | `AddSource("GameKit.Rankings.Ticker")` |

The Matchmaking `Telemetry/` subfolder exists and is the correct pattern. Rankings inlines its `ActivitySource` directly in the ticker class — inconsistent with Matchmaking. v2.1 should normalize this.

### Leader-election seams already present (v2.0)

Three independent LeaseHelper classes exist with duplicate logic:

| Class | Package | Lock key | LockTtlSeconds |
|-------|---------|----------|----------------|
| `RankingsTickerLeaseHelper` | Rankings | `gamekit:rankings:ticker:lease` | from `GameKitRankingsOptions.Ticker.LockTtlSeconds` |
| `RankDecayLeaseHelper` | Rankings | `gamekit:rankings:decay:lease` | from `GameKitRankingsOptions.Decay.LockTtlSeconds` |
| `MatchmakerLeaseHelper` implements `IMatchmakerLease` | Matchmaking | `gamekit:matchmaking:matcher:lock` | from `GameKitMatchmakingOptions.Ticker.LockTtlSeconds` |

All three use `LockTakeAsync / LockExtendAsync / LockReleaseAsync` (StackExchange.Redis Lua-script-verified). All use `MachineName:Guid` fencing tokens. Pattern is correct. Problem: the hardening logic (renew-or-bail, SIGTERM drain, churn edge cases) is duplicated and will drift.

### Admin health probes already present (v2.0)

`HealthProbeService` in `GameKit.Admin.UI` runs three probes:
1. Postgres `SELECT 1` with 2-second timeout
2. Redis `PingAsync()`
3. `ErrorRateRingBuffer` / `IRedisErrorRateCounter` (cross-replica error count)

The `IRedisErrorRateCounter` + `RedisErrorRateCounter` (Phase 12 multi-replica Admin) is already in the codebase — the Admin health panel already aggregates cross-replica error counts. This is the exact machinery that ASP.NET Core health checks would duplicate. Do not introduce `AddHealthChecks()` as a fourth probe path.

---

## Decision 1: Package Placement for Observability + Health

### The question

Should OTel conventions + `/health/live` + `/health/ready` live in `GameKit.Core` or a new `GameKit.Diagnostics` package?

### Decision: Extend GameKit.Core. Do NOT create GameKit.Diagnostics.

**Rationale:**

1. **"Install only what you need" argues AGAINST a new package for core infrastructure.** Observability and health endpoints are operational necessities, not optional features. A consumer who installs `GameKit.Core` and nothing else still needs `/health/live` (Kubernetes liveness probe) and the ability to opt into OTel. Requiring them to also install `GameKit.Diagnostics` adds a package they cannot skip.

2. **The seam already exists in Core.** `GameKit.Core` already references `OpenTelemetry` + `OpenTelemetry.Extensions.Hosting` (CLAUDE.md STACK.md). `ActivitySource` and `Meter` usage is already opt-in pattern-matched to what ASP.NET Core and EF Core do. Adding a thin builder extension (`AddGameKitObservability()` + `MapGameKitHealth()`) to Core is a minimal, additive change.

3. **Avoiding a new package avoids a new migration boundary, new NuGet package, new version-train entry.** The coordinated release train already has 10+ packages. A diagnostics package adds zero game-services value and introduces another package consumers must pin.

4. **The Admin health probes are the RIGHT model for the health endpoint** — but they live in the wrong layer (Admin.UI). The `/health/live` + `/health/ready` endpoints must be available to consumers who do NOT install Admin.UI (e.g. API-only installs). Moving the probe logic to Core (with Admin.UI calling into Core probes, not vice versa) is the correct refactor.

5. **Convention-only centralization, not a new package.** Naming conventions (`GameKit.*` source prefix, metric naming scheme) and the `AddGameKitObservability()` extension can live in Core. Each per-package `Telemetry/` class registers its own `ActivitySource`/`Meter` — no centralized class is needed for that.

**Outcome: NO new GameKit.Diagnostics package. All changes go into existing packages + sample app.**

### Specific placement decisions

| Concern | Where it lives | Why |
|---------|---------------|-----|
| `/health/live` + `/health/ready` endpoints | `GameKit.Core` — new `MapGameKitHealth()` in `CoreEndpointExtensions` | Must work without Admin.UI; Core owns `GameKitDbContext` (Postgres probe) |
| Postgres liveness probe | `GameKit.Core` — reuse existing `GameKitOptions.ConnectionString` | Core already has the connection string |
| Redis readiness probe | `GameKit.Core` — optional `IConnectionMultiplexer?` (already an optional dep in Admin.UI probes) | Core should not hard-depend on Redis; Admin.UI already resolved this correctly |
| Migration-applied readiness gate | `GameKit.Core` — new `IMigrationReadinessReporter` interface + per-package implementations | Core gates Kestrel on migration completion already (via `IHostedService` ordering); readiness endpoint queries the same state |
| OTel naming conventions | `GameKit.Core` — `GameKitTelemetry` static class with `SourceNamePrefix = "GameKit."` + semantic-convention constants | Zero-weight; prevents per-package drift |
| `AddGameKitObservability()` builder extension | `GameKit.Core` — opt-in extension on `IGameKitBuilder` | Returns `IGameKitBuilder` so callers chain it; wires `AddOpenTelemetry()` shortcut + registers known source names |
| Per-package `Telemetry/` classes | Each package keeps its own (Matchmaking already has `Telemetry/`; Rankings needs to be refactored out of inline) | Package boundary; each package names its own instruments |
| Grafana/Prometheus/Tempo docker-compose | `samples/TicTacToeDuel/` — new `docker-compose.observability.yml` | Sample app is the composition root; does not belong in `src/` |
| Load test harness | `tests/GameKit.LoadTests/` — new project, not shipped | Same pattern as existing `tests/` projects |
| Docs site generation | Repo root + `docs/` — static site from XML doc comments via DocFX or similar; build step only | Not a runtime package |

---

## Decision 2: Health Endpoint Architecture

### Liveness vs. Readiness — concrete distinction

**Liveness** (`/health/live`): Is the process alive and not deadlocked? Answer: always `200 OK` once the host is running. No probes needed. This is Kubernetes' "should I restart the pod?" signal.

**Readiness** (`/health/ready`): Is the process ready to serve traffic? Requires:
- All per-package migrations applied (the EF advisory-lock HostedServices have completed)
- Postgres reachable (same `SELECT 1` as Admin.UI)
- Redis reachable if any Redis-dependent package is installed (Matchmaking, Presence, Lobby, Admin.UI)

### Integration with existing migration HostedServices

The per-package migration HostedServices already gate Kestrel: they run inside `IHost.StartAsync` before the host accepts traffic. The problem is the readiness probe needs to know when ALL migration services across ALL installed packages have completed — not just one.

**Design: `IMigrationReadinessReporter` (new, in GameKit.Core)**

```csharp
// GameKit.Core
public interface IMigrationReadinessReporter
{
    /// <summary>Reports whether this package's migrations have been applied.</summary>
    string PackageName { get; }
    bool IsReady { get; }
}
```

Each migration HostedService implements `IMigrationReadinessReporter` and sets `IsReady = true` on successful migration completion. The readiness probe resolves `IEnumerable<IMigrationReadinessReporter>` and returns `503` until all reporters are `IsReady`. Empty enumerable = no migrations registered = ready immediately (Core-only install with no migration packages).

This uses the existing `IEnumerable<T>` resolution pattern already used for `ISessionLifecycleObserver` and `IModelBuilderExtension`.

### Startup-gating vs. readiness endpoint

The readiness endpoint is NOT a startup gate — it is a probe. The actual startup gate already works via HostedService ordering. The readiness endpoint reflects the same state for load balancer health checks. These are NOT the same thing and must not be conflated.

### Reuse of existing Admin.UI probes

The Admin health panel's `IHealthProbeService` + `HealthProbeService` will be refactored to delegate to the new Core-level probes for Postgres + Redis. Admin.UI keeps the error-rate ring buffer and `IRedisErrorRateCounter` (those are admin-specific). The Core probes focus on dependency connectivity only.

**Dependency direction stays clean:** Core does not depend on Admin.UI. Admin.UI imports Core probes. This is the correct direction.

---

## Decision 3: OTel Conventions and Cross-Package Naming

### Current state (v2.0, verified)

- Matchmaking: `ActivitySource("GameKit.Matchmaking.Ticker", "1.0.0")` + `Meter("GameKit.Matchmaking", "1.0.0")`
- Rankings: `ActivitySource("GameKit.Rankings.Ticker", "1.0.0")` (inlined in `RankingsTickerService`)
- Core: no ActivitySource/Meter yet (only referenced in CLAUDE.md as planned)
- Auth, Admin.UI, Presence, Lobby: no ActivitySource/Meter yet

### Naming convention to adopt for v2.1

Source names follow `GameKit.<Package>.<Component>` pattern. Meter names follow `GameKit.<Package>` pattern.

| Package | ActivitySource name | Meter name |
|---------|--------------------|-----------:|
| Core | `GameKit.Core` (HTTP handlers, session ops) | `GameKit.Core` |
| Auth | `GameKit.Auth` (login, token rotation) | `GameKit.Auth` |
| Rankings | `GameKit.Rankings.Ticker` (already exists) | `GameKit.Rankings` |
| Matchmaking | `GameKit.Matchmaking.Ticker` (already exists) | `GameKit.Matchmaking` (already exists) |
| Presence | `GameKit.Presence` (heartbeat, in-match flip) | `GameKit.Presence` |
| Lobby | `GameKit.Lobby` (join, ready-check, transition) | `GameKit.Lobby` |
| Admin.UI | `GameKit.Admin` (admin actions) | `GameKit.Admin` |

### Centralization mechanism

A `GameKitTelemetry` static class in `GameKit.Core` defines only string constants:

```csharp
// GameKit.Core/Telemetry/GameKitTelemetry.cs
public static class GameKitTelemetry
{
    public const string SourcePrefix = "GameKit.";
    // Semantic attribute keys (follow OpenTelemetry semantic conventions where applicable)
    public const string AttrPlayerId   = "gamekit.player.id";
    public const string AttrLadderId   = "gamekit.ladder.id";
    public const string AttrPoolName   = "gamekit.pool.name";
    public const string AttrPackage    = "gamekit.package";
}
```

Each package's `Telemetry/` class references these constants. The `ActivitySource` and `Meter` instances remain in each package (not centralized). This avoids a hard `using` dependency from all packages on a new central class — each package just adopts the naming convention.

### `AddGameKitObservability()` extension

Located in `GameKit.Core/Builder/GameKitServiceCollectionExtensions.Observability.cs`. Opt-in call by consumer in their `Program.cs`:

```csharp
builder.Services.AddGameKit(opts => {...})
    .AddGameKitObservability(otel => otel
        .WithTracing(t => t.AddSource("GameKit.*"))
        .WithMetrics(m => m.AddMeter("GameKit.*")));
```

Internally calls `services.AddOpenTelemetry()` + wires the provided `Action<OpenTelemetryBuilder>`. Does NOT hard-depend on any OTel exporter — the consumer decides exporters. This matches the "opt-in everything" constraint.

---

## Decision 4: Multi-Replica Hardening

### Current correctness (v2.0, verified from code)

The `MatchmakerLeaseHelper` already uses `LockTakeAsync / LockExtendAsync / LockReleaseAsync` with Lua-script-verified release. The `InstanceId = $"{MachineName}:{Guid.NewGuid()}"` is a correct fencing token. `RenewLeaseAsync` returns `false` when the lock expired — callers check this. This is CORRECT.

The Rankings ticker and decay use the identical pattern.

### What is still missing / incomplete

1. **Graceful drain on SIGTERM/`ApplicationStopping`.** The ticker `BackgroundService` has `ExecuteAsync(CancellationToken stoppingToken)`. When `stoppingToken` fires, the current tick should complete its current pool sweep, then release the lock and exit cleanly. The current `MatchmakerTickerService` accepts `stoppingToken` in the `PeriodicTimer` loop — on cancellation the loop exits. The question is whether in-progress pool sweeps respect the token. This needs verification and hardening.

2. **Lock TTL vs. work duration.** The `LockTtlSeconds` default is 90 seconds; a tick budget is `MaxIterationBudgetMs=50`. These are not in tension for normal operation. But the `RenewLeaseAsync` call happens once per tick (every 500ms) with a 90-second TTL extension. This means the lock auto-extends each tick. The hardening needed: verify that `RenewLeaseAsync` is called BEFORE the per-pool sweeps, not after, so a long-running pool sweep doesn't cause the lock to expire mid-work.

3. **Shared `ILeaseHelper` interface.** `RankingsTickerLeaseHelper`, `RankDecayLeaseHelper`, and `MatchmakerLeaseHelper` all implement the same logic with no shared interface. A common `ILeaderLease` interface in `GameKit.Core` enables:
   - Consistent test patterns
   - A single churn-proof implementation to audit rather than three
   - Clear documentation of the fencing-token contract in one place

4. **Idempotency keys.** Session completion is already idempotent (`IIdempotencyStore` in Core). Matchmaking ticket enqueue is idempotent (`23505` on `UNIQUE(player_id, ladder_id)` in open status). Rank drain is idempotent (advisory lock serializes; `applied_at` column tracks). The missing piece: lobby ready-check → matchmaking enqueue idempotency (what happens if the same `TryStartMatchmakingAsync` is called twice during a partition recovery?).

### Hardening design

**`ILeaderLease` in `GameKit.Core`** — new interface:

```csharp
// GameKit.Core/Services/ILeaderLease.cs
public interface ILeaderLease
{
    string InstanceId { get; }
    Task<bool> TryAcquireLeaseAsync(CancellationToken ct);
    Task<bool> RenewLeaseAsync(CancellationToken ct);
    Task ReleaseLeaseAsync(CancellationToken ct);
}
```

The three existing LeaseHelper classes implement this interface (additive change — no breaking change). `ILeaderLease` lives in Core but does NOT force Core to depend on Redis — it is a pure interface. The implementations live in their respective packages.

**SIGTERM drain contract** — the pattern every `BackgroundService` that holds a lease MUST follow:

```csharp
// In MatchmakerTickerService, RankingsTickerService, RankDecayBackgroundService
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await using var lease = ...;
    while (!stoppingToken.IsCancellationRequested)
    {
        using var tickCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        tickCts.CancelAfter(MaxTickBudget);          // enforce budget
        bool leaseHeld = await lease.TryAcquireLeaseAsync(stoppingToken);
        if (!leaseHeld) { await PeriodicTimer.WaitForNextTickAsync(stoppingToken); continue; }

        try   { await RunTickAsync(tickCts.Token); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        finally { await lease.ReleaseLeaseAsync(CancellationToken.None); }  // release even on SIGTERM
    }
}
```

The `ReleaseLeaseAsync` MUST use `CancellationToken.None` (not `stoppingToken`) on the finally path — otherwise it would fail to release on SIGTERM, leaving the lock until TTL expiry.

---

## Decision 5: Backup / Restore / DR + Migration Ops

### Where tooling lives

**`GameKit.Cli` (existing package, `Spectre.Console.Cli`)** — new subcommands:

```
gamekit db backup --connection <cs> --out <path>      # pg_dump wrapper
gamekit db restore --connection <cs> --in <path>      # pg_restore wrapper
gamekit db migrate --package <pkg> --dry-run           # migration dry-run
gamekit db status                                      # per-package migration applied/pending
```

Rationale: CLI is already the extensible operator-facing tool (`GameKit.Cli` uses `Spectre.Console.Cli`). Adding subcommands is additive. These commands do NOT need to ship as library code — they are operator tooling.

Redis backup is documented (not automated) — Redis persistence is already configured with `--appendonly yes --appendfsync everysec` + `--save` RDB snapshots in `docker-compose.yml`. The runbook documents `redis-cli SAVE` + copy `/data/dump.rdb`.

### Migration dry-run

The per-package advisory-lock + HostedService pattern makes dry-run straightforward:

1. Build the migration script via `EF Core migrations script --idempotent` (already works for each package's design-time factory).
2. Apply to a local test DB (Testcontainers) before running in production.
3. No new framework changes needed — this is operator workflow documentation.

For v2.1, the `gamekit db migrate --dry-run` CLI command generates the SQL and prints it without executing. Implementation: resolve the package's `IDesignTimeDbContextFactory`, call `context.Database.GenerateMigrateScript()`, print to stdout.

### Per-package ordering documentation

Correct ordering (guaranteed by HostedService registration order in `AddGameKit()` chain, verified from `GameKitServiceCollectionExtensions`):

```
1. GameKitVersionAssertionHostedService  (Insert(0) — FIRST)
2. CoreMigrationHostedService (or whichever Core applies first)
3. AuthMigrationHostedService
4. AdminMigrationHostedService
5. RankingsMigrationHostedService
6. MatchmakingMigrationHostedService
7. LobbyMigrationHostedService
```

This ordering is enforced by the `services.Insert(0, ...)` + default-append pattern. It does not need to change for v2.1 — only needs to be documented clearly.

---

## Decision 6: Load Tests and Docs Site

### Load test harness location

**`tests/GameKit.LoadTests/`** — new project, NOT shipped as NuGet.

Follows the existing `tests/` project pattern. Uses `BenchmarkDotNet` for micro-benchmarks + `NBomber` (or a custom `Testcontainers`-based harness) for end-to-end load scenarios. NOT xUnit — load tests should not run in CI on every push. Separate dotnet run invocation.

The sample app `TicTacToeDuel` is the composition root for integration-style load tests (connect N simultaneous WebSocket clients to `/hubs/lobby`, measure fan-out latency). The load test project points at a running `TicTacToeDuel` instance (real Postgres + Redis via Testcontainers in the test fixture).

### Docs site location + generation

**`docs/` directory at repo root** — static site generated by DocFX from existing XML doc comments.

DocFX is the standard .NET documentation tool. It reads `.csproj` `<GenerateDocumentationFile>true` (which GameKit already enforces via `CS1591-as-error`) and generates HTML from XML doc comments. The site is not a NuGet package. It is a build artifact committed to `docs/` or deployed via CI.

Build step: `docfx docs/docfx.json` — runs in CI, outputs to `docs/_site/`. Not part of the `dotnet build` graph.

---

## Component Boundaries: New vs. Modified per Package

### GameKit.Core (MODIFIED)

New additions only — no breaking changes:

| Component | Type | Purpose |
|-----------|------|---------|
| `GameKitTelemetry` | NEW static class | Naming-convention constants for all packages |
| `ILeaderLease` | NEW interface | Common contract for all leader-election lease helpers |
| `IMigrationReadinessReporter` | NEW interface | Per-package migration state for readiness endpoint |
| `AddGameKitObservability()` | NEW builder extension | Opt-in OTel SDK wiring |
| `MapGameKitHealth()` | NEW endpoint extension | `/health/live` + `/health/ready` endpoints |
| `CoreHealthProbe` | NEW internal class | Postgres `SELECT 1` probe; Redis `PingAsync()` probe |

### GameKit.Auth (MODIFIED — minor)

| Component | Type | Purpose |
|-----------|------|---------|
| `AuthActivitySource` | NEW class in `Telemetry/` | `ActivitySource("GameKit.Auth")` for login, token rotation spans |

### GameKit.Rankings (MODIFIED)

| Component | Type | Purpose |
|-----------|------|---------|
| `RankingsActivitySource` | NEW class in `Telemetry/` | Extract inline `_activitySource` from `RankingsTickerService` into canonical `Telemetry/` class; normalize with Matchmaking pattern |
| `RankingsMeter` | NEW class in `Telemetry/` | `Meter("GameKit.Rankings")` — add counters for drain batch size, match count per tick |
| `RankingsTickerLeaseHelper` | MODIFIED | Implement `ILeaderLease`; verify SIGTERM drain correctness |
| `RankDecayLeaseHelper` | MODIFIED | Implement `ILeaderLease`; verify SIGTERM drain correctness |
| `RankingsMigrationHostedService` | MODIFIED | Implement `IMigrationReadinessReporter` |

### GameKit.Matchmaking (MODIFIED)

| Component | Type | Purpose |
|-----------|------|---------|
| `MatchmakerLeaseHelper` | MODIFIED | Implement `ILeaderLease` (already close to the interface; additive) |
| `MatchmakingMigrationHostedService` | MODIFIED | Implement `IMigrationReadinessReporter` |
| Additional `MatchmakingMeter` counters | MODIFIED | Enqueue rate, match formation rate, proposal accept/decline rate |

### GameKit.Presence (MODIFIED — minor)

| Component | Type | Purpose |
|-----------|------|---------|
| `PresenceActivitySource` | NEW class in `Telemetry/` | `ActivitySource("GameKit.Presence")` for heartbeat spans |

### GameKit.Lobby (MODIFIED — minor)

| Component | Type | Purpose |
|-----------|------|---------|
| `LobbyActivitySource` | NEW class in `Telemetry/` | `ActivitySource("GameKit.Lobby")` for join/ready/transition spans |
| `LobbyMigrationHostedService` | MODIFIED | Implement `IMigrationReadinessReporter` |

### GameKit.Admin.UI (MODIFIED)

| Component | Type | Purpose |
|-----------|------|---------|
| `HealthProbeService` | MODIFIED | Delegate Postgres + Redis probes to Core's `CoreHealthProbe`; keep error-rate logic in Admin |
| `AdminActivitySource` | NEW class in `Telemetry/` | `ActivitySource("GameKit.Admin")` for admin action spans |
| `AdminMigrationHostedService` | MODIFIED | Implement `IMigrationReadinessReporter` |

### GameKit.Auth (migration hosted service)

| Component | Type | Purpose |
|-----------|------|---------|
| `AuthMigrationHostedService` | MODIFIED | Implement `IMigrationReadinessReporter` |

### samples/TicTacToeDuel (MODIFIED)

| Component | Type | Purpose |
|-----------|------|---------|
| `docker-compose.observability.yml` | NEW file | Grafana + Prometheus + Tempo stack; `docker compose -f docker-compose.yml -f docker-compose.observability.yml up` |
| OTel SDK wiring in `Program.cs` | MODIFIED | Add `AddGameKitObservability()` + exporter config for sample |

### tests/GameKit.LoadTests (NEW project)

| Component | Type | Purpose |
|-----------|------|---------|
| `MatchmakingTickerBenchmarks` | NEW | BenchmarkDotNet micro-benchmark for ticker throughput |
| `LobbySignalRLoadTest` | NEW | NBomber / Testcontainers end-to-end SignalR fan-out |
| `AuthThroughputBenchmarks` | NEW | login/token-refresh throughput under load |

### docs/ (NEW directory)

| Component | Type | Purpose |
|-----------|------|---------|
| `docfx.json` | NEW | DocFX config pointing at all src/*.csproj |
| Static site output | BUILD ARTIFACT | Per-package API docs + getting-started tutorial |

---

## Data Flow Changes

### Trace propagation through the matchmaking ticker

```
HTTP POST /api/mm/queue
    ↓ [GameKit.Core ActivitySource — "GameKit.Core" span "Matchmaking.Enqueue"]
    ↓ MatchmakingService.EnqueueAsync
    ↓ [propagate W3C TraceContext into Redis HSET field "trace_context"]
    ↓
MatchmakerTickerService.RunOnceAsync (BackgroundService — no HTTP context)
    ↓ [MatchmakingActivitySource.StartTickActivity() — "GameKit.Matchmaking.Ticker" span "Tick"]
    ↓ [MatchmakingActivitySource.StartPoolActivity() — child span "PoolSweep" per pool]
    ↓ [restore W3C TraceContext from Redis ticket's "trace_context" field → Activity.SetParentId]
    ↓ [spans carry ladderId, poolName, candidatesEvaluated, matchesFormed tags]
```

**Key design:** The ticker runs in a BackgroundService — no HTTP context = no automatic trace propagation. To link the match-formation span to the original enqueue request, the `trace_context` (W3C Traceparent header value) is stored in the Redis ticket hash at enqueue time and restored at match time via `Activity.SetParentId`. This creates a single causal trace across the async boundary.

### Trace propagation through the Lobby SignalR hub

```
HTTP POST /api/lobbies/{id}/ready   (player marks ready)
    ↓ [GameKit.Lobby "Lobby.MarkReady" span]
    ↓ LobbyService.TryStartMatchmakingAsync (all-ready trigger)
    ↓ IMatchmakingService.EnqueueAsync (cross-package call)
    ↓ [Activity context propagates in-process — no special handling needed]
    ↓ SignalR hub group broadcast to "lobby:{id}" group
```

For SignalR, Activity context propagates automatically in-process. The broadcast from the hub server to connected clients does NOT propagate trace context (WebSocket binary frames don't carry HTTP headers). This is acceptable — the server-side span captures the full operation.

### Readiness gate data flow

```
IHost.StartAsync
    ├── GameKitVersionAssertionHostedService (Insert(0))
    ├── CoreMigrationHostedService → sets IMigrationReadinessReporter.IsReady = true
    ├── AuthMigrationHostedService → sets IMigrationReadinessReporter.IsReady = true
    ├── ...all package migration services...
    └── Kestrel starts accepting traffic

GET /health/ready
    → resolve IEnumerable<IMigrationReadinessReporter>
    → if any IsReady == false → 503 Service Unavailable
    → CoreHealthProbe.CheckPostgresAsync() → 503 if fails
    → CoreHealthProbe.CheckRedisAsync() (if IConnectionMultiplexer registered) → 503 if fails
    → 200 OK
```

The readiness endpoint returns 503 during startup before migrations complete. This is the correct Kubernetes behavior: the pod is in "Terminating" or "Pending" state until migrations finish, then traffic routes to it.

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Introducing AddHealthChecks() as a parallel probe path

**What it looks like:** Adding `services.AddHealthChecks().AddNpgsql(...).AddRedis(...)` alongside the existing `HealthProbeService`.

**Why wrong:** GameKit already has Postgres + Redis probes in `HealthProbeService`. `AddHealthChecks()` would create a second, independent probe path with its own configuration, its own response format (`HealthReport` JSON vs. plain status), and its own middleware (`UseHealthChecks`). Two probe systems diverge. The Admin.UI health panel would not benefit from the new system. The new system would not benefit from the Admin's error-rate counter. The result is confusion about which probe is authoritative.

**Do instead:** Expose the probes already in `HealthProbeService` via the new `MapGameKitHealth()` endpoints. The Admin.UI panel calls the same `IHealthProbeService`. One probe path, two consumers (K8s readiness probe + admin panel).

### Anti-Pattern 2: Centralizing ActivitySource/Meter instances in a shared GameKit.Core class

**What it looks like:** `GameKitActivitySources.Matchmaking`, `GameKitActivitySources.Rankings` — all sources in one static class in Core.

**Why wrong:** Creates a hard compile-time coupling from every downstream package to Core's telemetry class. Matchmaking changes its source name → Core must be updated → all packages rebuild. Violates package independence.

**Do instead:** Each package owns its own `Telemetry/` class. Core provides only the naming conventions (string constants). Per-package telemetry classes are package-private or internal.

### Anti-Pattern 3: Storing raw lock keys (not using LockTakeAsync/LockReleaseAsync)

**What it looks like:** `db.StringSetAsync(lockKey, instanceId, ttl, When.NotExists)` + `db.KeyDeleteAsync(lockKey)`.

**Why wrong:** The `KeyDeleteAsync` path deletes the lock unconditionally — if the lock expired and another replica took it, you delete the other replica's lock. This is the primary source of split-brain in Redis-based leader election.

**Do instead:** `db.LockTakeAsync / LockReleaseAsync` (already the pattern in all three existing LeaseHelper classes). The `LockReleaseAsync` uses a Lua script that checks the value matches before deleting. Never replace this.

### Anti-Pattern 4: Running pg_dump from within the library's own connection

**What it looks like:** A `BackgroundService` or endpoint that calls `pg_dump` via `Process.Start` inside the GameKit library.

**Why wrong:** The library runs with the `gamekit_app` role (limited permissions). `pg_dump` requires superuser or a role with `pg_read_all_data` grant. Running it inside the app process also blocks the thread pool during the dump.

**Do instead:** The `gamekit db backup` CLI command runs as a separate process with the operator's credentials. Documented in the ops runbook, not automated within the library.

### Anti-Pattern 5: Creating a GameKit.Diagnostics package

**What it looks like:** Extracting health + OTel extensions into a new NuGet package.

**Why wrong:** See Decision 1 above. Adds a required package that consumers cannot skip, grows the release train, and splits operability concerns away from Core where the connection string and DbContext already live.

**Do instead:** Extend GameKit.Core with opt-in builder extensions.

---

## Suggested Build Order for v2.1 Phases

The following order respects dependency chains, ensures foundations exist before per-package instrumentation is wired, and follows the established pattern of getting infrastructure right before feature work.

```
Phase 13: Observability foundations (Core conventions + sample OTel stack)
    Rationale: naming conventions must be established BEFORE per-package Telemetry/ classes
    are modified. The sample dashboard must exist before per-package traces are wired.
    Deliverables:
    ├── GameKitTelemetry constants in Core
    ├── AddGameKitObservability() builder extension in Core
    ├── Normalize Rankings telemetry (extract into Telemetry/ class, match Matchmaking pattern)
    ├── docker-compose.observability.yml in TicTacToeDuel (Prometheus + Tempo + Grafana)
    └── Sample Program.cs wired to OTel SDK with OTLP exporter to Tempo

Phase 14: Health + readiness endpoints (Core probes + IMigrationReadinessReporter)
    Rationale: readiness gates depend on IMigrationReadinessReporter; that interface must exist
    before any package's MigrationHostedService implements it. Health probes must work before
    the hardening phase adds replica-churn tests that need readiness to behave correctly.
    Deliverables:
    ├── IMigrationReadinessReporter interface in Core
    ├── All 6 MigrationHostedServices implement IMigrationReadinessReporter
    ├── MapGameKitHealth() → /health/live + /health/ready in Core
    ├── CoreHealthProbe (Postgres + optional Redis)
    └── Refactor HealthProbeService to delegate Postgres/Redis to CoreHealthProbe

Phase 15: Per-package OTel instrumentation (one pass per package)
    Rationale: Phase 13 conventions + Phase 14 readiness endpoint must be stable before adding
    spans/metrics to each package. Adding instrumentation before the OTel stack is running
    means no feedback loop.
    Deliverables:
    ├── Auth: AuthActivitySource (login, token rotation spans)
    ├── Rankings: RankingsMeter counters (drain batch size, match count per tick)
    ├── Matchmaking: additional MatchmakingMeter counters; W3C trace propagation in ticker
    ├── Presence: PresenceActivitySource (heartbeat spans)
    ├── Lobby: LobbyActivitySource (join/ready/transition spans)
    └── Admin.UI: AdminActivitySource (admin action spans)

Phase 16: Multi-replica hardening (ILeaderLease + SIGTERM drain + idempotency audit)
    Rationale: Phase 14 health/readiness is a prerequisite — the multi-replica churn tests
    verify liveness/readiness behavior during leader transitions.
    Deliverables:
    ├── ILeaderLease interface in Core
    ├── All three LeaseHelper classes implement ILeaderLease
    ├── SIGTERM drain correctness audit + fixes in all BackgroundServices holding leases
    ├── Lobby TryStartMatchmakingAsync idempotency key (prevent double-enqueue on partition recovery)
    └── Multi-replica correctness test suite (2-replica Testcontainers integration tests)

Phase 17: Backup / restore / DR + migration ops
    Rationale: independent of telemetry and hardening; can run in parallel with Phase 16
    but sequenced here to stay after core stability work.
    Deliverables:
    ├── gamekit db status / backup / restore / migrate --dry-run CLI subcommands
    ├── Per-package migration ordering documentation
    └── Postgres + Redis DR runbook (docs/ops/backup-restore.md)

Phase 18: Security audit (threat-model verification + CVE review)
    Rationale: audit after all new v2.1 code is in place (health endpoints, new CLI commands,
    new OTel SDK wiring). CVE scan of all pinned NuGet versions.
    Deliverables:
    ├── Threat model re-verification (auth/admin/rate-limit/egress/GDPR)
    ├── dotnet list package --vulnerable audit
    └── Resolution of any findings

Phase 19: Load / performance testing
    Rationale: must run after all hardening (Phase 16) and instrumentation (Phase 15) are in
    place — otherwise benchmarks don't reflect the final code path.
    Deliverables:
    ├── tests/GameKit.LoadTests/ project (BenchmarkDotNet + NBomber or Testcontainers)
    ├── Matchmaking ticker throughput benchmarks
    ├── Lobby SignalR fan-out benchmarks (N concurrent clients)
    └── Auth login throughput benchmarks

Phase 20: Docs + tutorial (static site + getting-started guide)
    Rationale: last because the API surface is stable only after all prior phases complete.
    Deliverables:
    ├── docs/docfx.json + DocFX configuration
    ├── Getting-started tutorial (end-to-end from dotnet new gamekit to first match)
    └── Upgrade/compatibility guide (v1.0 → v2.1)
```

**Phase ordering rationale summary:**

- Phase 13 before Phase 15: OTel naming conventions before per-package instrumentation, to prevent immediately needing to rename instruments.
- Phase 14 before Phase 16: Health/readiness before multi-replica hardening, because the hardening test suite needs readiness probes to verify leader transitions.
- Phase 15 after Phase 14: Per-package spans/metrics only after the OTel sample stack is running, enabling immediate feedback during development.
- Phase 16 after Phase 14–15: Multi-replica hardening benefits from visibility (OTel traces) into leader-election behavior.
- Phase 17 can run in parallel with Phases 15–16 (no dependency on telemetry or hardening).
- Phase 18 after Phases 13–17: Security audit of completed code, not in-progress code.
- Phase 19 after Phase 18: Load tests run against the final, audited codebase.
- Phase 20 last: docs reflect the stable, final API.

---

## Sources

All findings verified by direct file reads from the actual v2.0 codebase. No external sources needed — the codebase IS the authoritative source.

| File | Relevance |
|------|-----------|
| `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` | Existing telemetry pattern; source name `"GameKit.Matchmaking.Ticker"` |
| `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` | Existing meter pattern; meter name `"GameKit.Matchmaking"` |
| `src/GameKit.Rankings/Services/RankingsTickerService.cs` | Inlined `ActivitySource("GameKit.Rankings.Ticker")` — needs extraction |
| `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` | Leader election pattern; `LockTakeAsync/LockExtendAsync/LockReleaseAsync`; fencing token |
| `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` | Duplicate leader election pattern — needs `ILeaderLease` interface |
| `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` | Third duplicate — needs `ILeaderLease` interface |
| `src/GameKit.Admin.UI/Services/HealthProbeService.cs` | Existing probes; `IRedisErrorRateCounter` already cross-replica |
| `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs` | Redis Pub/Sub relay; SIGTERM drain pattern reference |
| `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` | `services.Insert(0, ...)` HostedService ordering; `TryAddSingleton<IPlayerRatingProvider>` opt-port pattern |
| `samples/TicTacToeDuel/Program.cs` | Composition root; AddGameKit chain; no OTel wiring yet (v2.1 adds it) |
| `docker-compose.yml` | Postgres+Redis baseline; no Grafana/Prometheus/Tempo yet (v2.1 adds via overlay) |
| `.planning/STATE.md` | Advisory lock keys; per-package migration boundary; all locked decisions |

---
*Architecture research for: GameKit v2.1 — Operability & Hardening integration*
*Researched: 2026-06-08*
