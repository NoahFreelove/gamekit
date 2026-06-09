# Feature Research

**Domain:** Self-hosted game-services backend library — operability & hardening (v2.1)
**Researched:** 2026-06-08
**Confidence:** HIGH (for .NET/OTel/health patterns); MEDIUM (for DR runbook and security audit scope)

---

## Scope of This Document

This research covers the **7 v2.1 operability & hardening feature areas** only. All v1.0 and v2.0
game-services features (auth, matchmaking, rankings, lobby, presence, admin) are treated as given.
The library already has: opt-in OTel abstractions (`ActivitySource`/`Meter`), an admin health panel
(Postgres + Redis ping + fleet error counter), multi-replica Admin (Redis backplane), and
BackgroundService leader election (Redis `SET NX PX`). v2.1 builds on those seams — it does not
add new game-services surface.

**Library vs. consumer responsibility** is called out explicitly in every section. GameKit provides
the seam, the opt-in wiring, and the reference configuration; the operator runs the backend.

---

## Feature Landscape by Capability Area

---

### Area 1: Observability — OTel Traces + Metrics + Sample Dashboard

#### Table Stakes (Operators Expect These)

| Feature | Why Expected | Complexity | Package(s) Touched | Library or Consumer |
|---------|--------------|------------|-------------------|---------------------|
| `ActivitySource` spans on every HTTP handler path (auth, matchmaking enqueue, session complete, lobby state transitions) | Any operator connecting a tracing backend expects to see request traces end-to-end; a library that instruments nothing forces the operator to guess where latency lives | MEDIUM | `GameKit.Core`, `GameKit.Auth`, `GameKit.Matchmaking`, `GameKit.Rankings`, `GameKit.Lobby`, `GameKit.Presence` | Library provides named `ActivitySource` per package; consumer registers `AddSource("GameKit.*")` in their OTel SDK setup |
| `Meter` instruments for RED metrics on all HTTP endpoints: request count, error count, latency histogram | Operators running Prometheus/Grafana need request rate, error rate, duration per endpoint; these are the minimum signals for any production alert | MEDIUM | `GameKit.Core` (base meter helpers); package-specific meters in Auth, Matchmaking, Rankings, Lobby | Library provides; consumer opts in with `AddMeter("GameKit.*")` |
| Background-job metrics: matchmaking ticker lag, queue depth per pool, decay job run duration, leader-lock acquisition failures | BackgroundService jobs are opaque without instrumentation; operators cannot detect a stuck ticker or a Redis leader contention spike | MEDIUM | `GameKit.Matchmaking`, `GameKit.Rankings` (decay job) | Library provides; opt-in |
| Lobby SignalR metrics: connected clients, messages/sec, ready-check completion rate | SignalR fan-out is the primary scale concern for Lobby; operators need connection counts and message throughput to size their Redis backplane | MEDIUM | `GameKit.Lobby` | Library provides; opt-in |
| `/metrics` endpoint compatible with Prometheus scrape (or OTLP push) | Prometheus is the dominant self-hosted metrics backend; operators need a pull endpoint OR OTLP push to their collector | LOW | Sample app / consumer composition root | **Consumer responsibility**: add `OpenTelemetry.Exporter.Prometheus.AspNetCore` or configure OTLP exporter. GameKit does not add an exporter hard dependency. |
| Per-package metric namespace isolation: `gamekit.auth.*`, `gamekit.matchmaking.*`, etc. | Operator dashboards must distinguish signals from different subsystems; a flat `gamekit.*` namespace makes filtering impossible at scale | LOW | All packages | Library responsibility: name meters `GameKit.<PackageName>` |
| Trace context propagation through async paths (matchmaking ticker, decay BackgroundService, lobby ready-check broadcast) | W3C TraceContext must flow through background jobs so a client request trace connects to the downstream background work it triggered | MEDIUM | `GameKit.Matchmaking`, `GameKit.Rankings`, `GameKit.Lobby` | Library responsibility |
| Self-hosted sample dashboard (Grafana + Prometheus + Tempo via `docker-compose`) in the sample app | Operators need a working reference they can clone and adapt; a library that says "bring your own dashboard" with no starting point creates days of setup work | MEDIUM | `samples/TicTacToeDuel` | Library provides the `docker-compose.yml` + provisioned Grafana dashboards as part of the sample; **not** shipped in NuGet packages |

#### Differentiators

| Feature | Value Proposition | Complexity | Package(s) Touched | Library or Consumer |
|---------|-------------------|------------|-------------------|---------------------|
| Matchmaking ticker span linking: each ticker iteration starts a child span of the originating enqueue span (using `ActivityContext` propagated through the Redis ticket payload) | Makes the full journey — "player queued → ticker found match" — visible as a single trace tree rather than two disconnected spans. Nakama and PlayFab do not offer this for self-hosted setups | HIGH | `GameKit.Matchmaking` | Library; no consumer wiring needed |
| Lobby SignalR trace propagation: hub method invocations carry the calling client's W3C trace parent | Real-time event traces are normally disconnected from the HTTP request that caused them; propagating through SignalR method headers makes ready-check and lobby state change observable end-to-end | MEDIUM | `GameKit.Lobby` | Library |
| Pre-built Grafana dashboard JSON for matchmaking queue depth + ticker health committed in the repo | Operators can import a working dashboard in 30 seconds rather than building one from scratch | LOW | `samples/TicTacToeDuel/observability/dashboards/` | Library (sample) |
| `GameKit.Observability` convenience extension package that registers all ActivitySources + Meters + OTLP exporter in one call: `builder.Services.AddGameKitObservability(o => o.UseOtlpExporter("..."))` | Reduces consumer boilerplate from 20 lines to 1; still strictly opt-in; zero friction to get a working setup | LOW | New thin `GameKit.Observability` package | Library; consumer installs and calls once |

#### Anti-Features

| Feature | Why Problematic | What to Do Instead |
|---------|-----------------|-------------------|
| Shipping an OTel SDK hard dependency in every package | Forces the OTel SDK on every consumer even if they do not want observability; adds ~3 MB of transitive deps; violates "install only what you need" | Use only `System.Diagnostics.DiagnosticSource` (in-box, zero deps) in all packages; the OTel SDK is a consumer/`GameKit.Observability` concern |
| Auto-uploading metrics or traces to any SaaS endpoint (Datadog, New Relic, Honeycomb) from within the library | Phone-home; violates GPL self-hosted + zero-telemetry commitment | OTLP-only in sample stack; all exporters are consumer-configurable |
| Bundling a pre-configured Grafana/Prometheus container as a NuGet package side effect | NuGet packages cannot ship Docker containers; attempting this would be architecture category error | Grafana stack lives in `samples/` as `docker-compose.yml` + provisioned configs only |
| Injecting a `.NET Aspire` dashboard dependency | Aspire dashboard is useful in dev but not a self-hosted production tool; forces .NET Aspire on consumers who may not want it | OTLP + standalone Grafana is the production path; Aspire can be used by consumers independently if desired |
| Writing OTel trace IDs to the Postgres `admin_audit_log` table | Logs and traces have different retention policies; mixing them forces operators to keep the DB around as long as the trace store | Keep trace IDs in the trace backend only; audit log is for business events |

**Operator experience:** Add `GameKit.Observability`, configure one OTLP endpoint (or Prometheus scrape), start the sample `docker-compose up observability` stack, import the bundled Grafana dashboard. Within 5 minutes: RED metrics per endpoint, trace timelines for auth + matchmaking + lobby, ticker health gauge.

**Testability:** Integration tests assert that `ActivitySource` names are emitted correctly using an `InMemoryExporter`; metric counter assertions after specific operations (enqueue increments `gamekit.matchmaking.enqueue.total`).

---

### Area 2: Health & Readiness

#### Table Stakes (Operators Expect These)

| Feature | Why Expected | Complexity | Package(s) Touched | Library or Consumer |
|---------|--------------|------------|-------------------|---------------------|
| Separate `/health/live` and `/health/ready` endpoints with K8s-probe-compatible JSON responses (`{"status":"Healthy"}` shape) | K8s, Docker Swarm, and any modern orchestrator distinguish liveness (should the container be restarted?) from readiness (should traffic be sent?); a single `/health` endpoint cannot serve both probes correctly | LOW | `GameKit.Core` (extension method `AddGameKitHealthChecks()`) | Library provides the registrations + endpoint mapping; consumer maps the routes in their `Program.cs` |
| Readiness probe includes: Postgres connectivity (`SELECT 1`), Redis `PING`, pending EF migrations count = 0 | Operators must not receive traffic before the database is reachable and migrations have run; a running-but-broken replica causes data errors not just 503s | MEDIUM | `GameKit.Core` | Library provides check implementations |
| Liveness probe: lightweight, no DB/Redis calls, only checks that the process is alive and the DI container resolved successfully | Liveness must be very fast and never fail due to a downstream dependency (a slow Postgres cannot cause a container restart loop); this is the ASP.NET Core standard pattern | LOW | `GameKit.Core` | Library provides a simple "always healthy" liveness check; consumer may add custom checks |
| Migration-applied probe: each package's `IHealthCheck` verifies that its own migration history table exists and all known migrations are applied | Operators deploying multiple replicas need to ensure each replica has run its migrations before accepting traffic; this prevents split-schema scenarios | MEDIUM | `GameKit.Core`, `GameKit.Auth`, `GameKit.Matchmaking`, `GameKit.Rankings`, `GameKit.Lobby`, `GameKit.Presence` (one check per package) | Library provides per-package `IMigrationHealthCheck`; registered by each package's `Add*` extension method |
| Startup probe / startup-gating: block readiness until all readiness checks pass on first startup | K8s startup probes give longer initialization windows than liveness; the library should cooperate by not marking itself ready until DB + Redis + migrations are confirmed | LOW | `GameKit.Core` | Library behavior: readiness probe returns `Unhealthy` until all dependencies pass; K8s startup probe calls `/health/ready` repeatedly |
| Health check results surfaced in Admin UI health panel (extend existing panel) | The admin health panel already exists (v1.0); v2.1 should feed structured check results (component name, status, description) into the existing panel rather than duplicating it | LOW | `GameKit.Admin.UI` | Library: wire `HealthCheckService` results into the existing panel data push |

#### Differentiators

| Feature | Value Proposition | Complexity | Package(s) Touched | Library or Consumer |
|---------|-------------------|------------|-------------------|---------------------|
| Per-package degraded state (not just healthy/unhealthy): e.g. matchmaking ticker not holding leader lock = `Degraded` rather than `Unhealthy` | Three-state health (`Healthy`, `Degraded`, `Unhealthy`) maps cleanly to alert severity: `Unhealthy` = page on-call, `Degraded` = ticket for tomorrow. ASP.NET Core health checks support all three states | MEDIUM | `GameKit.Matchmaking`, `GameKit.Rankings`, `GameKit.Lobby` | Library |
| Redis leader-lock health probe: reports which replica currently holds the matchmaking leader lock and how long until expiry | Operators debugging multi-replica deployments need to know which replica is the "active" ticker; this is currently invisible | LOW | `GameKit.Matchmaking` | Library |
| Named health check groupings exposed as a `GET /health/detail` JSON report (tags: `db`, `redis`, `migrations`, `leader`) | Operators can query specific subsystem health programmatically (monitoring scripts, deployment gates) | LOW | `GameKit.Core` | Library |

#### Anti-Features

| Feature | Why Problematic | What to Do Instead |
|---------|-----------------|-------------------|
| Including business-logic checks in the liveness probe (player count, queue depth) | Makes liveness flaky under normal load spikes; triggers container restarts during peak play | Liveness = process alive only; business metrics go in readiness (for degraded) or OTel metrics |
| Auto-restarting the process when a readiness check fails | ASP.NET Core does not own its own process lifecycle; restart decisions belong to the orchestrator | Return `Unhealthy` from the health check endpoint and let K8s/Docker decide |
| Exposing sensitive infrastructure details (DB connection string, Redis host) in health check JSON responses | Health endpoint may be externally reachable; leaking infrastructure details is a security issue | Include only component name, status, and a human-readable description; never include connection details |
| A single shared `/health` endpoint for both probes | Readiness + liveness must be distinguishable by the orchestrator; a single endpoint cannot express "I'm alive but not ready to serve traffic" | Two endpoints: `/health/live` and `/health/ready` |

**Operator experience:** Add `AddGameKitHealthChecks()` in `Program.cs`. Map `/health/live` and `/health/ready` in the request pipeline. K8s deployment YAML references the two endpoints. Admin UI health panel automatically shows component-level status. Deploy a new replica: it stays out of rotation until Postgres, Redis, and all package migrations pass.

**Testability:** Integration tests (Testcontainers) assert: (a) `/health/ready` returns 503 when Postgres container is stopped; (b) `/health/ready` returns 503 when a migration is pending; (c) `/health/live` returns 200 regardless of Postgres state; (d) after applying all migrations, `/health/ready` returns 200.

---

### Area 3: Horizontal-Scale Hardening

#### Table Stakes (Operators Expect These)

| Feature | Why Expected | Complexity | Package(s) Touched | Library or Consumer |
|---------|--------------|------------|-------------------|---------------------|
| Leader election correctness under churn: when the leader replica is killed mid-tick, a new replica acquires the lock within at most one TTL interval and the ticker resumes without duplicate processing | Any operator running >1 replica in a K8s rolling deploy will experience leader handoff; the current SET NX PX implementation must be proven correct under concurrent acquisition attempts | MEDIUM | `GameKit.Matchmaking`, `GameKit.Rankings` (decay job) | Library; proven by automated tests |
| Graceful shutdown: on SIGTERM, in-flight HTTP requests are drained (ASP.NET Core default), leader lock is released proactively (SET PX 0), and the active ticker iteration completes before the `StopAsync` deadline | The default 30-second graceful shutdown window must be cooperated with; a replica that dies holding the leader lock causes a full TTL gap in ticker operation | MEDIUM | `GameKit.Matchmaking`, `GameKit.Rankings`, `GameKit.Core` (`BackgroundServiceBase`) | Library |
| Idempotent match creation: if two replicas race to create a match after a Redis Lua CAS fails to prevent the race, the second write must be a no-op (Postgres `INSERT … ON CONFLICT DO NOTHING` + idempotency key) | Rare but real under burst load; duplicate match rows corrupt session data | MEDIUM | `GameKit.Matchmaking`, `GameKit.Core` | Library |
| Lobby SignalR backplane correctness: all connected clients across all replicas receive hub events regardless of which replica sends them | Already wired (v2.0 Redis backplane); v2.1 must prove this works under replica restart and Redis reconnect by adding integration tests | LOW | `GameKit.Lobby`, `GameKit.Admin.UI` | Library proves via tests; consumer configures sticky sessions at LB |
| Documented sticky-session requirement: operators must configure load-balancer affinity (nginx `ip_hash`, K8s Service `sessionAffinity: ClientIP`, or cookie affinity) | Without sticky sessions, SignalR WebSocket upgrades fail randomly; this is the operator's most common multi-replica mistake | LOW | Docs / `GameKit.Lobby`, `GameKit.Admin.UI` | **Consumer responsibility**; library documents it clearly |

#### Differentiators

| Feature | Value Proposition | Complexity | Package(s) Touched | Library or Consumer |
|---------|-------------------|------------|-------------------|---------------------|
| Fuzz test for leader election: a Testcontainers test that starts 3 replicas, kills the leader at random intervals, and asserts that the ticker's total match count equals the number that would have been produced by a single-leader run (no duplicates, no gaps longer than 2× TTL) | This is the kind of test that rarely exists in OSS game backends and immediately proves production correctness | HIGH | `GameKit.Matchmaking` + test project | Library |
| Graceful drain test: integration test that sends 100 concurrent requests, then calls `SIGTERM` and asserts zero 5xx responses and zero match duplicates | Demonstrates zero-downtime rolling deploys in the test suite | MEDIUM | `GameKit.Core` + test project | Library |
| `BackgroundServiceBase` extract: shared base class for leader-elected background services (matchmaking ticker, decay job, future jobs) with built-in graceful drain, lock release on shutdown, and OTel instrumentation, eliminating copy-paste across packages | Currently ticker + decay job duplicate the leader election pattern; a shared base class reduces drift and makes the pattern auditable in one place | MEDIUM | `GameKit.Core` | Library |

#### Anti-Features

| Feature | Why Problematic | What to Do Instead |
|---------|-----------------|-------------------|
| Opinionated K8s manifests or Helm charts shipped as part of the library | Operator K8s topologies vary widely; prescriptive manifests will be wrong for most deployments and become a maintenance burden | Provide sample deployment YAML as documentation examples only (in `samples/`); never as a shipped artifact |
| Auto-configuring sticky sessions from within the library | The library cannot reach the load balancer; attempting this would require injecting middleware that sets response cookies, which is both fragile and presumptuous | Document the sticky session requirement clearly with nginx/K8s cookbook examples |
| Using a distributed lock library (e.g. RedLock.net) to replace the existing Redis SET NX PX pattern | The existing pattern is correct for single-Redis setups (which is all GameKit supports); RedLock introduces multi-master Redis complexity that conflicts with the self-hosted simplicity goal | Keep SET NX PX; document why RedLock is not needed for the supported topology |
| Attempting cross-replica state synchronization beyond what Redis pub/sub already provides | Full state replication (Raft, Paxos) is an order-of-magnitude more complex and not needed when Redis is the single source of truth for all mutable state | Redis pub/sub for admin events + Redis sorted sets for queue state is sufficient |

**Operator experience:** Deploy 3 replicas behind nginx with `ip_hash`. Kill the leader pod mid-game. Observe: new leader acquires lock within 5 seconds, no match created twice, no lobby messages dropped, health endpoint on remaining replicas shows healthy. Rolling deploy: new replica starts, readiness probe gates traffic, old replica drains 30s, zero dropped WebSocket connections.

**Testability:** Leader election fuzz test; graceful drain test; idempotency test (concurrent `SessionCompleteAsync` calls for the same idempotency key produce exactly one row); SignalR multi-replica fan-out test using two in-process test servers.

---

### Area 4: Backup / Restore / DR + Migration Ops

#### Table Stakes (Operators Expect These)

| Feature | Why Expected | Complexity | Package(s) Touched | Library or Consumer |
|---------|--------------|------------|-------------------|---------------------|
| Documented Postgres backup runbook: `pg_dump` logical backup + WAL-G or Barman incremental backup with point-in-time recovery (PITR) instructions | Self-hosters running a stateful game backend need a working restore procedure before they go to production; a library with no DR guidance is irresponsible | LOW (docs) | `docs/runbooks/postgres-backup-restore.md` | **Consumer responsibility** to run; library provides the runbook |
| Documented Redis backup runbook: RDB snapshot copy + AOF truncation guidance for pre-destructive-operation recovery (`FLUSHALL` guard) | Redis holds live matchmaking queue state; operators must know what they can and cannot recover from a crash | LOW (docs) | `docs/runbooks/redis-backup-restore.md` | **Consumer responsibility** to run; library provides the runbook |
| Verified round-trip restore test: a script (not just documentation) that backs up the sample app's DB, drops it, restores it, and asserts the app passes its health checks | A runbook no one has tested is not a runbook; the round-trip test makes restore confidence a CI artifact | MEDIUM | `scripts/dr-roundtrip-test.sh` + `samples/TicTacToeDuel` | Library provides the script; consumer runs it in their environment |
| EF Core migration dry-run: `dotnet ef migrations script --idempotent` + a documented per-package application order | Operators must be able to see the SQL that will run before running it; idempotent scripts make re-runs safe | LOW | `docs/migration-ops.md` | Library documents the commands; EF Core provides the tooling |
| Per-package migration ordering documentation: which package's migrations must run before which others (Core → Auth → Matchmaking → Rankings → Lobby → Presence) | Operators applying migrations from multiple packages in the wrong order will hit FK constraint violations | LOW (docs) | `docs/migration-ops.md` | Library documents ordering; FK constraints enforce it at DB level |
| EF Core migration rollback procedure: documented `dotnet ef database update <PreviousMigration>` pattern with a warning about destructive `Down()` methods | Operators who ship a bad migration need to know how to roll back; the Down() gotcha (data loss if not hand-written carefully) must be called out | LOW (docs) | `docs/migration-ops.md` | Library documents; consumer executes |

#### Differentiators

| Feature | Value Proposition | Complexity | Package(s) Touched | Library or Consumer |
|---------|-------------------|------------|-------------------|---------------------|
| `gamekit migrations list` CLI command that shows all packages' pending migration counts and the recommended application order | Operators with multiple packages installed need a unified view; `dotnet ef migrations list` is per-context only | MEDIUM | `GameKit.Cli` | Library |
| `gamekit migrations apply --dry-run` CLI command that prints the SQL for all pending migrations across all installed packages without executing | One-stop dry-run for multi-package deployments; fills the gap that `dotnet ef migrations script` is per-context | MEDIUM | `GameKit.Cli` | Library |
| Backup verification test committed to the test suite: Testcontainers spins up Postgres, seeds data, runs `pg_dump`, truncates tables, runs `pg_restore`, asserts data integrity | Makes backup correctness a CI gate rather than a doc assumption | MEDIUM | `tests/GameKit.DR.Tests/` | Library |

#### Anti-Features

| Feature | Why Problematic | What to Do Instead |
|---------|-----------------|-------------------|
| Auto-running migrations on startup (EF Core `MigrateAsync()` in `Program.cs`) | In a multi-replica deployment, all replicas race to apply migrations simultaneously; the advisory lock in EF Core's Npgsql migration runner mitigates but does not eliminate this; for production, migrations should be applied as a pre-deploy step | Recommend `migrate` as a separate deploy step via CLI or an init container; document the risk of `MigrateAsync()` in multi-replica setups |
| Shipping a managed backup service that writes backups to a location within the library's control | The library has no knowledge of the operator's storage topology; a built-in backup service would write to a path that may not be backed by durable storage | Provide the script + runbook; let the operator wire backup destination to S3, NFS, local disk, etc. |
| Bundling pgBackRest or Barman as a dependency | These are server-side tools, not .NET libraries; they cannot be a NuGet dependency | Reference them in the runbook; let the operator install them on their server |
| Opinionated migration auto-rollback on deploy failure | Rollback logic that runs automatically can cause data loss if the `Down()` methods are incomplete; the operator must decide | Document the rollback procedure; never auto-execute it |

**Operator experience:** Before first deploy: run `gamekit migrations apply --dry-run`, review SQL, run `gamekit migrations apply`. Before major release: `pg_dump` to durable storage, run DR round-trip test script, confirm health checks pass. On rollback need: `dotnet ef database update <PreviousMigration>` per the documented order. Redis: copy current RDB file before any bulk operation.

**Testability:** CI gate: backup round-trip Testcontainers test. `gamekit migrations list` integration test: assert correct ordering output. `--dry-run` test: assert SQL output contains expected table names.

---

### Area 5: Security Audit

#### Table Stakes (Operators Expect These)

| Feature | Why Expected | Complexity | Package(s) Touched | Library or Consumer |
|---------|--------------|------------|-------------------|---------------------|
| JWT threat-model verification: algorithm confusion (none/RS256 downgrade), audience/issuer validation, expiry enforcement, refresh token revocation completeness | JWT is the v1 auth foundation; production-grade libraries ship with a documented threat model for their auth layer | MEDIUM | `GameKit.Auth` | Library: verify implementation matches threat model; document findings |
| Admin endpoint authentication audit: all `/admin/*` routes require `GameKitAdmin` cookie scheme; no admin route is accessible to JWT-authenticated game clients | Admin UI cookie auth scheme was introduced in v1; the audit verifies no route accidentally falls through to game-client auth | LOW | `GameKit.Admin.UI` | Library: route audit test + documentation |
| Rate-limit audit: all public auth endpoints (`/api/auth/login`, `/api/auth/register`, `/api/auth/refresh`) have rate limits registered and enforced | Rate limiting was added in v1; the audit verifies the rate-limit helpers are applied consistently across all write endpoints, not just a subset | MEDIUM | `GameKit.Auth`, `GameKit.Core` | Library: audit test that enumerates endpoints and asserts rate-limit policy presence |
| GDPR compliance audit: `DeletePlayerAsync` reaches all FK tables (identity, credentials, refresh tokens, ranks, session participants, lobby members, matchmaking tickets); soft-delete vs hard-delete policy is documented | GDPR delete was implemented in v1 but v2 added new tables (lobby, party, regional pools); the audit verifies new tables are covered | MEDIUM | `GameKit.Core`, `GameKit.Lobby`, `GameKit.Matchmaking` | Library |
| Egress audit: verify no package makes outbound HTTP calls to any external endpoint beyond configured OAuth providers; no phone-home, no CDN, no cloud API | GPL self-hosted commitment; consumers must be able to run air-gapped | MEDIUM | All packages | Library: static analysis + integration test asserting no unregistered outbound HTTP |
| `dotnet list package --vulnerable` / OWASP Dependency-Check scan committed as a CI gate | Supply-chain security is table stakes for any library targeting production use; known CVEs in transitive dependencies must be surfaced | LOW | CI / `Directory.Build.props` | Library: add CI step; consumer benefits automatically |
| Security checklist document (threat model → implementation → test mapping) | Operators adopting a library for auth/auth-adjacent work need to audit it; a traceable checklist lets them verify claims | LOW (docs) | `docs/security-checklist.md` | Library |

#### Differentiators

| Feature | Value Proposition | Complexity | Package(s) Touched | Library or Consumer |
|---------|-------------------|------------|-------------------|---------------------|
| Automated security integration test: test that a JWT with `alg: none` is rejected; a JWT with wrong audience is rejected; an expired token is rejected; a revoked refresh token cannot be exchanged | Most OSS game backends don't ship these as automated tests; they prove the auth implementation is not just "works in the happy path" | MEDIUM | `GameKit.Auth` test project | Library |
| SBOM generation (`dotnet sbom-tool generate` or NuGet package lock files) committed as a CI artifact | Software Bill of Materials is increasingly required for enterprise consumers; generating it costs nothing but demonstrates supply-chain hygiene | LOW | CI | Library |
| Refresh token SHA-256 invariant test: assert that no migration or code change stores a raw token in `refresh_tokens.token_hash` (the column name enforces intent; a test asserts the hash algorithm is applied) | Enforces the security invariant documented in PROJECT.md; prevents a future contributor from accidentally storing raw tokens | LOW | `GameKit.Auth` test project | Library |
| Admin CSRF verification test: assert that state-changing admin API calls without an antiforgery token return 400 | CSRF protection was added in v1; the test makes it a regression gate | LOW | `GameKit.Admin.UI` test project | Library |

#### Anti-Features

| Feature | Why Problematic | What to Do Instead |
|---------|-----------------|-------------------|
| Shipping a WAF or rate-limiting proxy as part of the library | Nginx, Cloudflare, or application-level rate limiting is the operator's infrastructure concern; the library cannot know the operator's network topology | Library provides rate-limit `IServiceCollection` helpers; operator configures the outer network layer |
| Auto-blocking IPs or geo-filtering | Geolocation requires a database lookup (MaxMind GeoIP or similar) which is either a SaaS dependency or a large bundled dataset; both violate the self-hosted/no-SaaS constraint | Document that IP filtering is an nginx/firewall/load-balancer responsibility |
| Running a vulnerability scanner on the consuming application's code | The library has no visibility into the consumer's application code and cannot take responsibility for vulnerabilities introduced by the consumer | Scope the CVE scan to GameKit's own dependencies only |
| Storing audit log entries with PII beyond what is strictly necessary | Audit logs are long-lived; overlogging PII increases GDPR exposure | Audit log entries record action, actor player ID, target player ID, timestamp — never email, password hash, or OAuth tokens |

**Operator experience:** Clone repo, run `dotnet list package --vulnerable`. Review `docs/security-checklist.md`. Run the security integration test suite. Check the SBOM artifact in CI. Before going to production: verify rate limits are configured, admin endpoint is not publicly routable without VPN, JWT signing key is rotated from the default sample key.

**Testability:** CI gates: `dotnet list package --vulnerable` returns zero high/critical CVEs. Auth security tests: algorithm confusion rejection, audience/issuer validation, expiry, refresh token revocation. GDPR delete completeness test: seed player + all FK tables, call `DeletePlayerAsync`, assert all tables return zero rows for that player ID.

---

### Area 6: Load / Performance Testing

#### Table Stakes (Operators Expect These)

| Feature | Why Expected | Complexity | Package(s) Touched | Library or Consumer |
|---------|--------------|------------|-------------------|---------------------|
| Auth throughput benchmark: `login + token issue` at target p99 latency (BCrypt cost factor tuning guidance) | Auth is the first bottleneck in any game backend at launch; operators need a BCrypt cost factor table vs latency vs CPU to tune for their hardware | MEDIUM | `GameKit.Auth` | Library provides BenchmarkDotNet micro-benchmark; operator runs on their hardware to tune |
| Matchmaking ticker throughput benchmark: max queue depth before ticker lag exceeds configured interval; messages/sec at target replica count | Operators sizing Redis + replica count for their player base need a number to start from | MEDIUM | `GameKit.Matchmaking` | Library provides benchmark; operator adapts |
| Lobby SignalR fan-out load test: N connected clients, 1 message broadcast, measure delivery time distribution | SignalR fan-out is the dominant scale concern for Lobby; 1,000 concurrent lobby members is a realistic target for a busy game | HIGH | `GameKit.Lobby` | Library provides NBomber scenario; consumer runs against their deployed stack |
| Documented performance baselines committed as part of the test suite: `benchmarks/BASELINES.md` listing the machine spec + .NET version + result for each benchmark | Baselines without machine context are meaningless; committed baselines with spec allow future contributors to detect regressions | LOW (docs) | `benchmarks/BASELINES.md` | Library |
| BenchmarkDotNet micro-benchmarks for hot paths: JWT validation, BCrypt verify, Glicko-2 rating calculation, matchmaking ticket Redis round-trip | Micro-benchmarks catch regressions introduced by refactoring without requiring a full load test run | MEDIUM | `benchmarks/` project | Library |

#### Differentiators

| Feature | Value Proposition | Complexity | Package(s) Touched | Library or Consumer |
|---------|-------------------|------------|-------------------|---------------------|
| CI benchmark gate: if any benchmark regresses >20% from the committed baseline, CI fails | Prevents performance regressions from being merged silently; this is rare in OSS game backends and immediately signals quality discipline | MEDIUM | CI / `benchmarks/` | Library |
| Tuning guide document: BCrypt cost factor vs latency table, Redis connection pool sizing, EF Core query plan analysis for the top-5 hot queries (matchmaking ticket lookup, player rank fetch, lobby member list) | Operators cannot tune what they cannot measure; a concrete tuning guide with GameKit-specific query patterns saves hours of guesswork | MEDIUM (docs) | `docs/performance-tuning.md` | Library documents; consumer configures |
| NBomber scenario for matchmaking: simulate 500 players queuing simultaneously, assert p99 match time < 5 seconds at default config | End-to-end load scenario that validates the ticker keeps up under realistic game-launch burst conditions | HIGH | `tests/GameKit.LoadTests/` (NBomber) | Library; consumer runs against their deployed stack |

#### Anti-Features

| Feature | Why Problematic | What to Do Instead |
|---------|-----------------|-------------------|
| Shipping load tests that run in CI against a production environment | Load tests against production cause real impact; they belong in a dedicated load-test environment | Load test scenarios are committed but run manually or in a dedicated CI stage; never part of the main test run |
| Benchmarks that assume specific hardware (e.g. "this benchmark requires an M3 Mac") | Hardware-specific benchmarks are not reproducible in CI or on operator hardware | Document hardware spec for committed baselines; benchmarks are portable BenchmarkDotNet jobs |
| Auto-tuning BCrypt cost factor based on benchmarks at startup | Startup-time tuning delays service availability and makes the cost factor non-deterministic between deploys | Provide the tuning table; let the operator set the cost factor as a config value before deploy |
| Performance tests that depend on network calls to external services | External network calls introduce non-determinism; benchmarks must be reproducible | All benchmarks run against Testcontainers (local Postgres + Redis) or in-process test doubles |

**Operator experience:** `dotnet run --project benchmarks/ --configuration Release` — produces a Markdown table of hot-path timings. Consult `docs/performance-tuning.md` to translate benchmark results to config values. Run NBomber load scenario against staging: confirm p99 match time is within baseline before opening registration.

**Testability:** BenchmarkDotNet jobs are hermetic; CI comparison script reads `BASELINES.md` and fails if any measurement regresses >20%. NBomber scenarios are committed and runnable with `dotnet test` against a local Testcontainers stack.

---

### Area 7: Docs & Tutorial

#### Table Stakes (Operators Expect These)

| Feature | Why Expected | Complexity | Package(s) Touched | Library or Consumer |
|---------|--------------|------------|-------------------|---------------------|
| Per-package API reference generated from XML doc comments (DocFX or equivalent), self-hosted as static HTML | Every serious .NET library ships API docs; GameKit enforces XML comments as a CS1591 error — the docs site is the payoff for that discipline | MEDIUM | All packages; `docs/` site | Library generates; consumer self-hosts (or GitHub Pages) |
| Getting-started tutorial: "from `dotnet new gamekit` to first authenticated player in 15 minutes" walkthrough, runnable against the sample app | The most common reason developers abandon a library is that the first-run experience is painful; a working tutorial is the library's front door | MEDIUM | `docs/tutorial/getting-started.md` + `samples/TicTacToeDuel` | Library provides; sample app is the verification target |
| Upgrade / compatibility guide for v2.1 from v2.0 (breaking changes, migration order changes, config additions) | Operators upgrading from v2.0 need to know what changed; without a guide they will hit breaking changes silently | LOW | `docs/upgrade-v2.1.md` | Library provides |
| Concepts documentation per package: what the package does, what interfaces it exposes, what the operator configures, what the library handles vs. what the consumer handles | API reference without concepts docs forces developers to read source code to understand intent | MEDIUM | `docs/concepts/` | Library provides |
| Sample app (`TicTacToeDuel`) functioning as a self-contained quickstart: `docker-compose up` starts Postgres + Redis + the app + the observability stack + seed data | The sample app is the integration test and the tutorial simultaneously; it must be kept current with every feature | MEDIUM | `samples/TicTacToeDuel` | Library maintains |

#### Differentiators

| Feature | Value Proposition | Complexity | Package(s) Touched | Library or Consumer |
|---------|-------------------|------------|-------------------|---------------------|
| Docs site generated and published to GitHub Pages on every release tag via CI | Zero manual publishing step; the docs are always current with the latest release; demonstrates release discipline | LOW (CI) | `docs/` + CI | Library |
| Runbook library (`docs/runbooks/`): one runbook per operational concern (backup/restore, rolling deploy, migration apply, incident response for matchmaking outage) | Self-hosters have no operations team; a library of runbooks is a force multiplier; rare in OSS game backends | MEDIUM | `docs/runbooks/` | Library |
| Architecture decision records (ADRs) for key v1 + v2 decisions (Glicko-2 choice, BackgroundService over Hangfire, MinVer, etc.) | ADRs let contributors understand why a decision was made before trying to change it; they signal that the project is maintained with intentionality | LOW (docs) | `docs/adr/` | Library |
| Interactive API explorer link from the docs site to the sample app's Swagger UI | Developers can try API calls without setting up a local environment; lowers the barrier to evaluation | LOW | `docs/` | Library (docs site link) |

#### Anti-Features

| Feature | Why Problematic | What to Do Instead |
|---------|-----------------|-------------------|
| Hosted documentation that requires an account or login | Operators who want to audit the library's docs before adopting it must be able to do so without creating an account | Static HTML on GitHub Pages; no auth wall |
| Auto-generated API docs without conceptual documentation | Raw XML comment output alone is not useful; it answers "what does this method do" but not "why does this interface exist" | DocFX supports both API reference and Markdown concept pages; use both |
| Tutorial that requires cloud credentials or a SaaS account | Violates the self-hosted promise; any developer who follows the tutorial must be able to complete it with only Docker + .NET SDK | Tutorial is completable with `docker-compose up` on a local machine; zero cloud credentials required |
| Versioned docs for every minor release | Maintaining N versions of docs is significant overhead; for a library at v2.x the current-release docs are sufficient | Single-version docs for the latest release; upgrade guides bridge the gap for operators on older versions |
| Changelog generated from AI summarization | The changelog is a trust signal; it must be human-curated and match the actual commit history | Conventional commits + human-reviewed CHANGELOG.md per release |

**Operator experience:** Navigate to GitHub Pages docs site. Follow "Getting Started" — runs in 15 minutes with only Docker + `dotnet new gamekit`. Consult the runbook when preparing a production deploy. Upgrade from v2.0: read `docs/upgrade-v2.1.md`, apply listed config changes, run `gamekit migrations apply`. The API reference is searchable and cross-linked with the concept docs.

**Testability:** Docs build CI step: `docfx build --warningsAsErrors` ensures no broken cross-references. Tutorial CI: the sample app's `docker-compose up` + smoke test (register a player, complete a match) passes on every commit. Link checker on the generated site.

---

## Feature Dependencies

```
Area 1 (Observability)
    └──extends──> GameKit.Core (existing ActivitySource/Meter abstractions)
    └──extends──> GameKit.Matchmaking (ticker spans, queue depth metrics)
    └──extends──> GameKit.Lobby (SignalR trace propagation)
    └──enables──> Area 2 health metrics export
    └──new package──> GameKit.Observability (thin convenience wrapper)

Area 2 (Health & Readiness)
    └──extends──> GameKit.Core (AddGameKitHealthChecks)
    └──extends──> GameKit.Admin.UI (existing health panel data feed)
    └──requires──> Area 3 (leader lock health probe needs stable leader election)
    └──per-package migration probes──> all packages

Area 3 (Horizontal-Scale Hardening)
    └──extends──> GameKit.Matchmaking (leader election + graceful drain)
    └──extends──> GameKit.Rankings (decay job graceful drain)
    └──extends──> GameKit.Lobby (SignalR multi-replica correctness)
    └──extracts──> GameKit.Core BackgroundServiceBase (shared leader election base)

Area 4 (Backup / DR / Migration Ops)
    └──extends──> GameKit.Cli (new migrate commands)
    └──touches all packages' migration histories

Area 5 (Security Audit)
    └──verifies──> GameKit.Auth (JWT, refresh token, rate limit)
    └──verifies──> GameKit.Admin.UI (cookie auth, CSRF, route guards)
    └──verifies──> GameKit.Core (GDPR delete completeness, egress)
    └──verifies──> GameKit.Lobby (new FK tables covered by GDPR delete)

Area 6 (Load / Performance Testing)
    └──exercises──> GameKit.Auth (auth throughput)
    └──exercises──> GameKit.Matchmaking (ticker throughput)
    └──exercises──> GameKit.Lobby (SignalR fan-out)
    └──depends on──> Area 1 (OTel metrics for benchmark correlation)

Area 7 (Docs & Tutorial)
    └──requires──> All packages have XML doc coverage (already enforced)
    └──requires──> Sample app current with all v2.1 features
    └──references──> Area 4 runbooks, Area 5 security checklist, Area 6 tuning guide
```

### Dependency Notes

- **Area 1 (Observability) is the foundation for Areas 2, 3, and 6.** Health metrics are most useful when exportable via OTel; horizontal-scale hardening tests become more interpretable with trace context; load tests are validated with OTel signals. Observability should be the first area delivered.

- **Area 3 (Horizontal-scale hardening) must precede the `BackgroundServiceBase` extraction.** Both the matchmaking ticker and the decay job will be refactored to use the shared base; stability of that base must be proven before Area 6 load tests run against it.

- **Area 4 (DR) and Area 5 (Security) are largely independent.** They can be developed in parallel. Both produce documentation + automated tests; neither requires changes to existing game-services logic.

- **Area 7 (Docs) is last but continuous.** The docs site scaffolding can be set up early; the content is populated as the other areas land.

- **GameKit.Observability is a new package.** It is a thin convenience wrapper — a single `AddGameKitObservability()` extension method that wires all existing `ActivitySource`/`Meter` registrations and optionally configures an OTLP exporter. It has zero runtime behavior of its own; it depends on all game-services packages. This means operators who want zero-friction observability install `GameKit.Observability`; operators who want manual control wire each `ActivitySource`/`Meter` individually in their own OTel SDK setup.

---

## Phase Recommendations

### Phase 13 — Observability + Health (Areas 1 + 2)
- OTel `ActivitySource` + `Meter` across all packages
- `GameKit.Observability` convenience package
- Sample docker-compose observability stack (Prometheus + Tempo + Grafana)
- `AddGameKitHealthChecks()` with liveness/readiness/migration probes
- Admin UI health panel feed
- Rationale: unlocks metrics for load testing (Area 6) and health probes for hardening (Area 3)

### Phase 14 — Horizontal-Scale Hardening (Area 3)
- `BackgroundServiceBase` extract
- Leader election fuzz test
- Graceful drain integration test
- Idempotency guards for match creation
- SignalR multi-replica correctness tests
- Rationale: must be proven correct before load tests exercise it

### Phase 15 — Backup/DR + Security Audit (Areas 4 + 5, parallel)
- Postgres + Redis backup runbooks
- DR round-trip test
- `gamekit migrations` CLI commands
- JWT threat model tests
- GDPR delete completeness audit
- Egress audit
- CVE scan CI gate
- Security checklist doc
- Rationale: independent of each other; both produce docs + tests

### Phase 16 — Load Testing + Performance (Area 6)
- BenchmarkDotNet hot-path benchmarks
- NBomber matchmaking + SignalR load scenarios
- Committed baselines + CI regression gate
- Performance tuning guide
- Rationale: requires Observability (Area 1) and stable Hardening (Area 3) to be useful

### Phase 17 — Docs & Tutorial (Area 7)
- DocFX site generation + GitHub Pages CI
- Getting-started tutorial
- Upgrade guide (v2.0 → v2.1)
- Concepts docs per package
- Runbook library
- Rationale: content from all prior areas must be stable before docs are finalized

---

## Feature Prioritization Matrix

| Feature | Operator Value | Implementation Cost | Priority |
|---------|---------------|---------------------|----------|
| ActivitySource + Meter across all packages | HIGH | MEDIUM | P1 |
| Health /live + /ready + migration probes | HIGH | LOW | P1 |
| Self-hosted observability docker-compose sample | HIGH | MEDIUM | P1 |
| Graceful drain + leader election fuzz test | HIGH | HIGH | P1 |
| JWT + GDPR security audit tests | HIGH | MEDIUM | P1 |
| CVE scan CI gate | HIGH | LOW | P1 |
| Postgres + Redis backup runbooks | HIGH | LOW | P2 |
| GameKit.Observability convenience package | MEDIUM | LOW | P2 |
| BackgroundServiceBase extract | MEDIUM | MEDIUM | P2 |
| gamekit migrations CLI commands | MEDIUM | MEDIUM | P2 |
| DR round-trip test | MEDIUM | MEDIUM | P2 |
| BenchmarkDotNet hot-path benchmarks | MEDIUM | MEDIUM | P2 |
| NBomber matchmaking load scenario | MEDIUM | HIGH | P2 |
| DocFX site + GitHub Pages CI | HIGH | MEDIUM | P2 |
| Getting-started tutorial | HIGH | MEDIUM | P2 |
| Performance tuning guide | MEDIUM | MEDIUM | P3 |
| Architecture decision records | LOW | LOW | P3 |
| NBomber SignalR fan-out load scenario | MEDIUM | HIGH | P3 |
| Admin health panel detailed structured output | LOW | LOW | P3 |

---

## Sources

- [MS Learn: Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0) — HIGH
- [MS Learn: .NET Observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) — HIGH
- [MS Learn: Example: OTel with Prometheus, Grafana, and Jaeger](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-prgrja-example) — HIGH
- [OpenTelemetry: .NET Instrumentation Guide](https://opentelemetry.io/docs/languages/dotnet/instrumentation/) — HIGH
- [Grafana: intro-to-mltp (reference MLTP docker-compose)](https://github.com/grafana/intro-to-mltp) — HIGH
- [BenchmarkDotNet](https://benchmarkdotnet.org/) — HIGH
- [NBomber: Distributed load testing framework for .NET](https://nbomber.com/) — HIGH
- [MS Learn: EF Core applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) — HIGH
- [MS Learn: EF Core CLI tools reference](https://learn.microsoft.com/en-us/ef/core/cli/dotnet) — HIGH
- [Antoine Bernard: Generate EF Core rollback migration script](https://www.antoinebernard.com/how-to-generate-a-ef-core-migration-script-to-rollback-database/) — MEDIUM
- [Redis Persistence docs (official)](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/) — HIGH
- [pgBackRest](https://pgbackrest.org/) — HIGH
- [Severalnines: pgBackRest vs Barman](https://severalnines.com/blog/automating-backups-and-disaster-recovery-in-postgresql-at-scale-pgbackrest-vs-barman/) — MEDIUM
- [OWASP Dependency-Check](https://owasp.org/www-project-dependency-check/) — HIGH
- [OWASP Top 10 2025: A03 Supply Chain Failures](https://owasp.org/Top10/2025/A03_2025-Software_Supply_Chain_Failures/) — HIGH
- [DocFX: Static site generator for .NET API docs](https://dotnet.github.io/docfx/) — HIGH
- [GitHub: dotnet/docfx](https://github.com/dotnet/docfx) — HIGH
- [ABP.IO: Distributed locking in ASP.NET Core](https://abp.io/community/articles/why-do-you-need-distributed-locking-in-asp.net-core-fx1895hh) — MEDIUM
- [Mark Vincze: Graceful termination in Kubernetes with ASP.NET Core](https://blog.markvincze.com/graceful-termination-in-kubernetes-with-asp-net-core/) — MEDIUM
- [MS Learn: Redis backplane for ASP.NET Core SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0) — HIGH
- [codewithmukesh: Running Migrations in EF Core 10](https://codewithmukesh.com/blog/running-migrations-efcore/) — MEDIUM

---

*Feature research for: GameKit v2.1 — Operability & Hardening*
*Researched: 2026-06-08*
