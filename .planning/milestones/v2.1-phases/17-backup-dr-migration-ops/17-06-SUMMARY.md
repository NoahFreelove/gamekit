---
phase: 17-backup-dr-migration-ops
plan: "06"
subsystem: docs
tags: [runbooks, backup, dr, migration-ops, postgres, redis, documentation]
dependency_graph:
  requires: ["17-03", "17-04", "17-05"]
  provides: [DR-01-docs, DR-02-docs, DR-07-docs, RunbookFilesTests]
  affects: [docs/runbooks, docs/migration-ops.md, tests/GameKit.Core.Tests]
tech_stack:
  added: []
  patterns:
    - canonical runbook files at docs/runbooks/
    - GitRootLocator.FindRepoRoot() for path-resolution in file-existence tests
key_files:
  created:
    - docs/runbooks/postgres-backup-restore.md
    - docs/runbooks/redis-backup-restore.md
    - docs/migration-ops.md
    - tests/GameKit.Core.Tests/RunbookFilesTests.cs
  modified:
    - docs/ops/disaster-recovery.md
decisions:
  - "Refactored docs/ops/disaster-recovery.md into a cross-reference index pointing at the two new canonical runbooks rather than keeping duplicated procedure content"
  - "Used GitRootLocator.FindRepoRoot() (existing helper in GameKit.TestFixtures) rather than walking AppContext.BaseDirectory in the test — reuses the same path-resolution already used by LicenseHeaderTests and PostgresFixture"
  - "Three separate [Fact] tests (one per file) so a test failure names the exact missing or emptied file"
  - "250 bytes minimum threshold chosen to catch accidentally emptied files while allowing for very short future changes"
metrics:
  duration: "~12 minutes"
  completed: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_created: 4
  files_modified: 1
status: complete
---

# Phase 17 Plan 06: DR/Migration-Ops Runbooks + Docs + Presence Test Summary

**One-liner:** Canonical Postgres and Redis backup/restore runbooks (DR-01/DR-02) plus migration-ops doc (DR-07) split from existing disaster-recovery.md, with a file-existence regression test.

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Write the two canonical backup/restore runbooks (DR-01, DR-02) | 63a6545 | docs/runbooks/postgres-backup-restore.md, docs/runbooks/redis-backup-restore.md, docs/ops/disaster-recovery.md |
| 2 | Write docs/migration-ops.md (DR-07 docs component) | bc3c75e | docs/migration-ops.md |
| 3 | RunbookFilesTests — file-existence regression | 9a036cd | tests/GameKit.Core.Tests/RunbookFilesTests.cs |

## What Was Built

### docs/runbooks/postgres-backup-restore.md (DR-01, 301 lines)

Complete Postgres backup/restore runbook covering:
- `pg_dump --format=custom` logical backup with systemd timer example
- `gamekit db backup` / `gamekit db restore` CLI wrapper usage (Plans 03-04)
- WAL-G/Barman PITR guidance (self-hosted, no cloud SaaS)
- `pg_restore --no-owner --no-privileges` restore with role re-provisioning
- Explicit "encryption at rest is the operator's responsibility" note (T-17-06-01 / T-17-04-04 transfer)
- Verification section pointing at `Category=DisasterRecovery` test (Plan 05)
- GameKit-specific post-restore concerns: migration history tables, refresh tokens, admin audit log, JWT signing keys

### docs/runbooks/redis-backup-restore.md (DR-02, 240 lines)

Complete Redis backup/restore runbook covering:
- `BGSAVE` via `gamekit db backup --redis-connection` CLI (no redis-cli binary required)
- Manual `BGREWRITEAOF` + filesystem snapshot procedure (copies whole Multi-Part AOF directory)
- Live-replica strategy
- Full restore procedure (stop app fleet → stop Redis → replace data dir → start Redis → restart app fleet)
- AOF truncation with `redis-check-aof --fix` for corrupted AOF recovery
- **Pre-FLUSHALL/FLUSHDB destructive-operation guard** (T-17-06-02): take BGSAVE snapshot before any flush, confirm `rdb_last_bgsave_status:ok`

### docs/ops/disaster-recovery.md (refactored)

Converted from a ~410-line procedure document into a cross-reference index (~95 lines) that:
- Tables the two canonical runbook paths with requirement IDs
- Lists RPO/RTO targets
- Points at the CI `Category=DisasterRecovery` test
- Summarizes cross-cutting concerns (matchmaking ticker, refresh tokens, audit log) linking to the detail runbooks

### docs/migration-ops.md (DR-07, 270 lines)

New canonical migration-ops document covering:
- Per-package application ordering (Core→Auth→Admin→Rankings→Matchmaking→Lobby) with advisory lock keys and history table names
- `gamekit migrations list` usage and what it calls internally (`GetPendingMigrationsAsync`)
- `gamekit migrations apply --dry-run` idempotent SQL generation via `IMigrator.GenerateScript(MigrationsSqlGenerationOptions.Idempotent)` — zero DDL executed
- `Down()` `NotSupportedException` policy with exact code template
- GK0003 Roslyn analyzer build-time enforcement
- Timestamp ordering rule and `MigrationTimestampTests` regression test
- Restore-from-backup as the canonical rollback path (pre-migration backup checklist)
- Migration design rules for contributors (immutable files, CONCURRENTLY indexes, etc.)

### tests/GameKit.Core.Tests/RunbookFilesTests.cs

Three `[Fact]` tests using `GitRootLocator.FindRepoRoot()` to assert each doc exists and is non-trivial (> 200 bytes). All pass green.

## Verification Results

```
dotnet test tests/GameKit.Core.Tests --filter RunbookFiles -p:NuGetAudit=false
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 5 ms
```

Manual content check:
- `docs/runbooks/postgres-backup-restore.md`: contains `FLUSHALL` — N/A. Contains `pg_dump`, `pg_restore`, `gamekit db backup`, `DisasterRecovery`, `encryption` — pass.
- `docs/runbooks/redis-backup-restore.md`: contains `FLUSHALL`, `BGSAVE`, `AOF`, `gamekit db backup --redis-connection` — pass.
- `docs/migration-ops.md`: contains `NotSupportedException`, `dry-run`, `gamekit migrations list`, `gamekit migrations apply`, `MigrationTimestampTests`, `DisasterRecovery` — pass.
- No cloud/SaaS backup service referenced anywhere — only `pg_dump`, `WAL-G`, `Barman`, `BGSAVE`, Redis RDB/AOF.

## Deviations from Plan

None — plan executed exactly as written.

The `docs/runbooks/` directory was created as part of Task 1 (it did not pre-exist). All task acceptance criteria and prohibition checks passed.

## Threat Surface Scan

No new network endpoints, auth paths, or schema changes introduced — documentation and test files only.

T-17-06-01 (backup PII encryption): mitigated by explicit "encryption at rest is the operator's responsibility" section in `postgres-backup-restore.md`.
T-17-06-02 (Redis FLUSHALL guard): mitigated by "Pre-destructive-operation guard" section in `redis-backup-restore.md`.

## Self-Check: PASSED

- [x] `docs/runbooks/postgres-backup-restore.md` exists (301 lines, non-empty)
- [x] `docs/runbooks/redis-backup-restore.md` exists (240 lines, non-empty)
- [x] `docs/migration-ops.md` exists (270 lines, non-empty)
- [x] `tests/GameKit.Core.Tests/RunbookFilesTests.cs` exists, contains `RunbookFiles`
- [x] Commits 63a6545, bc3c75e, 9a036cd exist in git log
- [x] `dotnet test --filter RunbookFiles` passes (3/3)
