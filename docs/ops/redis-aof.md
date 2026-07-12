<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Redis AOF + memory tuning

GameKit uses Redis 8.6.2 as the durable-enough store for matchmaking tickets,
party state, idempotency markers, presence keys, and the matchmaking ticker's
distributed lock. The configuration is **append-only file (AOF) with
`appendfsync everysec` + `maxmemory-policy noeviction`** — there is no
RDB-only mode supported, and there is no eviction-on-pressure mode supported.

This doc explains why and shows the production knobs to turn.

---

## Canonical configuration (dev)

The shipped `docker-compose.yml` launches `redis:8.6.2` with the following flags
(see `docker-compose.yml` lines 36-45 for the literal):

```yaml
command:
  - "redis-server"
  - "--appendonly"
  - "yes"
  - "--appendfsync"
  - "everysec"
  - "--maxmemory-policy"
  - "noeviction"
  - "--save"
  - "3600 1 300 100 60 10000"
```

The same flags are the production baseline. Translate them into
`/etc/redis/redis.conf` directives on bare metal:

```ini
appendonly yes
appendfsync everysec
maxmemory-policy noeviction
save 3600 1
save 300 100
save 60 10000
```

---

## Why AOF, not RDB-only

| Concern                                 | RDB-only                                                                                  | AOF (`everysec`)                                                              |
|-----------------------------------------|-------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------|
| Data loss window on crash               | Up to the last snapshot interval (minutes)                                                | Up to ~1 second of writes                                                     |
| Write throughput penalty                | Near-zero                                                                                 | Single `fsync` per second (file-system cache absorbs the rest)                |
| Recovery semantics                      | "Resume from snapshot, lose recent writes"                                                | "Replay every write since last AOF rewrite"                                   |
| Suitability for matchmaking tickets     | Bad — losing 5 minutes of tickets means players hard-stuck in dead queues                 | Good — at most 1s of in-flight enqueues drops on crash                        |
| Suitability for presence keys           | Acceptable (presence keys re-populate from client heartbeats within seconds)              | Good (and consistent with the rest of the surface)                            |
| Suitability for idempotency markers     | Bad — losing an idempotency marker means the duplicate request goes through               | Good                                                                          |

GameKit's design assumes Redis writes are durable to ~1 second of crash loss. Running
RDB-only voids that assumption. The `save` lines above keep RDB snapshots **in
addition to** AOF for fast cold-start recovery — Redis loads the RDB then replays
the AOF tail — but the AOF is the load-bearing durability mechanism.

---

## Why `maxmemory-policy noeviction`

The eviction policies (`allkeys-lru`, `volatile-lru`, etc.) silently delete keys
under memory pressure. Every GameKit Redis key has semantic meaning:

| Key family                         | Owner               | Effect if silently evicted                                                  |
|------------------------------------|---------------------|------------------------------------------------------------------------------|
| `mm:ticket:{id}`                   | `GameKit.Matchmaking` | Player's ticket vanishes mid-queue — UI shows "waiting" forever              |
| `mm:queue:{ladderId}`              | `GameKit.Matchmaking` | Queue entry gone — ticker sees no candidates, matches never form             |
| `mm:idempotency:{key}`             | `GameKit.Matchmaking` | Duplicate enqueue accepted — double-charges retry storms                     |
| `mm:lock:matchmaking-ticker`       | `GameKit.Matchmaking` | Leader election loses the lock — two tickers run, duplicate matches form     |
| `presence:{playerId}`              | `GameKit.Presence`    | Player reports offline despite live heartbeat — Admin UI lies                |
| `party:{partyId}`                  | `GameKit.Matchmaking` | Party state vanishes — members stuck in a half-deleted party                 |

The correct response to Redis hitting `maxmemory` is **operational alarm**, not
silent eviction. `noeviction` makes Redis return errors to clients on write
attempts, which surface as `RedisServerException` in the .NET layer — visible in
logs, easy to alert on, easy to triage.

If you genuinely need a cache (TTL-bounded, lossy-OK) for some game-specific feature
in your own consumer code, run a **second Redis instance** on a separate port and
use its own connection string. Do not flip GameKit's instance to an LRU policy.

---

## Memory sizing

Rough sizing for a single-instance deployment (one Redis process; not Cluster mode):

| Concurrent players | Working-set estimate | Recommended `maxmemory` |
|--------------------|---------------------|-------------------------|
| < 1,000            | ~50 MB              | 256 MB                  |
| 1,000 – 10,000     | ~500 MB             | 1 GB                    |
| 10,000 – 100,000   | ~5 GB               | 8 GB                    |
| 100,000+           | needs Cluster mode  | see below               |

Set `maxmemory` to roughly 2× your working-set estimate so the AOF rewrite buffer +
RDB fork have headroom. Set it via `redis.conf`:

```ini
maxmemory 8gb
```

Or at the docker-compose level:

```yaml
command:
  - "redis-server"
  - "--maxmemory"
  - "8gb"
  - "--appendonly"
  - "yes"
  # ... rest of the flags as above
```

For 100k+ concurrent players Redis Cluster mode (`cluster-enabled yes` plus a 6+
node topology) is the recommended path. GameKit is single-instance-tested today;
Cluster compatibility for matchmaking key distribution is feasible but not yet
empirically verified — track this as an integration gap in `docs/ops/disaster-recovery.md`.

---

## AOF rewrite tuning

Redis rewrites the AOF in the background to compact it. The default thresholds
(`auto-aof-rewrite-percentage 100 auto-aof-rewrite-min-size 64mb`) are reasonable
but read them once before going live:

```ini
auto-aof-rewrite-percentage 100
auto-aof-rewrite-min-size   64mb
```

This rewrites when the AOF doubles in size and is at least 64 MB. On a busy
production instance the AOF can grow to several GB between rewrites; tune
`auto-aof-rewrite-min-size` upward if you observe rewrite churn (`AOF rewrite
started` log lines every few seconds).

---

## Filesystem layout (bare metal)

Default Redis 8 data directory is `/var/lib/redis`. On a dedicated host:

```bash
# Dedicate a filesystem for Redis data — keeps AOF/RDB IO off the OS volume.
mkdir -p /var/lib/redis
chown redis:redis /var/lib/redis
chmod 0750 /var/lib/redis

# Confirm the redis.conf points there.
grep -E '^dir ' /etc/redis/redis.conf
# Expect: dir /var/lib/redis
```

Files Redis creates in `dir`:

| File                      | Purpose                                                          |
|---------------------------|------------------------------------------------------------------|
| `appendonly.aof.*.base.rdb`/`incr.aof`/`manifest` (Redis 7+) | Multi-part AOF + manifest after the AOF refactor                 |
| `dump.rdb`                | RDB snapshot (from the `save` lines)                              |
| `nodes.conf`              | Only present in Cluster mode                                      |

The "Multi-Part AOF" format from Redis 7.0 splits the AOF across `base` + `incr` files
referenced by `appendonly.aof.manifest`. Backups must copy the whole directory atomically;
copying just the manifest will not work.

---

## Operational checks

```bash
# Is AOF actually on?
redis-cli CONFIG GET appendonly
# Expect: 1) "appendonly"  2) "yes"

# What is the fsync policy?
redis-cli CONFIG GET appendfsync
# Expect: 1) "appendfsync"  2) "everysec"

# What policy gates memory pressure?
redis-cli CONFIG GET maxmemory-policy
# Expect: 1) "maxmemory-policy"  2) "noeviction"

# Current memory usage vs limit.
redis-cli INFO memory | grep -E '^(used_memory_human|maxmemory_human|maxmemory_policy):'

# Last AOF rewrite size + outcome.
redis-cli INFO persistence | grep -E '^(aof_enabled|aof_last_rewrite_time_sec|aof_current_size|aof_base_size):'

# Force an AOF rewrite NOW (useful before a planned restart).
redis-cli BGREWRITEAOF
```

---

## Backups (handoff to `disaster-recovery.md`)

The AOF is the source of truth for backup. Two approaches:

1. **Filesystem snapshot.** `BGREWRITEAOF` first to compact, then snapshot the
   `dir` directory (LVM snapshot, ZFS snapshot, `rsync` of consistent point).
2. **Live replication.** Run a Redis replica (`replicaof`) on a backup host;
   restore from the replica's AOF + RDB.

Detailed restore procedure: [`disaster-recovery.md`](disaster-recovery.md).

---

## Related runbooks

- [`bare-metal.md`](bare-metal.md) and [`container.md`](container.md) — where Redis
  fits in the deployment topology.
- [`disaster-recovery.md`](disaster-recovery.md) — backup + restore.
- [`migrations-runbook.md`](migrations-runbook.md) — Redis is unaffected by
  Postgres migrations but the matchmaking ticker requires Redis to be reachable
  before `IHost.StartAsync` completes.
