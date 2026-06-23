---
phase: 17
slug: backup-dr-migration-ops
reviewed: 2026-06-23
depth: standard
status: findings
critical: 0
warnings: 3
info: 1
---

# Phase 17 — Code Review

Security-critical surfaces all **confirmed sound**: no shell injection (`UseShellExecute=false` + explicit `FileName="pg_dump"`, no `/bin/sh -c`); PGPASSWORD strictly in `ProcessStartInfo.Environment`, never in args/logs; dry-run provably calls only `GenerateScript` (never `MigrateAsync`), integration test asserts DDL=0; GK0003 uses semantic `BaseType` chain (not filename) and correctly excludes ModelSnapshot/Designer; path-traversal rejection applied to both backup + restore; DR round-trip non-vacuous (seeds row, destroys, restores, asserts survival + /health/ready 200).

## Findings & Triage

| ID | Sev | File | Issue | Decision |
|----|-----|------|-------|----------|
| WR-01 | Warning | `DbBackupCommand.cs`, `DbRestoreCommand.cs` | `pg_dump`/`pg_restore` args built as one interpolated `Arguments` string → a path with a space splits into two argv entries and the command fails cryptically. | **FIX** — switch to `ProcessStartInfo.ArgumentList` (each arg verbatim). Idiomatic, eliminates the splitting/quoting class entirely. |
| WR-02 | Warning | `BackupPathValidator.cs` | Validator doesn't reject embedded spaces. | **RESOLVED-BY-WR-01** — `ArgumentList` handles spaces; no separate change needed (validator's job is traversal safety, not quoting). |
| WR-03 | Warning | `DbBackupCommand.cs` `BackupRedisAsync` | If no primary Redis endpoint is found (all replicas / empty endpoints), `SaveAsync` never runs but the command returns exit 0 — operator believes BGSAVE ran. Data-loss risk in a DR context. | **FIX** — track `primaryFound`; print error + return 1 if none. |
| IN-01 | Info | `MigrationDownMethodAnalyzer.cs` (GK0003) | Rejects the idiomatic expression-bodied `Down() => throw new NotSupportedException(...)` (block-only policy), and that branch is untested. | **FIX** — relax GK0003 to ALSO accept an expression-bodied member whose expression is exactly `throw new NotSupportedException(...)`; keep flagging any other expression body. Add tests for expression-bodied conforming (no diagnostic) + expression-bodied destructive (diagnostic). |

## Constraints for the fix
- After fixes, full solution must still build GREEN under `-warnaserror -p:NuGetAudit=false` with GK0003 active.
- Re-run affected tests: GK0003 analyzer tests, DbBackupCommand path/PGPASSWORD tests; confirm still green. No security property weakened.

## REVIEW COMPLETE
status: findings — 2 fixes (WR-01→resolves WR-02, WR-03) + 1 analyzer relax/test (IN-01).
