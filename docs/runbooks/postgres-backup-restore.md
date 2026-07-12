<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Postgres backup / restore runbook (DR-01)

This runbook covers logical backup with `pg_dump`, restore with `pg_restore`, point-in-time
recovery (PITR) with WAL archiving, and the `gamekit db backup` / `gamekit db restore` CLI
wrappers. It also records the GameKit-specific post-restore concerns that affect player data
integrity.

**Prerequisites:** `pg_dump` and `pg_restore` must be on the operator's PATH. GameKit does not
bundle or distribute these tools — they ship with every standard Postgres client installation.

---

## RPO / RTO targets

| Component    | Recommended RPO | Recommended RTO | Reason                                                                |
|--------------|-----------------|-----------------|-----------------------------------------------------------------------|
| Postgres     | 5 minutes       | 30 minutes      | Player progression, refresh tokens, rankings — losing > 5 min frustrates players |
| JWT signing keys | 0 (must survive) | 1 minute  | Without the private key, every player session is dead until rotation  |
| Role grants  | 0 (must survive) | n/a            | Idempotent script — re-run `docker/postgres/init/01-roles.sql` on fresh server |

---

## Strategy 1 — `pg_dump` logical backup (recommended for v1)

`pg_dump` writes a portable, version-independent dump that restores against any compatible
Postgres version. Use the `--format=custom` binary format — it supports parallel restore
(`pg_restore -j 4`) and selective table restore.

### Manual backup

```bash
# Run as the DDL role (gamekit_owner), not the runtime role (gamekit_app).
# gamekit_app does not have USAGE on every metadata table pg_dump touches.
sudo -u gamekit-backup pg_dump \
    --host=prod-postgres.internal \
    --port=5432 \
    --username=gamekit_owner \
    --no-password \
    --format=custom \
    --compress=6 \
    --file=/srv/backups/gamekit-$(date -u +%Y%m%d-%H%M).pgdump \
    gamekit

# Verify the dump is non-zero and well-formed.
LATEST=$(ls -t /srv/backups/gamekit-*.pgdump | head -1)
pg_restore --list "$LATEST" | head -10
```

`PGPASSWORD` should come from an env var or `~/.pgpass` (`0600` mode), never inlined on
the command line where it is visible in `ps` output.

### CLI wrapper

The `gamekit` CLI (Plans 03–04) wraps the above as a convenience:

```bash
# Backup via the CLI — PGPASSWORD is passed via the child-process environment, not CLI args.
gamekit db backup \
    --connection-string "Host=prod-postgres.internal;Database=gamekit;Username=gamekit_owner;Password=$OWNER_PW" \
    --output /srv/backups/gamekit-$(date -u +%Y%m%d-%H%M).pgdump
```

The `--output` path must be absolute and must not contain `..` segments. The CLI rejects
paths that fail this check before starting `pg_dump`.

**Note:** `pg_dump` must be on the operator's PATH — the CLI does not bundle it.

### Scheduled backup (systemd timer)

```ini
# /etc/systemd/system/gamekit-pgdump.service
[Unit]
Description=GameKit Postgres logical backup
After=network-online.target

[Service]
Type=oneshot
User=gamekit-backup
Group=gamekit-backup
EnvironmentFile=/etc/gamekit-backup/env
ExecStart=/usr/bin/pg_dump \
    --host=prod-postgres.internal \
    --port=5432 \
    --username=gamekit_owner \
    --no-password \
    --format=custom \
    --compress=6 \
    --file=/srv/backups/gamekit-%Y%m%d-%H%M.pgdump \
    gamekit
```

```ini
# /etc/systemd/system/gamekit-pgdump.timer
[Unit]
Description=Nightly GameKit Postgres backup

[Timer]
OnCalendar=*-*-* 02:30:00
Persistent=true

[Install]
WantedBy=timers.target
```

Enable: `sudo systemctl enable --now gamekit-pgdump.timer`

---

## Strategy 2 — `pg_basebackup` + WAL archiving (PITR, sub-5-minute RPO)

For sub-5-minute RPO, enable continuous WAL archiving and take a periodic base backup.
Both WAL-G and Barman are permissively licensed self-hosted tools that automate WAL archiving and
point-in-time recovery — neither requires a cloud service.

### WAL archiving configuration (`postgresql.conf`)

```ini
wal_level             = replica
archive_mode          = on
# Simple rsync target — replace with WAL-G or Barman push command as needed.
archive_command       = 'rsync --quiet %p /srv/wal-archive/%f'
archive_timeout       = 60     # roll a partial WAL every 60s under low write volume
```

### Weekly base backup

```bash
sudo -u postgres pg_basebackup \
    --host=prod-postgres.internal \
    --pgdata=/srv/backups/base-$(date -u +%Y%m%d) \
    --format=tar --gzip \
    --progress --verbose \
    --wal-method=fetch
```

### PITR restore

Extract the base backup, then set `recovery_target_time` in `recovery.conf` (or
`postgresql.auto.conf` on Postgres 12+):

```ini
# postgresql.auto.conf
restore_command      = 'cp /srv/wal-archive/%f %p'
recovery_target_time = '2026-05-25 14:23:00'
recovery_target_action = 'promote'
```

Start Postgres — it replays WAL until `recovery_target_time` then promotes.

---

## Storage discipline

- Backups land on a **separate host**, not the Postgres machine. Disk failure that takes
  down the live DB must not take down the backup.
- **Encryption at rest is the operator's responsibility.** The `pg_dump` output contains
  every player's refresh-token hash, admin password hash, and ranking history. Treat it as
  a crown jewel. Use `gpg --symmetric` per file, a LUKS-encrypted backup volume, or an
  operator-managed KMS solution. **GameKit does not encrypt backup artifacts.**
- Retain 30 days of nightly dumps + 1 year of monthlies as a reasonable default.
- **Test restores at least quarterly.** An untested backup is not a backup.
- Store encryption keys on a **different host** from the backup files — a single-host
  compromise that leaks both the encrypted dump and the GPG key defeats the encryption.

---

## Postgres restore

### Full restore procedure

```bash
# 1. Provision the 3 roles on the fresh server (idempotent).
sudo -u postgres psql -v ON_ERROR_STOP=1 \
    -f /path/to/gamekit/docker/postgres/init/01-roles.sql

# 2. Restore the dump as gamekit_owner (parallel restore, 4 workers).
sudo -u postgres pg_restore \
    --dbname=gamekit \
    --jobs=4 \
    --no-owner \
    --no-privileges \
    --verbose \
    /srv/backups/gamekit-20260525-0230.pgdump
```

`--no-owner` + `--no-privileges` prevents the dump's baked-in ownership/grant metadata
from conflicting with the fresh role state. After restore, re-apply default privileges:

```sql
-- Re-run the GRANT chunks from docker/postgres/init/01-roles.sql.
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT USAGE, SELECT ON SEQUENCES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT ON TABLES TO gamekit_reader;

-- Confirm table ownership.
SELECT n.nspname, c.relname, pg_get_userbyid(c.relowner) AS owner
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'gamekit' AND c.relkind = 'r'
LIMIT 5;
-- All rows should show owner = 'gamekit_owner'.
```

### CLI restore wrapper

```bash
gamekit db restore \
    --connection-string "Host=prod-postgres.internal;Database=gamekit;Username=gamekit_owner;Password=$OWNER_PW" \
    --database gamekit \
    --file /srv/backups/gamekit-20260525-0230.pgdump
```

The `--database` flag is required — the CLI does not infer the target from the connection
string alone, preventing a silent restore into the wrong database.

---

## GameKit-specific post-restore concerns

### Migration history tables

After a data restore, confirm every `gamekit.__ef_migrations_*` table is intact — `pg_dump`
preserves them but a selective restore can leave them out of sync. Check with:

```sql
SELECT * FROM gamekit.__ef_migrations_core      ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_auth      ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_admin     ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_rankings  ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_matchmaking ORDER BY "MigrationId";
SELECT * FROM gamekit.__ef_migrations_lobby     ORDER BY "MigrationId";
```

Use `gamekit migrations list` (see [docs/migration-ops.md](../migration-ops.md)) to
confirm zero pending migrations after restore.

### Refresh tokens

Refresh tokens live in Postgres (`gamekit.refresh_tokens`), not Redis. A restore that
rewinds past a player's logout resurrects that revoked token.

After any restore that rewinds beyond the refresh-token TTL (default 30 days), force-rotate
the JWT signing key (see [`jwt-keys.md`](../ops/jwt-keys.md) "Emergency rotation"). Any
"resurrected" refresh token is then invalid because its associated key is retired.

### Admin audit log

`gamekit.admin_audit_log` records every admin action (bans, role changes). After a restore,
this log rewinds — historical bans that were lifted may re-apply. Walk the `admin_audit_log`
table for the gap between the restore point and now; re-issue any actions that should still
be in effect.

### JWT signing keys

JWT keys are filesystem assets (`/srv/mygame/keys/`), **not** in Postgres. Back them up
out-of-band. A restore that re-creates the database but loses the JWT private key invalidates
every active session.

---

## Verification

The DR round-trip is validated by the CI test suite:

```bash
# Run the automated DR round-trip test (pg_dump → destroy → pg_restore → /health/ready 200).
dotnet test tests/GameKit.DR.Tests \
    --filter "Category=DisasterRecovery" \
    -p:NuGetAudit=false
```

The test (Plan 05) uses Testcontainers `IContainer.ExecAsync` to run `pg_dump` inside the
Postgres container, destroys the container, starts a fresh container, runs `pg_restore`, and
asserts `GET /health/ready` returns HTTP 200. This proves the full round-trip on every CI run
without requiring a production database.

---

## Common mistakes to avoid

- **Co-locating backups with the live data.** Disk failure takes both.
- **Skipping backup verification.** `pg_dump` exits 0 even when output is partially corrupt under
  disk-full conditions. Always `pg_restore --list` the dump as a smoke test.
- **Restoring without re-applying grants.** Skipping the `ALTER DEFAULT PRIVILEGES` re-run leaves
  `gamekit_app` without INSERT/UPDATE rights at runtime.
- **Forgetting the JWT keys.** A perfect database restore is useless if the JWT private key is gone.

---

## Related runbooks

- [`redis-backup-restore.md`](redis-backup-restore.md) — Redis RDB/AOF backup and restore.
- [`../ops/postgres-roles.md`](../ops/postgres-roles.md) — role re-provisioning during fresh-server restore.
- [`../ops/jwt-keys.md`](../ops/jwt-keys.md) — out-of-band key backup + emergency rotation.
- [`../migration-ops.md`](../migration-ops.md) — migration ordering, dry-run, Down() policy.
- [`../ops/migrations-runbook.md`](../ops/migrations-runbook.md) — migration history table integrity.
