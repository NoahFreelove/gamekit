---
phase: 14-health-readiness
plan: "01"
subsystem: core-health
tags: [health, readiness, migrations, redis, postgres]
dependency_graph:
  requires: []
  provides:
    - IMigrationReadinessReporter (public interface — six-package contract)
    - AddGameKitHealthChecks() returning IHealthChecksBuilder
    - MapGameKitHealth() on IEndpointRouteBuilder
    - GameKitHealthResponseWriter (whitelist ResponseWriter)
    - PostgresHealthCheck (SELECT 1, 2s timeout)
    - RedisHealthCheck (PING, conditional)
    - MigrationAggregateHealthCheck (aggregate over IEnumerable<IMigrationReadinessReporter>)
    - CoreMigrationReadinessReporter (DI GameKitDbContext, volatile _latched)
  affects:
    - src/GameKit.Core/GameKit.Core.csproj (StackExchange.Redis PackageReference added)
    - sibling plans 14-02..14-05 depend on IMigrationReadinessReporter and AddGameKitHealthChecks
tech_stack:
  added:
    - StackExchange.Redis PackageReference in GameKit.Core.csproj (already in Directory.Packages.props at 2.8.41; no new pin)
  patterns:
    - IHealthCheck with hand-authored descriptions (D-12/HLTH-05)
    - volatile bool _latched for once-per-lifetime migration readiness (D-07)
    - Utf8JsonWriter whitelist ResponseWriter (HLTH-05)
    - IServiceScopeFactory for scoped DbContext in singleton health check
    - Tag-based live/ready split (D-03): Predicate=_=>false vs Tags.Contains("ready")
    - Conditional IConnectionMultiplexer check registration (D-09)
key_files:
  created:
    - src/GameKit.Core/Health/IMigrationReadinessReporter.cs
    - src/GameKit.Core/Health/MigrationAggregateHealthCheck.cs
    - src/GameKit.Core/Health/CoreMigrationReadinessReporter.cs
    - src/GameKit.Core/Health/PostgresHealthCheck.cs
    - src/GameKit.Core/Health/RedisHealthCheck.cs
    - src/GameKit.Core/Health/GameKitHealthResponseWriter.cs
    - src/GameKit.Core/Builder/GameKitHealthBuilderExtensions.cs
  modified:
    - src/GameKit.Core/GameKit.Core.csproj
decisions:
  - "CoreMigrationReadinessReporter uses IServiceScopeFactory (not ctor-injected GameKitDbContext) because the aggregate check is singleton and DbContext is scoped"
  - "StackExchange.Redis added as PackageReference to GameKit.Core.csproj so IConnectionMultiplexer type is resolvable at compile time without forcing Redis on consumers (check only registered when multiplexer is present in DI)"
  - "cref for GetPendingMigrationsAsync and AddGameKitHealthChecks converted to <c>...</c> text to avoid CS1574 doc warnings during compilation"
metrics:
  duration: 4 minutes
  completed_date: "2026-06-15"
  tasks: 3
  files: 8
---

# Phase 14 Plan 01: Core Health Foundation Summary

**One-liner:** Built-in ASP.NET Core health checks with tag-based live/ready split, conditional Redis gate, migration-aggregate check with latch, and whitelist Utf8JsonWriter response writer.

## What Was Built

Seven new files providing the complete health-check foundation for Phase 14:

- **`IMigrationReadinessReporter`** — public interface with `ValueTask<bool> IsReadyAsync(CancellationToken)` and documented D-07 latch contract (six packages implement this)
- **`MigrationAggregateHealthCheck`** — iterates `IEnumerable<IMigrationReadinessReporter>`, counts pending, returns `"{N} of {total} migration sets pending"` or `"all migration sets applied"`
- **`CoreMigrationReadinessReporter`** — singleton that resolves a scoped `GameKitDbContext` via `IServiceScopeFactory` per probe, calls `GetPendingMigrationsAsync()`, and latches after first `true`
- **`PostgresHealthCheck`** — `SELECT 1` with `CommandTimeout = 2`, returns `"connected"` or `"database unreachable"` (bare `catch`, no ex.Message)
- **`RedisHealthCheck`** — `IDatabase.PingAsync()`, returns `"ping ok"` or `"ping failed"` (bare `catch`)
- **`GameKitHealthResponseWriter`** — `Utf8JsonWriter` over `MemoryStream` emitting only `{status, checks:[{name,status,description}]}`; `Exception`/`Data`/`Tags` intentionally omitted
- **`GameKitHealthBuilderExtensions`** — `AddGameKitHealthChecks()` returning `IHealthChecksBuilder`; `MapGameKitHealth()` mapping `/health/live` (Predicate=`_=>false`) and `/health/ready` (tag-filtered, Degraded→200, Unhealthy→503)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] CS1574 cref resolution on forward-declared type**
- **Found during:** Task 1 first build
- **Issue:** `<see cref="DatabaseFacade.GetPendingMigrationsAsync"/>` in `CoreMigrationReadinessReporter.cs` could not be resolved
- **Fix:** Changed to `<c>GetPendingMigrationsAsync</c>` plain text
- **Files modified:** `src/GameKit.Core/Health/CoreMigrationReadinessReporter.cs`
- **Commit:** 73e9808

**2. [Rule 3 - Blocking] StackExchange.Redis not referenced in GameKit.Core.csproj**
- **Found during:** Task 2 first build
- **Issue:** `IConnectionMultiplexer` type from `StackExchange.Redis` was not resolvable in `RedisHealthCheck.cs` because `GameKit.Core.csproj` did not have a `PackageReference` for it
- **Fix:** Added `<PackageReference Include="StackExchange.Redis" />` to `GameKit.Core.csproj`. The package was already pinned at 2.8.41 in `Directory.Packages.props` (no new pin added — D-01 preserved)
- **Files modified:** `src/GameKit.Core/GameKit.Core.csproj`
- **Commit:** 118aaed

**3. [Rule 3 - Blocking] CS1574 cref on forward-declared AddGameKitHealthChecks**
- **Found during:** Task 2 first build (same build as deviation #2)
- **Issue:** `<see cref="Builder.GameKitHealthBuilderExtensions.AddGameKitHealthChecks"/>` in `RedisHealthCheck.cs` could not be resolved (the class hadn't been created yet)
- **Fix:** Changed to `<c>GameKitHealthBuilderExtensions.AddGameKitHealthChecks</c>` plain text
- **Files modified:** `src/GameKit.Core/Health/RedisHealthCheck.cs`
- **Commit:** 118aaed

## Security Review

- HLTH-05 / D-12: All three check classes use bare `catch { }` blocks returning hand-authored constant descriptions. `ex.Message`, `ex.GetType().Name`, and `ex.ToString()` are absent from all code paths. Verified by `grep` returning zero non-comment matches.
- `GameKitHealthResponseWriter` explicitly omits `HealthReportEntry.Exception`, `.Data`, and `.Tags` — only `name`, `status`, `description` are written.
- Both health endpoints call `.AllowAnonymous()` — orchestrator probes are intentionally anonymous (T-14-03, accepted disposition).
- No connection strings, hostnames, or ports appear in any check description.

## Threat Flags

None. All threat model items (T-14-01, T-14-02, T-14-03, T-14-SC) have `mitigate` or `accept` dispositions that are fully addressed by the implementation.

## Known Stubs

None. All seven files are fully implemented and functional. No placeholder return values or TODO comments that would affect the plan's goal.

## Commits

| Task | Hash | Description |
|------|------|-------------|
| Task 1 | 73e9808 | feat(14-01): add IMigrationReadinessReporter contract, aggregate check, and Core reporter |
| Task 2 | 118aaed | feat(14-01): add Postgres/Redis health checks and whitelist ResponseWriter |
| Task 3 | 095731d | feat(14-01): add AddGameKitHealthChecks() and MapGameKitHealth() public surface |

## Self-Check: PASSED

All 7 created files verified present on disk. All 3 task commits verified in git log.

| Check | Result |
|-------|--------|
| `IMigrationReadinessReporter.cs` | FOUND |
| `MigrationAggregateHealthCheck.cs` | FOUND |
| `CoreMigrationReadinessReporter.cs` | FOUND |
| `PostgresHealthCheck.cs` | FOUND |
| `RedisHealthCheck.cs` | FOUND |
| `GameKitHealthResponseWriter.cs` | FOUND |
| `GameKitHealthBuilderExtensions.cs` | FOUND |
| Commit 73e9808 | FOUND |
| Commit 118aaed | FOUND |
| Commit 095731d | FOUND |
