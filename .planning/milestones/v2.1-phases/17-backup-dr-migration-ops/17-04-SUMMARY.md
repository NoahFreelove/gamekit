---
phase: 17-backup-dr-migration-ops
plan: "04"
subsystem: cli
tags: [dr, backup, restore, pg_dump, pg_restore, redis, bgsave, security, path-traversal]
dependency_graph:
  requires: ["17-03"]
  provides: ["DR-06"]
  affects: ["src/GameKit.Cli/Program.cs", "src/GameKit.Cli/Commands/Db/"]
tech_stack:
  added: []
  patterns:
    - "ProcessStartInfo shell-out with PGPASSWORD in Environment (not CLI args)"
    - "BackupPathValidator static guard — rejects relative paths and .. traversal before process start"
    - "StackExchange.Redis IServer.SaveAsync(SaveType.BackgroundSave) for Redis BGSAVE (no redis-cli dependency)"
    - "Internal static seam (BuildPgDumpStartInfo / BuildPgRestoreStartInfo) for unit-testable argument/env construction"
key_files:
  created:
    - src/GameKit.Cli/Commands/Db/BackupPathValidator.cs
    - src/GameKit.Cli/Commands/Db/DbBackupCommand.cs
    - src/GameKit.Cli/Commands/Db/DbRestoreCommand.cs
    - tests/GameKit.Cli.Tests/DbBackupCommandTests.cs
  modified:
    - src/GameKit.Cli/Program.cs
decisions:
  - "PGPASSWORD placed in ProcessStartInfo.Environment exclusively — never in CLI args (T-17-04-02 / ps-visibility mitigation)"
  - "BackupPathValidator.IsSafeAbsolutePath rejects relative paths AND any .. segment on any OS path separator (T-17-04-01)"
  - "DbRestoreCommand requires explicit --database flag and prints resolved target before running (T-17-04-03)"
  - "Redis BGSAVE via StackExchange.Redis IServer.SaveAsync(SaveType.BackgroundSave) — no redis-cli PATH dependency (Research Open Question #2 resolved)"
  - "Internal BuildPgDumpStartInfo / BuildPgRestoreStartInfo static seams allow unit tests to assert PGPASSWORD placement without running binaries"
metrics:
  duration: "~20 minutes"
  completed: "2026-06-23T06:51:36Z"
  tasks_completed: 3
  tasks_total: 3
  files_created: 4
  files_modified: 1
status: complete
---

# Phase 17 Plan 04: DB Backup/Restore CLI Commands Summary

**One-liner:** `gamekit db backup/restore` wrappers shelling out to pg_dump/pg_restore with PGPASSWORD-in-env path-traversal-guarded Redis-BGSAVE-via-StackExchange.Redis satisfying DR-06.

## What Was Built

Three tasks delivered the `gamekit db` CLI branch and its security infrastructure:

**Task 1 — BackupPathValidator + DbBackupCommand**

`BackupPathValidator.IsSafeAbsolutePath(string)` is a static guard that:
- Returns `false` for any relative path (not `Path.IsPathRooted`)
- Returns `false` for any path containing a `..` segment, splitting on both `Path.DirectorySeparatorChar` and `Path.AltDirectorySeparatorChar`
- Returns `true` only for clean absolute paths

`DbBackupCommand` (`gamekit db backup`):
- Resolves connection string from `--connection-string`, `GAMEKIT_MIGRATIONS_CONNECTION`, or `GAMEKIT_CONNECTION` (matches existing command pattern)
- Validates `--output` via `BackupPathValidator` and exits code 2 before starting any process on rejection
- Parses connection string via `NpgsqlConnectionStringBuilder` to extract host/port/database/username/password
- Builds `ProcessStartInfo` with `FileName = "pg_dump"`, `UseShellExecute = false`, `RedirectStandardError = true`
- Arguments: `--host=... --port=... --username=... --format=custom --file=<output> <database>` — password intentionally absent
- Sets `psi.Environment["PGPASSWORD"] = password` when a password is present (T-17-04-02 mitigation)
- Internal static `BuildPgDumpStartInfo` seam exposes argument/env construction for unit tests
- Optional `--redis-connection`: connects via `ConnectionMultiplexer.ConnectAsync`, calls `IServer.SaveAsync(SaveType.BackgroundSave)` on the primary, reports the Redis data directory, and instructs the operator to copy the RDB file manually

**Task 2 — DbRestoreCommand + Program.cs registration**

`DbRestoreCommand` (`gamekit db restore`):
- Same connection-string and path-validation pattern as backup
- Requires explicit `--database <NAME>` flag (T-17-04-03 mitigation — prevents silent restore into the wrong DB)
- Prints resolved host/port/database before starting the process so operator can confirm the target
- Arguments: `--host=... --port=... --username=... --dbname=<database> --no-owner --no-privileges <file>`
- PGPASSWORD via environment, same pattern as backup
- Internal static `BuildPgRestoreStartInfo` seam for unit tests

`Program.cs` receives a new `AddBranch("db", ...)` block alongside the existing `migrations`, `admin`, and `service-token` branches. The `migrations` branch from Plan 17-03 is intact.

**Task 3 — DbBackupCommandTests (unit tests, no Docker)**

13 unit tests covering:
- `BackupPathValidator`: 8 cases (relative path, relative no-dot, absolute with `..`, absolute `..` at end, absolute `..` in middle, clean absolute, root path, null/empty/whitespace)
- `DbBackupCommand.BuildPgDumpStartInfo`: PGPASSWORD in env + absent from args, no-password case, expected flags check
- `DbRestoreCommand.BuildPgRestoreStartInfo`: PGPASSWORD in env + absent from args, expected flags check

All 13 tests pass. No Docker required, no real pg_dump/pg_restore invoked.

## Deviations from Plan

### Auto-fixed Issues

None.

### Minor Adjustments

**1. [Rule 1 - Bug] ConfigGetAsync returns KeyValuePair array, not string**

- **Found during:** Task 1, first build attempt
- **Issue:** `await server.ConfigGetAsync("dir")` returns `KeyValuePair<string, string>[]`, not `string`; the initial cast caused CS0030
- **Fix:** Changed to `dirConfig?.Length > 0 ? dirConfig[0].Value : "(unknown)"` — correctly extracts the value from the first key-value pair
- **Files modified:** `src/GameKit.Cli/Commands/Db/DbBackupCommand.cs`
- **Commit:** Included in Task 1 commit (4ec5eb3)

## STRIDE Threat Coverage

| Threat | Mitigation | Verified by |
|--------|-----------|-------------|
| T-17-04-01 Tampering (path traversal) | `BackupPathValidator.IsSafeAbsolutePath` rejects before process start | 5 unit tests in `DbBackupCommandTests` |
| T-17-04-02 Info Disclosure (password in ps) | `PGPASSWORD` in `ProcessStartInfo.Environment` only | 2 unit tests asserting env presence + args absence |
| T-17-04-03 Tampering (wrong DB) | `--database` required; target printed before restore | Code review — `DbRestoreCommand` enforces at entry |
| T-17-04-04 Info Disclosure (PII in dump) | Documented operator responsibility | Noted in runbook (DR-01, Plan 17-01) |

## Known Stubs

None — the commands are fully implemented. The round-trip integration test (pg_dump → destroy → pg_restore → health check) is Plan 17-05 (DR-03), which exercises the binaries inside a Testcontainers container.

## Threat Flags

None — no new network endpoints, auth paths, or schema changes introduced. The only trust boundary crossed is the operator's filesystem and child-process environment, both already in scope of the plan's threat model.

## Self-Check: PASSED

Files exist:
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Cli/Commands/Db/BackupPathValidator.cs` — FOUND
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Cli/Commands/Db/DbBackupCommand.cs` — FOUND
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Cli/Commands/Db/DbRestoreCommand.cs` — FOUND
- `/home/noah/Desktop/projects/gamekit/tests/GameKit.Cli.Tests/DbBackupCommandTests.cs` — FOUND

Commits:
- `4ec5eb3` — feat(17-04): add BackupPathValidator + DbBackupCommand (DR-06)
- `263b283` — feat(17-04): add DbRestoreCommand + register db branch in Program.cs (DR-06)
- `deb09ad` — test(17-04): DbBackupCommandTests — path-traversal rejection + PGPASSWORD-not-in-args (DR-06)

Build: `dotnet build src/GameKit.Cli -warnaserror -p:NuGetAudit=false` → 0 errors, 0 warnings.
Tests: `dotnet test tests/GameKit.Cli.Tests --filter DbBackupCommand` → 13 passed, 0 failed.
