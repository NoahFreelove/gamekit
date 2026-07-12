<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Redis backup / restore runbook (DR-02)

This runbook covers RDB snapshot backup (`BGSAVE`), AOF management, the restore procedure
(stop → replace data directory → restart), and the mandatory pre-destructive-operation guard
that must precede any `FLUSHALL` or `FLUSHDB` command.

GameKit's Redis configuration uses **AOF (`appendfsync everysec`) as the primary durability
mechanism** plus periodic RDB snapshots for fast cold-start. See
[`../ops/redis-aof.md`](../ops/redis-aof.md) for AOF configuration details.

**Self-hosted only.** No cloud backup service is referenced in this runbook — all procedures
use standard Redis tooling available in any self-hosted Redis deployment.

---

## RPO / RTO targets

| Component | Recommended RPO        | Recommended RTO | Reason                                                                      |
|-----------|------------------------|-----------------|-----------------------------------------------------------------------------|
| Redis     | ~1 second (AOF everysec) | 5 minutes     | Live queues + presence; players accept "queue restart needed" but not "your party vanished" |

---

## Data stored in Redis

| Key family                   | Owner                 | Effect if lost                                           |
|------------------------------|-----------------------|----------------------------------------------------------|
| `mm:ticket:{id}`             | `GameKit.Matchmaking` | Player's ticket vanishes mid-queue                       |
| `mm:queue:{ladderId}`        | `GameKit.Matchmaking` | Queue entry gone; matches never form                     |
| `mm:idempotency:{key}`       | `GameKit.Matchmaking` | Duplicate enqueues on client retry                       |
| `mm:lock:matchmaking-ticker` | `GameKit.Matchmaking` | Leader election loses the lock; duplicate matches form   |
| `presence:{playerId}`        | `GameKit.Presence`    | Player reports offline despite live heartbeat            |
| `party:{partyId}`            | `GameKit.Matchmaking` | Party state vanishes; members stuck in a half-deleted party |

`maxmemory-policy noeviction` ensures Redis never silently evicts these keys — it returns
an error on write when memory is exhausted, which surfaces as a logged exception.

---

## Pre-destructive-operation guard

**`FLUSHALL` and `FLUSHDB` are irreversible.** Before issuing either command:

1. **Take a snapshot first** — run steps 1–2 of Strategy 1 below before issuing `FLUSHALL`.
2. **Pause the matchmaking ticker fleet** — see "Matchmaking ticker" note in the restore
   procedure below. The ticker reads stale tickets if Redis is flushed while it is running.
3. Confirm the snapshot is complete: `redis-cli -a "$REDIS_PASSWORD" INFO persistence | grep rdb_last_bgsave_status`
   must return `ok` before you flush.

```bash
# Safe sequence before FLUSHALL:
redis-cli -a "$REDIS_PASSWORD" BGSAVE
# Wait for BGSAVE to complete:
while [ "$(redis-cli -a "$REDIS_PASSWORD" INFO persistence | grep rdb_last_bgsave_status | cut -d: -f2 | tr -d '\r')" != "ok" ]; do sleep 1; done

# THEN stop the app fleet.
sudo systemctl stop mygame.service

# THEN issue FLUSHALL.
redis-cli -a "$REDIS_PASSWORD" FLUSHALL

# Bring the app back up after the operation that needed the flush.
sudo systemctl start mygame.service
```

Skipping the snapshot means a FLUSHALL is irrecoverable.

---

## Strategy 1 — RDB snapshot backup (recommended)

Issue a `BGSAVE` to compact state into the RDB file, then copy the entire data directory.
The `gamekit db backup --redis-connection` CLI command (Plan 04) handles the `BGSAVE` step
via StackExchange.Redis — no `redis-cli` binary is required on the operator's machine.

### CLI-assisted backup

```bash
# Issue BGSAVE via the CLI (no redis-cli dependency).
# The CLI prints the Redis data directory path after BGSAVE completes.
gamekit db backup \
    --connection-string "..." \
    --output /srv/backups/gamekit-$(date -u +%Y%m%d-%H%M).pgdump \
    --redis-connection "prod-redis.internal:6379,password=$REDIS_PW"

# Then copy the RDB file from the reported data directory.
# (The CLI prints the dir path — confirm it before copying.)
sudo cp /var/lib/redis/dump.rdb /srv/backups/gamekit-redis-$(date -u +%Y%m%d-%H%M).rdb
```

The CLI issues `BGSAVE` via StackExchange.Redis's `IServer.SaveAsync(SaveType.BackgroundSave)`.
It does not copy the file — the operator must copy it from the Redis data directory, which is
printed in the CLI output.

### Manual backup (without CLI)

```bash
# 1. Force an AOF rewrite to compact the file before snapshotting.
redis-cli -a "$REDIS_PASSWORD" BGREWRITEAOF
# Wait for rewrite to finish:
while [ "$(redis-cli -a "$REDIS_PASSWORD" INFO persistence | grep aof_rewrite_in_progress | cut -d: -f2 | tr -d '\r')" != "0" ]; do
    sleep 1
done

# 2. Snapshot the entire data directory (AOF + RDB as a unit).
#    Redis 7+ uses the Multi-Part AOF (base.rdb + incr.aof + manifest).
#    Always snapshot the whole directory — individual file copies corrupt the manifest.
sudo tar czf /srv/backups/gamekit-redis-$(date -u +%Y%m%d-%H%M).tar.gz \
    -C /var/lib/redis .

# 3. Verify the archive.
tar tzf /srv/backups/gamekit-redis-$(date -u +%Y%m%d-%H%M).tar.gz | head -5
# Expect: appendonly.aof.*, dump.rdb listed.
```

---

## Strategy 2 — Live replica

```ini
# On the replica host's redis.conf:
replicaof prod-redis-primary.internal 6379
replica-read-only yes
masterauth <PRIMARY_REDIS_PASSWORD>
requirepass <REPLICA_REDIS_PASSWORD>
```

The replica maintains its own AOF + RDB in real time. Backups become "snapshot the replica"
with zero impact on the primary. Restore from a replica snapshot using Strategy 1's restore
procedure below.

---

## Redis restore procedure

```bash
# 1. Pause the matchmaking ticker fleet (prevents duplicate matches on stale tickets).
sudo systemctl stop mygame.service

# 2. Stop Redis.
sudo systemctl stop redis-server

# 3. Move aside the old data directory.
sudo mv /var/lib/redis /var/lib/redis.broken.$(date -u +%Y%m%d-%H%M)
sudo mkdir -p /var/lib/redis
sudo chown redis:redis /var/lib/redis

# 4. Extract the backup (restores the whole Multi-Part AOF + RDB as a unit).
sudo tar xzf /srv/backups/gamekit-redis-20260525-0230.tar.gz \
    -C /var/lib/redis
sudo chown -R redis:redis /var/lib/redis

# 5. Bring Redis back up.
sudo systemctl start redis-server

# 6. Confirm Redis is healthy.
redis-cli -a "$REDIS_PASSWORD" PING     # expect PONG
redis-cli -a "$REDIS_PASSWORD" DBSIZE   # confirm key count is plausible

# 7. Re-start the app fleet.
sudo systemctl start mygame.service
```

**Important:** Redis 7+ uses the Multi-Part AOF format (`appendonly.aof.manifest` +
`base.rdb` + `incr.aof` files). Restore the **whole `dir` directory** as a unit — restoring
individual files invariably corrupts the manifest and prevents Redis from starting.

---

## AOF truncation (repairing a corrupted AOF)

If Redis refuses to start after a crash and logs `Unexpected end of file` or `Bad checksum`,
the AOF tail was corrupted mid-write. Truncate it:

```bash
# Redis 7+ multi-part AOF — fix the incremental AOF file.
redis-check-aof --fix /var/lib/redis/appendonly.aof.1.incr.aof

# Confirm the fix was applied, then restart.
sudo systemctl start redis-server
```

`redis-check-aof --fix` truncates at the last consistent write — you lose at most 1 second
of data (the `everysec` window). This is preferable to losing the entire AOF.

---

## GameKit-specific concern: matchmaking ticker

The matchmaking ticker (`MatchmakingTickerHostedService`) processes Redis sorted sets every
500 ms. If you restore Redis from an older snapshot while the app fleet is running, the ticker
reads stale tickets and may form duplicate matches.

**Always stop the app fleet before restoring Redis.** Re-start after the restore is confirmed
healthy (step 7 above).

---

## Operational checks

```bash
# 1. Confirm AOF is on and using everysec fsync.
redis-cli -a "$REDIS_PASSWORD" CONFIG GET appendonly
redis-cli -a "$REDIS_PASSWORD" CONFIG GET appendfsync
# Expect: yes / everysec

# 2. Confirm last BGSAVE succeeded.
redis-cli -a "$REDIS_PASSWORD" INFO persistence | grep rdb_last_bgsave_status
# Expect: rdb_last_bgsave_status:ok

# 3. Confirm backups are arriving.
ls -lt /srv/backups/gamekit-redis-*.tar.gz | head -3

# 4. Confirm replicas (if any) are caught up.
redis-cli -h replica.internal -a "$REPLICA_PW" INFO replication \
    | grep -E '^(role|master_link_status|slave_repl_offset):'
```

---

## Common mistakes to avoid

- **Restoring individual AOF/RDB files** instead of the whole directory — corrupts the
  Multi-Part AOF manifest.
- **Flushing without a prior snapshot** (`FLUSHALL`/`FLUSHDB` without BGSAVE) — data is
  irrecoverably gone.
- **Restoring Redis without stopping the app fleet** — the matchmaking ticker forms duplicate
  matches within the first tick.
- **Co-locating backups on the Redis host** — disk failure takes both the live data and the backup.

---

## Related runbooks

- [`postgres-backup-restore.md`](postgres-backup-restore.md) — Postgres backup and restore.
- [`../ops/redis-aof.md`](../ops/redis-aof.md) — AOF + RDB configuration, memory tuning.
- [`../ops/disaster-recovery.md`](../ops/disaster-recovery.md) — overview index of all backup runbooks.
