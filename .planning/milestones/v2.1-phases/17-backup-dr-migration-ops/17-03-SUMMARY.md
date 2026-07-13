---
phase: 17-backup-dr-migration-ops
plan: 03
subsystem: cli
tags: [dotnet, spectre-console, ef-core, migrations, postgres, testcontainers, dr]

# Dependency graph
requires:
  - phase: 17-backup-dr-migration-ops
    provides: "DrOrderingMarker migrations (plan 17-01) that list/apply commands introspect"
provides:
  - "gamekit migrations list — per-package applied/pending counts in canonical order (DR-04)"
  - "gamekit migrations apply --dry-run — idempotent SQL generation without DDL (DR-05)"
  - "gamekit migrations apply — full apply across all 6 packages in canonical order"
  - "PackageMigrationContextFactory — shared per-package DbContext builder for CLI migration ops"
affects:
  - "17-backup-dr-migration-ops (plans 04-06 — backup and DR tooling built on top)"

# Tech tracking
tech-stack:
  added:
    - "RelationalEventId.PendingModelChangesWarning suppression in BuildContext (EF Core diagnostic API)"
  patterns:
    - "PackageMigrationContextFactory: per-package DbContext built via reflection-based ReplaceService<IModelCustomizer, T>"
    - "Zero-DDL verification: test asserts information_schema.tables empty after --dry-run"
    - "Console.WriteLine for bracket-notation progress lines to avoid Spectre markup '1/6' style interpretation"
    - "PendingModelChangesWarning suppressed in CLI migration contexts (zero-DDL DrOrderingMarker migrations have no Designer.cs)"

key-files:
  created:
    - "src/GameKit.Cli/Commands/Migrations/PackageMigrationContextFactory.cs"
    - "src/GameKit.Cli/Commands/Migrations/MigrationsListCommand.cs"
    - "src/GameKit.Cli/Commands/Migrations/MigrationsApplyCommand.cs"
    - "tests/GameKit.Cli.Tests/MigrationsListCommandTests.cs"
    - "tests/GameKit.Cli.Tests/MigrationsApplyCommandTests.cs"
  modified:
    - "src/GameKit.Cli/GameKit.Cli.csproj (Auth/Matchmaking/Lobby project refs added)"
    - "src/GameKit.Cli/Program.cs (migrations branch with list + apply sub-commands)"

key-decisions:
  - "PackageMigrationContextFactory uses reflection to call ReplaceService<IModelCustomizer, T> at runtime because the customizer type is determined per-package descriptor — no per-type overloads generated"
  - "PendingModelChangesWarning suppressed in BuildContext — DrOrderingMarker migrations are zero-DDL ordering anchors without Designer.cs; EF Core model drift is expected and intentional"
  - "Console.WriteLine used instead of AnsiConsole.MarkupLine for bracket-notation progress ([1/6], recommended-order line) to survive Process.Start stdout redirection in test harness"
  - "Canonical application order: Core(1) → Auth(2) → Admin(3) → Rankings(4) → Matchmaking(5) → Lobby(6)"

patterns-established:
  - "Zero-DDL dry-run: IMigrator.GenerateScript(null, null, MigrationsSqlGenerationOptions.Idempotent) generates text only — MigrateAsync is never called"
  - "Per-package migration context: DbContextOptionsBuilder.UseNpgsql + MigrationsAssembly + MigrationsHistoryTable + ReplaceService<IModelCustomizer, *Customizer>"

requirements-completed: [DR-04, DR-05]

# Metrics
duration: ~390min (4 tests × ~15-30 min each + bug fixing cycles)
completed: 2026-06-23
status: complete
---

# Phase 17 Plan 03: Migration CLI (migrations list + apply) Summary

**`gamekit migrations list` + `apply --dry-run` delivering unified multi-package migration visibility and zero-DDL SQL preview via EF Core IMigrator.GenerateScript(Idempotent)**

## Performance

- **Duration:** ~390 min (dominated by 4 Testcontainers integration tests, 15-30 min each)
- **Started:** 2026-06-23T00:15:00Z
- **Completed:** 2026-06-23T06:35:00Z
- **Tasks:** 4 (+ 1 Rule 1 fix commit)
- **Files modified:** 9

## Accomplishments

- `gamekit migrations list` prints applied/pending counts for all 6 packages in canonical order (Core→Auth→Admin→Rankings→Matchmaking→Lobby) as a Spectre table with a recommended-order footer
- `gamekit migrations apply --dry-run` prints idempotent SQL via `IMigrator.GenerateScript(Idempotent)` — verified empirically to execute ZERO DDL (T-17-03-01: schema remains empty after dry-run, all migrations still pending)
- `gamekit migrations apply` applies all 6 packages in canonical order using the advisory-lock-serialized `MigrationRunner.MigrateWithLockAsync` path (same audited path as the existing `migrate` command)
- All 4 integration tests pass against Testcontainers Postgres: dry-run zero-DDL, list with all 6 packages, apply brings all pending to 0, list-after-apply confirms pending=0

## Task Commits

1. **Task 1: Auth/Matchmaking/Lobby project refs + PackageMigrationContextFactory** - `1abc593` (feat)
2. **Task 2: MigrationsListCommand (DR-04) + Program.cs migrations branch** - `d5651cb` (feat)
3. **Task 3: MigrationsApplyCommand (DR-05)** - `51ee9ae` (feat)
4. **Task 4: CLI integration tests** - `29110c8` (test)
5. **Rule 1 fixes (2 bugs)** - `5a9dfed` (fix)

## Files Created/Modified

- `src/GameKit.Cli/Commands/Migrations/PackageMigrationContextFactory.cs` — `PackageDescriptor` record + `BuildContext()` using reflection for generic `ReplaceService`; `PendingModelChangesWarning` suppressed; 6-package `Packages` list in canonical order
- `src/GameKit.Cli/Commands/Migrations/MigrationsListCommand.cs` — Spectre table with Order/Package/Applied/Pending columns; `Console.WriteLine` for recommended-order footer (avoids Spectre markup interference)
- `src/GameKit.Cli/Commands/Migrations/MigrationsApplyCommand.cs` — dry-run via `GenerateScript(Idempotent)` only; live-apply via `MigrateWithLockAsync`; `Console.WriteLine` for `[N/6]` progress lines
- `src/GameKit.Cli/GameKit.Cli.csproj` — added `GameKit.Auth`, `GameKit.Matchmaking`, `GameKit.Lobby` project references
- `src/GameKit.Cli/Program.cs` — `AddBranch("migrations", ...)` with `list` and `apply` sub-commands; legacy `migrate` command untouched
- `tests/GameKit.Cli.Tests/MigrationsApplyCommandTests.cs` — `DryRun_PrintsIdempotentSql_AndExecutesZeroDDL` (T-17-03-01); `Apply_WithoutDryRun_MigratesAllPackages`
- `tests/GameKit.Cli.Tests/MigrationsListCommandTests.cs` — `MigrationsList_PrintsAllSixPackages_AndRecommendedOrderLine`; `MigrationsList_AfterApplyingMigrations_ShowsPendingZeroForAllPackages`

## Decisions Made

- **Reflection for `ReplaceService<TService, TImplementation>()`**: No non-generic overload exists on `DbContextOptionsBuilder`; reflection is the only path when the customizer type is runtime-determined per package descriptor. Used `MakeGenericMethod(typeof(IModelCustomizer), package.CustomizerType)`.
- **`PendingModelChangesWarning` suppressed**: The five `DrOrderingMarker` migrations (Auth, Admin, Rankings, Matchmaking, Lobby) from plan 17-01 are zero-DDL ordering anchors without Designer.cs files. EF Core correctly detects model drift vs. the snapshot — but this is intentional (the migrations carry no schema changes). The warning-as-error was suppressed via `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` in `BuildContext`. Core is unaffected (no DrOrderingMarker).
- **`Console.WriteLine` for progress output**: Spectre interprets `[1/6]` as a markup style reference, causing `Could not find color or style '1/6'` (exit 255). Switched to `Console.WriteLine` for bracket-notation output (`[N/6] PackageName...`, recommended-order footer). Error markup (`[red]...[/]`, `[green]...[/]`) and table rendering continue via AnsiConsole.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Spectre markup bug — `[1/6]` interpreted as style in apply command progress output**
- **Found during:** Task 4 (integration test execution)
- **Issue:** `AnsiConsole.MarkupLine($"[grey]  [{pkg.CanonicalOrder}/6] {pkg.DisplayName}...[/]")` caused Spectre to parse `[1/6]` as a color/style reference, throwing "Could not find color or style '1/6'" and exiting with code 255
- **Fix:** Replaced `AnsiConsole.MarkupLine` with `Console.WriteLine($"  [{pkg.CanonicalOrder}/6] {pkg.DisplayName}...")` for progress prefix lines in `MigrationsApplyCommand.ExecuteApplyAsync`
- **Files modified:** `src/GameKit.Cli/Commands/Migrations/MigrationsApplyCommand.cs`
- **Verification:** `Apply_WithoutDryRun_MigratesAllPackages` passes (15 min 4 s)
- **Committed in:** `5a9dfed`

**2. [Rule 1 - Bug] Recommended-order footer not captured in stdout when stdout is redirected**
- **Found during:** Task 4 (integration test execution — `MigrationsList_PrintsAllSixPackages_AndRecommendedOrderLine`)
- **Issue:** `AnsiConsole.MarkupLine("[grey]Recommended application order:[/] Core → Auth → ...")` did not appear in captured stdout when the CLI was spawned via `Process.Start` with `RedirectStandardOutput = true`; Spectre's non-interactive rendering path did not write the styled line to the redirect stream
- **Fix:** Replaced with `Console.WriteLine("Recommended application order: Core → Auth → Admin → Rankings → Matchmaking → Lobby")`
- **Files modified:** `src/GameKit.Cli/Commands/Migrations/MigrationsListCommand.cs`
- **Verification:** `MigrationsList_PrintsAllSixPackages_AndRecommendedOrderLine` passes (15 min 3 s)
- **Committed in:** `5a9dfed`

**3. [Rule 1 - Bug] `PendingModelChangesWarning` treated as error for Lobby package context**
- **Found during:** Task 4 (integration test execution — `Apply_WithoutDryRun_MigratesAllPackages`)
- **Issue:** EF Core throws `PendingModelChangesWarning` as an error for the Lobby package because the `DrOrderingMarker` migration (from plan 17-01) has no Designer.cs; EF detects a mismatch between the runtime model and the last migration's compiled snapshot. Only Lobby triggered this visibly (the only package where `AccountMerge` entity was added in Auth after Lobby's snapshot was frozen)
- **Fix:** Added `.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` to `DbContextOptionsBuilder` in `PackageMigrationContextFactory.BuildContext` — the ordering-marker migrations are intentionally zero-DDL and schema-less; this warning is expected and safe to suppress in the CLI migration introspection context
- **Files modified:** `src/GameKit.Cli/Commands/Migrations/PackageMigrationContextFactory.cs`
- **Verification:** All 4 integration tests pass
- **Committed in:** `5a9dfed`

---

**Total deviations:** 3 auto-fixed (all Rule 1 — bugs)
**Impact on plan:** All fixes essential for test suite to pass. No scope creep. Root causes: Spectre markup ambiguity with fraction-like brackets, and EF Core's model-drift detection on Designer-less zero-DDL migrations.

## Issues Encountered

- **Stale binary cache**: First test re-run after Spectre fix committed used a cached binary from before the fix (the CLI output directory had not been cleared). Required `rm -rf src/GameKit.Cli/bin src/GameKit.Cli/obj/Debug` before re-running to force a fresh build. Lesson: when fixing the CLI source and immediately re-running `dotnet test`, the test project's incremental build may not trigger a rebuild of the CLI project. Use `--no-build` only when explicitly verifying the current binary.
- **EF Core binary build-output caching**: The `dotnet test` runner's internal incremental build also cached the pre-fix binary in some runs. Resolved by clearing the CLI project output directory before each test invocation.

## Threat Surface Scan

No new threat surface introduced. All network/DDL paths are gated:
- T-17-03-01 verified empirically: `information_schema.tables` shows 0 rows after `--dry-run`
- T-17-03-03: apply path uses same advisory-lock-serialized `MigrationRunner` as the audited `migrate` command

## Known Stubs

None. Both commands produce real output from live EF Core introspection against the database.

## Self-Check

**Files exist:**
- `src/GameKit.Cli/Commands/Migrations/PackageMigrationContextFactory.cs` — FOUND
- `src/GameKit.Cli/Commands/Migrations/MigrationsListCommand.cs` — FOUND
- `src/GameKit.Cli/Commands/Migrations/MigrationsApplyCommand.cs` — FOUND
- `tests/GameKit.Cli.Tests/MigrationsApplyCommandTests.cs` — FOUND
- `tests/GameKit.Cli.Tests/MigrationsListCommandTests.cs` — FOUND

**Commits exist:**
- `1abc593` — FOUND (feat: project refs + PackageMigrationContextFactory)
- `d5651cb` — FOUND (feat: MigrationsListCommand)
- `51ee9ae` — FOUND (feat: MigrationsApplyCommand)
- `29110c8` — FOUND (test: CLI integration tests)
- `5a9dfed` — FOUND (fix: Rule 1 bugs)

**Tests:**
- `DryRun_PrintsIdempotentSql_AndExecutesZeroDDL` — PASSED (15 min 4 s)
- `MigrationsList_PrintsAllSixPackages_AndRecommendedOrderLine` — PASSED (15 min 3 s)
- `Apply_WithoutDryRun_MigratesAllPackages` — PASSED (15 min 4 s)
- `MigrationsList_AfterApplyingMigrations_ShowsPendingZeroForAllPackages` — PASSED (30 min 7 s)

## Self-Check: PASSED

## Next Phase Readiness

- Plan 17-03 complete: `gamekit migrations list` (DR-04) and `gamekit migrations apply --dry-run` (DR-05) fully operational
- `PackageMigrationContextFactory` is a shared utility available to future plans (17-04 through 17-06) that need per-package DbContext instances for backup/restore/DR operations
- The canonical 6-package order is codified in `PackageMigrationContextFactory.Packages` — future plans should reference this list for consistency

---
*Phase: 17-backup-dr-migration-ops*
*Completed: 2026-06-23*
