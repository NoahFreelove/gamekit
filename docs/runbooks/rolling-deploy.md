<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Runbook: zero-downtime rolling deploy

This runbook covers the procedure for deploying a new version of your
GameKit-consuming application with zero player-visible downtime in a
multi-replica setup. It focuses on the GameKit-specific considerations
(migrations, leader-lock TTL, graceful drain) that do not appear in generic
Kubernetes or load-balancer deployment guides.

**Related docs (read these first if you are setting up multi-replica for the
first time):**

- [docs/ops/multi-replica.md](../ops/multi-replica.md) — prerequisites: shared
  Data Protection key ring, SignalR Redis backplane, sticky-session configuration
- [docs/ops/migrations-runbook.md](../ops/migrations-runbook.md) — per-package
  migration history tables, advisory locks, rollback considerations
- [docs/architecture/signalr-multi-replica.md](../architecture/signalr-multi-replica.md)
  — how LobbyHub + AdminEventHub behave across replicas

---

## Pre-deploy checklist

Work through this checklist **before** moving any traffic.

### 1. Confirm migration state is clean

```bash
# Run against each package with the owner connection string.
# All packages should report "No pending migrations."
dotnet ef migrations list \
  --project src/GameKit.Core \
  --connection "$MIGRATIONS_CONN" \
  --no-build -c Release

dotnet ef migrations list \
  --project src/GameKit.Auth \
  --connection "$MIGRATIONS_CONN" \
  --no-build -c Release

# Repeat for GameKit.Admin.UI, GameKit.Rankings, GameKit.Matchmaking, GameKit.Lobby
```

If pending migrations exist, apply them **before** the rolling deploy starts.
Running `AutoMigrate=true` across multiple newly-started replicas simultaneously
is safe (the per-package advisory lock prevents double-apply) but slows startup.
Prefer a separate pre-deploy migration step. See
[docs/ops/migrations-runbook.md](../ops/migrations-runbook.md).

### 2. Verify leader-lock TTL headroom

The matchmaking ticker and rankings ticker hold a Redis distributed lock with a
TTL of 30 seconds (renewed every iteration). During a rolling deploy a replica
that is being shut down releases its lock on SIGTERM — but a brief window exists
between SIGTERM and the lock release while the in-flight iteration completes.

```bash
# Inspect the current lock TTL (Redis CLI or your preferred client)
redis-cli -u "$REDIS_URL" TTL gamekit:leader:matchmaking_ticker
redis-cli -u "$REDIS_URL" TTL gamekit:leader:rank_decay_ticker
```

If the TTL is less than 5 seconds the lock is about to expire or be contested.
Wait for the current holder to renew (the lock is renewed at each tick, roughly
every 500 ms) before sending SIGTERM to that replica. In normal operation this
headroom is always comfortable.

### 3. Check active queue depth before draining

A high queue depth means players are mid-matchmaking. Avoid draining the
matchmaking-leader replica during a high-traffic window if possible.

```bash
# Requires admin cookie (or service token if your admin endpoints accept both)
curl -s -b "$ADMIN_COOKIE" https://your-app/admin/api/matchmaking/stats \
  | jq '.totalDepth'
```

A depth of 0 or near-0 is ideal. If depth is high, consider pausing the queue
temporarily (see [matchmaking-outage runbook](./matchmaking-outage.md) §drain and
pause) while you execute the rolling deploy.

---

## Deploy procedure: canary → drain → replace

This is the recommended sequence for a two-or-more-replica setup.

### Step 1: Deploy the canary replica

1. Pull the new image (or deploy the new binary) to **one** replica only.
2. Start the new replica but do **not** yet route production traffic to it.
3. Verify `GET /health/ready` on the canary returns `200 Healthy`:

   ```bash
   curl -s http://<canary-ip>:<port>/health/ready | jq '.status'
   # Expected: "Healthy"
   ```

4. Check logs for successful migration application:

   ```
   Applied migration: 20260627000000_DrOrderingMarker (GameKit.Lobby)
   Applied migration: 20260626000000_DrOrderingMarker (GameKit.Matchmaking)
   ```

   If the canary exits or logs migration errors, **stop here** and investigate
   before proceeding. Do not route traffic until the canary is healthy.

### Step 2: Route a canary traffic slice

1. Configure your load balancer / ingress to send ~5–10 % of traffic to the
   canary replica.
2. Monitor error rates and latency for 2–5 minutes. Check that
   `matchmaking_leader_lock` in `/health/ready` shows `Healthy` for at least
   one election cycle (the new replica should acquire the lock once the old
   leader drains).

### Step 3: Drain and replace the old replicas

For each remaining old replica:

1. **Signal to drain:** send SIGTERM (or the equivalent for your orchestrator —
   Kubernetes `kubectl drain`, `docker stop`, systemd `systemctl stop`).

2. **Graceful-drain behavior (SCALE-05):** on SIGTERM the replica:
   - Stops accepting new requests (load balancer healthcheck begins failing →
     traffic routes away).
   - Allows in-flight HTTP requests to complete (ASP.NET Core's default 30 s
     shutdown timeout).
   - Runs the current ticker iteration to completion (the matchmaking ticker and
     rankings ticker check `CancellationToken.IsCancellationRequested` only at
     the top of each loop, not mid-iteration).
   - Releases the leader lock with `CancellationToken.None` — the release
     succeeds even if the host cancellation token is already cancelled.

3. Wait for the replica to exit cleanly (exit code 0). If it has not exited
   within 45 seconds, force-kill — the lock TTL (30 s) will expire and the
   remaining replicas will elect a new leader automatically.

4. Start the new replica. Confirm `/health/ready → 200 Healthy` before routing
   production traffic to it.

5. Repeat for each remaining old replica.

### Step 4: Post-deploy verification

After all replicas are running the new version:

1. `GET /health/ready` on every replica → `200 Healthy`, all entries healthy.
2. Admin stats — confirm queue depth is processing normally.
3. If you use the observability stack: check that traces from the new version
   appear in your collector / Grafana dashboard.

---

## Rollback decision gate

Trigger a rollback if any of the following occur during or after the deploy:

| Condition | Action |
|-----------|--------|
| `GET /health/ready` → `503` on the canary after 60 s | Abort — investigate before proceeding |
| `matchmaking_leader_lock` Unhealthy for > 60 s on all replicas | No leader elected — restart one replica, inspect Redis lock key |
| Error rate > baseline + 5 % for > 5 min | Roll back the canary; investigate |
| Migration error in canary logs | Abort; run `dotnet ef migrations list` to confirm state; do NOT apply new replicas |
| New replica exits at startup | See [docs/ops/migrations-runbook.md](../ops/migrations-runbook.md) §troubleshooting |

**Rollback steps:**

1. Remove the new-version replicas from traffic rotation.
2. Restart old-version replicas (they will not re-apply migrations that are
   already applied — the EF advisory lock is idempotent).
3. Verify `GET /health/ready → 200 Healthy` on the old-version replicas.
4. Investigate the failure before re-attempting the deploy.

> **Note on migration rollback:** GameKit migrations use the `Down()` method
> convention introduced in Phase 17 — `Down()` throws `NotSupportedException`.
> Migrations are intentionally forward-only. If a migration must be undone,
> do so manually with a corrective migration. Never call `dotnet ef database
> update <previous>` expecting it to succeed.

---

## SignalR considerations

During a rolling deploy some players will have existing SignalR connections
(`/hubs/lobby`, `/admin/hubs/events`) to the replica being drained. The Redis
backplane ensures that broadcasts continue to reach those clients via the
remaining replicas, but the WebSocket connection to the drained replica will
drop and the client must reconnect.

The TicTacToeDuel sample and the GameKit Admin UI both include client-side
reconnect logic. Ensure your lobby clients handle the `onreconnecting` /
`onreconnected` / `onclose` SignalR events. See
[docs/architecture/signalr-multi-replica.md](../architecture/signalr-multi-replica.md)
for the reconnect behaviour and sticky-session requirements.

---

## Quick reference

```bash
# Pre-deploy: check migration state (all packages)
for pkg in GameKit.Core GameKit.Auth GameKit.Admin.UI GameKit.Rankings \
           GameKit.Matchmaking GameKit.Lobby; do
    echo "=== $pkg ==="
    dotnet ef migrations list --project "src/$pkg" \
      --connection "$MIGRATIONS_CONN" --no-build -c Release
done

# Health check (replace with your replica address)
curl -s http://localhost:5000/health/ready | jq .

# Leader-lock TTL inspection
redis-cli -u "$REDIS_URL" TTL gamekit:leader:matchmaking_ticker

# Matchmaking queue depth (requires admin auth)
curl -s -b "$ADMIN_COOKIE" https://your-app/admin/api/matchmaking/stats | jq .
```
