---
phase: 09-regional-matchmaking-pools-backfill
plan: "02"
subsystem: matchmaking
tags: [redis, dotnet, efcore, asp-net-core, regional-pools, matchmaking, integration-tests]

# Dependency graph
requires:
  - phase: 09-regional-matchmaking-pools-backfill
    plan: "01"
    provides: AllowedRegions config field, MatchmakingLadderConfig, EnqueueOutcome enum, RED test scaffolds

provides:
  - RegionName field on EnqueueRequest with FluentValidation character-class + length guard
  - InvalidRegion = 8 on EnqueueOutcome enum
  - AllowedRegions membership check in MatchmakingService.EnqueueAsync
  - Pool resolution RegionName → resolvedPool in MatchmakingEndpoints.EnqueueAsync
  - GetPoolNamesForLadder helper in MatchmakerTickerService (yields "default" + AllowedRegions)
  - Per-pool lease renewal before each ProcessPoolAsync call
  - RegionalPoolTests (4 facts) green: SC1_Enqueue_MismatchedRegionName_Returns400, SC1_NullRegion_RoutesToDefaultPool, SC2_RegionalKey_IsDistinctFromDefaultKey, SC2_TickerGlob_PicksUpBothRegionalAndDefaultKeys
  - Core migration 20260606193305_AddParticipationFractionToSnapshot (fixes PendingModelChangesWarning blocker)
  - Matchmaking migration 20260520000000 uses IF NOT EXISTS for ParticipationFraction (idempotency)

affects:
  - 09-03-PLAN.md (backfill tickets)
  - 09-04-PLAN.md (participation fraction rating guard)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "GetPoolNamesForLadder: static helper that yields 'default' then AllowedRegions entries — used to drive the ticker's per-pool loop"
    - "Cross-package column addition idempotency: raw SQL uses IF NOT EXISTS when a Core migration may have already added the column"
    - "RegionName → resolvedPool: endpoint resolves at HTTP layer before passing to service, keeping IMatchmakingService signature unchanged"

key-files:
  created:
    - src/GameKit.Core/Migrations/20260606193305_AddParticipationFractionToSnapshot.cs
    - src/GameKit.Core/Migrations/20260606193305_AddParticipationFractionToSnapshot.Designer.cs
  modified:
    - src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs
    - src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs
    - src/GameKit.Matchmaking/Services/IMatchmakingService.cs
    - src/GameKit.Matchmaking/Services/MatchmakingService.cs
    - src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs
    - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
    - src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs
    - src/GameKit.Matchmaking/Migrations/GameKitDbContextModelSnapshot.cs
    - src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs
    - tests/GameKit.Matchmaking.Integration.Tests/RegionalPoolTests.cs

key-decisions:
  - "RegionName resolution happens at HTTP layer: endpoint resolves req.RegionName ?? req.PoolName and passes as poolName to service — IMatchmakingService signature unchanged"
  - "Validation is two-layer: FluentValidation regex ^[a-zA-Z0-9\\-]+$ (character-class guard, T-09-02-01) + AllowedRegions membership check in service (null/default bypasses membership check)"
  - "GetPoolNamesForLadder always yields 'default' first, then AllowedRegions — guarantees backward compat for ladders with no AllowedRegions"
  - "Core migration AddParticipationFractionToSnapshot fixes pre-existing PendingModelChangesWarning: SessionParticipant.ParticipationFraction was added to entity but Core snapshot lacked it; Matchmaking raw SQL migration updated to IF NOT EXISTS for idempotency"

patterns-established:
  - "Per-pool ticker loop: outer loop over ladders, inner loop over GetPoolNamesForLadder(cfg), renew lease before each pool"
  - "Cross-package column idempotency: if both a Core migration and a Matchmaking raw-SQL migration can add the same column, use IF NOT EXISTS in the raw SQL"

requirements-completed: [MATCH-18]

# Metrics
duration: ~45min
completed: 2026-06-06
---

# Phase 9 Plan 02: Regional Pool Routing Summary

**MATCH-18 regional pool routing: RegionName HTTP field with FluentValidation guard, AllowedRegions membership check in MatchmakingService, GetPoolNamesForLadder ticker loop, all 4 RegionalPoolTests green**

## Performance

- **Duration:** ~45 min
- **Started:** 2026-06-06T00:00:00Z
- **Completed:** 2026-06-06T19:40:00Z
- **Tasks:** 3
- **Files modified:** 11

## Accomplishments

- Task 1: Added `RegionName` to `EnqueueRequest` record and `FluentValidation` validator with `^[a-zA-Z0-9\-]+$` + max 64 chars guard; added `InvalidRegion = 8` to `EnqueueOutcome` enum
- Task 2: Added AllowedRegions membership check in `MatchmakingService.EnqueueAsync` (`null` / `"default"` bypass list; region validation before any Redis write); updated `MatchmakingEndpoints.EnqueueAsync` to resolve `RegionName → pool` and map `InvalidRegion` to HTTP 400
- Task 3: Implemented `GetPoolNamesForLadder` in `MatchmakerTickerService`; updated inner loop with per-pool lease renewal; updated `MatchmakingTestApp` with `configureLadder` callback and `GetTicker()` helper; implemented all 4 `RegionalPoolTests` facts — all pass

## Task Commits

Each task was committed atomically:

1. **Task 1: EnqueueRequest.RegionName + validator + EnqueueOutcome.InvalidRegion** - `f55fae6` (feat)
2. **Task 2: MatchmakingService AllowedRegions guard + endpoint resolution** - `1a6c870` (feat)
3. **Task 3: Pool loop, RegionalPoolTests green, snapshot fix** - `0001965` (feat)

## Files Created/Modified

- `src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs` - Added `RegionName` as last optional parameter
- `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs` - Added `RegionName` regex + length rules
- `src/GameKit.Matchmaking/Services/IMatchmakingService.cs` - Added `InvalidRegion = 8` to `EnqueueOutcome`
- `src/GameKit.Matchmaking/Services/MatchmakingService.cs` - Added AllowedRegions guard before Redis writes
- `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` - `resolvedPool = req.RegionName ?? req.PoolName`; `InvalidRegion` → HTTP 400
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` - `GetPoolNamesForLadder` helper; per-pool inner loop with lease renewal
- `src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs` - `IF NOT EXISTS` on `ParticipationFraction` column
- `src/GameKit.Matchmaking/Migrations/GameKitDbContextModelSnapshot.cs` - Updated via `dotnet ef` (Ladder table name fix)
- `src/GameKit.Core/Migrations/20260606193305_AddParticipationFractionToSnapshot.cs` - New migration syncing Core snapshot
- `src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs` - Updated to include `ParticipationFraction`
- `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` - `configureLadder` callback; `GetTicker()` helper
- `tests/GameKit.Matchmaking.Integration.Tests/RegionalPoolTests.cs` - 4 facts implemented and passing

## Decisions Made

- `RegionName` resolution at HTTP layer (not service layer): keeps `IMatchmakingService` signature unchanged; `resolvedPool = req.RegionName ?? req.PoolName` in endpoint handler
- Two-layer validation: FluentValidation regex for character-class (T-09-02-01 security requirement — region name used as Redis key component); AllowedRegions membership check in service for business logic
- `GetPoolNamesForLadder` always yields `"default"` first — backward compat for ladders without `AllowedRegions`
- Cross-package snapshot fix: Core migration `AddParticipationFractionToSnapshot` takes ownership of the column in Core migrations; Matchmaking raw SQL uses `IF NOT EXISTS` for idempotency

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed pre-existing PendingModelChangesWarning that blocked all integration tests using ApplyMatchmakingMigrationsAsync**
- **Found during:** Task 3 (RegionalPoolTests verification)
- **Issue:** `SessionParticipant.ParticipationFraction` was added to the Core entity class in Plan 09-01 but no Core migration or snapshot update was created. EF Core 10's `Migrator.ValidateMigrations` compared the Core runtime model to the Core snapshot, detected the mismatch, and threw `PendingModelChangesWarning` as an exception on every call to `MigrateAsync`. The `ConfigureWarnings(Ignore)` suppression in the migration context builders did not apply to the Core DI context used by `ApplyMatchmakingMigrationsAsync`
- **Fix:** Added Core migration `20260606193305_AddParticipationFractionToSnapshot` (with proper Up/Down) to sync the Core snapshot. Updated Matchmaking migration `20260520000000_MatchmakingBackfillRegions` to use `ADD COLUMN IF NOT EXISTS` so it is idempotent when Core migration runs first on a fresh DB. Deleted leftover `ModelCheck2` migration stub from investigation
- **Files modified:** `src/GameKit.Core/Migrations/20260606193305_AddParticipationFractionToSnapshot.cs`, `src/GameKit.Core/Migrations/20260606193305_AddParticipationFractionToSnapshot.Designer.cs`, `src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs`, `src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs`
- **Verification:** `dotnet ef migrations has-pending-model-changes` returns clean for both Core and Matchmaking; `ReconcilerSweepTests` 5/5 green; `RegionalPoolTests` 4/4 green
- **Committed in:** `0001965` (Task 3 commit)

---

**Total deviations:** 1 auto-fixed (Rule 3 — blocking issue)
**Impact on plan:** Required fix to unblock integration test suite. No scope creep — the issue was a direct consequence of Plan 09-01 adding `ParticipationFraction` to the entity class without updating the Core snapshot.

## Issues Encountered

- EF Core 10's `Migrator.ValidateMigrations` threw `PendingModelChangesWarning` even when `ConfigureWarnings(Ignore)` was applied to migration contexts, because the Core DI context (from `AddGameKit()`) did not have the suppression. Root cause: Core snapshot was missing `ParticipationFraction` property. Design-time CLI (`dotnet ef migrations has-pending-model-changes`) and empty-migration check confirmed no actual schema diff — purely a snapshot hash mismatch. Fixed by adding proper Core migration + IF NOT EXISTS on the raw SQL.

## Known Stubs

None — all functionality fully wired.

## Threat Flags

None — no new network endpoints, auth paths, or trust-boundary schema changes beyond what the plan's threat model covers. `RegionName` is validated by FluentValidation (character-class regex) before service layer and against `AllowedRegions` in service before any Redis key composition (T-09-02-01 satisfied).

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Plan 09-03 (Backfill tickets) can proceed: `TicketType` column and Redis key helpers are in place from Plan 09-01; regional pool loop in ticker is operational
- Plan 09-04 (Participation fraction rating guard) can proceed: `ParticipationFraction` column is now properly tracked in Core migrations and will be applied on fresh DB installs

---
*Phase: 09-regional-matchmaking-pools-backfill*
*Completed: 2026-06-06*
