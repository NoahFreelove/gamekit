---
phase: 17-backup-dr-migration-ops
plan: "02"
subsystem: build-tooling, migration-ops
tags: [analyzer, roslyn, gk0003, migration-timestamp, dr-04, dr-05, dr-07]
dependency_graph:
  requires: ["17-01"]
  provides: ["GK0003 build-time gate", "MigrationTimestampTests CI gate"]
  affects: ["GameKit.Build", "tests/GameKit.Build.Tests", "tests/GameKit.Core.Tests"]
tech_stack:
  added: []
  patterns:
    - "Roslyn DiagnosticAnalyzer (netstandard2.0, EnforceExtendedAnalyzerRules) matching GK0001/GK0002 architecture"
    - "CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> harness with in-memory EF Core stubs"
    - "MigrationAttribute.Id reflection for canonical EF timestamp extraction"
key_files:
  created:
    - src/GameKit.Build/MigrationDownMethodAnalyzer.cs
    - tests/GameKit.Build.Tests/MigrationDownAnalyzerTests.cs
    - tests/GameKit.Core.Tests/MigrationTimestampTests.cs
  modified:
    - src/GameKit.Build/AnalyzerReleases.Unshipped.md
    - tests/GameKit.Core.Tests/GameKit.Core.Tests.csproj
decisions:
  - "Used MigrationAttribute.Id (not type.Name) for timestamp lookup — class names in this project are semantic (e.g. DrOrderingMarker), not timestamp-prefixed; the attribute always carries the canonical id"
  - "Added DropTable/DropColumn to EfStubs MigrationBuilder in analyzer tests — stub must include these methods or the test compilation fails with CS1061 before the analyzer can run"
  - "Added ProjectReferences for all 6 packages to GameKit.Core.Tests.csproj — required to anchor assembly via typeof() for reflection-based timestamp scan"
metrics:
  duration: "~7 minutes"
  completed: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_created: 3
  files_modified: 2
status: complete
---

# Phase 17 Plan 02: GK0003 Analyzer + MigrationTimestampTests Summary

GK0003 Roslyn analyzer enforcing Down()-throw-only policy with 7 unit tests, plus MigrationTimestampTests asserting Cross-package latest-timestamp ascent (Core < Auth < Admin < Rankings < Matchmaking < Lobby).

## One-Liner

GK0003 DiagnosticAnalyzer blocks non-conforming migration Down() at compile time; MigrationTimestampTests locks the canonical package ordering via MigrationAttribute reflection.

## What Was Built

### Task 1: GK0003 MigrationDownMethodAnalyzer

`src/GameKit.Build/MigrationDownMethodAnalyzer.cs` — a `netstandard2.0` Roslyn `DiagnosticAnalyzer` that:

1. Syntactically pre-filters for `Down(MigrationBuilder)` method declarations
2. Semantically verifies the declaring type inherits (transitively) from `Microsoft.EntityFrameworkCore.Migrations.Migration` via `BaseType` walk (Pitfall-7 guard)
3. Checks the body is exactly ONE `ThrowStatementSyntax` whose thrown expression is `ObjectCreationExpressionSyntax` with last identifier `NotSupportedException`
4. Emits `GK0003` (Error, `GameKit.Security` category) if any check fails — empty bodies, destructive calls, wrong exception type, multiple statements, and expression-bodied forms all fail

`src/GameKit.Build/AnalyzerReleases.Unshipped.md` — GK0003 row added under `### New Rules`.

### Task 2: Analyzer Unit Tests

`tests/GameKit.Build.Tests/MigrationDownAnalyzerTests.cs` — 7 test cases using `CSharpAnalyzerTest<MigrationDownMethodAnalyzer, DefaultVerifier>` with inline EF Core stubs (minimal `Migration` base + `MigrationBuilder` with `DropTable`/`DropColumn`):

| Test | Expected |
|------|----------|
| `ConformingDown_SingleThrowNotSupportedException_NoDiagnostic` | zero GK0003 |
| `ConformingDown_UpWithDropTable_NoDiagnosticOnUp` | zero GK0003 (Up is not gated) |
| `NonMigrationClass_DownMethod_NoDiagnostic` | zero GK0003 (Pitfall-7 ModelSnapshot guard) |
| `DestructiveDown_DropTable_ReportsGK0003` | one GK0003 at Down identifier |
| `EmptyDown_NoDiagnosticExpected_ReportsGK0003` | one GK0003 (Pitfall-4 empty body) |
| `WrongException_InvalidOperationException_ReportsGK0003` | one GK0003 |
| `MultipleStatements_DropThenThrow_ReportsGK0003` | one GK0003 |

All 7 pass.

### Task 3: MigrationTimestampTests

`tests/GameKit.Core.Tests/MigrationTimestampTests.cs` — two facts:

- `PackageMigrations_LatestTimestamp_AreInCorrectOrder`: scans each package assembly for `Migration` subclasses, reads `[Migration("timestamp_Name")]` attribute `Id` property, orders by Ordinal, and asserts each package's latest `Id` is lexicographically greater than the previous package's.
- `AllPackages_HaveAtLeastOneMigration`: asserts `NotEmpty` per package, guarding against silent packaging failures.

`tests/GameKit.Core.Tests/GameKit.Core.Tests.csproj` — added `ProjectReference` to Auth, Admin.UI, Rankings, Matchmaking, and Lobby so the test can anchor each assembly via `typeof(AuthInitial)` etc.

Verified ordering (from `MigrationAttribute.Id`):

| Package | Latest Migration Id | Ordinal Comparison |
|---------|--------------------|--------------------|
| Core | `20260622000000_AddGameSessionIdempotencyKey` | 1st |
| Auth | `20260623000000_DrOrderingMarker` | > Core ✓ |
| Admin | `20260624000000_DrOrderingMarker` | > Auth ✓ |
| Rankings | `20260625000000_DrOrderingMarker` | > Admin ✓ |
| Matchmaking | `20260626000000_DrOrderingMarker` | > Rankings ✓ |
| Lobby | `20260627000000_DrOrderingMarker` | > Matchmaking ✓ |

## Verification Results

- `dotnet build src/GameKit.Build -warnaserror -p:NuGetAudit=false` → **Build succeeded, 0 warnings, 0 errors**
- `dotnet test tests/GameKit.Build.Tests --filter MigrationDownAnalyzer -p:NuGetAudit=false` → **Passed: 7/7**
- `dotnet test tests/GameKit.Core.Tests --filter MigrationTimestampTests -p:NuGetAudit=false` → **Passed: 2/2**
- Full solution build `dotnet build GameKit.sln -warnaserror -p:NuGetAudit=false` → **Build succeeded, 0 warnings, 0 errors** (proves all 14 Down() bodies from Plan 01 satisfy GK0003)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] EfStubs MigrationBuilder missing DropTable/DropColumn methods**

- **Found during:** Task 2 — first test run
- **Issue:** The `EfStubs` constant declared a minimal `MigrationBuilder {}` with no methods. Test code calling `migrationBuilder.DropTable(...)` failed with `CS1061` before the analyzer could run, causing 4 of 7 tests to fail with compiler error diagnostics instead of the expected GK0003 diagnostics.
- **Fix:** Added `DropTable` and `DropColumn` methods to the stub `MigrationBuilder` class in `EfStubs`.
- **Files modified:** `tests/GameKit.Build.Tests/MigrationDownAnalyzerTests.cs`

**2. [Rule 1 - Bug] MigrationTimestampTests used type.Name instead of MigrationAttribute.Id**

- **Found during:** Task 3 — first test run
- **Issue:** The initial implementation sorted `Migration` subclasses by `t.Name` (the class name, e.g. `DrOrderingMarker`). In this codebase, migration class names are semantic (not timestamp-prefixed), so both Auth's and Admin's latest migration had `t.Name == "DrOrderingMarker"` — the ordering assertion failed because `"DrOrderingMarker" == "DrOrderingMarker"` is not `> 0`.
- **Fix:** Changed reflection to read `[Migration("20260623000000_DrOrderingMarker")]` attribute `Id` property, which always carries the canonical EF Core timestamp-prefixed migration identifier regardless of class name.
- **Files modified:** `tests/GameKit.Core.Tests/MigrationTimestampTests.cs`

## Known Stubs

None — all data flows are wired and all tests validate real production migration assemblies.

## Threat Flags

No new network endpoints, auth paths, file access patterns, or schema changes introduced. This plan adds only build-time analysis and test code.

## Self-Check: PASSED

- `src/GameKit.Build/MigrationDownMethodAnalyzer.cs` — FOUND
- `tests/GameKit.Build.Tests/MigrationDownAnalyzerTests.cs` — FOUND
- `tests/GameKit.Core.Tests/MigrationTimestampTests.cs` — FOUND
- commit `7daab63` — FOUND (feat(17-02): GK0003 analyzer)
- commit `ea48a6d` — FOUND (test(17-02): GK0003 analyzer tests)
- commit `04ea211` — FOUND (feat(17-02): MigrationTimestampTests)
