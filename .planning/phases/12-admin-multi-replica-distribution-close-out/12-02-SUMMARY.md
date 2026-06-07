---
phase: 12-admin-multi-replica-distribution-close-out
plan: 02
subsystem: ui
tags: [blazor, mudblazor, rankings, admin-ui, integration-test, audit-log, rank-adjust]

# Dependency graph
requires:
  - phase: 04-rankings
    provides: IRankAdjustService, RankAdjustDialog.razor, admin_audit_log row with action admin.player.rank_adjust
  - phase: 03-admin-ui
    provides: IPlayerSearchService, IDialogService, MissingPackageAlert, PlayerDetailPane.OpenBanDialog pattern

provides:
  - Working /admin/rankings/adjust page: player-search + IDialogService.ShowAsync<RankAdjustDialog> launch
  - SC#3 integration test proving IRankAdjustService.AdjustAsync writes admin_audit_log row
  - InternalsVisibleTo(GameKit.Admin.Integration.Tests) grant in Rankings for test-time configuration access

affects:
  - any future plan touching RankAdjust.razor, admin audit test patterns, or Rankings+Admin.UI cross-package testing

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Replace Type.GetType reflection DI guard with Sp.GetService<TService>() is not null"
    - "AdminTestHost with Rankings migration: ApplyRankingsMigrationAsync + RankAdjustRuntimeQueryCustomizer"
    - "configureExtraServices: register scoped services directly (not full AddRankings) to avoid hosted service conflicts"

key-files:
  created:
    - tests/GameKit.Admin.Integration.Tests/RankAdjustServiceTests.cs
  modified:
    - src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor
    - tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj
    - src/GameKit.Rankings/AssemblyInfo.cs

key-decisions:
  - "Direct DI check (Sp.GetService<IRankAdjustService>() is not null) replaces fragile Type.GetType reflection guard per RESEARCH anti-pattern guidance"
  - "In RankAdjustServiceTests, register IRankAdjustService/IValidator<RankAdjustRequest> directly via configureExtraServices rather than calling AddRankings() to avoid StartupLadderUpserter conflicts in test host"
  - "RankAdjustRuntimeQueryCustomizer extends AdminRuntimeQueryCustomizer pattern with all 7 Rankings entity configurations so GameKitDbContext.Set<Ladder>()/Set<PlayerRank>() resolve correctly"

patterns-established:
  - "AdminTestHost + Rankings integration test: apply Rankings migration separately before StartAsync, then register services directly in configureExtraServices"

requirements-completed: [ADMIN-15]

# Metrics
duration: 25min
completed: 2026-06-06
---

# Phase 12 Plan 02: Rank-Adjust Close-Out Summary

**Dead /admin/rankings/adjust stub replaced with player-search + IDialogService.ShowAsync<RankAdjustDialog> flow; SC#3 integration test proves AdjustAsync writes admin_audit_log row (action 'admin.player.rank_adjust') against real Postgres**

## Performance

- **Duration:** ~25 min
- **Started:** 2026-06-06T00:00:00Z
- **Completed:** 2026-06-06T00:00:00Z
- **Tasks:** 2
- **Files modified:** 4 (1 created, 3 modified)

## Accomplishments

- Replaced the dead "Rank adjust flow will render when GameKit.Rankings ships" placeholder in `RankAdjust.razor` with a functional MudTextField player-search and `IDialogService.ShowAsync<RankAdjustDialog>` flow mirroring `PlayerDetailPane.OpenBanDialog` exactly
- Dropped the fragile `Type.GetType("GameKit.Rankings.IRankingAlgorithm, GameKit.Rankings")` reflection guard; replaced with direct `Sp.GetService<IRankAdjustService>() is not null` DI check
- Created SC#3 integration test that seeds a player and ladder, calls `IRankAdjustService.AdjustAsync`, and asserts an `admin_audit_log` row exists with `action == "admin.player.rank_adjust"` and the correct `ActorId`
- Added `InternalsVisibleTo("GameKit.Admin.Integration.Tests")` to Rankings `AssemblyInfo.cs` so the test-time `RankAdjustRuntimeQueryCustomizer` can access `LadderConfiguration`, `PlayerRankConfiguration`, etc.

## Task Commits

1. **Task 1: Replace dead RankAdjust.razor stub** - `642fe5c` (feat)
2. **Task 2: SC#3 integration test** - `ebb79ec` (feat)

## Files Created/Modified

- `src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor` - Dead stub replaced with working player-search + dialog-launch flow
- `tests/GameKit.Admin.Integration.Tests/RankAdjustServiceTests.cs` - SC#3 integration test: seeds player+ladder, calls AdjustAsync, asserts admin_audit_log row
- `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj` - Added ProjectReference to GameKit.Rankings
- `src/GameKit.Rankings/AssemblyInfo.cs` - Added InternalsVisibleTo(GameKit.Admin.Integration.Tests)

## Decisions Made

- **Direct DI check over reflection:** Replaced `Type.GetType("GameKit.Rankings.IRankingAlgorithm, ...")` with `Sp.GetService<GameKit.Rankings.Services.IRankAdjustService>() is not null` — more robust, survives assembly renames, avoids false negatives if the type was loaded via a different load context.
- **Minimal service registration in test:** Instead of calling `b.AddRankings()` via an `IGameKitBuilder` (which would register `RankingsMigrationHostedService`, `StartupLadderUpserter`, etc. and require ladder config), the test registers only `IRankAdjustService`, `IValidator<RankAdjustRequest>`, and `IOptions<GameKitRankingsOptions>` directly via `configureExtraServices`.
- **Separate Rankings migration pass:** `ApplyRankingsMigrationAsync` applies the Rankings migration before `AdminTestHost.StartAsync` (which runs Core+Auth+Admin). This matches the established per-package migration pattern.
- **`RankAdjustRuntimeQueryCustomizer`:** A new customizer is created in the test file applying all Auth+Admin+Rankings entity configurations so the test-time `GameKitDbContext` can resolve `Set<Ladder>()`, `Set<PlayerRank>()`, and `Set<AdminAuditLog>()` in a single context.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Added `@using Microsoft.Extensions.DependencyInjection` to RankAdjust.razor**
- **Found during:** Task 1 (RankAdjust.razor replacement)
- **Issue:** `Sp.GetService<T>()` is a generic extension method from `Microsoft.Extensions.DependencyInjection`; without the using directive the Razor compiler emits CS0308 (non-generic `IServiceProvider.GetService(Type)` cannot be used with type arguments).
- **Fix:** Added `@using Microsoft.Extensions.DependencyInjection` to the component's using block.
- **Files modified:** `src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor`
- **Verification:** `dotnet build GameKit.Admin.UI.csproj` passes clean after the fix.
- **Committed in:** `642fe5c`

**2. [Rule 3 - Blocking] Added InternalsVisibleTo + ProjectReference for internal Rankings configurations**
- **Found during:** Task 2 (RankAdjustServiceTests.cs)
- **Issue:** `LadderConfiguration`, `PlayerRankConfiguration`, etc. are `internal sealed class` in `GameKit.Rankings`. Without `InternalsVisibleTo("GameKit.Admin.Integration.Tests")` in Rankings `AssemblyInfo.cs` and a `ProjectReference` in the test `.csproj`, the `RankAdjustRuntimeQueryCustomizer` inside the test file cannot access these configurations.
- **Fix:** Added InternalsVisibleTo grant in Rankings AssemblyInfo.cs; added ProjectReference to test csproj.
- **Files modified:** `src/GameKit.Rankings/AssemblyInfo.cs`, `tests/GameKit.Admin.Integration.Tests/GameKit.Admin.Integration.Tests.csproj`
- **Verification:** `dotnet build GameKit.Admin.Integration.Tests.csproj` passes clean after the fixes.
- **Committed in:** `ebb79ec`

---

**Total deviations:** 2 auto-fixed (1 missing-using Rule 2, 1 blocking access Rule 3)
**Impact on plan:** Both fixes necessary for compilation and test execution. No scope creep — plan did not specify the specific using or InternalsVisibleTo because they arise naturally during implementation.

## Issues Encountered

None beyond the two auto-fixed deviations above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- ADMIN-15 fully closed: the dead stub is gone, the working dialog is reachable, and SC#3 is green.
- Rankings+Admin integration testing pattern established (`RankAdjustRuntimeQueryCustomizer`, `ApplyRankingsMigrationAsync`) is reusable for future cross-package tests.

## Known Stubs

None - the RankAdjust.razor page is fully wired. The dialog (`RankAdjustDialog.razor`) was already complete before this plan.

## Threat Flags

No new security surface introduced. The `@attribute [Authorize(Policy = AdminPolicies.Superadmin)]` directive is retained verbatim (T-12-02-EOP mitigation). The audit write happens inside `IRankAdjustService.AdjustAsync` (T-12-02-REP mitigation), not in the page.

## Self-Check: PASSED

Files present:
- FOUND: src/GameKit.Admin.UI/Components/Pages/RankAdjust.razor
- FOUND: tests/GameKit.Admin.Integration.Tests/RankAdjustServiceTests.cs

Commits:
- FOUND: 642fe5c (feat(12-02): replace dead RankAdjust.razor stub)
- FOUND: ebb79ec (feat(12-02): SC#3 integration test + Rankings project ref)

Build: `dotnet build GameKit.sln -warnaserror` → Build succeeded, 0 warnings, 0 errors.
Tests: `dotnet test --filter RankAdjust` → Passed: 2, Failed: 0.

---
*Phase: 12-admin-multi-replica-distribution-close-out*
*Completed: 2026-06-06*
