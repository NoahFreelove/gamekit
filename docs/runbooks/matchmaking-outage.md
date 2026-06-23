<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Runbook: matchmaking outage — incident response

This runbook covers how to diagnose and remediate a matchmaking outage: the
state where players cannot find matches because the matchmaking ticker has
stopped, no replica holds the leader lock, or the ticket queue is backed up.

**Related docs:**

- [docs/ops/redis-aof.md](../ops/redis-aof.md) — Redis AOF configuration and
  memory tuning; required reading if the remediation escalates to Redis failover
- [docs/runbooks/redis-backup-restore.md](./redis-backup-restore.md) — procedure
  for Redis backup and restore; use if Redis data is corrupted or lost

---

## Symptoms

| Symptom | Likely cause |
|---------|-------------|
| Players report matches are not forming | Ticker stopped or queue backed up |
| `GET /health/ready` returns `503` with `matchmaking_leader_lock: Unhealthy` | No replica holds the leader lock |
| `GET /admin/api/matchmaking/stats` shows queue depth growing, proposals = 0 | Ticker not running |
| Redis memory at `maxmemory` limit | Eviction impossible (`noeviction` policy) — Redis blocks writes |
| All replicas report `matchmaking_leader_lock: Unhealthy` in `/health/ready` | Leader lock expired and no replica acquired it |

---

## Step 1: Establish the scope

```bash
# Check every replica's readiness
curl -s http://<replica-1>/health/ready | jq '.entries.matchmaking_leader_lock'
curl -s http://<replica-2>/health/ready | jq '.entries.matchmaking_leader_lock'
# Repeat for all replicas

# Expected healthy output:
# { "status": "Healthy" }
#
# Unhealthy output (leader lock not held):
# { "status": "Unhealthy", "description": "No leader lock held by any known replica" }
```

If any replica shows `matchmaking_leader_lock: Healthy` the ticker is running —
the outage may be a slow-match condition (pool too narrow) rather than a ticker
failure. Check queue depth first before escalating.

---

## Step 2: Inspect the queue depth

```bash
# Requires admin cookie (or service token where applicable)
curl -s -b "$ADMIN_COOKIE" https://your-app/admin/api/matchmaking/stats
```

Expected response structure:

```json
{
  "totalDepth": 42,
  "byPool": {
    "default": { "depth": 42, "proposalsEmitted": 0 }
  },
  "leaderLockHeld": false,
  "tickerState": "stopped"
}
```

Key fields:

| Field | Interpretation |
|-------|----------------|
| `totalDepth` > 0, `proposalsEmitted` = 0 | Ticker stopped — queue accumulating |
| `leaderLockHeld` = false | No replica currently owns the lock |
| `tickerState` = "stopped" | The in-process ticker loop exited |

---

## Step 3: Inspect the Redis leader-lock key

The matchmaking ticker uses a Redis distributed lock at the key
`gamekit:leader:matchmaking_ticker` (SET NX PX pattern with 30 s TTL).

```bash
# Check if the lock key exists (0 = does not exist → no leader)
redis-cli -u "$REDIS_URL" EXISTS gamekit:leader:matchmaking_ticker

# If it exists, check TTL and current holder
redis-cli -u "$REDIS_URL" TTL gamekit:leader:matchmaking_ticker
redis-cli -u "$REDIS_URL" GET gamekit:leader:matchmaking_ticker
```

**Interpretation:**

| `EXISTS` | `TTL` | Meaning |
|----------|-------|---------|
| 0 | — | No lock held — all replicas failed to acquire or lock expired |
| 1 | > 0 | Lock held — the holder value is the instance ID; check if that replica is healthy |
| 1 | -2 | Key exists but has no TTL set — anomalous; monitor for acquisition |

If the lock is held by a replica and that replica shows `matchmaking_leader_lock:
Unhealthy` in `/health/ready`, there is a divergence between the Redis lock state
and the replica's health view. Restart that replica (see Step 4).

---

## Step 4: Remediation

### Case A: Lock expired, no replica acquired it (most common)

This occurs after a rolling deploy gap, a Redis blip, or all replicas crashing
simultaneously.

1. Check that at least one replica is running and healthy:

   ```bash
   curl -s http://<replica>/health/live | jq '.status'
   # Expected: "Healthy"
   ```

2. If replicas are running but the lock is not being acquired, restart the
   matchmaking ticker in the application. GameKit's background ticker starts on
   application startup — restarting a replica is sufficient.

3. After restart, verify within 10 seconds:

   ```bash
   redis-cli -u "$REDIS_URL" EXISTS gamekit:leader:matchmaking_ticker
   # Expected: 1 (lock acquired by the newly started replica)
   ```

4. Confirm proposals resume:

   ```bash
   curl -s -b "$ADMIN_COOKIE" https://your-app/admin/api/matchmaking/stats \
     | jq '.leaderLockHeld, .proposalsEmitted'
   # Expected: true, > 0
   ```

### Case B: Queue backed up — drain and pause

If the queue depth is very high and you need to drain players gracefully before
a remediation step that requires downtime:

```bash
# Pause the queue — stops accepting new enqueue requests
curl -s -X POST -b "$ADMIN_COOKIE" \
  https://your-app/admin/api/matchmaking/control/pause

# Drain the existing queue — cancels pending tickets
curl -s -X POST -b "$ADMIN_COOKIE" \
  https://your-app/admin/api/matchmaking/control/drain
```

After remediation, resume the queue:

```bash
curl -s -X POST -b "$ADMIN_COOKIE" \
  https://your-app/admin/api/matchmaking/control/resume
```

> **Note:** Players with cancelled tickets will need to re-enqueue. The admin
> drain does not disconnect players from the game — it only cancels queued
> tickets that have not yet formed a match.

### Case C: Redis memory at limit (noeviction blocks writes)

See [docs/ops/redis-aof.md](../ops/redis-aof.md) for the canonical Redis memory
configuration. GameKit uses `maxmemory-policy noeviction` — when Redis reaches
its memory limit it rejects writes rather than silently evicting matchmaking
state.

Symptoms: Redis `SET NX PX` commands fail, ticker lock acquisition fails,
ticket enqueue fails.

```bash
# Check Redis memory usage
redis-cli -u "$REDIS_URL" INFO memory | grep used_memory_human
redis-cli -u "$REDIS_URL" INFO memory | grep maxmemory_human
```

If used memory is at or near `maxmemory`:

1. Increase `maxmemory` in your Redis config and `CONFIG SET maxmemory <new-value>`.
2. Or reduce GameKit's Redis footprint: drain and clear expired tickets
   (matchmaking ticket keys are TTL'd but very high queue depths accumulate).
3. Restart the matchmaking ticker once memory headroom is restored.

### Case D: Redis failover required

If Redis itself is unhealthy (not just memory-full), follow the
[Redis backup and restore runbook](./redis-backup-restore.md) for the full
failover procedure.

After Redis is restored:

1. All replicas will automatically reconnect (StackExchange.Redis uses Polly-backed
   exponential backoff internally — see
   [ADR-0007](../adr/0007-fluentvalidation-explicit.md) for the resilience pattern).
2. The matchmaking ticker will acquire the leader lock within one tick cycle
   (~500 ms) of the first successful Redis connection.
3. **Active match sessions are NOT in Redis** — they are durable in Postgres. Only
   the live ticket queue and party state live in Redis. Reconnecting players must
   re-enqueue; in-progress game sessions are unaffected.

---

## Escalation matrix

| Condition | First responder action | Escalation |
|-----------|----------------------|------------|
| Lock expired, replicas healthy | Restart one replica | None |
| All replicas down | Start a new replica | Infra on-call |
| Redis memory full | Increase maxmemory | Redis admin |
| Redis data loss | Redis restore runbook | DB admin |
| Ticker restarted but still no proposals | Check ladder config (pool names, algo config) | App team |

---

## Post-incident checklist

- [ ] Queue depth returned to 0 or expected level
- [ ] `GET /health/ready` → `200 Healthy` on all replicas, `matchmaking_leader_lock: Healthy`
- [ ] Admin stats show `proposalsEmitted > 0` for active queues
- [ ] Redis memory below 80 % of `maxmemory`
- [ ] Incident timeline documented in your ops log
- [ ] Alerted player support if significant queue disruption occurred

---

## Quick reference

```bash
# Health — all entries including leader lock
curl -s http://localhost:5000/health/ready | jq .

# Queue stats
curl -s -b "$ADMIN_COOKIE" https://your-app/admin/api/matchmaking/stats | jq .

# Redis leader-lock inspection
redis-cli -u "$REDIS_URL" GET gamekit:leader:matchmaking_ticker
redis-cli -u "$REDIS_URL" TTL gamekit:leader:matchmaking_ticker

# Admin: pause queue
curl -X POST -b "$ADMIN_COOKIE" https://your-app/admin/api/matchmaking/control/pause

# Admin: drain queue
curl -X POST -b "$ADMIN_COOKIE" https://your-app/admin/api/matchmaking/control/drain

# Admin: resume queue
curl -X POST -b "$ADMIN_COOKIE" https://your-app/admin/api/matchmaking/control/resume
```
