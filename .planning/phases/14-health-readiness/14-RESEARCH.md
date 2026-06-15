# Phase 14: Health & Readiness — Research

**Researched:** 2026-06-14
**Domain:** ASP.NET Core built-in health checks, K8s liveness/readiness semantics, EF Core pending-migration probing, Redis distributed lock inspection
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Health-check framework (HLTH-01, HLTH-03)**
- D-01: Use the built-in `Microsoft.Extensions.Diagnostics.HealthChecks` (shared framework — zero new NuGet pin). Use `IHealthCheck`, `AddHealthChecks()`, `MapHealthChecks()`. NOT Xabaril.
- D-02: New Core surface `AddGameKitHealthChecks()` returning `IHealthChecksBuilder`; `MapGameKitHealth()` maps `/health/live` + `/health/ready` (anonymous, no rate limit, excluded from OpenAPI).

**Live vs ready separation (HLTH-01)**
- D-03: `/health/live` uses `Predicate = _ => false` (no checks execute, 200 while process alive). `/health/ready` uses `Predicate = c => c.Tags.Contains("ready")`.
- D-04: `Healthy` → 200, `Degraded` → 200 (stays in rotation), `Unhealthy` → 503.

**Migration readiness (HLTH-02)**
- D-05: `IMigrationReadinessReporter` in `GameKit.Core`. Six implementations: Core, Auth, Admin.UI, Rankings, Matchmaking, Lobby. Each registered as `AddSingleton<IMigrationReadinessReporter, …>()` by its package's `Add*` builder.
- D-06: Single Core aggregate check `"migrations"` (tagged `ready`) resolves `IEnumerable<IMigrationReadinessReporter>`. Returns `Unhealthy` while any reporter reports pending, `Healthy` once all report applied.
- D-07: Reporters call `GetPendingMigrationsAsync()` against their own package's migration-history table. Once a reporter observes "all applied" it latches that result.

**Dependency gating (HLTH-02)**
- D-08: Postgres `SELECT 1` check always registered + tagged `ready`. Fail → `Unhealthy` (503).
- D-09: Redis `PING` check registered ONLY when `IConnectionMultiplexer` is present in DI. Absence never blocks readiness. When present, fail → `Unhealthy` (503).

**Leader-lock probe (HLTH-03, HLTH-04)**
- D-10: `"matchmaking-leader"` readiness check ships in `GameKit.Matchmaking`, self-registers into shared builder. `Healthy` when this replica holds lock, `Degraded` (never `Unhealthy`) when not.
- D-11: Non-acquiring read — add `QueryLeaseAsync()` to `IMatchmakerLease`/`RedisMatchmakerLease` returning holder InstanceId + TTL via `LockQueryAsync` + `KeyTimeToLiveAsync`.

**Payload PII/infra-safety (HLTH-05)**
- D-12: Custom `ResponseWriter` emitting only `{ status, checks:[{name,status,description}] }`. Omit `Exception`, `Data`, `Tags`. Hand-authored infra-free descriptions.
- D-13: Replica InstanceId (`MachineName:Guid`) intentionally surfaced (HLTH-04 requires it). Distinct from dependency-host leak HLTH-05 forbids.
- D-14: A test asserts no health response body contains connection-string fragments / host / port / `Password=` / `Host=` patterns.

**Admin.UI delegation (HLTH-06)**
- D-15: `HealthProbeService` refactored to delegate to Core `HealthCheckService.CheckHealthAsync()`. Delete `ProbePostgresAsync`/`ProbeRedisAsync`. Status map: `Healthy→"OK"`, `Degraded→"Degraded"`, `Unhealthy→"Down"`.
- D-16: Error-rate tile stays Admin-local (`ErrorRateRingBuffer`/`IRedisErrorRateCounter`). NOT a readiness dependency.

### Claude's Discretion

- Exact namespace/file layout for new Core health types (`GameKit.Core.Health` vs `.Data`).
- Whether `AddGameKitHealthChecks()` extends `IGameKitBuilder` or `IServiceCollection`.
- Precise check-name strings and tag constant.
- Whether leader read uses `LockQueryAsync` vs raw `GET`+`PTTL`.
- Migration-latch caching mechanism.
- How sibling packages discover the shared `IHealthChecksBuilder` (explicit `Add*HealthChecks()` inside each package's `Add*` builder vs Scrutor scan).

### Deferred Ideas (OUT OF SCOPE)

- Shared-Redis fleet-drain tradeoff (→ Phase 16).
- K8s manifest + probe-tuning docs (→ docs phase / DOCS-04).
- A combined `/health` aggregate endpoint.
- Per-package OTel spans/metrics (→ Phase 15).
- Leader-churn / SIGTERM-drain / request-storm correctness (→ Phase 16).
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| HLTH-01 | `AddGameKitHealthChecks()` + map helpers expose `/health/live` (process-only, 200) and `/health/ready` (dependency-gated, 503 until ready) | D-01..D-04; confirmed by official MS docs that `Predicate = _ => false` returns 200 |
| HLTH-02 | Readiness probes cover Postgres, Redis (conditional), and per-package migrations via `IMigrationReadinessReporter` | D-05..D-09; `GetPendingMigrationsAsync()` semantics verified; conditional Redis pattern confirmed |
| HLTH-03 | Three-state health — matchmaking follower reports `Degraded`, not `Unhealthy` | D-03, D-04, D-10; `HealthStatus` enum confirmed: `Healthy`/`Degraded`/`Unhealthy` |
| HLTH-04 | Leader-lock probe identifies which replica holds lock + TTL remaining | D-10, D-11; `LockQueryAsync` + `KeyTimeToLiveAsync` confirmed non-mutating |
| HLTH-05 | Health payloads never leak infra details (connection strings, hostnames, credentials) | D-12; default writer confirmed to write only status string; custom writer pattern documented |
| HLTH-06 | Admin.UI health panel consumes `HealthCheckService`; `HealthProbeService` no longer duplicates probes | D-15, D-16; existing `HealthProbeService`, `HealthReport`, `HealthTile`, `Health.razor` all read |
</phase_requirements>

---

## Summary

Phase 14 delivers K8s-correct liveness/readiness endpoints across the GameKit deployment. The implementation is entirely within the ASP.NET Core built-in `Microsoft.Extensions.Diagnostics.HealthChecks` (shared framework — no new NuGet package). All sixteen decisions in CONTEXT.md are fully resolvable with available APIs; the main implementation work is plumbing six `IMigrationReadinessReporter` implementations, the conditional Redis check, the non-acquiring leader-lock probe, the custom JSON response writer, and the Admin.UI delegation refactor.

The `AddHealthChecks()` call is idempotent (`TryAddSingleton` under the hood), so Core calls it once and sibling packages obtain an `IHealthChecksBuilder` by calling `AddHealthChecks()` again — this is the established additive pattern. The `HealthCheckService` is registered in DI as a singleton, enabling Admin.UI's `HealthProbeService` to inject and delegate to it (D-15).

The migration readiness check requires each package to construct a migration-scoped `DbContext` (the identical `Build*MigrationContext` pattern every `*MigrationHostedService` already uses) so that `GetPendingMigrationsAsync()` queries the correct per-package `MigrationsHistoryTable`. The result must be latched on first `Healthy` observation because migrations are never un-applied at runtime, and re-querying on every probe would be expensive.

**Primary recommendation:** Implement `AddGameKitHealthChecks()` as an extension on `IGameKitBuilder` (matching `AddGameKitObservability()`), returning `IHealthChecksBuilder` so sibling packages chain their own `AddCheck` calls. Store the builder on `IGameKitBuilder` or obtain it by re-calling `services.AddHealthChecks()` from each sibling's `Add*` extension — both patterns work because `AddHealthChecks` is idempotent.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Expose `/health/live` + `/health/ready` HTTP endpoints | API / Backend (`GameKit.Core`) | — | Health endpoints are server-side infrastructure; never browser-side |
| Postgres `SELECT 1` check | API / Backend (`GameKit.Core`) | — | Core owns the connection string and `GameKitOptions` |
| Redis `PING` check | API / Backend (`GameKit.Core`) | Matchmaking/Presence register `IConnectionMultiplexer` | Core registers check, but only when multiplexer is present in DI |
| Migrations aggregate check | API / Backend (`GameKit.Core`) | Each sibling package registers its own reporter | Core owns the aggregate `IHealthCheck`; reporters are contributed per-package |
| Per-package migration reporter | Each sibling package | — | Each package owns its own history table + migration assembly |
| Leader-lock probe | API / Backend (`GameKit.Matchmaking`) | — | Lock lives in Matchmaking; Core must not reference Matchmaking |
| Admin health panel display | Frontend Server (Blazor Server, `GameKit.Admin.UI`) | Core `HealthCheckService` | Blazor panel is thin adapter; Core provides data |
| PII-safe JSON serialization | API / Backend (`GameKit.Core`) | — | Custom `ResponseWriter` lives in Core health infrastructure |

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.Extensions.Diagnostics.HealthChecks` | 10.0 (shared framework) | `IHealthCheck`, `HealthCheckService`, `AddHealthChecks()` | Ships in `Microsoft.AspNetCore.App`; zero NuGet pin; official ASP.NET Core primitive |
| `Microsoft.AspNetCore.Diagnostics.HealthChecks` | 10.0 (shared framework) | `MapHealthChecks()`, `HealthCheckOptions`, `HealthCheckResponseWriters` | Same shared-framework assembly; zero NuGet pin |
| `StackExchange.Redis` | 2.8.41 (already pinned) | `IDatabase.LockQueryAsync` + `KeyTimeToLiveAsync` for leader-lock probe (D-11) | Already repo-pinned; non-mutating read API available since 2.x |
| EF Core / Npgsql | 10.0.6 / 10.0.1 (already pinned) | `DbContext.Database.GetPendingMigrationsAsync()` in migration reporters | Already repo-pinned; per-package scoped `DbContext` construction mirrors existing `Build*MigrationContext` pattern |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Text.Json` (in-box) | 10.0 | `Utf8JsonWriter` for custom `ResponseWriter` (D-12) | Preferred over Newtonsoft; no allocations from external libraries |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Built-in health checks | Xabaril `AspNetCore.HealthChecks.*` | D-01 forbids: extra NuGet deps, no benefit for what we need |
| Custom `ResponseWriter` | Default plaintext writer | Default emits only status string, no JSON — insufficient for HLTH-04 (leader TTL) and HLTH-05 (must verify no exception leakage) |
| `LockQueryAsync` + `KeyTimeToLiveAsync` | Raw `StringGet` + `KeyExpireTimeAsync` | Both approaches are non-mutating; `LockQueryAsync` is the higher-level StackExchange.Redis API and is semantically clearer |

**Installation:** No new packages. All dependencies are shared-framework or already pinned in `Directory.Packages.props`.

---

## Package Legitimacy Audit

> This phase installs no new NuGet packages. All APIs used (`Microsoft.Extensions.Diagnostics.HealthChecks`, `Microsoft.AspNetCore.Diagnostics.HealthChecks`) ship in the `Microsoft.AspNetCore.App` shared framework that the project already targets.

| Package | Registry | Status | Disposition |
|---------|----------|--------|-------------|
| Built-in health checks (shared framework) | N/A — part of `net10.0` ASP.NET Core | Shared framework | Approved — no install needed |

**Packages removed due to slopcheck [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

---

## Architecture Patterns

### System Architecture Diagram

```
                    Orchestrator (K8s kubelet)
                           |
          ┌────────────────┼────────────────┐
          ▼                                 ▼
   GET /health/live                 GET /health/ready
   Predicate = _ => false           Predicate = c => c.Tags.Contains("ready")
   (no checks execute)              |
   → 200 Healthy always             ├── "postgres" check → SELECT 1 → 200/503
                                    ├── "redis" check (if IConnectionMultiplexer in DI)
                                    │   → PING → 200/503
                                    ├── "migrations" aggregate check
                                    │   → IEnumerable<IMigrationReadinessReporter>
                                    │   → GetPendingMigrationsAsync() per package
                                    │   → Unhealthy until all 6 report 0 pending
                                    │   → latched Healthy thereafter
                                    └── "matchmaking-leader" (if Matchmaking installed)
                                        → LockQueryAsync + KeyTimeToLiveAsync
                                        → Healthy (leader) / Degraded (follower)

Custom ResponseWriter (D-12):
  { "status": "...", "checks": [{ "name": "...", "status": "...", "description": "..." }] }
  Omits: Exception, Data, Tags (prevents Npgsql host:port leaks)

                    Admin.UI Health Panel (D-15/D-16)
                           |
             HealthCheckService.CheckHealthAsync()  ← delegates to Core
                           |
             Project HealthReport.Entries to HealthTile[]
             Healthy→"OK", Degraded→"Degraded", Unhealthy→"Down"
                           |
             + Admin-local error-rate tile (ErrorRateRingBuffer — NOT readiness)
```

### Recommended Project Structure

New files to create:

```
src/GameKit.Core/
├── Health/
│   ├── IMigrationReadinessReporter.cs       # new interface (D-05)
│   ├── MigrationAggregateHealthCheck.cs     # new aggregate IHealthCheck (D-06)
│   ├── PostgresHealthCheck.cs               # new Postgres SELECT 1 check (D-08)
│   ├── GameKitHealthResponseWriter.cs       # new custom ResponseWriter (D-12)
│   └── CoreMigrationReadinessReporter.cs    # Core's reporter implementation (D-05)
├── Builder/
│   └── GameKitHealthBuilderExtensions.cs    # AddGameKitHealthChecks() + MapGameKitHealth() (D-02)

src/GameKit.Auth/
└── Health/
    └── AuthMigrationReadinessReporter.cs    # Auth's reporter (D-05)

src/GameKit.Admin.UI/
└── Health/
    └── AdminMigrationReadinessReporter.cs   # Admin's reporter (D-05)
 (modify Services/HealthProbeService.cs)     # delegate to HealthCheckService (D-15)

src/GameKit.Rankings/
└── Health/
    └── RankingsMigrationReadinessReporter.cs

src/GameKit.Matchmaking/
└── Health/
│   ├── MatchmakingMigrationReadinessReporter.cs
│   └── MatchmakingLeaderHealthCheck.cs      # non-acquiring leader probe (D-10/D-11)
 (modify Services/IMatchmakerLease.cs)       # add QueryLeaseAsync()
 (modify Services/RedisMatchmakerLease.cs)   # implement QueryLeaseAsync()

src/GameKit.Lobby/
└── Health/
    └── LobbyMigrationReadinessReporter.cs

tests/GameKit.Core.Integration.Tests/
└── HealthEndpointTests.cs                   # live vs ready, migrations pending, leak assertions

tests/GameKit.Matchmaking.Integration.Tests/
└── MatchmakingLeaderHealthCheckTests.cs     # Degraded when follower, Healthy when leader

tests/GameKit.Admin.Integration.Tests/
 (modify HealthProbeTests.cs)               # update for delegation path
```

### Pattern 1: AddGameKitHealthChecks() — IGameKitBuilder Extension

**What:** Mirrors `AddGameKitObservability()` — an extension on `IGameKitBuilder` that calls `services.AddHealthChecks()` (idempotent), registers Core checks, and returns `IHealthChecksBuilder` for sibling packages to extend.

**When to use:** Called once in the consumer's `Program.cs` after `AddGameKit(...)`.

```csharp
// Source: mirrors GameKitObservabilityBuilderExtensions.cs pattern
// AddHealthChecks() is idempotent (TryAddSingleton internally) — safe to call from Core
// and then again from each sibling's Add* extension.
public static IHealthChecksBuilder AddGameKitHealthChecks(
    this IGameKitBuilder builder)
{
    ArgumentNullException.ThrowIfNull(builder);

    var hcBuilder = builder.Services.AddHealthChecks();

    // D-08: Postgres SELECT 1 — always registered, tagged "ready"
    hcBuilder.AddCheck<PostgresHealthCheck>("postgres", tags: new[] { "ready" });

    // D-09: Redis PING — registered only when IConnectionMultiplexer is present in DI
    // Check at registration time whether the multiplexer is already registered.
    // If matchmaking/presence is configured BEFORE AddGameKitHealthChecks, it's present.
    // If configured AFTER, sibling packages call AddHealthChecks() themselves.
    // Pattern: check IServiceCollection for IConnectionMultiplexer descriptor.
    if (builder.Services.Any(sd =>
            sd.ServiceType == typeof(IConnectionMultiplexer)))
    {
        hcBuilder.AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready" });
    }

    // D-06: Migrations aggregate — always registered, tagged "ready"
    hcBuilder.AddCheck<MigrationAggregateHealthCheck>("migrations", tags: new[] { "ready" });

    return hcBuilder;
}
```

**Important:** The `IConnectionMultiplexer` check-at-registration-time has a sequencing risk — if the consumer registers Redis AFTER calling `AddGameKitHealthChecks()`, the Redis check won't be registered. The planner should note this and recommend that the consumer registers Redis before calling `AddGameKitHealthChecks()` (or document that sibling packages like `AddMatchmaking()` should call `builder.Services.AddHealthChecks().AddCheck<RedisHealthCheck>(...)` themselves if they discover a multiplexer). See Pitfall 1.

### Pattern 2: MapGameKitHealth() — Tag-Based Live/Ready Separation

**What:** Two `MapHealthChecks` calls with different predicates. The custom `ResponseWriter` is shared.

```csharp
// Source: Microsoft Learn "Health checks in ASP.NET Core" (aspnetcore-10.0)
// [VERIFIED: docs.microsoft.com/aspnet/core/host-and-deploy/health-checks]
public static IEndpointRouteBuilder MapGameKitHealth(
    this IEndpointRouteBuilder routes)
{
    ArgumentNullException.ThrowIfNull(routes);

    // D-03: liveness — no checks execute; 200 whenever process is alive
    routes.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = GameKitHealthResponseWriter.WriteAsync,
    }).AllowAnonymous();

    // D-03/D-04: readiness — only "ready"-tagged checks; Degraded→200, Unhealthy→503
    routes.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = c => c.Tags.Contains("ready"),
        ResponseWriter = GameKitHealthResponseWriter.WriteAsync,
        ResultStatusCodes =
        {
            [HealthStatus.Healthy]   = StatusCodes.Status200OK,
            [HealthStatus.Degraded]  = StatusCodes.Status200OK,   // D-04: stays in rotation
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        },
    }).AllowAnonymous();

    return routes;
}
```

Note: `AllowAnonymous()` is required because the middleware order in `Program.cs` runs `UseGameKitAuth()` (authentication) before endpoint mapping. Health endpoints must bypass auth without `ExcludeFromRateLimiting()` needing to be called explicitly (health endpoints are not behind the rate limiter if they are mapped OUTSIDE the rate-limited route group — which is already the case since `MapGameKitHealth()` is called in the flat endpoint pipeline, not inside a rate-limited group).

### Pattern 3: IMigrationReadinessReporter and the Latch

**What:** A simple interface with a latch (volatile bool or `Lazy<bool>`). Once migrations are observed as all-applied, they never become un-applied at runtime, so the check can return `true` forever after.

```csharp
// Source: designed from project's Build*MigrationContext pattern
// [CITED: src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs]
public interface IMigrationReadinessReporter
{
    /// <summary>
    /// Returns <c>true</c> when all migrations for this package are applied.
    /// Latches on first <c>true</c> result — subsequent calls return without
    /// querying Postgres (migrations are never un-applied at runtime).
    /// </summary>
    ValueTask<bool> IsReadyAsync(CancellationToken ct);
}

// Concrete implementation skeleton (same for all 6 packages — only constants differ):
internal sealed class MatchmakingMigrationReadinessReporter : IMigrationReadinessReporter
{
    private readonly GameKitOptions _opts;
    private volatile bool _latched;

    public MatchmakingMigrationReadinessReporter(GameKitOptions opts) => _opts = opts;

    public async ValueTask<bool> IsReadyAsync(CancellationToken ct)
    {
        if (_latched) return true;

        await using var ctx = BuildMatchmakingMigrationContext(
            _opts.ConnectionString);
        var pending = await ctx.Database
            .GetPendingMigrationsAsync(ct)
            .ConfigureAwait(false);
        if (!pending.Any())
        {
            _latched = true;
            return true;
        }
        return false;
    }

    // Same Build*MigrationContext as MatchmakingMigrationHostedService —
    // must suppress PendingModelChangesWarning (ConfigureWarnings) and
    // use ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>
}
```

**Core reporter (CoreMigrationReadinessReporter)** is different: it uses the standard `GameKitDbContext` injection path (no custom model customizer needed since Core's context is the "ground truth" model). It can call `GetPendingMigrationsAsync()` directly on a scoped `GameKitDbContext` obtained from `IServiceProvider`.

### Pattern 4: MigrationAggregateHealthCheck

```csharp
// Source: IHealthCheck pattern from Microsoft Learn
// [CITED: learn.microsoft.com/aspnet/core/host-and-deploy/health-checks]
internal sealed class MigrationAggregateHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IMigrationReadinessReporter> _reporters;

    public MigrationAggregateHealthCheck(
        IEnumerable<IMigrationReadinessReporter> reporters)
        => _reporters = reporters;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        var pendingCount = 0;
        foreach (var reporter in _reporters)
        {
            if (!await reporter.IsReadyAsync(ct).ConfigureAwait(false))
                pendingCount++;
        }

        if (pendingCount > 0)
            return HealthCheckResult.Unhealthy(
                $"{pendingCount} of {_reporters.Count()} migration sets pending");

        return HealthCheckResult.Healthy("all migration sets applied");
    }
}
```

### Pattern 5: Custom ResponseWriter (D-12)

**What:** Whitelist-only JSON writer using `Utf8JsonWriter`. Emits only `status` + `checks[{name,status,description}]`. No `Exception`, no `Data`, no `Tags`.

```csharp
// Source: adapted from official custom ResponseWriter example on MS Learn
// [CITED: learn.microsoft.com/aspnet/core/host-and-deploy/health-checks]
internal static class GameKitHealthResponseWriter
{
    internal static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";

        var options = new JsonWriterOptions { Indented = false };
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, options))
        {
            writer.WriteStartObject();
            writer.WriteString("status", report.Status.ToString());
            writer.WriteStartArray("checks");
            foreach (var (name, entry) in report.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", name);
                writer.WriteString("status", entry.Status.ToString());
                // D-12: description is the only additional field.
                // D-12: Exception, Data, Tags are intentionally OMITTED.
                writer.WriteString("description", entry.Description ?? string.Empty);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ctx.Response.WriteAsync(
            Encoding.UTF8.GetString(ms.ToArray()));
    }
}
```

**HLTH-05 note:** The description field is hand-authored in each `IHealthCheck` — never populated from `exception.Message` or from connection string properties. Examples: "database unreachable", "ping failed", "3 of 6 migration sets pending", "leader: pod-a:guid, ttl: 87s", "not leader: current holder pod-b:guid, ttl: 55s".

### Pattern 6: Non-Acquiring Leader Probe (D-10/D-11)

**What:** Add `QueryLeaseAsync()` to `IMatchmakerLease` and implement it in `RedisMatchmakerLease` using `LockQueryAsync` (returns holder InstanceId string, or `RedisValue.Null` if unlocked) and `KeyTimeToLiveAsync` (returns remaining TTL as `TimeSpan?`).

```csharp
// Source: StackExchange.Redis IDatabaseAsync interface
// [CITED: github.com/StackExchange/StackExchange.Redis/blob/main/src/StackExchange.Redis/Interfaces/IDatabaseAsync.cs]

// Add to IMatchmakerLease:
/// <summary>
/// Returns the current lock holder's instance id and remaining TTL without acquiring
/// or modifying the lock. Returns <c>null</c> holder when the key is absent or expired.
/// </summary>
Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct);

public sealed record LeaseStatus(string? HolderInstanceId, TimeSpan? Ttl);

// Implement in RedisMatchmakerLease:
public async Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct)
{
    try
    {
        var db = _redis.GetDatabase();
        var holder = await db.LockQueryAsync(_lockKey).ConfigureAwait(false);
        var ttl    = await db.KeyTimeToLiveAsync(_lockKey).ConfigureAwait(false);
        return new LeaseStatus(
            holder.HasValue ? (string?)holder : null,
            ttl);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "QueryLeaseAsync: Redis unavailable.");
        return new LeaseStatus(null, null);
    }
}

// MatchmakingLeaderHealthCheck (tagged "ready", registered in AddMatchmaking()):
internal sealed class MatchmakingLeaderHealthCheck : IHealthCheck
{
    private readonly IMatchmakerLease _lease;

    public MatchmakingLeaderHealthCheck(IMatchmakerLease lease)
        => _lease = lease;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var status = await _lease.QueryLeaseAsync(ct).ConfigureAwait(false);

        if (status.HolderInstanceId == ((RedisMatchmakerLease)_lease).InstanceId)
            // D-13: InstanceId is intentionally surfaced (HLTH-04)
            return HealthCheckResult.Healthy(
                $"leader: {status.HolderInstanceId}, ttl: {status.Ttl?.TotalSeconds:F0}s");

        // D-10: Degraded (not Unhealthy) — follower stays in rotation
        return HealthCheckResult.Degraded(
            status.HolderInstanceId is not null
                ? $"not leader; holder: {status.HolderInstanceId}, ttl: {status.Ttl?.TotalSeconds:F0}s"
                : "not leader; lock currently unheld");
    }
}
```

**Dependency note:** `MatchmakingLeaderHealthCheck` takes `IMatchmakerLease` directly. Since `RedisMatchmakerLease` exposes `InstanceId` as a public property, the check needs to compare. Either cast (fragile) or add `string InstanceId { get; }` to `IMatchmakerLease`. The planner should choose the cleaner design: adding `InstanceId` to `IMatchmakerLease` is the right approach.

### Pattern 7: Admin.UI HealthProbeService Delegation (D-15)

**What:** Replace `ProbePostgresAsync` and `ProbeRedisAsync` in `HealthProbeService` with a call to `HealthCheckService.CheckHealthAsync()`. Project `HealthReport.Entries` to `HealthTile` records using the existing status map.

```csharp
// Source: project's existing HealthProbeService + HealthCheckService DI injection pattern
// [CITED: src/GameKit.Admin.UI/Services/HealthProbeService.cs]
// [CITED: src/GameKit.Admin.UI/Http/Contracts/HealthReport.cs]
public sealed class HealthProbeService : IHealthProbeService
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ErrorRateRingBuffer _errors;
    private readonly IClock _clock;
    private readonly IRedisErrorRateCounter? _redisErrors;

    public HealthProbeService(
        HealthCheckService healthCheckService,
        ErrorRateRingBuffer errors,
        IClock clock,
        IConnectionMultiplexer? redis = null,       // kept for error-counter only
        IRedisErrorRateCounter? redisErrors = null)
    {
        _healthCheckService = healthCheckService;
        _errors = errors;
        _clock = clock;
        _redisErrors = redisErrors;
    }

    public async Task<HealthReport> ProbeAsync(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService
            .CheckHealthAsync(cancellationToken)
            .ConfigureAwait(false);

        // Map Core-sourced checks to HealthTile records
        var pgEntry  = GetTile(report, "postgres");
        var redisEntry = GetTile(report, "redis");
        var err = await ProbeErrorRateAsync(cancellationToken).ConfigureAwait(false);

        return new HealthReport(pgEntry, redisEntry, err, _clock.UtcNow);
    }

    private static HealthTile GetTile(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report,
        string checkName)
    {
        if (!report.Entries.TryGetValue(checkName, out var entry))
            return new HealthTile("Down", "not configured", null);

        var status = entry.Status switch
        {
            HealthStatus.Healthy   => "OK",
            HealthStatus.Degraded  => "Degraded",
            HealthStatus.Unhealthy => "Down",
            _                     => "Down",
        };
        return new HealthTile(status, entry.Description ?? string.Empty,
            entry.Duration.TotalMilliseconds);
    }

    // ProbeErrorRateAsync remains unchanged (D-16: error-rate is Admin-local, NOT readiness)
}
```

**HealthReport record change:** The current `HealthReport` takes three positional `HealthTile` params (Postgres, Redis, ErrorRate). After D-15, it must still work for `Health.razor` (which references `_report?.Postgres`, `_report?.Redis`, `_report?.ErrorRate`). No Blazor component changes are needed — the view contract is preserved.

**HealthCheckService naming:** In .NET 10 the concrete class is `Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService`. Inject it directly (it is registered as a singleton by `AddHealthChecks()`). Do not wrap it in an interface — HLTH-06 explicitly names it.

### Anti-Patterns to Avoid

- **Registering `IConnectionMultiplexer` check unconditionally:** If Redis is absent from DI, the Redis `IHealthCheck` constructor will throw on instantiation when the check runs. Use the conditional-registration pattern (D-09).
- **Using `exception.Message` in check descriptions:** Npgsql exceptions embed `host:port`; Postgres auth exceptions embed usernames. All descriptions MUST be hand-authored strings.
- **Using the default health check response writer:** The default writes only a plaintext status string. While it does NOT serialize exception details, it also provides no structured data for HLTH-04. Use the custom writer for both endpoints.
- **Calling `GetPendingMigrationsAsync()` on the runtime `GameKitDbContext`:** The runtime context's `MigrationsHistoryTable` is `__ef_migrations_core` (Core's table). Querying pending migrations through it will report only Core's pending state, not Auth's / Matchmaking's etc. Each reporter MUST construct a package-scoped context using the same `Build*MigrationContext` pattern as the `*MigrationHostedService`.
- **Acquiring the Redis lock during the health probe:** `LockTakeAsync` modifies state. Use `LockQueryAsync` + `KeyTimeToLiveAsync` only (read-only, non-acquiring).
- **Placing health endpoints inside a rate-limited route group:** Orchestrator probes must never be throttled. `MapGameKitHealth()` must map endpoints in the flat pipeline before any `RequireRateLimiting()` group.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Liveness/readiness endpoint plumbing | Custom HTTP middleware with `if (db.CanConnect())` | `MapHealthChecks()` + `HealthCheckOptions.Predicate` | Built-in handles predicate filtering, ResultStatusCodes, response writing in one call |
| Health check aggregation | Manual loop + HTTP 200/503 decision | `HealthCheckService.CheckHealthAsync()` | Framework aggregates all registered checks, computes worst-case status, exposes `HealthReport` |
| JSON health response | `JsonSerializer.Serialize(new { ... })` | `Utf8JsonWriter` in a custom `ResponseWriter` | `Utf8JsonWriter` avoids heap allocations; `JsonSerializer.Serialize(healthReport)` would serialize `Exception` transitively |
| Status code decision | `if (status == "Unhealthy") return 503` | `HealthCheckOptions.ResultStatusCodes` | Framework evaluates the status dictionary before writing; no custom code path needed |

**Key insight:** The built-in `HealthCheckService` is the single integration seam — once it's wired, `MapHealthChecks` handles the HTTP layer and `HealthCheckService.CheckHealthAsync()` handles the Admin.UI layer. Both use the same registered check set.

---

## Common Pitfalls

### Pitfall 1: IConnectionMultiplexer Registration Order vs Health Check Registration
**What goes wrong:** `AddGameKitHealthChecks()` is called before `AddMatchmaking()` (or `AddPresence()` / `AddLobby()`), so `IConnectionMultiplexer` is not yet in the service collection. The conditional Redis check (D-09) is not registered. Redis becomes unreachable but the pod reports `Healthy` (no Redis check to fail it).
**Why it happens:** Consumers call `AddGameKitHealthChecks()` immediately after `AddGameKit()`, before chaining sibling packages.
**How to avoid:** Two safe patterns:
  1. **Consumer-side ordering:** Document that `AddGameKitHealthChecks()` must be called AFTER all sibling packages that register Redis (i.e., after `AddMatchmaking()`, `AddPresence()`, `AddLobby()`).
  2. **Deferred check registration:** Each Redis-using sibling package's `Add*` extension method calls `services.AddHealthChecks().AddCheck<RedisHealthCheck>(...)` itself — so the check is registered whenever the package is installed, regardless of `AddGameKitHealthChecks()` call order. The planner should decide which pattern to recommend; option 2 is more resilient.
**Warning signs:** Redis is configured but `/health/ready` returns 200 with no `redis` entry in the checks array.

### Pitfall 2: GetPendingMigrationsAsync() Opens a Connection on Every Call
**What goes wrong:** Each `/health/ready` probe call executes `GetPendingMigrationsAsync()` for all six reporters, opening six Postgres connections every N seconds.
**Why it happens:** The EF Core `GetPendingMigrationsAsync()` call always connects to query `__ef_migrations_<package>` table if not latched.
**How to avoid:** The latch pattern (D-07) is the mitigation. Once a reporter returns `true`, it sets `_latched = true` and subsequent calls return immediately without a DB round-trip. The reporter context is created per-call (not cached) to avoid `DbContext` lifetime issues, but is immediately disposed after the check.
**Warning signs:** High Postgres connection count during steady-state readiness probes.

### Pitfall 3: PendingModelChangesWarning Breaks GetPendingMigrationsAsync
**What goes wrong:** Calling `GetPendingMigrationsAsync()` on a migration context without `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` throws `InvalidOperationException` on consumer startup (EF Core 10 enforces the warning-as-error default).
**Why it happens:** The hand-authored migration snapshots in Matchmaking, Rankings, and Lobby don't exactly match EF Core's internal model hash. The same warning suppression is already applied in every `Build*MigrationContext` method.
**How to avoid:** Copy the `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` call into each reporter's context builder, matching the pattern in `MatchmakingMigrationHostedService.BuildMatchmakingMigrationContext()`. Core and Auth reporters do NOT need this suppression (their snapshots match).
**Warning signs:** `InvalidOperationException: 'The model for context 'GameKitDbContext' has pending changes...'` during health probe.

### Pitfall 4: HealthCheckService vs HealthCheckService Namespace Collision
**What goes wrong:** `HealthCheckService` is the concrete class in `Microsoft.Extensions.Diagnostics.HealthChecks`. Admin.UI's existing `HealthProbeService` is in `GameKit.Admin.UI.Services`. When refactoring HealthProbeService to inject `HealthCheckService`, the using directives may collide.
**Why it happens:** Both are named `*Service` and the namespaces are similar.
**How to avoid:** Use a using alias: `using CoreHealthCheckService = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService;` or use the fully-qualified name in the constructor.

### Pitfall 5: MapGameKitHealth() Placed Inside Auth or Rate-Limit Middleware
**What goes wrong:** Health endpoints return 401/429 to the K8s kubelet, causing the pod to be permanently removed from rotation.
**Why it happens:** Health endpoints mapped inside a group that calls `RequireAuthorization()` or `RequireRateLimiting()` inherit those policies.
**How to avoid:** `MapGameKitHealth()` must be called in the flat endpoint pipeline in `Program.cs` alongside `MapGameKit()` / `MapAuth()`. The `AllowAnonymous()` call on each health endpoint provides defense-in-depth. Both endpoints are anonymous (D-02).
**Warning signs:** `/health/live` returns 401 or 429 from the test client.

### Pitfall 6: Default Degraded→503 Behavior
**What goes wrong:** By default, `MapHealthChecks` maps `Degraded` to `503`. A follower replica (no leader lock) reports `Degraded`, which is treated as `503`, draining the pod from the load balancer — the exact opposite of D-04's intent.
**Why it happens:** The default `ResultStatusCodes` in ASP.NET Core maps `Degraded → 200` (this is actually the default, contrary to common assumption). **Confirmed:** the ASP.NET Core default `HealthCheckOptions.ResultStatusCodes` maps `Healthy → 200`, `Degraded → 200`, `Unhealthy → 503`. D-04 is consistent with the default.
**How to avoid:** Set `ResultStatusCodes` explicitly in `MapGameKitHealth()` to document the intent, even though it matches the default. This makes the `Degraded → 200` guarantee explicit and protected against future framework changes.
**Warning signs:** A follower replica's `/health/ready` returns 503.

---

## Code Examples

### Registration in TicTacToeDuel Program.cs

```csharp
// After all sibling package Add* calls, so IConnectionMultiplexer is registered
var gameKitBuilder = builder.Services.AddGameKit(...);
gameKitBuilder.AddAuth(...)
             .AddMatchmaking(...)   // registers IConnectionMultiplexer
             .AddLobby()
             .AddPresence();

// Call AddGameKitHealthChecks AFTER Redis-using packages
gameKitBuilder.AddGameKitHealthChecks();
// Or alternatively, each Add* extension self-registers its own checks —
// then AddGameKitHealthChecks() is just the Core-level check registration.

// In the app pipeline — OUTSIDE auth + rate-limit groups:
app.MapGameKitHealth();   // BEFORE MapGameKit() so no auth group wraps it
app.MapGameKit();
app.MapAuth();
// ...
```

### IHealthChecksBuilder Chain from Sibling Package (option 2 — resilient)

```csharp
// In MatchmakingBuilderExtensions.cs AddMatchmaking():
public static IGameKitMatchmakingBuilder AddMatchmaking(
    this IGameKitBuilder builder, Action<GameKitMatchmakingOptions>? configure = null)
{
    // ... existing registrations ...

    // Self-register Redis check — additive, idempotent; safe if AddHealthChecks
    // has already been called by Core or another sibling
    builder.Services.AddHealthChecks()
        .AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready" })
        .AddCheck<MatchmakingLeaderHealthCheck>("matchmaking-leader", tags: new[] { "ready" });

    // ... return builder
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Xabaril `AspNetCore.HealthChecks.*` for Postgres/Redis checks | Built-in `AddCheck<T>()` | ASP.NET Core 2.2+ for built-in; community now recommends built-in for simple checks | Zero extra NuGet deps; official API surface |
| Single `/health` endpoint with all checks | Separate `/health/live` + `/health/ready` per K8s probe semantics | K8s best-practice evolution (2019+) | Liveness never fails on DB blip; readiness gates on actual dependencies |
| Default plaintext response writer | Custom JSON `ResponseWriter` | Always available; community convention to customize | Structured data for HLTH-04 (leader TTL); PII safety for HLTH-05 |

**Deprecated/outdated:**
- `HealthCheckResponseWriters.WriteDetailedJson` (Xabaril): writes exceptions and full data — unsafe per HLTH-05. Use hand-rolled `Utf8JsonWriter` whitelist.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `AddHealthChecks()` is idempotent (`TryAddSingleton`) so calling it from Core and again from each sibling package's `Add*` extension is safe | Architecture Patterns, Pattern 1 | Duplicate registrations would break DI; plan would need a single `IHealthChecksBuilder` passed through `IGameKitBuilder` instead |
| A2 | `HealthCheckService` (the concrete singleton) is resolvable from DI after `AddHealthChecks()` without an additional `AddSingleton` call | Pattern 7 (Admin.UI delegation) | Admin.UI's `HealthProbeService` would fail to resolve at startup |
| A3 | Default `HealthCheckOptions.ResultStatusCodes` maps `Degraded → 200` (not 503) | Pitfall 6 | Follower replicas would be incorrectly drained; explicit `ResultStatusCodes` in `MapGameKitHealth()` mitigates this regardless |
| A4 | `LockQueryAsync` on a key that has no TTL (lock not held) returns `RedisValue.Null` (i.e., `!holder.HasValue`) | Pattern 6 | Leader probe would misreport; test must cover the "lock unheld" case |
| A5 | `IConnectionMultiplexer` is registered in DI by the consumer before any package's `Add*` builder call | Pitfall 1 / Pattern 1 | Conditional Redis check would not be registered; mitigated by option 2 (sibling self-registers) |

**Claims A1, A2, A3 are MEDIUM confidence** — verified via official documentation and source inspection, but not run against .NET 10.0 directly in this session.
**Claims A4, A5 are ASSUMED** — based on StackExchange.Redis API surface and project patterns.

---

## Open Questions (RESOLVED)

1. **IConnectionMultiplexer registration order (Pitfall 1)**
   - What we know: `IConnectionMultiplexer` is registered by the consumer (before any `Add*` call) in `TicTacToeDuel/Program.cs`, not by any GameKit package builder.
   - What's unclear: Should each Redis-using package's `Add*` extension self-register the Redis health check (option 2), or should the consumer always call `AddGameKitHealthChecks()` after all packages are registered (option 1)?
   - **RESOLVED — Core is the SINGLE owner of the conditional "redis" connectivity check (registered only when an IConnectionMultiplexer is already in the service collection at AddGameKitHealthChecks() time). Redis-using sibling packages do NOT register their own "redis" check. The operator registers the multiplexer up-front before AddGameKit*/AddGameKitHealthChecks (as the TicTacToeDuel sample does), so Core's conditional check reliably sees it.** This supersedes the earlier "option 2 (sibling self-registers)" recommendation below: a single owner removes the duplicate-name registration risk entirely while still honouring "install only what you need" — a Core-only install registers no "redis" check, and any Redis-using install gets exactly one. Matchmaking self-registers ONLY its distinct "matchmaking-leader" check (Degraded-only). See Plan 14-01 Task 3 (sole "redis" registration site) + Plan 14-03 Task 2 (Matchmaking "redis" check removed).
   - Recommendation: Use option 2 (sibling self-registers `RedisHealthCheck`). This is resilient to any consumer call order and follows the "install only what you need" principle. The planner should implement Redis check registration inside `AddMatchmaking()` / `AddPresence()` / `AddLobby()` with `TryAdd` semantics (i.e., `AddCheck` with a name check to avoid duplicate registrations). `AddHealthChecks().AddCheck<RedisHealthCheck>("redis", ...)` is idempotent only if you ensure the same name isn't added twice.

2. **`IMatchmakerLease.InstanceId` exposure**
   - What we know: `RedisMatchmakerLease` exposes `public string InstanceId` but `IMatchmakerLease` does not.
   - What's unclear: Should `InstanceId` be added to `IMatchmakerLease` so `MatchmakingLeaderHealthCheck` can compare without casting?
   - **RESOLVED — Plan 14-03 Task 1 adds `string InstanceId { get; }` to IMatchmakerLease.** The property is already on the concrete `RedisMatchmakerLease`, so the interface extension is a non-breaking addition that lets `MatchmakingLeaderHealthCheck` compare the holder without an unsafe cast.
   - Recommendation: Add `string InstanceId { get; }` to `IMatchmakerLease`. The property is already on the concrete class and the interface extension is a non-breaking addition. Avoids unsafe cast in the health check.

3. **HealthReport record shape after D-15**
   - What we know: Current `HealthReport(Postgres, Redis, ErrorRate, CheckedAt)` has fixed positional tiles. After delegation, Core's `HealthCheckService` may expose additional checks (e.g., "migrations", "matchmaking-leader") not currently in the record.
   - What's unclear: Should `HealthReport` grow to include the new checks, or should `Health.razor` only show the Postgres + Redis + ErrorRate tiles it currently renders?
   - **RESOLVED — keep the existing HealthReport(Postgres, Redis, ErrorRate, CheckedAt) / HealthTile records unchanged (Plan 14-04); only the data SOURCE changes.** Postgres + Redis tiles are projected from Core's `HealthCheckService` "postgres"/"redis" entries (delegation, D-15); the ErrorRate tile stays Admin-local (D-16). No view-layer or record edits — HLTH-06 is satisfied by re-sourcing the two delegated tiles, not by growing the record.
   - Recommendation: Keep `HealthReport` as-is for Phase 14 (Postgres tile from Core's postgres check, Redis tile from Core's redis check, ErrorRate tile from Admin-local source). The Admin health panel is not required to display migrations or leader status in Phase 14 — HLTH-06 says "Admin.UI health panel displays structured check results sourced from `HealthCheckService`", which is satisfied by delegating the Postgres + Redis tiles. Future panels can add migration/leader tiles.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | All | ✓ | 10.0.106 (pinned via global.json) | — |
| Postgres 17.9 (Testcontainers) | Integration tests | ✓ | `PostgresFixture` in TestFixtures | — |
| Redis 8.6.2 (Testcontainers) | Integration tests | ✓ | `RedisFixture` in TestFixtures | — |
| `Microsoft.Extensions.Diagnostics.HealthChecks` | Core health infrastructure | ✓ | shared framework (net10.0) | — |
| `Microsoft.AspNetCore.Diagnostics.HealthChecks` | `MapHealthChecks()` | ✓ | shared framework (net10.0) | — |

No missing dependencies.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 + Testcontainers 4.11.0 |
| Config file | `tests/xunit.runner.json` |
| Quick run command | `dotnet test tests/GameKit.Core.Integration.Tests/ -x` |
| Full suite command | `dotnet test tests/ --filter "Category=Integration" -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| HLTH-01 | `GET /health/live` returns 200 when Postgres container stopped | Integration | `dotnet test tests/GameKit.Core.Integration.Tests/ -x --filter "FullyQualifiedName~HealthEndpointTests"` | ❌ Wave 0 |
| HLTH-01 | `GET /health/ready` returns 503 while any migration pending | Integration | same | ❌ Wave 0 |
| HLTH-01 | `GET /health/ready` returns 200 once all 6 reporters report ready | Integration | same | ❌ Wave 0 |
| HLTH-02 | Core-only (no Redis): `/health/ready` 503 when Postgres down, 200 when up | Integration | same | ❌ Wave 0 |
| HLTH-02 | With Redis: `/health/ready` 503 when Redis down, 200 when up | Integration | same | ❌ Wave 0 |
| HLTH-03 | Follower replica: `GET /health/ready` returns 200 (Degraded, not Unhealthy) | Integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/ -x --filter "FullyQualifiedName~MatchmakingLeaderHealthCheckTests"` | ❌ Wave 0 |
| HLTH-04 | Leader probe identifies holder + TTL | Integration | same | ❌ Wave 0 |
| HLTH-05 | No response body contains host/port/Password=/Host= fragments | Integration | `dotnet test tests/GameKit.Core.Integration.Tests/ -x --filter "FullyQualifiedName~HealthLeakTests"` | ❌ Wave 0 |
| HLTH-06 | Admin.UI `HealthProbeService` delegates to `HealthCheckService` (no raw Npgsql/Redis) | Unit | `dotnet test tests/GameKit.Admin.Tests/ -x --filter "FullyQualifiedName~HealthProbeServiceTests"` | ❌ Wave 0 |
| HLTH-06 | Admin health panel renders Core-sourced tiles | Integration | `dotnet test tests/GameKit.Admin.Integration.Tests/ -x --filter "FullyQualifiedName~HealthProbeTests"` | ✅ (needs update) |

### Sampling Rate

- **Per task commit:** `dotnet test tests/GameKit.Core.Integration.Tests/ -x`
- **Per wave merge:** `dotnet test tests/ --filter "Category=Integration" -x`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `tests/GameKit.Core.Integration.Tests/HealthEndpointTests.cs` — covers HLTH-01, HLTH-02, HLTH-05
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderHealthCheckTests.cs` — covers HLTH-03, HLTH-04
- [ ] `tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs` — covers HLTH-06 (unit test: verify no NpgsqlConnection/IDatabase constructor param remains)
- [ ] Update `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` — existing tests pass after delegation refactor

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | Health endpoints are anonymous (D-02/D-03) |
| V3 Session Management | no | No session on health endpoints |
| V4 Access Control | yes | `.AllowAnonymous()` on health endpoints; ensure no admin policy leaks in |
| V5 Input Validation | no | Health endpoints accept no user input |
| V6 Cryptography | no | No cryptography in health checks |
| V7 Error Handling | yes | Custom `ResponseWriter` omits `Exception` field (D-12); descriptions are hand-authored |
| V8 Data Protection | yes | No connection strings/hostnames/passwords in response body (HLTH-05); test asserts this (D-14) |

### Known Threat Patterns for Health Endpoints

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Infra recon via exception messages in health payload | Information Disclosure | Custom `ResponseWriter` whitelist (D-12); leak test (D-14) |
| Health endpoint rate-abuse (expensive probe per call) | DoS | Latch pattern in migration reporters (D-07); orchestrator probe frequency bounded by K8s configuration |
| Auth bypass via health endpoint path | Elevation of Privilege | `AllowAnonymous()` is intentional + documented; health endpoints carry no sensitive data beyond InstanceId (D-13) |
| Admin policy accidentally applied to health routes | Elevation of Privilege / Denial | Map health endpoints outside any auth group; integration test verifies anonymous access |

---

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn: Health checks in ASP.NET Core (aspnetcore-10.0)](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0) — confirmed: `Predicate = _ => false` → 200; default `ResultStatusCodes` (`Degraded → 200`); `HealthCheckOptions` API; custom `ResponseWriter` pattern; `AddHealthChecks()` is idempotent
- [StackExchange.Redis IDatabaseAsync interface (GitHub)](https://github.com/StackExchange/StackExchange.Redis/blob/main/src/StackExchange.Redis/Interfaces/IDatabaseAsync.cs) — `LockQueryAsync(key)` returns current holder value (non-mutating); `KeyTimeToLiveAsync(key)` returns remaining TTL (non-mutating)
- [HealthCheckServiceCollectionExtensions (aspnetcore GitHub)](https://github.com/dotnet/aspnetcore/blob/main/src/HealthChecks/HealthChecks/src/DependencyInjection/HealthCheckServiceCollectionExtensions.cs) — `AddHealthChecks()` uses `TryAddSingleton`; idempotent/additive

### Secondary (MEDIUM confidence)
- `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` — `AddGameKitObservability()` shape that `AddGameKitHealthChecks()` mirrors
- `src/GameKit.Admin.UI/Services/HealthProbeService.cs` — existing `ProbePostgresAsync`/`ProbeRedisAsync` probe logic to delete
- `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` — `InstanceId = MachineName:Guid`; `LockTakeAsync`/`LockReleaseAsync` pattern from which non-acquiring `QueryLeaseAsync` is derived
- `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs` — `BuildMatchmakingMigrationContext()` pattern replicated in migration reporters (including `PendingModelChangesWarning` suppression)
- All six `*MigrationConstants.cs` files — `MigrationsHistoryTable` names for each package's reporter

### Tertiary (LOW confidence / ASSUMED)
- A4: `LockQueryAsync` returns `RedisValue.Null` when key not held — inferred from API contract; needs test coverage
- A5: Consumer always registers `IConnectionMultiplexer` before sibling package `Add*` calls — true in current `TicTacToeDuel/Program.cs` but not guaranteed by library contract

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — shared framework, zero new packages, all APIs confirmed
- Architecture: HIGH — follows established GameKit patterns (observability builder, migration hosted service, per-package migration context)
- Pitfalls: HIGH — grounded in existing codebase analysis and confirmed API behavior
- Assumptions: MEDIUM — A1/A2/A3 confirmed via documentation; A4/A5 are ASSUMED

**Research date:** 2026-06-14
**Valid until:** 2026-09-14 (stable ASP.NET Core APIs; StackExchange.Redis 2.8.x line)

---

*Phase: 14-health-readiness*
*Research completed: 2026-06-14*
