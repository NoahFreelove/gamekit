<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Disaster recovery — overview

This document is the **index** for GameKit's backup and recovery documentation.
The detailed step-by-step procedures have been split into canonical runbook files
under `docs/runbooks/` so each procedure can be linked individually in runbook
tooling and incident response checklists.

---

## Canonical runbooks

| Runbook | Covers | Requirement |
|---------|--------|-------------|
| [`docs/runbooks/postgres-backup-restore.md`](../runbooks/postgres-backup-restore.md) | `pg_dump` logical backup, `pg_restore`, WAL-G/Barman PITR, `gamekit db backup` / `gamekit db restore` CLI wrappers, encryption-at-rest note, DR test | DR-01 |
| [`docs/runbooks/redis-backup-restore.md`](../runbooks/redis-backup-restore.md) | RDB snapshot backup via `BGSAVE`, AOF management, Multi-Part AOF restore, FLUSHALL/FLUSHDB guard | DR-02 |
| [`docs/migration-ops.md`](../migration-ops.md) | Per-package migration ordering, `gamekit migrations list`, `gamekit migrations apply --dry-run`, Down() policy, timestamp rule, restore-from-backup rollback | DR-07 |

---

## RPO / RTO summary

| Component        | Recommended RPO      | Recommended RTO | Runbook link                                                   |
|------------------|----------------------|-----------------|----------------------------------------------------------------|
| Postgres         | 5 minutes            | 30 minutes      | [postgres-backup-restore.md](../runbooks/postgres-backup-restore.md) |
| Redis            | ~1 second (AOF everysec) | 5 minutes   | [redis-backup-restore.md](../runbooks/redis-backup-restore.md) |
| JWT signing keys | 0 (must survive)     | 1 minute        | [jwt-keys.md](jwt-keys.md)                                     |
| Role grants      | 0 (must survive)     | n/a             | [postgres-roles.md](postgres-roles.md) — idempotent re-provision |

---

## CI-proven round-trip

The DR round-trip (dump → destroy → restore → health check) is validated automatically:

```bash
dotnet test tests/GameKit.DR.Tests \
    --filter "Category=DisasterRecovery" \
    -p:NuGetAudit=false
```

The test (Plan 05) uses Testcontainers `IContainer.ExecAsync` to run `pg_dump` inside a
Postgres container, destroys the container, starts a fresh container, runs `pg_restore`, and
asserts `GET /health/ready` returns HTTP 200. Run this before any deployment that follows a
schema-altering migration.

---

## GameKit-specific concerns (cross-cutting)

These concerns apply across both Postgres and Redis restores — see the individual runbooks for
the exact recovery steps.

| Concern | Impact | Where to look |
|---------|--------|---------------|
| Matchmaking ticker | Reads stale tickets if Redis is restored while the fleet is live — pause fleet first | [redis-backup-restore.md](../runbooks/redis-backup-restore.md) |
| Idempotency keys | A Redis restore from an older snapshot can allow previously-deduplicated requests to replay | [redis-backup-restore.md](../runbooks/redis-backup-restore.md) |
| Refresh tokens | A Postgres restore that rewinds past a logout resurrects revoked tokens — force-rotate JWT key | [postgres-backup-restore.md](../runbooks/postgres-backup-restore.md) |
| Admin audit log | Post-restore, the audit log rewinds — re-issue bans/role changes for the gap window | [postgres-backup-restore.md](../runbooks/postgres-backup-restore.md) |
| JWT signing keys | Filesystem assets — must be backed up out-of-band; not in Postgres or Redis | [jwt-keys.md](jwt-keys.md) |

---

## Restore drill (run quarterly)

```bash
# 1. Provision a sandbox host with Postgres + Redis.
# 2. Restore the most recent Postgres backup (see postgres-backup-restore.md).
# 3. Restore the most recent Redis backup (see redis-backup-restore.md).
# 4. Boot a sandbox app instance pointing at the restored stack.
# 5. Spot-check: login as a known player, confirm /api/me returns expected profile.
# 6. Run: dotnet test tests/GameKit.DR.Tests --filter "Category=DisasterRecovery"
# 7. Record the time-to-success; revisit strategy if it exceeds RTO.
```

---

## Related runbooks

- [`postgres-roles.md`](postgres-roles.md) — re-provisioning roles during a fresh-server restore.
- [`redis-aof.md`](redis-aof.md) — AOF + RDB configuration and memory tuning.
- [`jwt-keys.md`](jwt-keys.md) — out-of-band key backup + emergency rotation.
- [`migrations-runbook.md`](migrations-runbook.md) — migration history table integrity after restore.
