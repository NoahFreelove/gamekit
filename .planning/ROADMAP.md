# Roadmap: GameKit

## Milestones

- ✅ **v1.0 — Initial 6-Phase Build-Out** — Phases 1–6 (shipped 2026-05-30) — [archive](milestones/v1.0-ROADMAP.md)
- ✅ **v2.0 — Expansion: Providers, Lobby & Rating-Aware Play** — Phases 7–12 (shipped 2026-06-07) — [archive](milestones/v2.0-ROADMAP.md)
- 🔄 **v2.1 — Operability & Hardening** — Phases 13–21 (in progress)

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

### v2.1 — Operability & Hardening (Phases 13–21)

- [x] **Phase 13: Observability Foundations** — PII lint gate + Core OTel conventions + `AddGameKitObservability()` + sample observability stack (completed 2026-06-14)
- [x] **Phase 14: Health & Readiness** — `MapGameKitHealth()` + `IMigrationReadinessReporter` across all packages + three-probe model + Admin.UI delegation (completed 2026-06-15)
- [x] **Phase 15: Per-Package OTel Instrumentation** — Activity spans + RED metrics + W3C trace propagation wired into every package (completed 2026-06-22)
- [x] **Phase 16: Multi-Replica Hardening** — `ILeaderLease` abstraction + SIGTERM drain + idempotency + split-brain CI gate (completed 2026-06-22)
- [x] **Phase 17: Backup / DR + Migration Ops** — CLI commands + Postgres/Redis runbooks + DR round-trip CI test + migration-ops docs (completed 2026-06-23)
- [x] **Phase 18: Security Audit** — JWT/admin/GDPR/egress/rate-limit audit tests + CVE CI gate + security checklist (completed 2026-06-23)
- [ ] **Phase 19: Load / Performance Testing** — BenchmarkDotNet micro-benchmarks + k6 load scenarios + CI regression gate + tuning guide
- [ ] **Phase 20: Docs & Tutorial** — DocFX API reference + getting-started tutorial + upgrade guide + runbook library
- [ ] **Phase 21: Final Demo — 3D Multiplayer Platformer** — single loadable image (admin server + GameKit + fully-customized example) you `docker load`/run, then play a 3D multiplayer game in the web browser

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

**Plans**: 6/6 plans complete
**Wave 1**

- [x] 15-01-PLAN.md — Core telemetry constants (Lobby/Rankings source+meter names, check.result) + reflection-test extension + Wave-0 PII/W3C test scaffolds

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 15-02-PLAN.md — Matchmaking OBS-04 metrics (ticker lag, queue-depth gauge, lease/lock/matches/budget counters) + Matchmaking PII test
- [x] 15-04-PLAN.md — Rankings RankingsMeter + decay-job duration/rows + RankDecay span + Rankings PII test
- [x] 15-05-PLAN.md — Lobby greenfield Telemetry/ (LobbyMeter, LobbyActivitySource, connection tracker) + SignalR metrics + ready-check span + Lobby PII test

**Wave 3** *(blocked on Wave 2 completion)*

- [x] 15-03-PLAN.md — Matchmaking OBS-06 W3C trace propagation across the Redis fan-in (enqueue write → ticker restore → MatchFormation span + fan-in links) + W3C tests

**Wave 4** *(blocked on Wave 3 completion)*

- [x] 15-06-PLAN.md — AddGameKitObservability registration + collector namespace + matchmaking dashboard PromQL corrections + full-suite gate

**UI hint**: no

### Phase 16: Multi-Replica Hardening

**Goal**: Multi-replica deployments are proven correct under leader churn, SIGTERM, and concurrent request storms — duplicate matches are impossible, graceful drain is zero-downtime, and a CI gate enforces these invariants before load tests run
**Depends on**: Phase 14, Phase 15
**Requirements**: SCALE-01, SCALE-02, SCALE-03, SCALE-04, SCALE-05, SCALE-06
**Success Criteria** (what must be TRUE):

  1. `ILeaderLease` in `GameKit.Core` is the single interface all three lease helpers implement; a grep of `src/` shows no `LockTakeAsync` call outside a class that implements `ILeaderLease`
  2. A two-replica Testcontainers integration test (`MatchmakerSplitBrainTests`) simulates lease expiry mid-tick and asserts zero duplicate rows in `game_sessions` and no ticker gap longer than one lock TTL — this test is a required CI gate
  3. A graceful-drain integration test sends 100 concurrent in-flight requests, triggers SIGTERM, and asserts zero 5xx responses and zero duplicate matches; `ReleaseLeaseAsync` is verified to use `CancellationToken.None` (not the stopping token) on all finally paths
  4. Concurrent match-formation writes (`ProposalService.CreateSessionAsync`) for the same proposal/idempotency key produce exactly one `game_sessions` row (`INSERT … ON CONFLICT DO NOTHING` proven by a dedicated Testcontainers test). (`SessionCompleteAsync` is already idempotent via its own `SessionCompleteIdempotency` table; the split-brain risk is the formation write, which research confirmed is the correct target.)
  5. A SignalR multi-replica integration test with real Testcontainers Redis backplane confirms all connected lobby **and admin** clients receive hub events regardless of which replica sends them under replica restart and Redis reconnect (covers both the Lobby hub and the Admin hub, per SCALE-06 "Lobby + Admin")

**Plans**: 6 plans

- [ ] 16-01-PLAN.md — Extract `ILeaderLease` + `LeaseStatus` into `GameKit.Core`; adapt all four lease helpers (SCALE-01) [Wave 1]
- [ ] 16-02-PLAN.md — Core migration + `game_sessions.IdempotencyKey` + idempotent match-formation write (SCALE-03 impl) [Wave 1]
- [ ] 16-03-PLAN.md — Fix 5 finally-path lease releases to `CancellationToken.None` + static grep gate (SCALE-02) [Wave 1]
- [ ] 16-04-PLAN.md — Extend `MatchmakingTestApp` + `MatchmakerSplitBrainTests` split-brain CI gate + idempotency proof (SCALE-04, SCALE-03) [Wave 2]
- [ ] 16-05-PLAN.md — `GracefulDrainTests` — 100 concurrent requests + SIGTERM → zero 5xx, lock released (SCALE-05) [Wave 3]
- [ ] 16-06-PLAN.md — Lobby `SignalRReplicaTests` + Admin `AdminSignalRReplicaTests` multi-replica restart/reconnect + sticky-session operator doc covering both hubs (SCALE-06: Lobby + Admin) [Wave 1]

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

**Plans**: 6 plans

- [ ] 17-01-PLAN.md — Convert all 14 migration Down() bodies to throw NotSupportedException + add 5 ordering-marker migrations (DR-04, DR-05, DR-07)
- [ ] 17-02-PLAN.md — GK0003 Down()-policy analyzer + analyzer tests + MigrationTimestampTests (DR-04, DR-05, DR-07)
- [ ] 17-03-PLAN.md — `gamekit migrations list` + `apply --dry-run` CLI + per-package context factory + CLI tests (DR-04, DR-05)
- [ ] 17-04-PLAN.md — `gamekit db backup` / `db restore` CLI (pg_dump/pg_restore wrappers + Redis BGSAVE) + path-traversal guard (DR-06)
- [ ] 17-05-PLAN.md — DR round-trip Testcontainers test: dump → destroy → restore → /health/ready 200 (DR-03)
- [ ] 17-06-PLAN.md — Postgres + Redis backup/restore runbooks + migration-ops docs + RunbookFilesTests (DR-01, DR-02, DR-07)

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

**Plans**: 6 plans
Plans:

- [ ] 18-01-PLAN.md — SEC-07: CVE CI gate (NuGetAuditMode=all) + MessagePack 3.1.7 transitive pin; full solution builds clean without suppression [Wave 1]
- [ ] 18-02-PLAN.md — SEC-04: GDPR delete completeness — IGdprDeleteExtension fixes party_members + account_merges RESTRICT FKs + GdprDeleteCoverageTests [Wave 2]
- [ ] 18-03-PLAN.md — SEC-01: JWT threat tests (alg:none / downgrade / wrong aud-iss / expired) + revoked-refresh-exchange test [Wave 2]
- [ ] 18-04-PLAN.md — SEC-02/03: admin route-enumeration auth audit + auth rate-limit enumeration audit [Wave 2]
- [ ] 18-05-PLAN.md — SEC-05/06: Apple/Google egress wiring fix + egress/refresh-hash/CSRF tests + static air-gap CI gate [Wave 2]
- [ ] 18-06-PLAN.md — SEC-08: docs/security-checklist.md threat→implementation→test traceability [Wave 3]

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

**Plans**: 1/5 plans executed

- [x] 19-01-PLAN.md — BenchmarkDotNet project + 5 hot-path micro-benchmarks (PERF-01)
- [ ] 19-02-PLAN.md — k6 SignalR spike + reusable helper + README + GO/NO-GO checkpoint (PERF-04a)
- [ ] 19-03-PLAN.md — CompareBaseline regression-gate tool + proving self-test + push-to-main CI job (PERF-06)
- [ ] 19-04-PLAN.md — Capture + commit baseline JSON and BASELINES.md (PERF-02)
- [ ] 19-05-PLAN.md — k6 matchmaking-burst + auth throughput, SignalR fan-out, performance-tuning.md (PERF-03, PERF-04b, PERF-05)

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

### Phase 21: Final Demo — 3D Multiplayer Platformer

**Goal**: A single, loadable container image showcases GameKit end-to-end — someone runs the image and immediately plays a 3D multiplayer game in their web browser. The image is the GameKit-enabled admin server bundled with a *fully customized* example GameKit integration (not the bare sample), proving the library's composability and self-host story in one artifact.
**Depends on**: Phase 20 (final v2.1 capstone — runs against the complete, audited, documented package surface)
**Requirements**: R1–R11 (locked in 21-SPEC.md, ambiguity 0.14)
**Success Criteria** (what must be TRUE):

  1. **Single loadable image**: a user can obtain one image (`docker load` from a published tarball, or `docker run`/`docker compose up` against a single published image) and reach a playable game with zero manual config beyond starting the container — Postgres + Redis either embedded or brought up by the same compose file
  2. **Play in the browser**: the 3D multiplayer client is served by the image and runs in a stock web browser (no native client install, no engine download) — points the engine choice toward WebGL/three.js or a WebGL export rather than a native Godot/Unity binary
  3. **Admin server IS the image**: the running container is the GameKit admin server (Blazor admin console + GameKit packages) — an operator can open the admin UI and see the live players, matches, and sessions created by people playing the browser demo
  4. **Fully customized GameKit example**: the bundled integration is a non-trivial, customized use of GameKit (custom matchmaking strategy / ranking config / lobby flow wired into the game), demonstrating the "every algorithm is a replaceable interface" value — not the unmodified TicTacToeDuel sample
  5. **Real server↔GameKit auth**: a real game server (not a stub) authenticates to GameKit with the correct service-to-service primitive (dedicated server credential / JWT scope / mTLS — confirmed during spec) and drives matchmaking via the GameKit HTTP API
  6. **One-command demo**: the full stack (browser client + game server + GameKit backend + Postgres + Redis) comes up from a single command and is reproducible offline with zero cloud credentials, honoring the self-hosted/GPL constraint

**Notes**:

  - **Promoted from Backlog Phase 999.1** (captured 2026-06-14). The browser-playable + single-loadable-image framing is the new, narrowing constraint added when promoting: the original sketch allowed any native engine and a multi-file compose; this phase requires browser play and a load-and-go image built around the admin server.
  - GPL compatibility of any bundled engine/runtime bits must be checked before vendoring (per project license constraint).
  - Likely the v2.1 capstone; could alternatively be split into its own demo milestone if scope (real 3D game + game server + secure auth + image packaging) proves too large for one phase — decide at /gsd-spec-phase / /gsd-discuss-phase 21.

**Plans**: 6 plans

Plans:
**Wave 1**

- [ ] 21-01-PLAN.md — Foundation: two sample project shells + two test projects + Testcontainers fixture + reuse CLI + sln wiring (R1)

**Wave 2** *(blocked on Wave 1 completion)*

- [ ] 21-02-PLAN.md — Custom IMatchmakingStrategy (best-time) + custom IRankingAlgorithm (fixed-delta, D-09 amended) + unit tests (R5, R6)
- [ ] 21-03-PLAN.md — three.js browser client (vendored, no CDN) + guest onboarding + level + REUSE/notices (R2, R8, R11)

**Wave 3** *(blocked on Wave 2 completion)*

- [ ] 21-04-PLAN.md — Host composition (custom seams resolved, admin, WebSocket) + embedded GameServer IHostedService + run-summary validation (R4, R5, R6, R7, R8, R9)

**Wave 4** *(blocked on Wave 3 completion)*

- [ ] 21-05-PLAN.md — Multi-stage Dockerfile + single compose (only app port) + offline docker save tarball (R3, R11)

**Wave 5** *(blocked on Wave 4 completion)*

- [ ] 21-06-PLAN.md — Integration/smoke: resolution, guest, player-JWT-401/403, idempotency, lobby→1v1+abort, full-loop+concurrent, compose-port, human-verify (R5, R7, R8, R9, R10)

**UI hint**: yes

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
| 15. Per-Package OTel Instrumentation | v2.1 | 6/6 | Complete    | 2026-06-22 |
| 16. Multi-Replica Hardening | v2.1 | 6/6 | Complete | 2026-06-22 |
| 17. Backup / DR + Migration Ops | v2.1 | 6/6 | Complete | 2026-06-23 |
| 18. Security Audit | v2.1 | 6/6 | Complete | 2026-06-23 |
| 19. Load / Performance Testing | v2.1 | 1/5 | In Progress|  |
| 20. Docs & Tutorial | v2.1 | 0/TBD | Not started | — |
| 21. Final Demo — 3D Multiplayer Platformer | v2.1 | 0/TBD | Not started | — |

---

## Backlog

Candidate work not yet promoted into an active phase. Promote via `/gsd:review-backlog`.

### Phase 999.1: Final Demo — 3D Multiplayer Platformer (✅ PROMOTED → Phase 21 on 2026-06-22)

> **Promoted to active roadmap as Phase 21** (v2.1 capstone) on 2026-06-22. See the Phase 21 detail entry above. Kept here for provenance.

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
