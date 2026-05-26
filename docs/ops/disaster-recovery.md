<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Disaster recovery

This runbook covers backup + restore for the two stateful components GameKit
depends on (Postgres + Redis), and the GameKit-specific concerns that ride on
top — idempotency-key replay, matchmaking ticker pause, refresh-token sanity.

The objective is **recovery within a known RPO/RTO**, not zero data loss.
Operators pick the trade-off that matches their game's tolerance.

---

## RPO / RTO targets

| Component         | Recommended RPO  | Recommended RTO | Reason                                                              |
|-------------------|------------------|-----------------|---------------------------------------------------------------------|
| Postgres          | 5 minutes        | 30 minutes      | Player progression, refresh tokens, rankings — losing > 5 min frustrates players |
| Redis             | ~1 second (AOF `everysec`) | 5 minutes       | Live queues + presence; players accept "queue restart needed" but not "your party vanished" |
| JWT signing keys  | 0 (must survive) | 1 minute        | Without the private key, every player session is dead until rotation |
| Postgres roles + grants | 0          | n/a — re-provision from `docker/postgres/init/01-roles.sql` | Idempotent script — re-run on fresh server |

You can hit tighter RPO with synchronous replication (Postgres streaming
replication; Redis replicas) — at a cost in write latency. The defaults below
assume an async-replica + nightly-snapshot strategy.

---

## Postgres backup

### Strategy 1 — `pg_dump` (logical backup, recommended for v1)

`pg_dump` writes a portable, version-independent dump that restores against any
compatible Postgres version. Use it for nightly snapshots:

```bash
# Run as a service user, NOT as gamekit_app — pg_dump needs USAGE on every
# schema it touches. gamekit_owner has that; gamekit_app does not have grants
# on the public schema's metadata tables.
sudo -u gamekit-backup pg_dump \
    --host=prod-postgres.internal \
    --port=5432 \
    --username=gamekit_owner \
    --no-password \
    --format=custom \
    --compress=6 \
    --file=/srv/backups/gamekit-$(date -u +%Y%m%d-%H%M).pgdump \
    gamekit

# Confirm.
ls -lh /srv/backups/ | tail -3
```

`--format=custom` (the `pg_restore`-compatible binary format) is the
preferred choice — it supports parallel restore (`pg_restore -j 4`) and
selective restore (table-by-table, schema-by-schema). Plain SQL dumps
(`--format=plain`) are easier to grep but slower to restore.

`PGPASSWORD` should come from an env var, or `~/.pgpass` (`0600` mode), or a
systemd `EnvironmentFile`. Never inline the password in the command.

Pair `pg_dump` with a systemd timer:

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

Enable: `sudo systemctl enable --now gamekit-pgdump.timer`.

### Strategy 2 — `pg_basebackup` + WAL archive (point-in-time recovery)

For sub-5-minute RPO, switch to continuous WAL archiving and a periodic base
backup:

```bash
# postgresql.conf
wal_level             = replica
archive_mode          = on
archive_command       = 'rsync --quiet %p /srv/wal-archive/%f'
archive_timeout       = 60     # roll a partial WAL every 60s even if low write volume
```

Take a base backup weekly:

```bash
sudo -u postgres pg_basebackup \
    --host=prod-postgres.internal \
    --pgdata=/srv/backups/base-$(date -u +%Y%m%d) \
    --format=tar --gzip \
    --progress --verbose \
    --wal-method=fetch
```

Restore: extract the base backup, then PITR by setting
`recovery_target_time = '2026-05-25 14:23:00'` in `recovery.conf` (or via
`recovery_target` GUC in postgres.auto.conf on Postgres 12+).

### Storage discipline

- Backups land on a **separate host**, not the Postgres machine. If a disk
  failure took down the live DB, a co-located backup is useless.
- Encrypt at rest (`gpg --symmetric` per file, or LUKS-encrypted backup
  volume). The dump contains every refresh-token hash and admin password hash
  — treat it as a crown jewel.
- Retention: 30 days of nightly dumps + 1 year of monthlies is a reasonable
  default; adjust to your compliance regime.
- Test restores **at least quarterly**. An untested backup is not a backup.

---

## Postgres restore

```bash
# 1. Provision the 3 roles on the fresh server (idempotent).
sudo -u postgres psql -v ON_ERROR_STOP=1 \
    -f /path/to/gamekit/docker/postgres/init/01-roles.sql

# 2. Restore the dump as gamekit_owner.
sudo -u postgres pg_restore \
    --dbname=gamekit \
    --jobs=4 \
    --no-owner \
    --no-privileges \
    --verbose \
    /srv/backups/gamekit-20260525-0230.pgdump
```

`--no-owner` + `--no-privileges` is important on a 3-role restore: the dump
contains owner + grant metadata baked at backup time, which may not match the
fresh server's role state if you rotated passwords or re-created the roles.
After the restore, re-apply the default privileges:

```sql
-- Re-run the GRANT chunks of docker/postgres/init/01-roles.sql.
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT USAGE, SELECT ON SEQUENCES TO gamekit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner IN SCHEMA gamekit
    GRANT SELECT ON TABLES TO gamekit_reader;

-- Fix table ownership.
ALTER SCHEMA gamekit OWNER TO gamekit_owner;
ALTER DATABASE gamekit OWNER TO gamekit_owner;

-- Confirm.
SELECT n.nspname, c.relname, pg_get_userbyid(c.relowner) AS owner
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'gamekit' AND c.relkind = 'r'
LIMIT 5;
-- All rows should show owner = 'gamekit_owner'.
```

After the data restore, walk through the [migrations runbook](migrations-runbook.md)
to confirm every `gamekit.__ef_migrations_*` table is intact — pg_dump
preserves them but a hand-edited restore can leave them out of sync.

---

## Redis backup

GameKit's Redis configuration uses AOF (see [`redis-aof.md`](redis-aof.md)) so
the live AOF is itself a near-real-time backup if it lives on a separate volume.

### Strategy 1 — filesystem snapshot of `dir`

```bash
# 1. Force an AOF rewrite to compact the file before snapshotting.
redis-cli -a "$REDIS_PASSWORD" BGREWRITEAOF
# Wait for the BGREWRITEAOF to finish:
while [ "$(redis-cli -a $REDIS_PASSWORD INFO persistence | grep aof_rewrite_in_progress | cut -d: -f2 | tr -d '\r')" != "0" ]; do
    sleep 1
done

# 2. Snapshot the data directory.
sudo tar czf /srv/backups/gamekit-redis-$(date -u +%Y%m%d-%H%M).tar.gz \
    -C /var/lib/redis .

# 3. Verify the archive.
tar tzf /srv/backups/gamekit-redis-$(date -u +%Y%m%d-%H%M).tar.gz | head -5
# Expect to see appendonly.aof.* and dump.rdb listed.
```

### Strategy 2 — live replica

```bash
# On the replica host's redis.conf:
replicaof prod-redis-primary.internal 6379
replica-read-only yes
masterauth <PRIMARY_REDIS_PASSWORD>
requirepass <REPLICA_REDIS_PASSWORD>
```

The replica builds + maintains its own AOF + RDB in real time. Backups become
"snapshot the replica" — no impact on the primary.

---

## Redis restore

```bash
# 1. Stop Redis.
sudo systemctl stop redis-server

# 2. Wipe / move aside the old data directory.
sudo mv /var/lib/redis /var/lib/redis.broken.$(date -u +%Y%m%d-%H%M)
sudo mkdir -p /var/lib/redis
sudo chown redis:redis /var/lib/redis

# 3. Extract the backup.
sudo tar xzf /srv/backups/gamekit-redis-20260525-0230.tar.gz \
    -C /var/lib/redis

sudo chown -R redis:redis /var/lib/redis

# 4. Bring Redis back up.
sudo systemctl start redis-server

# 5. Confirm.
redis-cli -a "$REDIS_PASSWORD" ping
redis-cli -a "$REDIS_PASSWORD" DBSIZE
```

Redis 7+ uses the "Multi-Part AOF" format with `appendonly.aof.manifest` +
`base.rdb` + `incr.aof` files. Restore the **whole `dir` directory** as a unit;
restoring individual files invariably corrupts the manifest.

---

## GameKit-specific concerns during restore

### Concern 1 — matchmaking ticker

The matchmaking ticker (`MatchmakingTickerHostedService`) processes Redis sorted
sets every 500 ms. If you restore Redis from an older snapshot while the app
fleet is still running, the ticker will read stale tickets and may form
duplicate matches.

Procedure:

```bash
# 1. BEFORE restoring Redis, pause the ticker fleet.
#    Either stop the app process(es):
sudo systemctl stop mygame.service

#    Or, if you have admin-CLI access while one app is still up, use the
#    admin command-palette "Pause Matchmaking" action (Phase 5 D-9) — sets
#    the admin_pause flag, ticker honors it within one tick.

# 2. Restore Redis per the procedure above.

# 3. Re-start the app fleet. Each replica's ticker re-acquires the
#    distributed lock (mm:lock:matchmaking-ticker) and resumes processing.
sudo systemctl start mygame.service
```

### Concern 2 — idempotency keys

`mm:idempotency:{key}` Redis entries deduplicate POST retries. After a Redis
restore from an older snapshot, **previously-deduplicated requests can replay**
— a client retrying a queue-enqueue will not see the cached marker and may
double-enqueue.

For most games this is a non-issue (the duplicate enqueue surfaces as "you are
already in queue" via the Postgres-side ticket-uniqueness check). If your game
charges premium currency on enqueue, audit the post-restore window and refund
duplicates.

### Concern 3 — refresh tokens

Refresh tokens live in Postgres (`gamekit.refresh_tokens`), not Redis. A
Postgres restore from before a player's logout brings that revoked token back
to life — a stolen-then-revoked token becomes valid again.

After any Postgres restore that rewinds beyond the refresh-token TTL (default
30 days), assume the worst and force-rotate the JWT signing key (see
[`jwt-keys.md`](jwt-keys.md), "Emergency rotation"). The next refresh per
player will mint a token signed with the new key, and any "resurrected" old
refresh token is now invalid because its associated key is retired.

### Concern 4 — admin audit log

`gamekit.admin_audit_log` records every admin action (bans, role changes,
admin creations). After a restore, this log rewinds — historical bans that
were lifted may re-apply. Walk through the `admin_audit_log` table for the
gap between the restore point and now; re-issue any actions that should still
be in effect.

### Concern 5 — JWT signing keys

The JWT keys themselves are filesystem assets (`/srv/mygame/keys/`), NOT in
Postgres or Redis. They must be backed up out-of-band — see
[`jwt-keys.md`](jwt-keys.md). A restore that re-creates the database but loses
the JWT private key invalidates every active session until a new key is
provisioned and rolled out.

---

## Restore drill (run quarterly)

```bash
# 1. Provision a sandbox host (VM or container) with Postgres + Redis.
# 2. Restore the most recent backup of each:
sudo -u postgres pg_restore --dbname=gamekit /srv/backups/gamekit-LATEST.pgdump
sudo systemctl stop redis-server
sudo tar xzf /srv/backups/gamekit-redis-LATEST.tar.gz -C /var/lib/redis
sudo systemctl start redis-server

# 3. Boot a sandbox app instance pointing at the restored stack.
ConnectionStrings__GameKit="...sandbox-postgres..." \
ConnectionStrings__Redis="sandbox-redis:6379" \
dotnet /srv/mygame/MyGame.dll &

# 4. Spot-check the surface:
#    - Login as a known player; confirm /auth/me returns the expected profile.
#    - Issue an authenticated /api/sessions/{id}/complete; confirm 200.
#    - Check the admin panel renders without missing-package alerts.
#    - Check /api/mm/enqueue works against the restored Redis.

# 5. Tear down the sandbox.
```

Record the time-to-success in your runbook. If it slips beyond your RTO target,
revisit the backup strategy.

---

## Operational checks

```bash
# 1. Confirm backups are happening.
ls -lt /srv/backups/ | head -5
sudo systemctl status gamekit-pgdump.timer

# 2. Confirm latest backup is not zero-size or corrupt.
LATEST=$(ls -t /srv/backups/gamekit-*.pgdump | head -1)
pg_restore --list "$LATEST" | head -10

# 3. Confirm the live AOF is being written.
sudo stat /var/lib/redis/appendonly.aof.*
# 'Modify' timestamp should advance every second under any non-trivial load.

# 4. Confirm replicas (if any) are caught up.
redis-cli -h replica.internal -a "$REPLICA_PASSWORD" INFO replication \
    | grep -E '^(role|master_link_status|slave_repl_offset):'
```

---

## Common mistakes to avoid

- **Co-locating backups with the live data.** Disk failure takes both. Backups
  belong on a separate host or off-machine storage.
- **Skipping backup verification.** `pg_dump` exits 0 even when its output is
  partially corrupt under disk-full conditions. Always `pg_restore --list` the
  dump as a smoke test.
- **Restoring without re-applying grants.** Skipping the `ALTER DEFAULT
  PRIVILEGES` re-run leaves `gamekit_app` without the rights it needs at
  runtime. The DIST-02 test catches the inverse (reader-with-INSERT), but no
  test catches the post-restore grant gap — verify manually.
- **Restoring Redis without pausing the ticker.** Duplicate matches form
  within the first tick after restore.
- **Forgetting the JWT keys.** A perfect Postgres + Redis restore is useless
  if the JWT private key is gone — every session is dead until you provision
  a new key + force-rotate.
- **Backup encryption keys stored on the same host.** If a single host
  compromise leaks both the encrypted backups and the GPG key, the encryption
  bought nothing. Store the keys in a separate secret manager.

---

## Related runbooks

- [`postgres-roles.md`](postgres-roles.md) — re-provisioning roles during a
  fresh-server restore.
- [`redis-aof.md`](redis-aof.md) — the AOF you are backing up.
- [`jwt-keys.md`](jwt-keys.md) — out-of-band key backup + emergency rotation.
- [`migrations-runbook.md`](migrations-runbook.md) — migration history table
  integrity after a restore.
