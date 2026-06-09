# Pitfalls Research

**Domain:** Adding observability, health/readiness, multi-replica hardening, backup/DR, security audit, load testing, and docs to a mature GPL self-hosted .NET 10 game-services library (GameKit v2.1)
**Researched:** 2026-06-08
**Confidence:** HIGH — grounded in live codebase inspection (MatchmakerLeaseHelper, MatchmakingMeter, AdminLiveBroadcastService, RankDecayLeaseHelper, MigrationRunner), locked STATE.md decisions, and v2.0 PITFALLS.md baseline

> **Scope note:** This file covers ONLY v2.1-specific pitfalls (observability, health, multi-replica, backup/DR, migration ops, load testing, security audit, docs). It does not duplicate v2.0 pitfalls (Apple OAuth, account merge, advisory-lock keys, SignalR sticky sessions, rating feedback loop, etc.) — those are baseline at `.planning/milestones/v2.0-research/PITFALLS.md`.

---

## Critical Pitfalls

### Pitfall 1: [GPL/TELEMETRY LANDMINE] PII or Secrets Emitted Into OTel Spans

**What goes wrong:**
A span attribute on `PoolSweep` includes `playerId` (UUID), or a failed-auth span carries `username` or partial token hash in an exception attribute. Under GPL + zero-telemetry constraints, this is a GDPR data leak AND a license violation: the library would be actively emitting player-identifying data to wherever the consumer has wired their OTel exporter (Tempo, Jaeger, a cloud OTLP endpoint).

The concrete risk points in this codebase:
- `MatchmakingActivitySource.StartPoolActivity(ladderId, poolName)` — safe as written; `ladderId` is an opaque Guid. Danger: if a developer adds `ticketId` or `playerId` as a tag on the inner match-formation span.
- `MatchmakingMeter.DroppedEvents` counter — safe (reason tag only). Danger: adding `playerId` to disambiguate whose ticket dropped.
- Auth spans wrapping login: exception attributes from a failed `BCrypt.Verify` or Argon2 verify could inadvertently include the `username` claim from the request context.
- Lobby SignalR spans: a developer instruments `LobbyHub.SendMessage` and adds `message.Body` as a span attribute to aid debugging.

**Why it happens:**
OTel span attributes are the go-to debugging tool. Adding `playerId` to a span during development "to see which player hit the slow path" is natural. The problem is that it persists to production, where the span propagates to whatever sink the consumer configured — which could be a SaaS OTLP endpoint (violates GPL zero-cloud), or a self-hosted Tempo instance where the span is retained for 30 days (violates GDPR right-to-erasure).

**How to avoid:**
- Establish and enforce a **span attribute allow-list** rule in `ARCHITECTURE.md` and the contributing guide: permitted tags are structural identifiers (ladder ID, session ID, pool name, package name, version) and counters/latencies. Forbidden: player IDs, usernames, device fingerprints, token hashes, IP addresses, any claim value.
- Add a CI lint step (grep or Roslyn analyzer) that fails if `SetTag` or `AddTag` is called with parameter names containing `player`, `user`, `email`, `token`, `ip`, `fingerprint`, `credential`.
- In `MatchmakingActivitySource.StartPoolActivity`, the `ladderId` Guid is safe — but add a comment explicitly documenting that `matchId` is safe to add; `playerId` is NOT. Mirror this in every `StartActivity` helper added during v2.1.
- For exception recording (`activity.RecordException(ex)`): call the overload that takes the exception object, not one that formats `ex.Message` as a tag value — exception messages from auth failures can include username fragments.
- **GPL note:** if PII lands in spans and the consumer forwards them to a SaaS OTel endpoint, GameKit has been used to facilitate phone-home telemetry, which undermines the self-hosted value proposition and arguably the GPL spirit.

**Warning signs:**
- Any `SetTag` call where the second argument is a local variable of type `Guid playerId`, `string username`, or `string token`.
- `RecordException(ex, new TagList { { "input", rawInput } })` patterns.
- Span search in Tempo returns player-identifiable data.

**Phase to address:**
Phase 13 (Observability). Before any `ActivitySource` is added to Auth, Presence, Rankings, or Lobby, the allow-list rule must be written and the CI lint check active. Do not add instrumentation first and audit later.

---

### Pitfall 2: [SECURITY CRITICAL] Prometheus Scrape Endpoint Left Unauthenticated and Network-Exposed

**What goes wrong:**
The standard OpenTelemetry Prometheus exporter mounts at `/metrics` with no authentication. In a self-hosted deployment (Docker, bare metal, Kubernetes), if the app binds to `0.0.0.0`, `/metrics` is reachable from the same network segment as `/api` — or worse, from the public internet if the operator is naive about network segregation. The `/metrics` response exposes:
- Queue depths (how many players are waiting for a match per ladder — competitive intelligence)
- Error rates and latency histograms (reveals degraded states before the operator knows)
- Internal counters (`matchmaking.analytics.dropped_events`) that reveal internal architectural details

For a GPL self-hosted library this is doubly dangerous: the operator's players are GameKit's users, and exposing queue depth per ladder leaks business-sensitive data.

**Why it happens:**
ASP.NET Core's `UseOpenTelemetryPrometheusScrapingEndpoint()` maps `/metrics` with no `RequireAuthorization()` chain. The .NET OTel docs show the one-liner without any auth scaffolding. Developers copy the one-liner and ship.

**How to avoid:**
- GameKit must NOT call `UseOpenTelemetryPrometheusScrapingEndpoint()` automatically — this is the consumer's responsibility (opt-in OTel principle).
- In the sample `TicTacToeDuel` `Program.cs` and in the getting-started tutorial, always show the scrape endpoint gated:
  ```csharp
  app.MapPrometheusScrapingEndpoint("/metrics")
     .RequireAuthorization("gamekit.admin.admin")
     // OR: restrict to a localhost/internal network CIDR via middleware
  ```
- In the `docker-compose.yml` for the observability sample stack, Prometheus scrapes via a Docker-internal network (no published port for `/metrics`) — the scrape target is `gamekit-api:8080/metrics` on the `gamekit-internal` network, not `localhost:8080/metrics` exposed to the host.
- Document in the ops guide: `/metrics` MUST be on a non-public network interface OR protected by IP allowlist middleware OR gated behind the admin authentication scheme.
- Add a startup warning log when `ASPNETCORE_ENVIRONMENT=Production` and the Prometheus endpoint is registered without any authorization.

**Warning signs:**
- `curl http://your-server/metrics` returns data without a 401/403.
- The `docker-compose.yml` observability stack publishes port 8080 (the app) to `0.0.0.0:8080` and `/metrics` is reachable from outside the container network.
- The getting-started tutorial shows `UseOpenTelemetryPrometheusScrapingEndpoint()` as a one-liner with no auth annotation.

**Phase to address:**
Phase 13 (Observability). The sample stack docker-compose must segregate the scrape endpoint into an internal Docker network. The tutorial must show auth-gated metrics from the first example.

---

### Pitfall 3: [MULTI-REPLICA] LockTtlSeconds=90 Causes Split-Brain When Ticker Takes Longer Than 90s Under Load

**What goes wrong:**
`GameKitMatchmakingTickerOptions.LockTtlSeconds = 90` (confirmed in codebase). The ticker's `MaxIterationBudgetMs = 50ms` per tick means under normal load one tick completes in ~50ms, and the lease is renewed every tick. But under load testing (1,000 concurrent tickets, 8 ladders), the Redis round-trips for `LockExtendAsync` + sorted-set `ZRANGE`/`ZRANGEBYSCORE` operations can balloon. If a GC pause, thread-pool starvation, or transient Redis latency spike causes the leader to miss multiple consecutive renewal windows, the 90s TTL expires.

When that happens:
1. Replica B acquires the lock (sees TTL expired, `LockTakeAsync` succeeds).
2. Replica A's `RenewLeaseAsync` returns `false` — but only if Replica A checks it. The `MatchmakerLeaseHelper.RenewLeaseAsync` returns `false` correctly. The danger is at the caller: if the ticker's `RunOnceAsync` ignores the `false` return from `RenewLeaseAsync` and continues processing the current pool sweep, both replicas form matches simultaneously.

The existing `MatchmakerLeaseHelper` documents this: "Pitfall §6 (renew-or-bail): callers MUST check the return value." This is now the blast radius for v2.1's load test phase: the first time a sustained load test runs the ticker under realistic multi-replica concurrency, this path will be exercised.

**Why it happens:**
The `RenewLeaseAsync` Lua-script path is correct. The failure is at the caller level: the ticker's `RunOnceAsync` loop must check the `false` return at the renewal point and abort the current tick. A developer adding a new per-pool step (e.g., a regional backfill sweep) may add the step AFTER the renewal check, meaning the new step runs even after leadership is lost.

Additionally, the decay `RankDecayLeaseHelper` has the same pattern (confirmed in codebase: `LockExtendAsync`, `RenewLeaseAsync`). The decay job runs less frequently but against a potentially large `player_ranks` table — a full-table decay pass could take 10-30 seconds, and if the decay lock TTL is shorter than the pass, a second replica starts a concurrent decay.

**How to avoid:**
- For the matchmaking ticker: the renewal check must be the **first** thing inside each pool-sweep loop body, not just at the top of the tick. If `RenewLeaseAsync` returns `false`, bail the entire tick immediately — not just the current pool.
- For the decay service: set `Decay.LockTtlSeconds` to at least 2× the expected worst-case decay run time. Given a 100k-player ladder and a 30ms-per-batch processing rate at batch size 500, worst case is ~6 seconds. A 60-second TTL with mid-run renewal every 10s is safe.
- Add a `MatchmakerSplitBrainTests` integration test: two `WebApplicationFactory` instances sharing a Testcontainers Redis; suspend Replica A's heartbeat (mock `LockExtendAsync` to return `false`); assert Replica B takes leadership and Replica A's ticker stops forming matches.
- Log a `LogWarning` when `RenewLeaseAsync` returns `false` — this must be observable in Prometheus as a counter.
- The load test (Phase 16) MUST include a multi-replica scenario: 2 replicas, 1000 tickets, sustained 60 seconds — verify zero duplicate matches in `game_sessions`.

**Warning signs:**
- Duplicate rows in `game_sessions` with the same `(ticket_id)` pair on different session IDs.
- `MatchmakerLeaseHelper: failed to extend lease — treating as lease lost` log line followed by ticket events being processed.
- Decay job writes `rank_adjust_audit` rows with identical timestamps from two different server hostnames.

**Phase to address:**
Phase 15 (Horizontal-Scale Hardening). The split-brain integration test must be a gate. Phase 16 (Load Testing) must verify under sustained concurrent load.

---

### Pitfall 4: [OBSERVABILITY] Metric Label Cardinality Explosion — Ticket/Player IDs as Tags

**What goes wrong:**
A developer adds a histogram for matchmaking latency and tags it with `ticket_id` (UUID) to enable per-ticket drill-down. In Prometheus, each unique label combination is a separate time series. With 1,000 concurrent tickets generating 500ms ticks, that is 1,000 new time series per 500ms — 2,000/second. Prometheus's in-memory time-series storage (TSDB) will exhaust available RAM within minutes. The Prometheus process OOMs, scraping stops, and the observability stack goes dark.

The same explosion happens if `player_id`, `session_id`, or `match_id` are used as histogram or counter tags. Even with low traffic, a game with 10,000 registered players generates 10,000 label cardinality if `player_id` appears on any counter.

In the existing `MatchmakingMeter.DroppedEvents` counter, the only tag is `reason` (two values: `channel_full`, `polly_exhausted`) — this is correct. The risk is that v2.1 adds NEW meters for latency histograms, queue depths, etc.

**Why it happens:**
OpenTelemetry's instrument API makes adding tags trivial. Developers from application (not library) backgrounds are accustomed to tagging spans with entity IDs for trace search — they apply the same pattern to metrics, not realizing that spans (sampled, bounded) and metrics (aggregated, unbounded) have opposite cardinality requirements.

**How to avoid:**
- **Hard rule (document in ARCHITECTURE.md):** No GameKit metric instrument may carry a label whose cardinality is proportional to data volume (ticket counts, player counts, session counts). Permitted high-cardinality dimensions: ladder name (bounded by config, typically < 10), region name (bounded by config, typically < 20), reason/result codes (bounded enum strings), package name.
- For the matchmaking queue-depth gauge: tag by `(ladder, region)` — bounded, not by ticket ID.
- For auth throughput counter: tag by `provider` (guest, password, steam, discord, google, apple, epic) — 7 values, bounded.
- For histogram bucket boundaries (latency): use explicit boundaries aligned to SLO thresholds: `[1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000]` milliseconds. Do NOT leave bucket boundaries at the OpenTelemetry default (0..infinity with auto-generated boundaries), which produces unbounded storage growth.
- Add a test in `MatchmakingMeter`'s test suite: use a `MeterListener` to capture all tag key names emitted by every instrument; assert none of them equal `ticketId`, `playerId`, `sessionId`, `matchId`.

**Warning signs:**
- Prometheus metrics page shows cardinality > 10,000 for any single metric name.
- Grafana dashboard loads slowly because Prometheus queries return thousands of series.
- Prometheus process memory grows linearly with ticket volume instead of staying flat.
- Any `SetTag` or instrument tag key whose value is a `Guid` or 36-character string.

**Phase to address:**
Phase 13 (Observability). Write the cardinality allow-list rule before writing any instrument. Include the `MeterListener` tag-key assertion test in each package's telemetry unit tests.

---

### Pitfall 5: [OBSERVABILITY] Trace-Context Loss Across PeriodicTimer Background Threads

**What goes wrong:**
The matchmaking ticker (`MatchmakerTickerService`) runs on a `PeriodicTimer` background thread. When a player's HTTP request (enqueue ticket) creates an OTel trace, the `Activity.Current` context is captured on the request thread. The matchmaking ticker runs independently on its own thread pool thread — it has NO parent `Activity` unless explicitly propagated.

This means the entire match-formation lifecycle (enqueue → ticker picks ticket → match found → session created) appears as disconnected spans in Tempo/Jaeger:
- The `/matchmaking/enqueue` request span shows a ticket enqueued.
- The `MatchmakerTickerService.RunOnceAsync` span has no parent — it starts a new trace root.
- The `SessionCompleteService` span is another disconnected trace root.

For diagnosing "why did this ticket wait 45 seconds to match?", the operator must manually correlate three unrelated traces by ticket ID — which is fragile and error-prone.

The same problem affects:
- The reconciler `BackgroundService` (matches tickets swept from Redis to Postgres).
- `RankDecayBackgroundService` (decay run has no parent trace).
- `IdempotencyCleanupService`.
- Admin `LiveBroadcastService` (Redis Pub/Sub messages arrive as their own trace root).

**Why it happens:**
`Activity.Current` is `AsyncLocal<T>` — it flows through `await` on the same logical async chain. A `BackgroundService` using `PeriodicTimer` is NOT on the same async chain as the HTTP request that enqueued the ticket. Context propagation across process boundaries or independent async chains requires explicit `Activity.SetParentId` or `Activity.SetParent(ActivityContext)` at the point where the work item is dequeued.

**How to avoid:**
- For matchmaking: when a ticket is enqueued, serialize the current `Activity.Context` (trace ID + span ID + trace flags) into the Redis ticket hash as a `traceContext` field (e.g., W3C Trace-Context format: `00-{traceId}-{spanId}-01`). When the ticker dequeues the ticket, parse the `traceContext` field and set it as the parent of the ticker's `PoolSweep` activity via `Source.StartActivity("PoolSweep", ActivityKind.Consumer, parentContext)`.
- This creates a causal trace: HTTP enqueue → ticker dequeue → match formation → session create — all linked.
- For background services with no inbound trace context (decay, cleanup, reconciler): start a new root span tagged with the service name and run ID. Don't force a fake parent — disconnected root spans are fine for periodic maintenance work.
- Add a "trace propagation" integration test: enqueue a ticket, wait for match formation, query Tempo (or use `ActivityListener`) — assert that the match-formation span has the enqueue span's trace ID as an ancestor.
- **Do NOT** propagate `HttpContext.TraceIdentifier` (ASP.NET's internal request ID) as the trace parent — it is not a W3C trace context and will corrupt the OTel trace.

**Warning signs:**
- Tempo shows 3+ disconnected traces for a single ticket's lifecycle.
- `Activity.Current?.TraceId` is different in `MatchmakerTickerService` than in the original enqueue endpoint.
- No `parentSpanId` attribute on `PoolSweep` spans.

**Phase to address:**
Phase 13 (Observability). Trace propagation via Redis ticket hash is the standard pattern for async worker context propagation. Implement it in the same plan as `MatchmakingActivitySource` instrumentation expansion.

---

### Pitfall 6: [HEALTH] Liveness Probe Fails on Transient Redis Blip, Triggering Pointless Pod Restart

**What goes wrong:**
The existing `IHealthProbeService.ProbeAsync` runs a Postgres `SELECT 1` + Redis `PingAsync`. If this method is wired directly as a Kubernetes/Docker liveness probe, a 500ms Redis timeout (transient, network hiccup, not an outage) causes the liveness check to fail. Kubernetes kills and restarts the pod. The restart:
1. Releases the matchmaker Redis lock (leader dies).
2. Drops all active SignalR lobby connections.
3. Runs all 6 package migrations (even though they're already applied — fast, but still causes a startup delay).
4. Forces all connected clients to reconnect.

A single transient Redis blip causes a full pod restart cascade in a 3-replica deployment: all 3 probes fail simultaneously, Kubernetes restarts all 3, the entire cluster restarts.

**Why it happens:**
Conflating liveness (is the process alive and not deadlocked?) with readiness (is the process ready to serve traffic?) with dependency health (are dependencies reachable?). The `IHealthProbeService.ProbeAsync` is the RIGHT check for the admin health PANEL (shows operators what's degraded). It is the WRONG check for the LIVENESS probe.

Additionally, the current probe creates a new `NpgsqlConnection` per call (confirmed: `IHealthProbeService` XML doc: "Postgres connectivity via `SELECT 1` on an `NpgsqlConnection`"). Under probe storms (Kubernetes default: every 10 seconds, 3 replicas = 18 Postgres connections/minute just for health probes), this adds real connection-pool pressure.

**How to avoid:**
- **Liveness**: a trivial always-returning endpoint (`GET /health/live` → 200 OK with `{"status":"alive"}`). No dependency check. No DB connection. Only fails if the process is deadlocked or crashed. No `IHealthProbeService` involved.
- **Readiness**: checks that migrations have been applied (read a `bool _migrationsApplied` flag set by `MigrationHostedService` after its lock-and-migrate completes) AND that the Npgsql connection pool can be acquired (one `SELECT 1` via the pool, NOT a new connection per probe). Fails until the startup hosted services complete.
- **Startup probe**: same as readiness, but with a longer `initialDelaySeconds` / `failureThreshold` to account for migration time on a cold start.
- **Dependency health** (existing `IHealthProbeService`): exposed at `GET /admin/api/health` for human operators, NOT wired to Kubernetes probes. Keep as-is.
- Add `GameKitLivenessHealthCheck` and `GameKitReadinessHealthCheck` that implement `IHealthCheck` and register with `services.AddHealthChecks()` in `AddGameKit()` — not `AddGameKitAdmin()`. Every GameKit consumer gets the probes automatically; they just need to call `app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })`.
- Use a connection from the pool (NOT `new NpgsqlConnection(connStr)`) for the readiness probe to avoid connection-pool exhaustion.

**Warning signs:**
- Kubernetes events show repeated `Liveness probe failed` → `Killing container` → restart cycles correlated with Redis CPU spike or network event.
- Postgres `pg_stat_activity` shows connections from `gamekit-health-probe` at a rate of `N_replicas / probe_interval`.
- All replicas restart simultaneously after a Redis sentinel failover.

**Phase to address:**
Phase 14 (Health & Readiness). The three-endpoint model (live/ready/dependency-health) must be decided before writing any health check code. The probe implementations should register in `GameKit.Core` (live + ready) so they're available without `GameKit.Admin.UI`.

---

### Pitfall 7: [HEALTH] Readiness Probe Never Becomes Ready Because It Checks an Optional Dependency

**What goes wrong:**
A readiness probe that checks Redis availability will never flip ready on a Core-only install (no matchmaking, no Redis registered). Worse: if `IConnectionMultiplexer` is registered but Redis is temporarily unreachable at startup (Redis container starts slower than the app), the readiness probe fails indefinitely and the pod never enters service — even though Postgres is healthy and the app can serve auth requests.

The existing `AdminLiveBroadcastService` handles Redis absence correctly: `if (_mux is null) return;`. The health probe must apply the same conditional logic.

**Why it happens:**
Developers write readiness checks by iterating all registered `IHealthCheck` implementations. If one check depends on `IConnectionMultiplexer` being non-null and non-degraded, the check fails when Redis is temporarily slow — and "temporarily slow" at startup is normal during docker-compose bring-up.

**How to avoid:**
- Tag-based health check filtering: `GameKitRedisHealthCheck` is tagged `"redis"`. Readiness endpoint: `Predicate = check => check.Tags.Contains("core")`. Redis check only appears in the dependency-health panel (`check.Tags.Contains("redis")`).
- Startup readiness check: migration completion flag + Postgres pool (tagged `"core"`). Nothing else.
- The Redis health check is an informational probe only — it informs the admin panel's Redis tile, but a Redis failure does NOT flip the pod unready (Polly handles transient Redis failures in the matchmaking ticker; the pod can still serve auth, rankings, presence).
- Document this explicitly: GameKit's readiness = "migrations applied + Postgres reachable". Redis, while critical to matchmaking, is handled at the application level via Polly — not at the orchestration level via readiness.

**Warning signs:**
- Pod stays in `Terminating` or never-ready state after Redis restart even though the app is otherwise healthy.
- `kubectl describe pod` shows `Readiness probe failed` with a Redis connection error.
- Core-only (no matchmaking) installs fail readiness because the Redis health check was unconditionally registered.

**Phase to address:**
Phase 14 (Health & Readiness). Health check tag taxonomy must be designed before implementation.

---

### Pitfall 8: [BACKUP/DR] Backup Never Gets Restore-Tested — Discovered Corrupt During Incident

**What goes wrong:**
The ops guide documents `pg_dump` and Redis `BGSAVE`. The backup files exist on disk. But the backup has never been restored into a test environment. The actual restore fails because:
- The dump was taken while a migration was mid-flight (advisory lock held during dump → dump captured a partial migration state).
- The Redis dump (`dump.rdb`) is from a different point in time than the Postgres dump — matchmaking tickets in Redis reference `ticket_id`s that do not exist in Postgres (or Postgres has tickets marked `Matched` whose Redis sorted-set entries were already cleaned up in the 30-minute gap between dumps).
- The `pg_restore` process fails because the restore target has a newer version of a package's migration history table that the dump's schema doesn't match.
- The ops guide says to restore into a fresh Postgres, but the `init.sql` scripts that create `gamekit_owner`/`gamekit_app` roles are not in the dump — the restore fails with `role does not exist`.

**Why it happens:**
Backup procedures are written by people who have never attempted a restore in anger. The asymmetry between Postgres point-in-time and Redis snapshot consistency is invisible until a real incident. The multi-package migration history makes restoring into a specific migration state non-obvious.

**How to avoid:**
- Mandate a **restore rehearsal** as part of Phase 17 (Backup/DR): the phase is not complete until a full `pg_dump` → destroy → `pg_restore` → start app → run health probe passes in CI using Testcontainers.
- For the Postgres-Redis consistency gap: document a **backup window procedure**: (1) pause matchmaking by toggling `GameKitMatchmakingOptions.Ticker.TickIntervalMs` to a very large value via admin API or config reload, (2) wait for Redis queue to drain (all in-flight tickets complete or timeout), (3) take Postgres dump, (4) take Redis `BGSAVE`, (5) resume ticker. This reduces the consistency gap from "minutes" to "seconds" without requiring a coordinated atomic snapshot.
- In the restore runbook, explicitly include: `psql -c "CREATE ROLE gamekit_owner ..."` before `pg_restore`. Source the role-creation SQL from `docker/postgres/init/` — this directory already exists in the repo.
- For migration-state restoration: include a `SELECT * FROM __ef_migrations_<pkg>` query in the restore verification script. Assert all 6 packages' last migration timestamps match the deployed binary's expected state.
- Add a restore-test step to the Phase 17 CI job: dump → Testcontainers wipe → restore → assert `SELECT COUNT(*) FROM gamekit.players` matches pre-dump count.
- **Secrets in dumps:** `pg_dump` does not dump roles by default, but if `pg_dumpall` is used, it dumps role passwords. Document: use `pg_dump` (not `pg_dumpall`); store role passwords separately in a secrets manager; the `init.sql` creates roles — do not encode passwords in the dump.

**Warning signs:**
- The ops guide documents backups but has no "restore procedure" section.
- The last restore test was never performed (no CI job runs it).
- The Postgres dump timestamp and the Redis `LASTSAVE` timestamp differ by more than 5 minutes.
- A restore attempt produces `ERROR: role "gamekit_app" does not exist`.

**Phase to address:**
Phase 17 (Backup/DR + Migration Ops). Restore rehearsal is a gate criterion — the phase is not done until CI validates a complete dump-restore cycle.

---

### Pitfall 9: [MIGRATIONS] Dry-Run Breaks the Per-Package Advisory Lock + HostedService Ordering

**What goes wrong:**
An operator wants to preview what SQL a migration will generate before applying it (`dotnet ef migrations script`). They run the script against production, inspect the SQL, then decide to apply. Between the script run and the actual `dotnet database update`, another replica may have already applied the migration (via the `MigrationHostedService` on startup). The second `dotnet database update` call races with a live application's migration hosted service, both holding (or contending for) the advisory lock. If the manual `dotnet database update` holds the lock long enough, the live app's `MigrationHostedService` waits in `StartAsync` — and Kestrel does not start. The app appears unhealthy to the load balancer.

A separate problem: the per-package migration ordering convention uses deterministic timestamps (`20260415000000` for Core, `20260418000000` for Auth, etc.). If a developer adds a new v2.1 migration to `GameKit.Core` with an EF CLI-generated timestamp (`20260608123456`) and another package already has a migration with timestamp `20260610000000`, EF's migration history will apply Core's migration AFTER the other package's, which may depend on schema the Core migration hasn't added yet.

**Why it happens:**
The advisory-lock pattern was designed for parallel app startups, not for coordinating between app startup and manual `dotnet database update` invocations. They use the same lock key.

The timestamp ordering issue arises because the deterministic timestamp convention (`20260415000000`, `20260418000000`) was established for v1 packages but EF CLI always generates the current timestamp when `dotnet ef migrations add` is run.

**How to avoid:**
- For dry-run: always use `dotnet ef migrations script --idempotent` (generates `IF NOT EXISTS` guarded SQL) and review output — do NOT then run `dotnet database update` on a live system. The idempotent script is safe to apply even if already applied.
- For production migration ops: recommend the operator drain traffic first (set replicas=0 or cordon the node), then apply migrations, then scale back up. The advisory lock is designed for concurrent startups, not for mixed CLI + app scenarios.
- For the timestamp ordering convention: add a `MigrationTimestampTests` that asserts each package's latest migration timestamp is lexicographically greater than the previous package's latest timestamp (Core < Auth < Admin < Rankings < Matchmaking < Lobby). EF applies migrations in timestamp order; this test catches a cross-package ordering inversion at CI time.
- For down-migrations (rollback): document the policy clearly — GameKit does NOT support down-migrations in production. `MigrationBuilder.Down()` methods are generated by EF but should never be invoked against a live database. Reason: destructive down-migrations (DROP TABLE, DROP COLUMN) cannot be rolled back, and per-package migration boundary invariants make cross-package rollback impossible without coordinated downgrade of all packages simultaneously. The safe rollback path is: restore from backup.
- Add a CI check that asserts `Down()` methods in all migration files contain only `throw new NotSupportedException("GameKit does not support down-migrations in production.")`.

**Warning signs:**
- `pg_locks` shows two different PIDs holding (or waiting for) the same advisory lock key.
- App startup hangs at `[MigrationHostedService: acquiring advisory lock]` while a manual `dotnet database update` is in progress.
- A new Core migration has a timestamp earlier than an existing Auth migration.
- `dotnet ef migrations list` shows migrations applied out of expected order.

**Phase to address:**
Phase 17 (Backup/DR + Migration Ops). The `MigrationTimestampTests` and down-migration policy must be established. A dry-run runbook section is a deliverable of this phase.

---

### Pitfall 10: [LOAD TESTING] Testing on Localhost Hides Npgsql Default Pool Exhaustion

**What goes wrong:**
A load test runs against `localhost` with one app instance, one Postgres instance (the dev `docker-compose.yml`). The test forms 1,000 concurrent tickets and sees excellent throughput. The `LoadTestFixture` in `GameKit.Matchmaking.Integration.Tests` pins `MaxPoolSize=25` (from STATE.md: "Pitfall §8 mitigation: 1k-concurrent-ticket load test must not exhaust the default Npgsql pool"). In production, the consumer's app defaults to Npgsql's default `MaxPoolSize=100`, and with 4 replicas, each claiming 25% of the database connection limit, they hit Postgres's `max_connections=100` (common default) immediately under load.

The test passes with `MaxPoolSize=25` but production crashes with `NpgsqlException: sorry, too many clients already`.

A related problem: localhost TCP is zero-latency. The matchmaking ticker's `MaxIterationBudgetMs=50ms` budget is met easily on localhost where Redis round-trips take < 1ms. In production, Redis is a network hop away (2-5ms per round-trip). With 8 ladders and 4 pools each, the ticker does ~32 Redis ZRANGE operations per tick — that is 32 × 5ms = 160ms of Redis I/O per tick, which blows the 50ms budget immediately.

**Why it happens:**
Load tests on localhost are not representative of production network topology. The existing test fixture mitigates one aspect (pool size) but not the latency profile.

**How to avoid:**
- Load tests in Phase 16 MUST run against a topology that reflects production: app process → Redis via TCP (localhost is acceptable if measured latency is constrained to 2-5ms using `tc netem` or a separate Docker network with artificial delay), app process → Postgres via TCP with a `MaxPoolSize` that matches the per-replica production recommendation.
- Document a `MaxPoolSize` recommendation in the ops guide: with N replicas, each replica should set `MaxPoolSize = floor(postgres_max_connections * 0.8 / N) - 5` (80% headroom, minus 5 for admin/health-probe connections).
- The `MaxIterationBudgetMs=50ms` budget must be VALIDATED under realistic Redis latency (not localhost). If the budget is consistently exceeded, either increase it (allowing the ticker to run longer but reducing tick frequency) or reduce the per-tick work (skip ladders if budget is exhausted, resume on next tick with round-robin ladder selection).
- JIT/GC warmup: the load test must include a warmup phase (at minimum 1,000 tickets processed before measurements begin) to avoid JIT compilation dominating P50/P99 results.
- SignalR fan-out load test: lobby hub with 100 connected clients per lobby, 50 concurrent lobbies, rapid status changes (ReadyChecking→InGame). This MUST be tested against the Redis backplane (`AddStackExchangeRedis`) not just in-process — the backplane adds a Pub/Sub round-trip to every broadcast.

**Warning signs:**
- Load test P99 latency is below the production SLO but production P99 is 10× higher.
- Production Postgres logs show `FATAL: remaining connection slots are reserved for non-replication superuser connections`.
- Ticker traces show `MaxIterationBudgetMs exceeded` in production but never in load tests.
- SignalR fan-out load test uses `TestServer` without Redis backplane (backplane messages are sync-in-memory, not async pub/sub round-trip).

**Phase to address:**
Phase 16 (Load/Performance Testing). The test topology must document and enforce minimum network latency injection. Npgsql pool sizing recommendation must be a deliverable. SignalR fan-out must use a real Redis backplane.

---

### Pitfall 11: [SECURITY AUDIT] Dependency CVE Scan Treated as One-Time, Not CI-Gated

**What goes wrong:**
A security audit runs `dotnet list package --vulnerable` once during Phase 18. Three CVEs are found and remediated. The audit is declared complete. Six months later, a new CVE is disclosed in `StackExchange.Redis 2.8.41` (hypothetical). The library ships with the vulnerable version because there is no ongoing CI gate.

For a GPL library that operators self-host: the operator's exposure is determined by the version of GameKit they have deployed. If a CVE is present in a transitive dependency and the library does not pin a remediated version, the operator is exposed indefinitely until GameKit publishes a patch — and they won't know about it without a CI-gated notification.

**Why it happens:**
Security audits are treated as point-in-time events. NuGet vulnerability data is updated daily by the NuGet security team. The `dotnet list package --vulnerable` command returns current vulnerability data based on the NuGet advisory feed. There is no mechanism to block a build when a new advisory is published against an already-pinned version — unless CI is configured to run `--vulnerable --include-transitive` and fail on `HIGH` or `CRITICAL`.

**How to avoid:**
- Add a CI step: `dotnet list package --vulnerable --include-transitive` that fails the build if any package with severity `HIGH` or `CRITICAL` is found. Run this in the `main` CI workflow (not just at release time).
- Pin all dependencies in `Directory.Packages.props` (already done). When a CVE is patched in a new release, the CPM pin makes the remediation a single-line change.
- For transitive dependencies: also run `dotnet list package --outdated --include-transitive` monthly (a scheduled CI job, not blocking) to flag drift.
- Add a `SECURITY.md` to the repo documenting the process for reporting vulnerabilities and the expected response time.
- The audit phase must also cover: (1) egress allow-list guard (existing `EgressAllowListHandler`) still enforces `GameKitAuthOptions.AllowedProviderHosts`; (2) GDPR delete path covers all per-package tables (add a `GdprDeleteCoverage` integration test that asserts all player FK tables are cleaned); (3) rate-limit partition keys are correct under IPv6 (the `RemoteIp` parser must handle both `::1` and `127.0.0.1`).
- **Supply-chain blind spot:** check that none of the transitive dependencies have been transferred to new maintainers without source code audit (common attack vector). Flag any dep where the latest author does not match the author from v1 research.

**Warning signs:**
- `dotnet list package --vulnerable` output includes `HIGH` or `CRITICAL` entries that are not tracked in an issue.
- No CI step produces a build failure on a new CVE disclosure.
- `SECURITY.md` does not exist.
- The transitive dependency graph has changed since v1.0 without a review (`Directory.Packages.props` has new entries not in the original stack table).

**Phase to address:**
Phase 18 (Security Audit). The CI gate must be added as the FIRST task of this phase, before the manual threat-model review. "Audit complete" is defined as: CI gate is green + manual review of egress/rate-limit/GDPR coverage is documented.

---

### Pitfall 12: [DOCS] XML Doc Comments and Tutorial Drift — XML Docs Describe v1 Behavior

**What goes wrong:**
`MatchmakingActivitySource.StartPoolActivity` and `MatchmakingMeter.DroppedEvents` both carry XML doc comments cross-referencing "Pitfall §7" from the original research (the internal numbering of the original research doc). By v2.1, the pitfall numbering has changed (this document). If the XML doc cref points to a symbol that has been renamed or removed, CS1574 fires (enforced as error by `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` + `CS1591` policy). But if the cref is syntactically valid but semantically stale (points to the right symbol but describes old behavior), the doc comment will mislead consumers for years.

More commonly: the getting-started tutorial shows a `docker-compose.yml` for the observability stack. As Grafana, Prometheus, and Tempo update their container image versions, the tutorial's pinned image tags (`grafana/grafana:latest`, `prom/prometheus:v2.x`) become stale. The tutorial fails to run end-to-end with the image versions it specifies.

**Why it happens:**
Code is tested in CI; documentation is not. XML doc comments are checked for syntax (CS1574 cref validation) but not for semantic accuracy. Tutorial `docker-compose.yml` files are not run in CI — they're written and forgotten.

**How to avoid:**
- Add a **tutorial smoke test** to CI: `docker compose -f samples/TicTacToeDuel/docker-compose.observability.yml up -d --wait` → `curl http://localhost:3000/api/health` (Grafana) → `curl http://localhost:9090/-/ready` (Prometheus) → `curl http://localhost:3200/ready` (Tempo) → assert all return 200. Run this in a weekly scheduled CI job, not every commit (slow, but prevents silent rot).
- Pin exact image tags for ALL containers in the observability `docker-compose.yml` (not `latest`). Use Dependabot or Renovate (already a common choice for Docker image updates) to propose version bumps.
- For XML doc comments: establish the convention that XML docs cross-reference symbols (via `<see cref="..."/>`) not numbered pitfall lists. A `<see cref="MatchmakingActivitySource.SourceName"/>` is a verified, compiler-checked cross-reference. "Pitfall §7" is a plain-text string that silently becomes stale.
- For the MinVer release train: the upgrade guide must be tested — take a v2.0 sample app, follow the v2.0→v2.1 upgrade steps, and assert the result builds and passes health checks. This is a one-time test at release, not CI-gated.
- **Version skew in docs:** since all 7 NuGet packages share a single MinVer version, docs can always say "install version X" without per-package version confusion. The docs must display a SINGLE version badge, not per-package badges, to avoid the common mistake of installing mismatched versions.

**Warning signs:**
- `docker compose -f samples/.../docker-compose.observability.yml up` fails with `manifest for grafana/grafana:latest not found`.
- The tutorial's `dotnet add package GameKit.Core --version X` step uses a version that is not on NuGet.org (pre-publish).
- XML doc comment says "see Pitfall §N" where N refers to a section number that no longer exists in any current document.
- Getting-started tutorial has never been run end-to-end by a developer who is not the original author.

**Phase to address:**
Phase 19 (Docs & Tutorial). The tutorial smoke test in CI is a gate criterion for the phase. Pin all Docker image tags in observability compose files. Run the upgrade guide against a real v2.0 sample install.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Wire `/metrics` without auth, document "add auth later" | Faster to demo | Metrics endpoint permanently unauthenticated in most consumer deploys | Never in production samples |
| Use `ladderId` Guid as a Prometheus label on a per-tick histogram | Easy per-ladder drill-down | Cardinality proportional to ladder count — bounded, BUT establishes a pattern that developers will follow with `ticketId` | Only if ladder count is documented as bounded (< 50); never for `ticketId` |
| Skip the restore rehearsal, document backup procedure only | Saves a day of DR work | Backup discovered corrupt during actual incident | Never; DR without tested restore is theater |
| Use `Activity.Current?.TraceId.ToString()` as a log correlation ID instead of propagating context | 30 minutes of work | Logs and traces are correlated by string matching, not by proper trace context; breaks when the activity tree is complex | Acceptable as a TEMPORARY correlation aid while OTel propagation is being wired |
| Down-migrations with real DDL (DROP TABLE) | Allows rollback via EF CLI | Destructive and unrecoverable; violates per-package migration boundary for cross-package data | Never in production; generate `throw new NotSupportedException` in `Down()` |
| Run load tests on localhost with artificial traffic | Fast and deterministic | Masks network latency, connection pool limits, backplane overhead | Only for microbenchmarks (single-path latency); never for "production capacity" decisions |
| Health check that calls `IHealthProbeService.ProbeAsync` as the liveness probe | One implementation for all health | Kills pods on transient dependency blips; cascades in multi-replica deployments | Never; liveness and readiness must be separate probes |
| Tutorial `docker-compose.yml` uses `latest` image tags | No maintenance needed | Tutorial silently breaks when upstream images change | Never in a shipped tutorial; pin exact tags |

---

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| OTel + Prometheus (consumer-side) | Consumer calls `AddMeter("GameKit.Matchmaking")` but forgets `AddSource("GameKit.Matchmaking.Ticker")` — metrics appear but traces are missing | Document both the `AddMeter` and `AddSource` names in the `AddMatchmaking()` XML doc, with copy-paste examples |
| OTel + `BackgroundService` | Using `Activity.Current` inside `PeriodicTimer` loop — always `null` because the HTTP request context is not inherited | Use explicit `Source.StartActivity()` to create a new root span per tick, or propagate context via Redis as described in Pitfall 5 |
| Prometheus scrape + Docker Compose | App binds to `localhost:8080` inside container; Prometheus scrape target is also `localhost:9090` — the container localhost is isolated, scrape fails | Scrape target must be the container service name: `gamekit-api:8080/metrics` on the internal Docker network |
| Health checks + Kubernetes | `MapHealthChecks("/healthz/live")` registers at the root path; if `AddGameKitAdmin()` also registers routes under `/admin/*`, both work fine — but if the operator maps the admin group at a non-default mount path, they must also update the liveness probe URL | Document: liveness probe URL is always `/healthz/live` (rooted, not relative to admin mount path) |
| Grafana + Tempo (distributed tracing) | Grafana datasource points to `http://tempo:3200` but traces are stored under a tenant ID — queries return empty | For self-hosted single-tenant Tempo (the common case), no `X-Scope-OrgID` header is needed; for multi-tenant, the header must be set in Grafana's Tempo datasource config |
| `pg_dump` + per-package migrations | Dump taken with `--schema=gamekit` — includes only the `gamekit` schema; `__ef_migrations_core` and other history tables are in the `public` schema → history tables excluded from dump → restore succeeds but EF thinks no migrations are applied → runs all migrations again on first startup | Dump must include BOTH `--schema=gamekit` AND `--schema=public` (for migration history tables) OR use `pg_dump` without `--schema` to dump all schemas |
| Redis AOF + `BGSAVE` | `BGSAVE` captures the RDB snapshot; if AOF is enabled, the authoritative state is the AOF log — the RDB may be slightly stale. During restore, Redis replays the AOF, which may include commands that reference keys cleaned by the matchmaking reconciler after the RDB was written | For restore, the AOF is the correct source (not the RDB); document: copy `appendonly.aof` (not `dump.rdb`) in the backup procedure |
| OpenTelemetry SDK version in consumer vs. GameKit | GameKit references `OpenTelemetry` 1.10.x abstractions only. Consumer installs `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.11.x. SDK and instrumentation packages must share the same major version | Document: GameKit uses only the OTel API (`System.Diagnostics.Metrics`, `System.Diagnostics.DiagnosticSource`) — no OTel SDK dependency. Consumer installs the SDK + exporters independently. No version conflict possible because GameKit has no SDK dep. |

---

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Ticker budget of 50ms vs. production Redis RTT of 5ms × 32 ops per tick | `MaxIterationBudgetMs exceeded` log on every tick; effective tick frequency drops from 2/s to 0.3/s | Validate `MaxIterationBudgetMs` against measured Redis RTT in the target deployment; increase to at least `4 × N_ladders × N_pools × avg_redis_rtt_ms` | At N_ladders × N_pools > 10 in a network-separated deployment |
| ErrorRateRingBuffer per-replica, not cluster-aggregated | Admin health panel shows one replica's error rate; operator thinks cluster is healthy when 2 of 3 replicas are degraded | Add a Redis-backed cluster error rate aggregator (publish `LogLevel.Error` events to a Redis stream, aggregate in the health probe); expose per-instance + cluster-aggregate in the health panel | At > 1 replica |
| Prometheus label cardinality explosion (covered in Pitfall 4) | Prometheus OOM; scrape timeouts | Hard rule: no entity-ID labels | At > 1,000 unique label values |
| `DbContext` created per health probe (existing `IHealthProbeService`) | Postgres connection pool exhaustion under probe storms (Kubernetes: 3 replicas × 6 probes/minute = 18 extra connections/min) | Use a dedicated Npgsql connection from the pool for the `SELECT 1` probe, not a new `DbContext`; or use `NpgsqlConnection` with the app's connection string directly | At > 3 replicas with 10s probe interval |
| SignalR fan-out with large Lobby payloads over Redis backplane | Redis Pub/Sub message size grows; Redis CPU spikes; backplane latency > 100ms | Cap Lobby message payloads at 4KB (existing v2.0 pitfall); cap lobby chat history push to deltas (not full history) per SignalR connection | At > 50 active lobbies with 10 members each, sending chat at > 1 msg/s |
| OTel span recording on every matchmaking tick (500ms × N_spans) | Spans accumulate faster than the OTLP exporter can drain them; exporter buffer fills; spans dropped | Sample the ticker at 1% (or use a `ParentBased` sampler); per-pool spans are not needed on every tick — they are needed for diagnosing outliers | At > 1 tick/second with a span per pool per tick |

---

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Prometheus `/metrics` unauthenticated and network-exposed | Exposes internal topology, queue depths, error rates; competitive intelligence leak; potential information disclosure for attackers planning load-based timing attacks | Gate behind admin auth scheme or restrict to internal network interface via middleware (Pitfall 2 — SECURITY CRITICAL) |
| PII in OTel span attributes (`player_id`, `username`, `email`) | GDPR violation; potential phone-home if consumer forwards spans to a SaaS OTel endpoint (violates GPL self-hosted promise) | Span attribute allow-list; CI lint; no entity IDs in spans (Pitfall 1 — GPL LANDMINE) |
| Postgres dump includes role passwords (via `pg_dumpall`) | Role passwords exposed in backup file; if file is compromised, attacker has DB credentials | Use `pg_dump` (not `pg_dumpall`); store role creation SQL separately; never include `ENCRYPTED PASSWORD` in version-controlled init scripts |
| GDPR delete path missing for new v2 tables | Player data retained after deletion request; GDPR non-compliance | `GdprDeleteCoverage` integration test asserts all tables with `player_id` FK are cleaned by `GdprDeleteService`; any new v2.1 table added with a `player_id` FK must be added to the delete path |
| Rate-limit partition key fails for IPv6 (`::1` vs `127.0.0.1`) | A single IPv6 client can exceed the intended per-IP rate limit because `::1` and `::ffff:127.0.0.1` are treated as different keys | Normalize `RemoteIp` to a canonical form (strip IPv4-mapped IPv6 prefix) before using as the partition key; add a unit test with `::ffff:1.2.3.4` input |
| Admin health endpoint exposes migration history table names, exception stack traces, or internal service names | Information disclosure for attackers; stack traces reveal framework internals | Health endpoint returns structured `HealthReport` (existing — Postgres/Redis status + latency only, no stack traces). Ensure `Exception.Message` is NOT included in the `Detail` field for production responses — use a generic `"Postgres connectivity error"` not `ex.Message` |
| OTel exporter configured with a SaaS endpoint in sample config | The sample `appsettings.json` sets `OTEL_EXPORTER_OTLP_ENDPOINT=https://api.honeycomb.io` — if a consumer copies the sample without changing it, their telemetry goes to a SaaS endpoint | Sample config must use only self-hosted endpoints (`http://tempo:4317`, `http://prometheus:9090`). No SaaS OTLP endpoints anywhere in the codebase — violates GPL zero-cloud invariant |

---

## "Looks Done But Isn't" Checklist

- [ ] **OTel instrumentation:** `ActivitySource` and `Meter` declared in all packages, but `AddSource`/`AddMeter` registration examples are not in XML docs or the getting-started tutorial — operators won't see any traces/metrics. Verify: run the sample stack and confirm traces appear in Tempo for an enqueue request.
- [ ] **Prometheus endpoint:** `/metrics` is registered but has no auth. Verify: `curl -u "" http://your-app/metrics` returns 401, not metric data.
- [ ] **Liveness probe:** the probe is registered but calls `IHealthProbeService.ProbeAsync` (dependency check, not liveness). Verify: kill Redis, assert the liveness endpoint still returns 200.
- [ ] **Readiness probe:** the probe waits for migrations but does not wait for the Redis connection to warm up, causing premature ready status. Verify: start the app against a slow Redis, assert the readiness probe returns 503 until Redis is reachable AND migrations are complete.
- [ ] **Multi-replica ticker:** load test shows zero duplicate matches, but the test used a single replica. Verify: two-replica integration test with simulated leadership expiry produces zero duplicate `game_session` rows.
- [ ] **Backup restore:** the backup procedure is documented and the backup files exist, but no restore test has run. Verify: the Phase 17 CI job completes a full dump-restore cycle and the app passes health checks after restore.
- [ ] **Down-migrations:** `Down()` methods contain real DDL (generated by EF). Verify: grep all migration files for `ALTER TABLE` or `DROP TABLE` inside `Down()` methods — assert only `throw new NotSupportedException(...)` exists.
- [ ] **GDPR delete coverage:** new v2.1 tables (e.g., OTel span retention tables, backup log tables) added to Postgres are not included in `GdprDeleteService`. Verify: `GdprDeleteCoverage` integration test passes after adding all v2.1 tables.
- [ ] **Tutorial smoke test:** the tutorial runs end-to-end in CI (docker compose up → health probe → enqueue match → verify Grafana dashboard populated). Verify: CI weekly job is green.
- [ ] **CVE gate:** `dotnet list package --vulnerable` returns clean output in CI. Verify: CI build fails immediately if any HIGH/CRITICAL CVE is introduced.
- [ ] **No SaaS in sample config:** no `appsettings.json` or `docker-compose.yml` in the repo references a SaaS OTel endpoint, SaaS monitoring service, or phone-home URL. Verify: `grep -r "honeycomb\|datadog\|newrelic\|dynatrace\|cloudwatch" samples/ .planning/` returns empty.
- [ ] **Span PII:** no `SetTag` call in any package sets a tag whose value is a player ID, username, email, token, or IP address. Verify: CI lint check passes.

---

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| PII discovered in live Tempo spans | HIGH | Immediately redeploy with the offending tag removed; purge Tempo data for affected trace IDs via Tempo API (`DELETE /api/traces/{traceId}`); notify affected players per GDPR breach procedure |
| Prometheus endpoint found unauthenticated in production | MEDIUM | Add auth middleware or IP restriction; rolling restart; rotate any secrets that may have been exposed via metric label values (none in default config — risk is information disclosure, not secret leak) |
| Split-brain: duplicate game sessions from two leader replicas | HIGH | Stop all replicas immediately; identify duplicates via `SELECT * FROM game_sessions WHERE ...` (compare `created_at` timestamps within same `(player_id_a, player_id_b)` combinations); keep the earlier session, GDPR-delete the duplicate; compensate players via admin rank adjust; investigate `pg_locks` history |
| Redis lock expiry during decay run causing double-decay | MEDIUM | Identify duplicate `rank_adjust_audit` rows with `reason='decay'` and same `(player_id, period_end)`; reverse the extra deduction via admin rank-adjust API; increase `Decay.LockTtlSeconds` and redeploy |
| Backup restore fails due to missing roles | MEDIUM | Run `docker/postgres/init/01-roles.sql` against the restore target first; then retry `pg_restore`; if the dump is corrupt (partial migration state), fall back to the last known-good dump |
| Prometheus cardinality explosion | MEDIUM | Kill Prometheus; wipe the Prometheus data volume; redeploy Prometheus; redeploy the app with the offending metric removed; metrics are lost for the gap period (acceptable — they were corrupted anyway) |
| Tutorial docker-compose fails due to stale image tag | LOW | Update the image tag to the latest stable version; re-pin; update tutorial; CI smoke test prevents future rot |
| `dotnet ef migrations script` + live app lock contention | LOW | Wait for the advisory lock to release (the running app will unlock after migration completes); never run `dotnet database update` on a live system; use rolling restart with `MigrationHostedService` instead |

---

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| PII in OTel spans (GPL/GDPR landmine) | Phase 13 — Observability (first task, before any instrumentation) | CI lint: `grep -r 'SetTag.*playerId\|SetTag.*username\|SetTag.*email' src/` returns empty |
| Prometheus endpoint unauthenticated | Phase 13 — Observability | Integration test: unauthenticated `GET /metrics` returns 401 |
| Trace-context loss across `PeriodicTimer` (ticker/reconciler/decay) | Phase 13 — Observability | ActivityListener test: match-formation span has enqueue span's traceId as ancestor |
| Metric label cardinality explosion | Phase 13 — Observability | `MeterListener` test: asserts no tag key in any instrument matches `ticketId/playerId/sessionId/matchId` |
| Split-brain under LockTtl expiry | Phase 15 — Horizontal-Scale Hardening | Two-replica integration test: simulated lease expiry → zero duplicate game sessions |
| Leader lost mid-work (decay, ticker) | Phase 15 — Horizontal-Scale Hardening | Unit test: `RenewLeaseAsync` returning `false` causes immediate bail from current iteration |
| Liveness probe kills pods on transient Redis blip | Phase 14 — Health & Readiness | Integration test: Redis killed mid-run → liveness returns 200; readiness returns 503 |
| Readiness never ready due to optional dependency | Phase 14 — Health & Readiness | Integration test: Core-only install (no Redis) → readiness flips ready after Postgres + migrations |
| `IHealthProbeService` as liveness (wrong probe type) | Phase 14 — Health & Readiness | Architecture review gate: three-endpoint model documented before implementation |
| DbContext leak in health probes | Phase 14 — Health & Readiness | Integration test: 60 probe calls in 1 minute → `pg_stat_activity` shows <= 2 connections from probe |
| Backup never restore-tested | Phase 17 — Backup/DR + Migration Ops | CI: dump → Testcontainers destroy → restore → health probe passes |
| Redis-Postgres consistency gap during backup | Phase 17 — Backup/DR + Migration Ops | Runbook documents backup window procedure (pause ticker, drain, dump) |
| Secrets in Postgres dump | Phase 17 — Backup/DR + Migration Ops | CI: `grep -i 'password\|secret' dump.sql` returns empty |
| Dry-run/advisory-lock contention with live app | Phase 17 — Backup/DR + Migration Ops | Runbook documents: never `dotnet database update` on live system; use idempotent script |
| Down-migrations with real DDL | Phase 17 — Backup/DR + Migration Ops | CI: grep all `Down()` methods assert only `NotSupportedException` |
| Migration timestamp ordering inversion | Phase 17 — Backup/DR + Migration Ops | `MigrationTimestampTests`: assert lexicographic ordering across packages |
| Load test on localhost hides pool exhaustion | Phase 16 — Load/Performance Testing | Load test topology documentation; Npgsql pool sizing in ops guide |
| Load test hides Redis RTT budget problem | Phase 16 — Load/Performance Testing | `MaxIterationBudgetMs` validated against measured RTT; `BudgetExceeded` counter exposed as metric |
| SignalR fan-out without Redis backplane in load test | Phase 16 — Load/Performance Testing | Fan-out test uses real Testcontainers Redis with `AddStackExchangeRedis` |
| JIT/GC warmup skews load test P99 | Phase 16 — Load/Performance Testing | Load test runner: 1,000-request warmup before measurement window |
| CVE scan one-time, not CI-gated | Phase 18 — Security Audit | CI step: `dotnet list package --vulnerable --include-transitive` fails on HIGH/CRITICAL |
| GDPR delete missing new v2 tables | Phase 18 — Security Audit | `GdprDeleteCoverage` integration test: asserts all `player_id` FK tables cleaned |
| Rate-limit IPv6 normalization gap | Phase 18 — Security Audit | Unit test: `RemoteIp=::ffff:1.2.3.4` and `RemoteIp=1.2.3.4` produce the same partition key |
| SaaS OTLP endpoint in sample config | Phase 18 — Security Audit | CI: `grep -r 'honeycomb\|datadog\|newrelic' samples/` returns empty |
| Tutorial drift (docker-compose stale tags, XML docs stale) | Phase 19 — Docs & Tutorial | Weekly CI: tutorial smoke test (docker compose up → health probe → metrics visible in Grafana) |
| Docs-code version skew | Phase 19 — Docs & Tutorial | Upgrade guide tested against real v2.0 sample install before phase complete |

---

## Sources

- Codebase inspection: `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` + `MatchmakingActivitySource.cs` — HIGH (live source, confirms `ladderId`/`poolName` tag pattern; no `playerId` tag as yet)
- Codebase inspection: `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` — HIGH (confirms `LockTtlSeconds=90`, `RenewLeaseAsync` bail-on-false requirement, `InstanceId = MachineName:Guid` fencing token)
- Codebase inspection: `src/GameKit.Matchmaking/GameKitMatchmakingTickerOptions.cs` — HIGH (confirms `LockTtlSeconds=90`, `MaxIterationBudgetMs=50`)
- Codebase inspection: `src/GameKit.Admin.UI/Services/AdminLiveBroadcastService.cs` — HIGH (confirms Redis null-guard pattern for single-instance; `gamekit:admin:events` channel name)
- Codebase inspection: `src/GameKit.Admin.UI/Services/IHealthProbeService.cs` + `HealthReport.cs` — HIGH (confirms Postgres `SELECT 1` + Redis `PingAsync` probe; used for admin panel, NOT yet for k8s probes)
- Codebase inspection: `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` — HIGH (confirms decay uses same Lua-script `LockExtendAsync` pattern; distinct lock key from ticker)
- Codebase inspection: `docker-compose.yml` — HIGH (confirms Redis `--appendonly yes --appendfsync everysec`, no published `/metrics` port)
- STATE.md Decisions Locked: `LoadTestFixture pins MaxPoolSize=25` (Phase 05 pitfall §8 mitigation) — HIGH
- STATE.md Decisions Locked: `Redis --appendonly yes --appendfsync everysec` — HIGH
- [MS Learn: .NET Observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) — HIGH (ActivitySource, Meter, opt-in SDK pattern)
- [OpenTelemetry .NET: Best practices for instrument naming](https://opentelemetry.io/docs/languages/net/instrumentation/) — HIGH (cardinality guidance)
- [Prometheus: Best practices for metric and label naming](https://prometheus.io/docs/practices/naming/) — HIGH (cardinality explosion, label bound constraints)
- [Kubernetes: Configure Liveness, Readiness and Startup Probes](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/) — HIGH (three-probe model; liveness vs readiness vs startup)
- [MS Learn: ASP.NET Core health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) — HIGH (IHealthCheck, tag-based filtering, `MapHealthChecks`)
- [W3C Trace Context spec](https://www.w3.org/TR/trace-context/) — HIGH (trace-context propagation format for Redis ticket hash)
- [PostgreSQL: pg_dump reference](https://www.postgresql.org/docs/current/app-pgdump.html) — HIGH (`--schema` flag behavior re: history tables in `public` schema)
- [StackExchange.Redis: LockTake / LockRelease Lua script](https://stackexchange.github.io/StackExchange.Redis/Locking) — HIGH (fencing-token semantics confirmed)
- [GDPR Article 17 — Right to Erasure](https://gdpr.eu/right-to-be-forgotten/) — HIGH (trigger for GDPR delete coverage assertion)
- [NuGet: dotnet list package --vulnerable](https://learn.microsoft.com/en-us/nuget/consume-packages/audit-packages) — HIGH (CI-gated vulnerability scanning)
- GameKit v2.0 PITFALLS.md: pitfalls 4 (advisory lock collision), 5 (SignalR sticky sessions), 6-11 (rating/matchmaking) — HIGH (baseline; not duplicated here)

---

*Pitfalls research for: GameKit v2.1 — Observability, Health/Readiness, Horizontal-Scale Hardening, Backup/DR, Migration Ops, Load Testing, Security Audit, and Docs added to a mature GPL .NET 10 game-services library*
*Researched: 2026-06-08*
