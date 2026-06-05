---
phase: 04-rankings-sessions-gdpr
plan: 08
subsystem: rankings-gdpr-export-rank-adjust
tags: [gdpr, repeatable-read, serializable, rank-adjust, audit, sample-app]
dependency_graph:
  requires: [04-05, 04-07]
  provides: [RANK-12, RANK-13]
  affects: [GameKit.Rankings, GameKit.Admin.UI, samples/TicTacToeDuel]
tech_stack:
  added: []
  patterns:
    - REPEATABLE READ + SET TRANSACTION READ ONLY (Pitfall 5) for consistent GDPR snapshot
    - SERIALIZABLE tx for atomic rank-adjust UPDATE + audit INSERT (SC#6)
    - D-22 invariant: Rankings writes AdminAuditLog directly via _ctx.Set<AdminAuditLog>() (not IAdminAuditWriter)
    - Raw Npgsql SQL for Auth entity queries in GdprExportService (no cross-package EF dependency)
    - FaultAfterFirstSaveInterceptor for SC#6 atomicity proof via EF Core SaveChangesInterceptor
key_files:
  created:
    - src/GameKit.Rankings/Services/IGdprExportService.cs
    - src/GameKit.Rankings/Services/GdprExportService.cs
    - src/GameKit.Rankings/Services/GdprExportPayloadTooLargeException.cs
    - src/GameKit.Rankings/Services/IRankAdjustService.cs
    - src/GameKit.Rankings/Services/RankAdjustService.cs
    - src/GameKit.Rankings/Http/Contracts/GdprExportResponse.cs
    - src/GameKit.Rankings/Http/Contracts/RankAdjustRequest.cs
    - src/GameKit.Rankings/Http/RankingsPlayerEndpoints.cs
    - src/GameKit.Rankings/Http/Validators/RankAdjustRequestValidator.cs
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Export.cs
    - src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor
    - tests/GameKit.Rankings.Integration.Tests/GdprExportContractTests.cs
    - tests/GameKit.Rankings.Integration.Tests/AdminRankAdjustTransactionTests.cs
  modified:
    - src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs
    - src/GameKit.Rankings/Builder/RankingsApplicationBuilderExtensions.cs
    - src/GameKit.Admin.UI/Services/AdminAuditActions.cs
    - src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs
    - src/GameKit.Admin.UI/Components/Layout/MainLayout.razor
    - samples/TicTacToeDuel/TicTacToeDuel.csproj
    - samples/TicTacToeDuel/Program.cs
    - samples/TicTacToeDuel/README.md
decisions:
  - "RankAdjustService writes AdminAuditLog directly via _ctx.Set<AdminAuditLog>() within the SERIALIZABLE tx — avoids IAdminAuditWriter (Admin.UI) reference which would violate D-22. Same pattern as EndSeasonService in plan 04-07."
  - "GdprExportService uses raw Npgsql SQL for player_identities and player_credentials queries — Rankings cannot reference GameKit.Auth (D-22 invariant). PascalCase quoted column names required (EF Core convention, no snake_case mapping)."
  - "SC#6 atomicity proof uses EF Core SaveChangesInterceptor (FaultAfterFirstSaveInterceptor) that throws after the second SaveChangesAsync; verifies both rating change and audit insert are rolled back by the SERIALIZABLE transaction dispose."
  - "TicTacToeDuel Program.cs captures IGameKitBuilder before chaining — IGameKitRankingsBuilder does not extend IGameKitBuilder, so AddGameKitAdmin cannot be chained from AddLadder."
metrics:
  completed_date: "2026-05-16"
  tasks_completed: 3
  tasks_total: 3
  files_created: 13
  files_modified: 9
---

# Phase 04 Plan 08: GDPR Export + Rank-Adjust + Sample App Summary

**One-liner:** REPEATABLE READ GDPR export (7-table snapshot, 25 MB cap, no password_hash) + SERIALIZABLE rank-adjust (UPDATE + audit atomic) with Blazor dialog + full Phase 1-4 TicTacToeDuel boot.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | GdprExportService + RankAdjustService + endpoints + SC#5 | 139af06 | 16 files |
| 2 | RankAdjustDialog + MainLayout switch arm + SC#6 tests | f5341f4 | 3 files |
| 3 | TicTacToeDuel Phase 4 Rankings integration | 7f1b0c7 | 3 files |

## SC Anchor Results

**SC#5 (GdprExportContractTests) — 5 tests, all green:**
- `Response_Has_All_Documented_Top_Level_Keys`: exactly 6 JSON keys, no password_hash, external_id_hash present
- `NonExistentPlayer_Returns_Null`: service returns null for unknown player (caller maps to 404)
- `Excludes_GDPR_Cascade_Null_Rows`: Pitfall 7 — NULL PlayerId rows excluded by WHERE clause
- `Over_Cap_Throws_GdprExportPayloadTooLargeException`: 25 MB cap enforced (1-byte test override)
- `Export_Returns_Only_Pre_Snapshot_Sessions`: REPEATABLE READ isolation verified

**SC#6 (AdminRankAdjustTransactionTests) — 8 tests, all green:**
- `UpdateAndAudit_RollBack_Together_On_Failure`: FaultAfterFirstSaveInterceptor throws on 2nd SaveChanges; rating unchanged + zero audit rows
- `HappyPath_Adjusts_Rating_And_Writes_Audit`: rating updated + 1 audit row + correct before/after/delta
- `LazyCreate_When_PlayerRank_Missing`: RANK-07 carry; Before=0, RD>0, Volatility>0
- `OutOfBoundsRating_Below_Min_Throws` + `Above_Max_Throws`: ArgumentOutOfRangeException
- `EmptyReason_Throws_ArgumentException`: null/empty guard in service
- `MissingLadder_Throws_KeyNotFoundException`: 404 ladder not found
- `Adjust_Does_Not_Modify_RD_Or_Volatility`: D-20 — RD and Volatility unchanged in DB

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Ladder.DefaultRd / DefaultVolatility do not exist as direct properties**
- **Found during:** Task 1 build
- **Issue:** `RankAdjustService` referenced `ladder.DefaultRd` and `ladder.DefaultVolatility` which don't exist — these values are in the JSONB `Config` column
- **Fix:** Added `ReadLadderDefaults(Ladder ladder)` helper (mirrors `RankingsTickerService.ReadLadderDefaults` pattern) — parses from JSONB with Glicko-2 defaults (1500/350/0.06) as fallback
- **Files modified:** `src/GameKit.Rankings/Services/RankAdjustService.cs`
- **Commit:** 139af06

**2. [Rule 1 - Bug] GdprExportService raw SQL used snake_case column names**
- **Found during:** Task 1 build
- **Issue:** Raw SQL queries for `player_identities` and `player_credentials` used `provider`, `external_id`, `player_id` (snake_case) — EF Core uses PascalCase column names by convention, so Postgres treats them as lowercase and the queries would fail at runtime
- **Fix:** Replaced all column references with double-quoted PascalCase identifiers: `"Provider"`, `"ExternalId"`, `"PlayerId"`, `"CreatedAt"`, `"UpdatedAt"`
- **Files modified:** `src/GameKit.Rankings/Services/GdprExportService.cs`
- **Commit:** 139af06

**3. [Rule 2 - Missing] GdprExportContractTests missing Auth migration step**
- **Found during:** Task 1 test setup design
- **Issue:** `player_identities` and `player_credentials` tables are created by the Auth migration, not Core or Rankings. The test's `ApplyMigrationsAsync` only ran Core and Rankings migrations — the seed helpers would fail with "relation does not exist"
- **Fix:** Added Auth migration step (using `AuthMigrationConstants` + `AuthMigrationModelCustomizer`) between Core and Rankings migration passes
- **Files modified:** `tests/GameKit.Rankings.Integration.Tests/GdprExportContractTests.cs`
- **Commit:** 139af06

**4. [Rule 1 - Bug] Test seed helpers used incorrect snake_case column names**
- **Found during:** Task 1 test writing
- **Issue:** All `SeedXxx` helpers used SQL with snake_case column names (`id`, `display_name`, `player_id`, etc.) — but EF Core's PascalCase convention means Postgres column names are `Id`, `DisplayName`, `PlayerId`, etc. Raw INSERT without quotes would fail
- **Fix:** Updated all INSERT statements to use double-quoted PascalCase column names
- **Files modified:** `tests/GameKit.Rankings.Integration.Tests/GdprExportContractTests.cs`
- **Commit:** 139af06

**5. [Rule 1 - Bug] Missing `using Microsoft.EntityFrameworkCore.Storage` in GdprExportService**
- **Found during:** Task 1 build
- **Issue:** `tx.GetDbTransaction()` is an extension method in `Microsoft.EntityFrameworkCore.Storage` (`DbContextTransactionExtensions`) — not in scope without the using directive
- **Fix:** Added `using Microsoft.EntityFrameworkCore.Storage;`
- **Files modified:** `src/GameKit.Rankings/Services/GdprExportService.cs`
- **Commit:** 139af06

**6. [Rule 1 - Bug] TicTacToeDuel builder chain broken — IGameKitRankingsBuilder ≠ IGameKitBuilder**
- **Found during:** Task 3 build
- **Issue:** `AddRankings().AddLadder()` returns `IGameKitRankingsBuilder`, which does not extend `IGameKitBuilder`. Chaining `AddGameKitAdmin()` at the end of the rankings builder fails compilation
- **Fix:** Captured `IGameKitBuilder` in a local variable (`gameKitBuilder`) and called `AddRankings()` and `AddGameKitAdmin()` on it separately
- **Files modified:** `samples/TicTacToeDuel/Program.cs`
- **Commit:** 7f1b0c7

## Known Stubs

None — all endpoints are fully wired with real service implementations.

## Threat Flags

No new security-relevant surface beyond what is in the plan's threat model (T-04-08-CP, T-04-08-CL, T-04-08-NL, T-04-08-DO, T-04-08-SC, T-04-08-AT, T-04-08-RR, T-04-08-CR, T-04-08-RB).

## Self-Check: PASSED

Files verified present:
- src/GameKit.Rankings/Services/GdprExportService.cs — FOUND
- src/GameKit.Rankings/Services/RankAdjustService.cs — FOUND
- src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor — FOUND
- tests/GameKit.Rankings.Integration.Tests/GdprExportContractTests.cs — FOUND
- tests/GameKit.Rankings.Integration.Tests/AdminRankAdjustTransactionTests.cs — FOUND

Commits verified:
- 139af06 (Task 1+2 impl) — FOUND
- f5341f4 (Task 2 dialog + tests) — FOUND
- 7f1b0c7 (Task 3 sample app) — FOUND

Test results:
- GdprExportContractTests: 5/5 passed
- AdminRankAdjustTransactionTests: 8/8 passed
- Total: 13/13 passed
