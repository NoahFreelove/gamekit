---
phase: 17
fixed_at: 2026-06-23T04:02:00Z
review_path: .planning/phases/17-backup-dr-migration-ops/17-REVIEW.md
iteration: 1
findings_in_scope: 4
fixed: 3
skipped: 1
status: partial
---

# Phase 17: Code Review Fix Report

**Fixed at:** 2026-06-23T04:02:00Z
**Source review:** .planning/phases/17-backup-dr-migration-ops/17-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 4 (WR-01, WR-02, WR-03, IN-01)
- Fixed: 3 (WR-01 resolves WR-02 per REVIEW.md triage; WR-03; IN-01)
- Skipped: 1 (WR-02 — resolved by WR-01 per review triage)

## Fixed Issues

### WR-01: pg_dump/pg_restore ArgumentList switch

**Files modified:** `src/GameKit.Cli/Commands/Db/DbBackupCommand.cs`, `src/GameKit.Cli/Commands/Db/DbRestoreCommand.cs`, `tests/GameKit.Cli.Tests/DbBackupCommandTests.cs`
**Commit:** 9dc2134
**Applied fix:** Replaced the single interpolated `Arguments` string in both `BuildPgDumpStartInfo` and `BuildPgRestoreStartInfo` with individual `ProcessStartInfo.ArgumentList.Add(...)` calls (one entry per flag/value). The `Arguments` property is no longer set. All existing unit tests were updated to assert on `ArgumentList` instead of `Arguments`; two new regression tests (`PathWithSpaces_SurvivesVerbatimInArgumentList`) were added for both commands. The `Arguments` string being empty is now an explicit assertion. PGPASSWORD remains strictly in `Environment` — the test now iterates `ArgumentList` entries to verify the password is absent from every entry individually.

### WR-03: BackupRedisAsync exits 1 when no primary endpoint found

**Files modified:** `src/GameKit.Cli/Commands/Db/DbBackupCommand.cs`
**Commit:** 9dc2134 (included in WR-01 commit — both files staged together)
**Applied fix:** Added `primaryFound` boolean tracking in `BackupRedisAsync`. After iterating all endpoints, if no primary (`!server.IsReplica`) was found, the method prints `ERROR: No writable (primary) Redis endpoint found...` and returns `1`. The existing try/catch returning `1` on exception is preserved. A pure unit test without a live Redis connection is not feasible via the current `IServer`-shaped seam — the error branch is verified by code review and the DR integration test covers the happy path.

### IN-01: GK0003 accepts expression-bodied throw new NotSupportedException(...)

**Files modified:** `src/GameKit.Build/MigrationDownMethodAnalyzer.cs`, `tests/GameKit.Build.Tests/MigrationDownAnalyzerTests.cs`
**Commit:** f666523
**Applied fix:** Added `IsThrowNotSupportedExceptionExpression` helper method that returns `true` when the expression is a `ThrowExpressionSyntax` wrapping a `NotSupportedException` creation. Step 3 of `AnalyzeMethodDeclaration` now calls this helper before emitting GK0003 on a null `Body` — if the expression body is exactly `throw new NotSupportedException(...)`, the method returns without a diagnostic. Any other expression body (e.g. `=> migrationBuilder.DropTable(...)`) continues to emit GK0003. Block-bodied behaviour is fully unchanged. Two new analyzer test cases added: (a) `ExpressionBodied_ThrowNotSupportedException_NoDiagnostic` → no GK0003; (b) `ExpressionBodied_DestructiveCall_ReportsGK0003` → GK0003. All 22 analyzer tests pass.

## Skipped Issues

### WR-02: BackupPathValidator does not reject embedded spaces

**File:** `src/GameKit.Cli/Commands/Db/BackupPathValidator.cs`
**Reason:** Resolved by WR-01 per review triage — `ArgumentList` passes paths verbatim regardless of spaces, so the validator's job (path traversal safety) does not need to reject spaces. No separate change needed.
**Original issue:** Validator didn't reject paths with embedded spaces; with `Arguments`-string approach a space would split into two argv entries.

---

_Fixed: 2026-06-23T04:02:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
