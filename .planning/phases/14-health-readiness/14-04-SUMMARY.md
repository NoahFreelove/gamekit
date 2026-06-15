---
phase: 14-health-readiness
plan: "04"
subsystem: admin-health
tags: [health, admin, refactor, migration-readiness, delegation, hlth-02, hlth-06]
dependency_graph:
  requires: ["14-01"]
  provides: ["admin-health-panel-delegation", "admin-migration-reporter"]
  affects: ["GameKit.Admin.UI", "tests/GameKit.Admin.Tests"]
tech_stack:
  added: []
  patterns:
    - "HealthCheckService delegation (thin adapter over Core health checks)"
    - "IMigrationReadinessReporter latch pattern (volatile bool, steady-state zero-query)"
    - "Reflection-based unit test for constructor contract"
key_files:
  created:
    - src/GameKit.Admin.UI/Health/AdminMigrationReadinessReporter.cs
    - tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs
  modified:
    - src/GameKit.Admin.UI/Services/HealthProbeService.cs
    - src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs
decisions:
  - "HealthProbeService no longer opens NpgsqlConnection or calls IDatabase.PingAsync; delegates exclusively to HealthCheckService.CheckHealthAsync() (D-15 / HLTH-06)"
  - "Error-rate tile remains Admin-local via ErrorRateRingBuffer/IRedisErrorRateCounter (D-16 unchanged)"
  - "AdminMigrationReadinessReporter has no ConfigureWarnings — Admin snapshot matches model hash exactly"
  - "Defensive AddHealthChecks() in AddGameKitAdmin ensures HealthCheckService resolvable even without AddGameKitHealthChecks() (T-14-10)"
  - "Wave-0 unit test is reflection-only; runtime GetTile mapping covered by Wave-3 integration tests"
metrics:
  duration: "~10 minutes"
  completed: "2026-06-14"
  tasks_completed: 3
  tasks_total: 3
  files_changed: 4
---

# Phase 14 Plan 04: Admin Health Delegation + Sixth Migration Reporter Summary

HealthProbeService refactored as a thin adapter over Core HealthCheckService delegating Postgres/Redis tiles; AdminMigrationReadinessReporter added as the sixth and final IMigrationReadinessReporter in the six-reporter set.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Refactor HealthProbeService to delegate to HealthCheckService | f9e59e8 | HealthProbeService.cs, AdminBuilderExtensions.cs |
| 2 | Admin migration readiness reporter (sixth reporter) + self-registration | 0d47adf | AdminMigrationReadinessReporter.cs, AdminBuilderExtensions.cs |
| 3 | Wave-0 delegation unit test (HLTH-06 source assertion) | 12f2342 | HealthProbeServiceDelegationTests.cs |

## What Was Built

### Task 1: HealthProbeService delegation refactor (HLTH-06 / D-15)

Rewrote `HealthProbeService` in `src/GameKit.Admin.UI/Services/HealthProbeService.cs`:

- **Removed:** `ProbePostgresAsync` (lines 66-87, the raw `NpgsqlConnection` + `SELECT 1` path) and `ProbeRedisAsync` (lines 89-107, the raw `IDatabase.PingAsync` path)
- **Removed constructor params:** `GameKitOptions gameKitOpts` and the probe-purpose `IConnectionMultiplexer? redis`
- **Added constructor param:** `Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService healthCheckService`
- **Added:** `GetTile(CoreHealthReport, string)` private static method that projects Core health entries by name to `HealthTile` view records using the locked status map: `Healthy→"OK"`, `Degraded→"Degraded"`, `Unhealthy→"Down"`, absent entry→`"Down"/"not configured"`
- **Kept verbatim:** `ProbeErrorRateAsync` (Admin-local D-16 error-rate tile via `ErrorRateRingBuffer` / `IRedisErrorRateCounter`)
- **Added to AdminBuilderExtensions:** Defensive `builder.Services.AddHealthChecks()` call (idempotent TryAddSingleton) so `HealthCheckService` is always resolvable even on Admin-without-Core-health-checks installs (T-14-10 mitigation)

The `HealthReport` / `HealthTile` view records, `Health.razor`, `HealthTileView.razor`, and `StatusChip.razor` are **zero-edited** per UI-SPEC constraint.

### Task 2: AdminMigrationReadinessReporter (sixth reporter, HLTH-02 / D-05)

Created `src/GameKit.Admin.UI/Health/AdminMigrationReadinessReporter.cs`:

- `internal sealed class AdminMigrationReadinessReporter : IMigrationReadinessReporter`
- Latch pattern (`volatile bool _latched`) — steady-state queries zero Postgres once Admin migrations are applied (D-07 / T-14-04b)
- `BuildAdminMigrationContext` copied verbatim from `AdminMigrationHostedService` lines 68-82
- No `ConfigureWarnings` — Admin snapshot matches EF Core model hash exactly (per-package variation table)
- Registered via `builder.Services.AddSingleton<IMigrationReadinessReporter, AdminMigrationReadinessReporter>()` in `AddGameKitAdmin`, immediately after `AddHostedService<AdminMigrationHostedService>()`

Completes the six-reporter set: **Core / Auth / Rankings / Lobby / Matchmaking / Admin** — all enumerable singletons discovered by `MigrationAggregateHealthCheck` via `IEnumerable<IMigrationReadinessReporter>`.

### Task 3: Wave-0 delegation unit test

Created `tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs`:

- `ProbeAsync_Delegates_To_HealthCheckService_Not_NpgsqlConnection`: reflection over all `HealthProbeService` constructors, asserts no `NpgsqlConnection`, `GameKitOptions`, or `IConnectionMultiplexer` parameter
- `Constructor_Takes_HealthCheckService`: asserts a `HealthCheckService` parameter IS present on at least one constructor
- Pure unit test, no containers — 14ms execution time
- Both tests pass

## Verification Results

```
dotnet build src/GameKit.Admin.UI/GameKit.Admin.UI.csproj -p:NuGetAudit=false
  → Build succeeded, 0 Warning(s), 0 Error(s)

dotnet test tests/GameKit.Admin.Tests/ -p:NuGetAudit=false --filter "FullyQualifiedName~HealthProbeServiceDelegationTests"
  → Passed! Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 14 ms

grep -c "NpgsqlConnection|\.PingAsync|GameKitOptions" src/GameKit.Admin.UI/Services/HealthProbeService.cs
  → 1 (XML doc comment reference only — no code)

grep -c "ProbePostgresAsync|ProbeRedisAsync" src/GameKit.Admin.UI/Services/HealthProbeService.cs
  → 0

grep -n "AddSingleton<IMigrationReadinessReporter, AdminMigrationReadinessReporter>" src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs
  → 81: (line 81)

git diff --stat HEAD on view files (Health.razor, HealthTileView.razor, StatusChip.razor, HealthReport.cs)
  → NONE — zero view edits (UI-SPEC preserved)
```

## Deviations from Plan

None — plan executed exactly as written.

The one noted `grep -c` return of 1 for `NpgsqlConnection` is in an XML doc comment (`/// The previous direct <c>NpgsqlConnection</c> / <c>IDatabase.PingAsync</c> logic has been removed...`), not code — the raw probe logic is absent. This matches the intent of the acceptance criterion (no actual probe code remains).

## Known Stubs

None. All tiles are wired to live data sources: Postgres/Redis from `HealthCheckService.CheckHealthAsync()`, error rate from `ErrorRateRingBuffer`/`IRedisErrorRateCounter`.

## Threat Flags

No new network endpoints, auth paths, file access patterns, or schema changes introduced. The threat register entries T-14-09 and T-14-10 are mitigated as specified: the `GetTile` projection removes the old `ex.GetType().Name` leakage path, and `AddHealthChecks()` ensures graceful degradation without DI throws.

## Self-Check: PASSED

- `src/GameKit.Admin.UI/Health/AdminMigrationReadinessReporter.cs` — FOUND
- `tests/GameKit.Admin.Tests/HealthProbeServiceDelegationTests.cs` — FOUND
- Commit f9e59e8 — FOUND (refactor: HealthProbeService delegation)
- Commit 0d47adf — FOUND (feat: AdminMigrationReadinessReporter)
- Commit 12f2342 — FOUND (test: HealthProbeServiceDelegationTests)
