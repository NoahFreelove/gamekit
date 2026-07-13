// SPDX-License-Identifier: GPL-3.0-or-later
# Phase 14: Health & Readiness - Pattern Map

**Mapped:** 2026-06-14
**Files analyzed:** 22 new/modified files
**Analogs found:** 21 / 22

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Core/Health/IMigrationReadinessReporter.cs` | interface | request-response | `src/GameKit.Core/Services/IClock.cs` | role-match (small Core interface) |
| `src/GameKit.Core/Health/MigrationAggregateHealthCheck.cs` | service | request-response | `src/GameKit.Admin.UI/Services/HealthProbeService.cs` | role-match (dependency aggregation) |
| `src/GameKit.Core/Health/PostgresHealthCheck.cs` | service | request-response | `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (ProbePostgresAsync) | exact (same SELECT 1 / 2s timeout probe logic) |
| `src/GameKit.Core/Health/GameKitHealthResponseWriter.cs` | utility | request-response | none — no ResponseWriter in repo | no analog |
| `src/GameKit.Core/Health/CoreMigrationReadinessReporter.cs` | service | request-response | `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` (BuildMigrationContext) | role-match (Core migration context) |
| `src/GameKit.Core/Builder/GameKitHealthBuilderExtensions.cs` | config/builder | request-response | `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` | exact (opt-in IGameKitBuilder extension returning builder) |
| `src/GameKit.Auth/Health/AuthMigrationReadinessReporter.cs` | service | request-response | `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` | exact (BuildAuthMigrationContext pattern) |
| `src/GameKit.Admin.UI/Health/AdminMigrationReadinessReporter.cs` | service | request-response | `src/GameKit.Admin.UI/Data/AdminMigrationHostedService.cs` | exact (BuildAdminMigrationContext pattern) |
| `src/GameKit.Rankings/Health/RankingsMigrationReadinessReporter.cs` | service | request-response | `src/GameKit.Rankings/Data/RankingsMigrationHostedService.cs` | exact |
| `src/GameKit.Matchmaking/Health/MatchmakingMigrationReadinessReporter.cs` | service | request-response | `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs` | exact (BuildMatchmakingMigrationContext + PendingModelChangesWarning suppression) |
| `src/GameKit.Matchmaking/Health/MatchmakingLeaderHealthCheck.cs` | service | request-response | `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` | role-match (reads same Redis lock key) |
| `src/GameKit.Lobby/Health/LobbyMigrationReadinessReporter.cs` | service | request-response | `src/GameKit.Lobby/Data/LobbyMigrationHostedService.cs` | exact |
| `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` (modify) | interface | request-response | itself — add `QueryLeaseAsync` beside `TryAcquireLeaseAsync` / `ReleaseLeaseAsync` | self-analog |
| `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` (modify) | service | request-response | itself — implement `QueryLeaseAsync` beside `TryAcquireLeaseAsync` / `ReleaseLeaseAsync` | self-analog |
| `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (modify) | service | request-response | itself (current shape) + `MatchmakingBuilderExtensions.cs` (conditional-IConnectionMultiplexer pattern) | self-analog |
| `src/GameKit.Admin.UI/Services/IHealthProbeService.cs` (read-only) | interface | request-response | itself | self-analog |
| `src/GameKit.Admin.UI/Http/Contracts/HealthReport.cs` (read-only) | model | request-response | itself | self-analog |
| `samples/TicTacToeDuel/Program.cs` (modify) | config | request-response | itself — add `AddGameKitHealthChecks()` + `MapGameKitHealth()` beside existing `AddGameKitObservability()` + `MapGameKit()` wiring | self-analog |
| `tests/GameKit.Core.Integration.Tests/HealthEndpointTests.cs` | test | request-response | `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` | role-match |
| `tests/GameKit.Core.Integration.Tests/HealthLeakTests.cs` | test | request-response | `tests/GameKit.Core.Integration.Tests/EgressGuardTests.cs` | role-match (payload guard) |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderHealthCheckTests.cs` | test | request-response | `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerLeaseHelperTests.cs` | exact (Redis fixture + IAsyncLifetime + FlushDatabaseAsync) |
| `tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs` | test | request-response | `tests/GameKit.Admin.Tests/AdminAuthServiceTests.cs` | role-match (unit test, Moq stubs) |

---

## Pattern Assignments

### `src/GameKit.Core/Health/IMigrationReadinessReporter.cs` (interface, request-response)

**Analog:** `src/GameKit.Core/Services/IClock.cs`

**Why this analog:** Canonical pattern for a small, focused Core interface — GPL header, XML doc, single public method, no implementation detail, `namespace GameKit.Core.*`.

**Imports pattern** (lines 1-6):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Health;
```

**Core pattern** (lines 1-14 of IClock.cs):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Core.Services;

/// <summary>Abstraction over "current time" for testability. Production impl: <see cref="SystemClock"/>.</summary>
public interface IClock
{
    /// <summary>The current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
```

**Mirror as:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Health;

/// <summary>
/// Implemented once per package that owns migrations. Returns readiness for the
/// migrations-aggregate <c>"migrations"</c> <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck"/>.
/// Implementations latch on first <c>true</c> — migrations are never un-applied at runtime.
/// </summary>
public interface IMigrationReadinessReporter
{
    /// <summary>
    /// Returns <c>true</c> when all migrations for this package are applied.
    /// After the first <c>true</c> result, subsequent calls return <c>true</c> immediately
    /// without querying Postgres (latch pattern per D-07).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<bool> IsReadyAsync(CancellationToken ct);
}
```

---

### `src/GameKit.Core/Health/MigrationAggregateHealthCheck.cs` (service, request-response)

**Analog:** `src/GameKit.Admin.UI/Services/HealthProbeService.cs`

**Why this analog:** The existing `HealthProbeService.ProbeAsync` already demonstrates the pattern of injecting multiple dependencies, iterating over them, and aggregating results — one failure flips the overall status. The new aggregate IHealthCheck applies the same pattern to `IEnumerable<IMigrationReadinessReporter>`.

**Imports pattern** (lines 1-14 of HealthProbeService.cs — adapt namespace/using):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GameKit.Core.Health;
```

**Core pattern** — constructor + `CheckHealthAsync`:
```csharp
// internal sealed — never exposed as a NuGet public API
internal sealed class MigrationAggregateHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IMigrationReadinessReporter> _reporters;

    public MigrationAggregateHealthCheck(
        IEnumerable<IMigrationReadinessReporter> reporters)
        => _reporters = reporters;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var pendingCount = 0;
        var totalCount   = 0;
        foreach (var reporter in _reporters)
        {
            totalCount++;
            if (!await reporter.IsReadyAsync(cancellationToken).ConfigureAwait(false))
                pendingCount++;
        }

        if (pendingCount > 0)
            // D-12: hand-authored description — no connection string, no exception
            return HealthCheckResult.Unhealthy(
                $"{pendingCount} of {totalCount} migration sets pending");

        return HealthCheckResult.Healthy("all migration sets applied");
    }
}
```

---

### `src/GameKit.Core/Health/PostgresHealthCheck.cs` (service, request-response)

**Analog:** `src/GameKit.Admin.UI/Services/HealthProbeService.cs` — `ProbePostgresAsync` (lines 66-87)

**Why this analog:** `ProbePostgresAsync` is the exact probe logic to lift: `NpgsqlConnection`, `SELECT 1`, `CommandTimeout = 2`, exception → "Down". Phase 14 replaces this duplication with a Core `IHealthCheck` that all consumers share.

**Core probe logic to copy** (HealthProbeService.cs lines 66-87):
```csharp
private async Task<HealthTile> ProbePostgresAsync(CancellationToken cancellationToken)
{
    var sw = Stopwatch.StartNew();
    try
    {
        await using var conn = new NpgsqlConnection(_gameKitOpts.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.CommandTimeout = 2;
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();
        return result is 1
            ? new HealthTile("OK", "connected", sw.Elapsed.TotalMilliseconds)
            : new HealthTile("Degraded", $"unexpected result: {result}", sw.Elapsed.TotalMilliseconds);
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new HealthTile("Down", ex.GetType().Name, sw.Elapsed.TotalMilliseconds);
    }
}
```

**Adapt as IHealthCheck (D-12: use hand-authored description, not ex.Message):**
```csharp
internal sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly GameKitOptions _opts;

    public PostgresHealthCheck(GameKitOptions opts) => _opts = opts;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_opts.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 2;
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is 1
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database unreachable");  // D-12: no exception text
        }
        catch
        {
            // D-12: ex.Message MUST NOT be surfaced — Npgsql embeds host:port
            return HealthCheckResult.Unhealthy("database unreachable");
        }
    }
}
```

**Critical rule:** Never pass `ex.Message` or `ex.ToString()` to `HealthCheckResult.Unhealthy()`. The description must be a hand-authored constant string (D-12, HLTH-05).

---

### `src/GameKit.Core/Health/GameKitHealthResponseWriter.cs` (utility, request-response)

**Analog:** None exists in the repo.

**Why no analog:** No custom `ResponseWriter` pattern exists anywhere in GameKit. The RESEARCH.md provides the complete implementation using `Utf8JsonWriter` + `MemoryStream`. Copy from RESEARCH.md Pattern 5 verbatim. The key constraint (D-12): serialize only `status` + `checks[{name,status,description}]`. Do NOT include `Exception`, `Data`, or `Tags` fields from `HealthReportEntry`.

**Pattern from RESEARCH.md (Pattern 5) — confirmed implementation:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GameKit.Core.Health;

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
                // Exception, Data, Tags intentionally OMITTED.
                writer.WriteString("description", entry.Description ?? string.Empty);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return ctx.Response.WriteAsync(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
```

---

### `src/GameKit.Core/Builder/GameKitHealthBuilderExtensions.cs` (config/builder, request-response)

**Analog:** `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs`

**Why this analog:** `AddGameKitObservability()` is the canonical opt-in `IGameKitBuilder` extension in Core. It demonstrates: GPL header, `ArgumentNullException.ThrowIfNull(builder)`, operating on `builder.Services`, returning the builder type for chaining. `AddGameKitHealthChecks()` mirrors this shape exactly but returns `IHealthChecksBuilder` for sibling-package chaining (D-02).

**Imports pattern** (GameKitObservabilityBuilderExtensions.cs lines 1-11):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Builder;
```

**Extension method shape** (GameKitObservabilityBuilderExtensions.cs lines 88-131):
```csharp
public static IGameKitBuilder AddGameKitObservability(
    this IGameKitBuilder builder,
    Action<GameKitObservabilityOptions>? configure = null)
{
    ArgumentNullException.ThrowIfNull(builder);

    // ... operates on builder.Services ...

    return builder;
}
```

**Mirror as `AddGameKitHealthChecks()` + `MapGameKitHealth()`:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GameKit.Core.Builder;

/// <summary>
/// Builder extensions that register and map GameKit health-check endpoints.
/// Implements D-01 (built-in ASP.NET Core HealthChecks — zero new NuGet pin),
/// D-02 (AddGameKitHealthChecks / MapGameKitHealth surface),
/// D-03 (tag-based live/ready separation), D-04 (Degraded→200).
/// </summary>
public static class GameKitHealthBuilderExtensions
{
    /// <summary>
    /// Registers the GameKit health checks (Postgres SELECT 1, conditional Redis PING,
    /// migrations aggregate) and returns an <see cref="IHealthChecksBuilder"/> so sibling
    /// packages (Matchmaking, Presence, Lobby) can register their own checks additively.
    /// Call AFTER all sibling <c>Add*</c> extensions so <c>IConnectionMultiplexer</c>
    /// is already in DI when the conditional Redis-check registration runs.
    /// </summary>
    public static IHealthChecksBuilder AddGameKitHealthChecks(
        this IGameKitBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var hcBuilder = builder.Services.AddHealthChecks();

        hcBuilder.AddCheck<PostgresHealthCheck>("postgres", tags: new[] { "ready" });

        // D-09: Redis check only when IConnectionMultiplexer is already in DI
        if (builder.Services.Any(
                sd => sd.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer)))
        {
            hcBuilder.AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready" });
        }

        hcBuilder.AddCheck<MigrationAggregateHealthCheck>("migrations", tags: new[] { "ready" });

        return hcBuilder;
    }

    /// <summary>
    /// Maps <c>GET /health/live</c> (process-only, 200 always) and
    /// <c>GET /health/ready</c> (dependency-gated, Degraded→200, Unhealthy→503).
    /// Both endpoints are anonymous and excluded from rate limiting — must be called
    /// OUTSIDE any auth or rate-limit group in <c>Program.cs</c>.
    /// </summary>
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

        // D-03/D-04: readiness — "ready"-tagged checks; Degraded→200, Unhealthy→503
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
}
```

**MapGameKit analog** for `MapGameKitHealth` shape (GameKitApplicationBuilderExtensions.cs lines 71-78):
```csharp
public static IEndpointRouteBuilder MapGameKit(this IEndpointRouteBuilder routes)
{
    ArgumentNullException.ThrowIfNull(routes);
    var policies = routes.ServiceProvider.GetRequiredService<IGameKitRateLimitPolicies>();
    routes.MapPlayers();
    routes.MapSessions(policies);
    return routes;
}
```

---

### Six `*MigrationReadinessReporter.cs` implementations

All six reporters share the same pattern — only constants differ. The canonical template is extracted from `MatchmakingMigrationHostedService.cs`.

#### `src/GameKit.Matchmaking/Health/MatchmakingMigrationReadinessReporter.cs` (service, request-response)

**Analog:** `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs`

**Why this analog:** The `BuildMatchmakingMigrationContext` static method is the exact factory the reporter must replicate — same `MigrationsAssembly`, same `MigrationsHistoryTable`, same `ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()`, same `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))`. The reporter calls `GetPendingMigrationsAsync()` on a context built identically.

**Imports pattern** (MatchmakingMigrationHostedService.cs lines 1-13):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
```

**BuildMatchmakingMigrationContext — copy verbatim** (MatchmakingMigrationHostedService.cs lines 78-99):
```csharp
private static GameKitDbContext BuildMatchmakingMigrationContext(string connectionString)
{
    // Matchmaking-only migration context. Uses MatchmakingMigrationModelCustomizer which applies
    // the five Matchmaking configurations directly and excludes every Core / Auth / Admin / Rankings
    // entity from the migration diff (per-package migration boundary, PITFALLS #3).
    var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
        .UseNpgsql(connectionString, npg =>
        {
            npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
            npg.MigrationsHistoryTable(
                MatchmakingMigrationConstants.MigrationsHistoryTable,
                GameKitMigrationConstants.SchemaName);
        })
        .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
        // The hand-authored snapshot is structurally correct but does not match EF Core's
        // internal model hash exactly. Without this ignore, MigrateAsync raises
        // PendingModelChangesWarning as an exception on consumer startup.
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

    return new GameKitDbContext(optionsBuilder.Options);
}
```

**Latch pattern for reporter:**
```csharp
internal sealed class MatchmakingMigrationReadinessReporter : IMigrationReadinessReporter
{
    private readonly GameKitOptions _opts;
    private volatile bool _latched;

    public MatchmakingMigrationReadinessReporter(GameKitOptions opts) => _opts = opts;

    public async ValueTask<bool> IsReadyAsync(CancellationToken ct)
    {
        if (_latched) return true;

        var connStr = !string.IsNullOrWhiteSpace(_opts.MigrationsConnectionString)
            ? _opts.MigrationsConnectionString!
            : _opts.ConnectionString;

        await using var ctx = BuildMatchmakingMigrationContext(connStr);
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

    private static GameKitDbContext BuildMatchmakingMigrationContext(string connectionString)
    { /* copy verbatim from MatchmakingMigrationHostedService.BuildMatchmakingMigrationContext */ }
}
```

#### Per-package variation table

| Reporter | MigrationConstants class | ModelCustomizer class | Needs PendingModelChangesWarning suppress |
|----------|--------------------------|----------------------|-------------------------------------------|
| `CoreMigrationReadinessReporter` | `GameKitMigrationConstants` | none (no `ReplaceService`) | No |
| `AuthMigrationReadinessReporter` | `AuthMigrationConstants` | `AuthMigrationModelCustomizer` | No |
| `AdminMigrationReadinessReporter` | `AdminMigrationConstants` | `AdminMigrationModelCustomizer` | No |
| `RankingsMigrationReadinessReporter` | `RankingsMigrationConstants` | `RankingsMigrationModelCustomizer` | Yes |
| `MatchmakingMigrationReadinessReporter` | `MatchmakingMigrationConstants` | `MatchmakingMigrationModelCustomizer` | Yes |
| `LobbyMigrationReadinessReporter` | `LobbyMigrationConstants` | `LobbyMigrationModelCustomizer` | Yes |

**Core reporter is different:** The Core reporter does NOT use `BuildMigrationContext`. It injects a scoped `IServiceProvider` (or accepts `GameKitDbContext` from DI) because Core's context IS the runtime context and `MigrationsHistoryTable` is already configured correctly on the DI-registered `GameKitDbContext`. No `ReplaceService` needed.

**Auth reporter analog** (AuthMigrationHostedService.cs lines 67-84 — note: NO `ConfigureWarnings` needed):
```csharp
private static GameKitDbContext BuildAuthMigrationContext(string connectionString)
{
    var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
        .UseNpgsql(connectionString, npg =>
        {
            npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
            npg.MigrationsHistoryTable(
                AuthMigrationConstants.MigrationsHistoryTable,
                GameKitMigrationConstants.SchemaName);
        })
        .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>();
    // NOTE: Auth does NOT need ConfigureWarnings — its snapshot matches the model hash.

    return new GameKitDbContext(optionsBuilder.Options);
}
```

**DI registration for each reporter** — added to the package's `Add*` builder extension alongside the existing `AddHostedService<*MigrationHostedService>()`:
```csharp
// Pattern from MatchmakingBuilderExtensions.cs lines 83 — mirror exactly:
builder.Services.AddHostedService<MatchmakingMigrationHostedService>();
// ADD alongside it:
builder.Services.AddSingleton<IMigrationReadinessReporter, MatchmakingMigrationReadinessReporter>();
```

---

### `src/GameKit.Matchmaking/Health/MatchmakingLeaderHealthCheck.cs` (service, request-response)

**Analog:** `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs`

**Why this analog:** The health check reads the same Redis lock key the lease uses (`_lockKey` / `MatchmakingRedisKeys.MatcherLock`). The `IConnectionMultiplexer` injection, `_redis.GetDatabase()`, and try/catch-with-LogWarning error handling are all identical to `RedisMatchmakerLease`.

**Imports and constructor pattern** (RedisMatchmakerLease.cs lines 1-65):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Health;

internal sealed class MatchmakingLeaderHealthCheck : IHealthCheck
{
    private readonly IMatchmakerLease _lease;

    public MatchmakingLeaderHealthCheck(IMatchmakerLease lease)
        => _lease = lease;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _lease.QueryLeaseAsync(cancellationToken).ConfigureAwait(false);

        // D-13: InstanceId is intentionally surfaced (HLTH-04 requires replica identity)
        if (status.HolderInstanceId == _lease.InstanceId)
            return HealthCheckResult.Healthy(
                $"leader: {_lease.InstanceId}, ttl: {status.Ttl?.TotalSeconds:F0}s");

        // D-10: Degraded (not Unhealthy) — follower stays in rotation
        return HealthCheckResult.Degraded(
            status.HolderInstanceId is not null
                ? $"not leader; holder: {status.HolderInstanceId}, ttl: {status.Ttl?.TotalSeconds:F0}s"
                : "not leader; lock currently unheld");
    }
}
```

**Self-registration in `MatchmakingBuilderExtensions.cs`** — mirrors how `MatchmakingMigrationHostedService` is registered (line 83):
```csharp
// Inside AddMatchmaking(), after existing registrations:
builder.Services.AddHealthChecks()
    .AddCheck<MatchmakingLeaderHealthCheck>("matchmaking-leader", tags: new[] { "ready" });
```

---

### `src/GameKit.Matchmaking/Services/IMatchmakerLease.cs` (modify, interface)

**Analog:** itself — add `QueryLeaseAsync` + `InstanceId` beside the existing two methods.

**Current interface** (IMatchmakerLease.cs lines 33-48):
```csharp
public interface IMatchmakerLease
{
    Task<bool> TryAcquireLeaseAsync(CancellationToken ct);
    Task ReleaseLeaseAsync(CancellationToken ct);
}
```

**Add two members following the same XML doc style:**
```csharp
/// <summary>
/// Fencing-token-grade unique id for this process instance (<c>MachineName:Guid</c>).
/// </summary>
string InstanceId { get; }

/// <summary>
/// Returns the current lock holder's instance id and remaining TTL without acquiring
/// or modifying the lock. Returns <c>null</c> holder when the key is absent or expired.
/// </summary>
/// <param name="ct">Cancellation token.</param>
Task<LeaseStatus> QueryLeaseAsync(CancellationToken ct);
```

**`LeaseStatus` record** (new type in the same file or adjacent):
```csharp
/// <summary>Snapshot of a distributed leader lock: current holder + TTL.</summary>
/// <param name="HolderInstanceId">The holder's <c>InstanceId</c>, or <c>null</c> when unheld.</param>
/// <param name="Ttl">Remaining lease duration, or <c>null</c> when the key has no TTL.</param>
public sealed record LeaseStatus(string? HolderInstanceId, TimeSpan? Ttl);
```

---

### `src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs` (modify, service)

**Analog:** itself — implement `QueryLeaseAsync` beside `TryAcquireLeaseAsync` / `ReleaseLeaseAsync`.

**TryAcquireLeaseAsync as template** (RedisMatchmakerLease.cs lines 68-80) — same try/catch/LogWarning pattern:
```csharp
public async Task<bool> TryAcquireLeaseAsync(CancellationToken ct)
{
    try
    {
        var db = _redis.GetDatabase();
        return await db.LockTakeAsync(_lockKey, InstanceId, _ttl).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex,
            "RedisMatchmakerLease: failed to acquire lease — treating as not-leader.");
        return false;
    }
}
```

**Implement `QueryLeaseAsync` with same error-handling shape:**
```csharp
/// <inheritdoc />
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
        _logger.LogWarning(ex,
            "RedisMatchmakerLease: QueryLeaseAsync — Redis unavailable.");
        return new LeaseStatus(null, null);
    }
}
```

**Add `InstanceId` to the interface implementation** (already on the concrete class at line 46 — only needs surfacing on the interface):
```csharp
// Already exists on RedisMatchmakerLease.cs line 46:
public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";
```

---

### `src/GameKit.Admin.UI/Services/HealthProbeService.cs` (modify, service)

**Analog:** itself (current shape) + `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` for the optional-`IConnectionMultiplexer` injection pattern.

**Current constructor** (HealthProbeService.cs lines 40-55) — the optional `IConnectionMultiplexer? redis = null` pattern IS preserved (for error-rate counter), but `GameKitOptions _gameKitOpts` is removed:
```csharp
public HealthProbeService(
    GameKitOptions gameKitOpts,         // REMOVE — no longer needed
    ErrorRateRingBuffer errors,
    IClock clock,
    IConnectionMultiplexer? redis = null,       // KEEP — for error-rate counter only
    IRedisErrorRateCounter? redisErrors = null)
```

**Replace with:**
```csharp
public HealthProbeService(
    Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService healthCheckService,
    ErrorRateRingBuffer errors,
    IClock clock,
    IRedisErrorRateCounter? redisErrors = null)
{
    ArgumentNullException.ThrowIfNull(healthCheckService);
    ArgumentNullException.ThrowIfNull(errors);
    ArgumentNullException.ThrowIfNull(clock);
    _healthCheckService = healthCheckService;
    _errors = errors;
    _clock = clock;
    _redisErrors = redisErrors;
}
```

**`ProbeAsync` delegation pattern** (replacing `ProbePostgresAsync` + `ProbeRedisAsync` calls, keeping `ProbeErrorRateAsync`):
```csharp
public async Task<HealthReport> ProbeAsync(CancellationToken cancellationToken)
{
    var report = await _healthCheckService
        .CheckHealthAsync(cancellationToken)
        .ConfigureAwait(false);

    var pg    = GetTile(report, "postgres");
    var redis = GetTile(report, "redis");
    var err   = await ProbeErrorRateAsync(cancellationToken).ConfigureAwait(false);

    return new HealthReport(pg, redis, err, _clock.UtcNow);
}

private static HealthTile GetTile(
    Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report,
    string checkName)
{
    if (!report.Entries.TryGetValue(checkName, out var entry))
        return new HealthTile("Down", "not configured", null);

    var status = entry.Status switch
    {
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy   => "OK",
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded  => "Degraded",
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy => "Down",
        _                                                                      => "Down",
    };
    return new HealthTile(status, entry.Description ?? string.Empty,
        entry.Duration.TotalMilliseconds);
}
// ProbeErrorRateAsync: copy unchanged from HealthProbeService.cs lines 109-130
```

**Namespace collision mitigation:** Use `using CoreHealthCheckService = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService;` in the file header, or use the fully-qualified name as shown above.

**Delete:** `ProbePostgresAsync` (lines 66-87), `ProbeRedisAsync` (lines 89-107), and the `Npgsql` + `StackExchange.Redis` using directives that are no longer needed after those methods are removed.

---

### `samples/TicTacToeDuel/Program.cs` (modify)

**Analog:** itself — insert `AddGameKitHealthChecks()` + `MapGameKitHealth()` alongside existing calls.

**Existing `Add*` wiring** (Program.cs lines 29-146) — call `AddGameKitHealthChecks()` AFTER all `Add*` extensions that register `IConnectionMultiplexer` consumers. In TicTacToeDuel that means after `AddLobby()` (the last `Add*` call before `builder.Build()`):

```csharp
// After existing: gameKitBuilder.AddLobby();
gameKitBuilder.AddGameKitHealthChecks();  // D-02: must be last; IConnectionMultiplexer already in DI
```

**Existing `Map*` wiring** (Program.cs lines 172-180) — add `MapGameKitHealth()` BEFORE `MapGameKit()` so it sits outside the authorization pipeline:
```csharp
app.MapGameKitHealth();   // D-03: anonymous, no auth group — BEFORE MapGameKit
app.MapGameKit();
app.MapAuth();
// ... rest unchanged
```

The strict middleware order in Program.cs comments (line 155-159) is preserved: `UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → UseGameKitAdmin`. Health endpoint mapping happens in the flat pipeline (not inside any group), consistent with `MapGameKit()` / `MapAuth()` / `MapMatchmaking()` existing calls.

---

## Test Pattern Assignments

### `tests/GameKit.Core.Integration.Tests/HealthEndpointTests.cs` (test, request-response)

**Analog:** `tests/GameKit.Admin.Integration.Tests/HealthProbeTests.cs` + `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs`

**Why this analog:** `HealthProbeTests.cs` demonstrates the complete pattern: `[Collection("Admin")]`, `[Trait("Category", "Integration")]`, `PostgresFixture` + `RedisFixture` constructor injection, `AdminTestHost.StartAsync(...)`, resolve-from-DI, assert on report fields. `HealthEndpointTests` replaces `AdminTestHost` with a `WebApplicationFactory`-based Core-only host and uses `HttpClient` to hit `/health/live` + `/health/ready` directly.

**Collection and trait pattern** (HealthProbeTests.cs lines 22-23):
```csharp
[Collection("PostgresAndRedis")]
[Trait("Category", "Integration")]
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public HealthEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }
```

**PostgresAndRedis collection** is already defined in `tests/GameKit.Core.Integration.Tests/CollectionDefinitions.cs` (lines 14-17):
```csharp
[CollectionDefinition("PostgresAndRedis")]
public sealed class PostgresAndRedisCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture> { }
```

**HTTP assertion pattern** (from HealthProbeTests.cs adapted to HttpClient):
```csharp
[Fact]
public async Task Live_Returns_200_When_Postgres_Stopped()
{
    // Start host, stop Postgres container (or inject failing mock check),
    // GET /health/live → Assert.Equal(200, (int)response.StatusCode)
}

[Fact]
public async Task Ready_Returns_503_While_Migrations_Pending()
{
    // Start host WITHOUT applying migrations (AutoMigrate=false, migrations not run),
    // GET /health/ready → Assert.Equal(503, ...)
    // Then apply migrations → GET /health/ready → Assert.Equal(200, ...)
}
```

---

### `tests/GameKit.Core.Integration.Tests/HealthLeakTests.cs` (test, request-response)

**Analog:** `tests/GameKit.Core.Integration.Tests/EgressGuardTests.cs` (payload guard pattern)

**Why this analog:** EgressGuardTests asserts that no outbound HTTP traffic escapes the test boundary. HealthLeakTests follows the same "assert payload does NOT contain X" structure. Use `[Collection("PostgresAndRedis")]` + `[Trait("Category", "Integration")]`.

**Assertion pattern (D-14):**
```csharp
[Fact]
public async Task ReadyPayload_Does_Not_Contain_ConnectionString_Fragments()
{
    // GET /health/ready
    var body = await response.Content.ReadAsStringAsync();

    // Assert no connection-string fragments (D-14)
    Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Port=", body, StringComparison.OrdinalIgnoreCase);
    // Assert does not contain the actual configured hostname or port from the fixture:
    Assert.DoesNotContain(_pg.OwnerConnectionString.Split(';')
        .FirstOrDefault(p => p.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))?
        .Split('=')[1] ?? "__NOHOSTFOUND__",
        body, StringComparison.OrdinalIgnoreCase);
}
```

---

### `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderHealthCheckTests.cs` (test, request-response)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakerLeaseHelperTests.cs`

**Why this analog:** Directly tests the same Redis lock primitives. Uses `[Collection("Redis")]`, `IAsyncLifetime`, `ConnectionMultiplexer.ConnectAsync`, `_server.FlushDatabaseAsync()` for clean-slate isolation, and `MatchmakerLeaseHelper` / `RedisMatchmakerLease` construction via options.

**Test class structure** (MatchmakerLeaseHelperTests.cs lines 31-60 — copy directly):
```csharp
[Collection("Redis")]
[Trait("Category", "Integration")]
public sealed class MatchmakingLeaderHealthCheckTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer? _mux;

    public MatchmakingLeaderHealthCheckTests(RedisFixture redis) => _redis = redis;

    public async Task InitializeAsync()
    {
        var opts = ConfigurationOptions.Parse(_redis.ConnectionString);
        opts.AllowAdmin = true;
        _mux = await ConnectionMultiplexer.ConnectAsync(opts);
        await _mux.GetServer(_mux.GetEndPoints().First()).FlushDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null) await _mux.DisposeAsync();
    }

    [Fact]
    public async Task CheckHealthAsync_Returns_Healthy_When_This_Replica_Holds_Lock()
    { /* acquire via TryAcquireLeaseAsync → CheckHealthAsync → Assert Healthy */ }

    [Fact]
    public async Task CheckHealthAsync_Returns_Degraded_When_Another_Replica_Holds_Lock()
    { /* helper1 acquires → helper2's check → Assert Degraded (not Unhealthy) */ }

    [Fact]
    public async Task CheckHealthAsync_Returns_Degraded_When_Lock_Unheld()
    { /* no holder → Assert Degraded, description contains "unheld" */ }
}
```

---

### `tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs` (test, request-response)

**Analog:** `tests/GameKit.Admin.Tests/AdminAuthServiceTests.cs`

**Why this analog:** Unit test using Moq stubs. `AdminAuthServiceTests` demonstrates the `Mock<T>` + `mock.Object` + `new SomeService(stubs...)` + `await svc.MethodAsync()` + `Assert.*` pattern without spinning up a full host.

**Unit test pattern** (AdminAuthServiceTests.cs lines 17-60):
```csharp
// No [Collection] needed — pure unit test, no containers
public class HealthProbeServiceDelegationTests
{
    [Fact]
    public async Task ProbeAsync_Delegates_To_HealthCheckService_Not_NpgsqlConnection()
    {
        // Verify: no NpgsqlConnection / IDatabase constructor parameter on HealthProbeService
        // after refactor — constructor no longer takes GameKitOptions or IConnectionMultiplexer
        // for probe purposes.
        var ctors = typeof(HealthProbeService).GetConstructors();
        foreach (var ctor in ctors)
        {
            var paramTypes = ctor.GetParameters().Select(p => p.ParameterType);
            Assert.DoesNotContain(typeof(Npgsql.NpgsqlConnection), paramTypes);
            Assert.DoesNotContain(typeof(GameKitOptions), paramTypes);
        }
    }
}
```

---

## Shared Patterns

### GPL Header + XML Docs
**Source:** Every existing `.cs` file in the repo
**Apply to:** Every new file in this phase
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```
XML doc comments required on every `public` type and member (CLAUDE.md constraint).

### `internal sealed` for implementation types
**Source:** `MatchmakingMigrationHostedService.cs` (line 36), `RedisMatchmakerLease.cs` (line 35)
**Apply to:** All six `*MigrationReadinessReporter` classes, `MigrationAggregateHealthCheck`, `PostgresHealthCheck`, `GameKitHealthResponseWriter`, `MatchmakingLeaderHealthCheck`
```csharp
internal sealed class MatchmakingMigrationHostedService : IHostedService
```

### `ArgumentNullException.ThrowIfNull` guard
**Source:** `GameKitObservabilityBuilderExtensions.cs` line 92, `RedisMatchmakerLease.cs` lines 56-58
**Apply to:** Every public/internal constructor and extension method that receives reference parameters
```csharp
ArgumentNullException.ThrowIfNull(builder);
ArgumentNullException.ThrowIfNull(redis);
ArgumentNullException.ThrowIfNull(options);
```

### Optional DI dependency pattern (`? redis = null`)
**Source:** `HealthProbeService.cs` lines 44-45
**Apply to:** `PostgresHealthCheck`, `HealthProbeService` (post-refactor) — any dependency that may be absent on Core-only installs
```csharp
IConnectionMultiplexer? redis = null,
IRedisErrorRateCounter? redisErrors = null
```

### ConfigureAwait(false)
**Source:** `MatchmakingMigrationHostedService.cs` lines 68-70, `RedisMatchmakerLease.cs` lines 73, 89
**Apply to:** Every `await` in library code
```csharp
await db.LockTakeAsync(_lockKey, InstanceId, _ttl).ConfigureAwait(false);
await reporter.IsReadyAsync(ct).ConfigureAwait(false);
```

### `volatile bool _latched` latch pattern
**Source:** pattern described in RESEARCH.md — no existing analog in the repo
**Apply to:** All six `*MigrationReadinessReporter` implementations
```csharp
private volatile bool _latched;

if (_latched) return true;
// ... query ...
if (!pending.Any()) { _latched = true; return true; }
return false;
```

### Per-package migration context construction
**Source:** `MatchmakingMigrationHostedService.BuildMatchmakingMigrationContext()` (lines 78-99), `AuthMigrationHostedService.BuildAuthMigrationContext()` (lines 67-84)
**Apply to:** All six reporter `Build*MigrationContext` static helpers
```csharp
// Each reporter copies its package's Build*MigrationContext method exactly
// (same MigrationsAssembly, MigrationsHistoryTable, ModelCustomizer, optional ConfigureWarnings)
```

### Test: `[Collection]` + `[Trait("Category", "Integration")]`
**Source:** `HealthProbeTests.cs` lines 22-23, `MatchmakerLeaseHelperTests.cs` lines 29-30
**Apply to:** All new integration test classes
```csharp
[Collection("PostgresAndRedis")]   // or "Redis" for lease-only tests
[Trait("Category", "Integration")]
```

---

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `src/GameKit.Core/Health/GameKitHealthResponseWriter.cs` | utility | request-response | No custom ASP.NET Core `ResponseWriter` pattern exists anywhere in the GameKit codebase. Use RESEARCH.md Pattern 5 (Utf8JsonWriter approach) as the template. |

---

## Metadata

**Analog search scope:** `src/`, `tests/`, `samples/`
**Files scanned:** 26
**Pattern extraction date:** 2026-06-14

**Key patterns confirmed by reading actual source:**
1. GPL SPDX header + XML docs required on every public API — no exceptions.
2. `internal sealed` is the default visibility for all health-check implementation types.
3. `Build*MigrationContext` static factory in each `*MigrationHostedService` is the exact template for each reporter's context builder.
4. `MatchmakingMigrationHostedService` (and Rankings, Lobby) MUST include `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` — Core and Auth do NOT need it.
5. `AddGameKitObservability()` in `GameKitObservabilityBuilderExtensions.cs` is the canonical shape for `AddGameKitHealthChecks()` — same `this IGameKitBuilder builder` receiver, `ArgumentNullException.ThrowIfNull`, operates on `builder.Services`.
6. `MapGameKit()` in `GameKitApplicationBuilderExtensions.cs` is the canonical shape for `MapGameKitHealth()` — same `this IEndpointRouteBuilder routes`, `ArgumentNullException.ThrowIfNull`, returns `routes`.
7. `HealthProbeService.ProbePostgresAsync` is the exact probe body to lift into `PostgresHealthCheck.CheckHealthAsync` — adapt: replace `ex.GetType().Name` in the catch with a hand-authored constant string (`"database unreachable"`) per D-12.
8. `TryAcquireLeaseAsync` / `ReleaseLeaseAsync` in `RedisMatchmakerLease` are the template for `QueryLeaseAsync` — same `_redis.GetDatabase()`, same try/catch/LogWarning, same `.ConfigureAwait(false)`.
9. `MatchmakerLeaseHelperTests` is the direct template for `MatchmakingLeaderHealthCheckTests` — copy `IAsyncLifetime`, `FlushDatabaseAsync`, `ConnectionMultiplexer.ConnectAsync`, `AllowAdmin = true`.
10. `TicTacToeDuel/Program.cs` — `AddGameKitHealthChecks()` must be called AFTER `AddLobby()` (last Redis-using sibling) and `MapGameKitHealth()` must be the first `Map*` call in the flat pipeline (before any auth group).
