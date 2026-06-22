# Roadmap: GameKit

## Milestones

- ✅ **v1.0 — Initial 6-Phase Build-Out** — Phases 1–6 (shipped 2026-05-30) — [archive](milestones/v1.0-ROADMAP.md)
- ✅ **v2.0 — Expansion: Providers, Lobby & Rating-Aware Play** — Phases 7–12 (shipped 2026-06-07) — [archive](milestones/v2.0-ROADMAP.md)
- 🔄 **v2.1 — Operability & Hardening** — Phases 13–20 (in progress)

## Phases

<details>
<summary>✅ v1.0 — Initial 6-Phase Build-Out (Phases 1–6) — SHIPPED 2026-05-30</summary>

7 composable GPL NuGet packages (Core, Auth, Rankings, Matchmaking, Presence, Admin.UI, OpenApi) + CLI + template. 92/92 requirements. Full detail: [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md).

</details>

<details>
<summary>✅ v2.0 — Expansion: Providers, Lobby & Rating-Aware Play (Phases 7–12) — SHIPPED 2026-06-07</summary>

- [x] Phase 7: Core Rating Seam + Stateless Auth Packages (6/6 plans) — 2026-06-05
- [x] Phase 8: Rankings Depth + Rating-Aware Matchmaking (4/4 plans) — 2026-06-06
- [x] Phase 9: Regional Matchmaking Pools + Backfill (4/4 plans) — 2026-06-06
- [x] Phase 10: Account Merge (Isolated High-Risk) (4/4 plans) — 2026-06-06
- [x] Phase 11: GameKit.Lobby (New Package) (4/4 plans) — 2026-06-07
- [x] Phase 12: Admin Multi-Replica + Distribution Close-Out (4/4 plans) — 2026-06-07

29/29 requirements. Full detail: [milestones/v2.0-ROADMAP.md](milestones/v2.0-ROADMAP.md). Audit: [milestones/v2.0-MILESTONE-AUDIT.md](milestones/v2.0-MILESTONE-AUDIT.md).

</details>

### v2.1 — Operability & Hardening (Phases 13–20)

- [x] **Phase 13: Observability Foundations** — PII lint gate + Core OTel conventions + `AddGameKitObservability()` + sample observability stack (completed 2026-06-14)
- [x] **Phase 14: Health & Readiness** — `MapGameKitHealth()` + `IMigrationReadinessReporter` across all packages + three-probe model + Admin.UI delegation (completed 2026-06-15)
- [ ] **Phase 15: Per-Package OTel Instrumentation** — Activity spans + RED metrics + W3C trace propagation wired into every package
- [ ] **Phase 16: Multi-Replica Hardening** — `ILeaderLease` abstraction + SIGTERM drain + idempotency + split-brain CI gate
- [ ] **Phase 17: Backup / DR + Migration Ops** — CLI commands + Postgres/Redis runbooks + DR round-trip CI test + migration-ops docs
- [ ] **Phase 18: Security Audit** — JWT/admin/GDPR/egress/rate-limit audit tests + CVE CI gate + security checklist
- [ ] **Phase 19: Load / Performance Testing** — BenchmarkDotNet micro-benchmarks + k6 load scenarios + CI regression gate + tuning guide
- [ ] **Phase 20: Docs & Tutorial** — DocFX API reference + getting-started tutorial + upgrade guide + runbook library

## Phase Details

### Phase 13: Observability Foundations

**Goal**: The codebase has a PII-safe observability foundation — naming conventions locked, `AddGameKitObservability()` wired in Core, the sample self-hosted stack running — before any per-package instrumentation is written
**Depends on**: Phase 12 (v2.0 shipped baseline)
**Requirements**: OBS-01, OBS-02, OBS-03, OBS-07, OBS-08
**Success Criteria** (what must be TRUE):

  1. A CI lint gate fails the build if any `SetTag` / `AddTag` call in `src/` references a parameter name containing `player`, `user`, `email`, `token`, `ip`, or `fingerprint`
  2. `AddGameKitObservability()` is callable on `IGameKitBuilder` in `GameKit.Core`; it wires `ActivitySource`/`Meter` registrations for all known GameKit sources without forcing OTel SDK on consumers who omit the call
  3. `docker compose -f docker-compose.yml -f docker-compose.observability.yml up` in `samples/TicTacToeDuel` starts OTel Collector + Prometheus + Grafana + Tempo; the Prometheus `/metrics` scrape target is on the internal Docker network only — `curl http://localhost:9090` from the host does NOT reach app metrics
  4. `GameKitTelemetry` constants are the single source of truth for source name prefix and span attribute key names; per-package `Telemetry/` classes reference these constants (no magic strings)
  5. `RankingsActivitySource` extracted from inline `_activitySource` in `RankingsTickerService` into a canonical `Telemetry/` class, matching the existing Matchmaking pattern

**Plans**: 4 plans
Plans:
**Wave 1**

- [x] 13-01-PLAN.md — PII Roslyn analyzer (GK0001/GK0002) + allow-list + analyzer test project (OBS-07, the gate)
- [x] 13-04-PLAN.md — Sample observability stack: Collector/Prometheus/Tempo/Grafana compose + provisioned dashboards (OBS-08)

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 13-02-PLAN.md — GameKitTelemetry constants + AddGameKitObservability() + OTel PrivateAssets refs (OBS-01/02/03)

**Wave 3** *(blocked on Wave 2 completion)*

- [x] 13-03-PLAN.md — Extract RankingsActivitySource + normalize Matchmaking camelCase tags to lowercase-dotted (OBS-02/03)

**UI hint**: no

### Phase 14: Health & Readiness

**Goal**: Every GameKit deployment exposes separate `/health/live` and `/health/ready` endpoints with correct K8s-probe semantics; liveness never fails on a Redis blip; readiness gates on migrations + Postgres; Admin.UI delegates to Core probes
**Depends on**: Phase 13
**Requirements**: HLTH-01, HLTH-02, HLTH-03, HLTH-04, HLTH-05, HLTH-06
**Success Criteria** (what must be TRUE):

  1. `GET /health/live` returns 200 even when the Postgres container is stopped; `GET /health/ready` returns 503 while any package's migrations are pending, then flips to 200 once all six `IMigrationReadinessReporter` implementations report ready
  2. `GET /health/ready` returns 503 on a Core-only install (no Redis) if Postgres is unreachable, and returns 200 once Postgres is reachable — Redis absence does not prevent the pod from becoming ready
  3. The matchmaking ticker not holding the leader lock is reported as `Degraded` (not `Unhealthy`) on `/health/ready`; the probe identifies which replica holds the lock and the TTL remaining
  4. Health check JSON responses contain component name, status, and human-readable description only — no connection strings, hostnames, or credentials appear in any response body
  5. The Admin.UI health panel displays structured check results sourced from `HealthCheckService`; `HealthProbeService` no longer duplicates the Postgres + Redis probe logic

**Plans**: 5 plans
Plans:

**Wave 1**

- [x] 14-01-PLAN.md — Core health foundation: IMigrationReadinessReporter, Postgres/Redis/migration-aggregate checks, whitelist ResponseWriter, AddGameKitHealthChecks() + MapGameKitHealth() (HLTH-01/02/05)

**Wave 2** *(blocked on Wave 1)*

- [x] 14-02-PLAN.md — Auth + Rankings + Lobby migration readiness reporters (HLTH-02)
- [x] 14-03-PLAN.md — Matchmaking: IMatchmakerLease.QueryLeaseAsync + leader health check (Degraded-only) + sixth reporter + self-registration (HLTH-02/03/04)
- [x] 14-04-PLAN.md — Admin.UI HealthProbeService delegation refactor + Admin migration reporter + delegation unit test (HLTH-02/06)

**Wave 3** *(blocked on Wave 2)*

- [x] 14-05-PLAN.md — Integration tests (live/ready, leak, leader) + TicTacToeDuel wiring (HLTH-01/02/03/04/05)

**UI hint**: yes

### Phase 15: Per-Package OTel Instrumentation

**Goal**: Every HTTP handler path and background job in every GameKit package emits correctly-named, low-cardinality spans and RED metrics; W3C trace context flows from the enqueue HTTP request through the async ticker to match formation
**Depends on**: Phase 13, Phase 14
**Requirements**: OBS-04, OBS-05, OBS-06
**Success Criteria** (what must be TRUE):

  1. A `MeterListener` tag-key assertion test in each package passes: no instrument emits a tag whose key is `ticketId`, `playerId`, `sessionId`, `matchId`, or any player-identifying string
  2. A trace exported to Tempo for a matchmaking enqueue request shows the match-formation span as a descendant of the original enqueue span (W3C `traceparent` stored in the Redis ticket hash and restored in the ticker); the full lifecycle is visible as a single causal trace
  3. Lobby SignalR metrics — connected clients, messages/sec, ready-check completion rate — appear in Grafana under the `gamekit.lobby.*` namespace; background-job metrics (ticker lag, queue depth per pool, decay job duration, leader-lock acquisition failures) appear under `gamekit.matchmaking.*` and `gamekit.rankings.*`
  4. Pre-built Grafana dashboard JSON for matchmaking queue depth + ticker health is importable from `samples/TicTacToeDuel/observability/dashboards/` and renders correct data against the sample stack

**Plans**: 6 plans
- [ ] 15-01-PLAN.md — Core telemetry constants (Lobby/Rankings source+meter names, check.result) + reflection-test extension + Wave-0 PII/W3C test scaffolds
- [ ] 15-02-PLAN.md — Matchmaking OBS-04 metrics (ticker lag, queue-depth gauge, lease/lock/matches/budget counters) + Matchmaking PII test
- [ ] 15-03-PLAN.md — Matchmaking OBS-06 W3C trace propagation across the Redis fan-in (enqueue write → ticker restore → MatchFormation span + fan-in links) + W3C tests
- [ ] 15-04-PLAN.md — Rankings RankingsMeter + decay-job duration/rows + RankDecay span + Rankings PII test
- [ ] 15-05-PLAN.md — Lobby greenfield Telemetry/ (LobbyMeter, LobbyActivitySource, connection tracker) + SignalR metrics + ready-check span + Lobby PII test
- [ ] 15-06-PLAN.md — AddGameKitObservability registration + collector namespace + matchmaking dashboard PromQL corrections + full-suite gate
**UI hint**: no

### Phase 16: Multi-Replica Hardening

**Goal**: Multi-replica deployments are proven correct under leader churn, SIGTERM, and concurrent request storms — duplicate matches are impossible, graceful drain is zero-downtime, and a CI gate enforces these invariants before load tests run
**Depends on**: Phase 14, Phase 15
**Requirements**: SCALE-01, SCALE-02, SCALE-03, SCALE-04, SCALE-05, SCALE-06
**Success Criteria** (what must be TRUE):

  1. `ILeaderLease` in `GameKit.Core` is the single interface all three lease helpers implement; a grep of `src/` shows no `LockTakeAsync` call outside a class that implements `ILeaderLease`
  2. A two-replica Testcontainers integration test (`MatchmakerSplitBrainTests`) simulates lease expiry mid-tick and asserts zero duplicate rows in `game_sessions` and no ticker gap longer than one lock TTL — this test is a required CI gate
  3. A graceful-drain integration test sends 100 concurrent in-flight requests, triggers SIGTERM, and asserts zero 5xx responses and zero duplicate matches; `ReleaseLeaseAsync` is verified to use `CancellationToken.None` (not the stopping token) on all finally paths
  4. Concurrent `SessionCompleteAsync` calls for the same idempotency key produce exactly one `game_sessions` row (`INSERT … ON CONFLICT DO NOTHING` proven by a dedicated Testcontainers test)
  5. A SignalR multi-replica integration test with real Testcontainers Redis backplane confirms all connected lobby clients receive hub events regardless of which replica sends them under replica restart and Redis reconnect

**Plans**: TBD
**UI hint**: no

### Phase 17: Backup / DR + Migration Ops

**Goal**: Operators have a verified, CI-proven backup-restore procedure for Postgres + Redis and unified CLI tooling for migration dry-run and status; the restore rehearsal is a committed CI artifact, not just documentation
**Depends on**: Phase 13 (stable baseline; DR is otherwise independent of observability/hardening)
**Requirements**: DR-01, DR-02, DR-03, DR-04, DR-05, DR-06, DR-07
**Success Criteria** (what must be TRUE):

  1. A CI Testcontainers job completes the full DR round-trip: `pg_dump` → container destroy → `pg_restore` → app starts → `GET /health/ready` returns 200; the job is a committed CI gate (not just a script to run manually)
  2. `gamekit migrations list` prints every installed package's pending-migration count and the correct recommended application order (Core → Auth → Admin → Rankings → Matchmaking → Lobby)
  3. `gamekit migrations apply --dry-run` prints idempotent SQL for all pending migrations across all installed packages without executing any DDL against the database
  4. A CI check asserts that every `Down()` method in every migration file contains only `throw new NotSupportedException(...)` — no DROP TABLE, DROP COLUMN, or destructive DDL
  5. A `MigrationTimestampTests` suite asserts that each package's latest migration timestamp is lexicographically greater than the previous package's latest timestamp, enforcing the per-package application ordering

**Plans**: TBD
**UI hint**: no

### Phase 18: Security Audit

**Goal**: Every auth/admin/GDPR/egress/rate-limit security invariant is verified by an automated test and a CI gate; known CVEs are impossible to merge undetected; the full threat model is traceable from requirement to implementation to test
**Depends on**: Phase 16, Phase 17 (audit runs against completed code)
**Requirements**: SEC-01, SEC-02, SEC-03, SEC-04, SEC-05, SEC-06, SEC-07, SEC-08
**Success Criteria** (what must be TRUE):

  1. A `dotnet list package --vulnerable --include-transitive` CI step (running on every push to main) fails the build on any HIGH or CRITICAL CVE in GameKit's own dependency graph; the step is the first task added in this phase
  2. Automated JWT threat-model tests reject: `alg:none` attacks, wrong audience/issuer, expired tokens, and exchange of a revoked refresh token
  3. A route-enumeration integration test asserts every `/admin/*` route requires the `GameKitAdmin` cookie scheme and returns 401/403 for a request authenticated only with a player JWT; a CSRF regression test asserts state-changing admin calls without an antiforgery token return 400
  4. A `GdprDeleteCoverage` integration test seeds a player across every table (including v2.0 additions: `lobby_members`, `party_members`, `matchmaking_tickets`, regional pool refs, account-merge tombstones) and asserts zero residual rows post-`DeletePlayerAsync`
  5. A static check + integration test assert no package makes outbound HTTP beyond the configured OAuth provider hosts; a grep CI check asserts no SaaS OTLP endpoint appears anywhere in `samples/` or `src/`

**Plans**: TBD
**UI hint**: no

### Phase 19: Load / Performance Testing

**Goal**: Repeatable, hardware-annotated benchmarks establish documented performance baselines for every hot path; a CI regression gate fails if any benchmark regresses more than 20%; k6 load scenarios validate multi-replica correctness under realistic Redis RTT
**Depends on**: Phase 16, Phase 18 (load tests run against final audited hardened codebase; Phase 16 split-brain gate is a prerequisite)
**Requirements**: PERF-01, PERF-02, PERF-03, PERF-04, PERF-05, PERF-06
**Success Criteria** (what must be TRUE):

  1. `tests/GameKit.LoadTests/` BenchmarkDotNet micro-benchmarks cover: JWT validation, BCrypt + Argon2id verify, Glicko-2 rating calculation, and the matchmaking-ticket Redis round-trip; results are committed to `benchmarks/BASELINES.md` with machine spec and .NET version
  2. A CI benchmark regression gate fails the build if any hot-path benchmark regresses more than 20% from the committed baseline
  3. A k6 matchmaking burst scenario (500 players queue simultaneously against a local Testcontainers stack) completes with a measured p99 match time; the scenario is committed and reproducible without external services or cloud credentials
  4. A k6 Lobby SignalR fan-out scenario exercises the real Redis backplane (N connected clients, one broadcast) and produces a delivery-time distribution; a spike confirms k6 WebSocket framing is sufficient before the scenario is committed
  5. `docs/performance-tuning.md` documents the BCrypt/Argon2 cost-factor vs latency table, Npgsql connection-pool sizing formula, and the top-5 hot-query tuning notes

**Plans**: TBD
**UI hint**: no

### Phase 20: Docs & Tutorial

**Goal**: A developer with only Docker and the .NET SDK can complete the getting-started tutorial in under 15 minutes and reach a working first match with traces visible in Grafana; the DocFX API reference CI gate ensures XML doc coverage never regresses
**Depends on**: Phase 15, Phase 17, Phase 18, Phase 19 (API surface and all runbooks stable)
**Requirements**: DOCS-01, DOCS-02, DOCS-03, DOCS-04, DOCS-05, DOCS-06
**Success Criteria** (what must be TRUE):

  1. `docfx build --warningsAsErrors` passes in CI on every commit to main, generating an API reference from XML doc comments for all `src/` packages with zero broken cross-references
  2. The getting-started tutorial (`dotnet new gamekit` → first authenticated player + first completed match) is completable with `docker-compose up` and zero cloud credentials; a CI smoke test executes the tutorial path and asserts health checks pass
  3. Per-package concepts documentation exists in `docs/concepts/` explaining what the package does, which interfaces it exposes, and the library-vs-operator responsibility line
  4. `docs/upgrade-v2.1.md` documents all v2.0 → v2.1 config additions (new health/observability wiring, migration-order changes if any) and has been tested against a real v2.0 sample install
  5. `docs/runbooks/` contains runbooks for backup/restore, rolling deploy, migration apply, and matchmaking-outage incident response; `docs/security-checklist.md` maps threat model → implementation → test for auth/admin/rate-limit/egress/GDPR

**Plans**: TBD
**UI hint**: no

## Progress

| Phase | Milestone | Plans | Status | Completed |
|-------|-----------|-------|--------|-----------|
| 1–6. (v1.0 build-out) | v1.0 | 60 | Complete | 2026-05-30 |
| 7. Core Rating Seam + Stateless Auth Packages | v2.0 | 6/6 | Complete | 2026-06-05 |
| 8. Rankings Depth + Rating-Aware Matchmaking | v2.0 | 4/4 | Complete | 2026-06-06 |
| 9. Regional Matchmaking Pools + Backfill | v2.0 | 4/4 | Complete | 2026-06-06 |
| 10. Account Merge | v2.0 | 4/4 | Complete | 2026-06-06 |
| 11. GameKit.Lobby | v2.0 | 4/4 | Complete | 2026-06-07 |
| 12. Admin Multi-Replica + Distribution Close-Out | v2.0 | 4/4 | Complete | 2026-06-07 |
| 13. Observability Foundations | v2.1 | 4/4 | Complete    | 2026-06-14 |
| 14. Health & Readiness | v2.1 | 5/5 | Complete    | 2026-06-15 |
| 15. Per-Package OTel Instrumentation | v2.1 | 0/TBD | Not started | — |
| 16. Multi-Replica Hardening | v2.1 | 0/TBD | Not started | — |
| 17. Backup / DR + Migration Ops | v2.1 | 0/TBD | Not started | — |
| 18. Security Audit | v2.1 | 0/TBD | Not started | — |
| 19. Load / Performance Testing | v2.1 | 0/TBD | Not started | — |
| 20. Docs & Tutorial | v2.1 | 0/TBD | Not started | — |

---

## Backlog

Candidate work not yet promoted into an active phase. Promote via `/gsd:review-backlog`.

### Phase 999.1: Final Demo — 3D Multiplayer Platformer (BACKLOG)

**Goal:** A small 3D multiplayer platformer that showcases GameKit end-to-end as a live demo — GameKit hosts matchmaking; a real, containerized game server establishes secure server↔GameKit communication; the whole thing runs with a simple `docker compose up` so it can be demoed easily.

**Captured:** 2026-06-14 (user request during Phase 13 discussion)
**Milestone:** TBD — likely a v2.1 capstone or its own demo milestone (substantial: real game + game-server + secure server-to-GameKit auth + container packaging).
**Sketch of scope:**

- [ ] Tiny 3D multiplayer platformer client — whichever engine is convenient (Godot / Unity / three.js — pick for demo speed, mind GPL compatibility of any bundled engine bits).
- [ ] Real game server (not a sample stub) that authenticates to GameKit and drives matchmaking via the GameKit HTTP API.
- [ ] Secure server↔GameKit communication (service-to-service auth — confirm the right primitive: dedicated server credential / JWT scope / mutual TLS).
- [ ] Containerized so the full demo (game server + GameKit backend + Postgres + Redis) comes up with one `docker compose up`.

**Why backlog, not Phase 13:** This is a milestone-level demo deliverable, well outside Phase 13's observability-foundations scope. Recorded so it isn't lost; sequence it during milestone planning.

---
*v1.0 + v2.0 shipped. v2.1 roadmap created 2026-06-08 — 47/47 requirements mapped across Phases 13–20.*
