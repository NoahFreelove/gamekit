---
phase: 13-observability-foundations
plan: "04"
subsystem: infra
tags: [docker-compose, opentelemetry, prometheus, grafana, tempo, observability, otel-collector]

requires:
  - phase: 13-observability-foundations-01
    provides: Phase 13 wave 1 foundation (PII analyzer, GameKitTelemetry constants)

provides:
  - "Self-hosted observability stack: OTel Collector + Prometheus (host-isolated) + Grafana + Tempo via docker-compose overlay"
  - "Base compose for TicTacToeDuel sample (Postgres :5433 + Redis)"
  - "Two provisioned-as-code Grafana dashboards (matchmaking queue depth + ticker health)"
  - "Grafana auto-provisions Prometheus + Tempo datasources on container start"
  - "Jaeger (Apache-2.0) swap documented for Tempo (AGPLv3)"

affects:
  - phase-15-instrumentation
  - samples-TicTacToeDuel

tech-stack:
  added:
    - "otel/opentelemetry-collector-contrib:0.154.0 (Docker image, OTel Collector contrib)"
    - "prom/prometheus:v3.11.2 (Docker image, Prometheus)"
    - "grafana/grafana:13.0.2 (Docker image, Grafana)"
    - "grafana/tempo:2.6.1 (Docker image, Tempo trace store AGPLv3 operator-pulled)"
  patterns:
    - "Base + overlay docker-compose pair for sample stacks (base: Postgres + Redis; overlay: observability)"
    - "Prometheus host-isolation via omitting ports: key in service block (criterion #3)"
    - "Grafana provisioned-as-code via /etc/grafana/provisioning mount"
    - "OTLP push from host app to dockerized Collector on :4317; Collector fans out to Prometheus + Tempo"

key-files:
  created:
    - "samples/TicTacToeDuel/docker-compose.yml"
    - "samples/TicTacToeDuel/docker-compose.observability.yml"
    - "samples/TicTacToeDuel/observability/otel-collector-config.yml"
    - "samples/TicTacToeDuel/observability/prometheus.yml"
    - "samples/TicTacToeDuel/observability/tempo.yaml"
    - "samples/TicTacToeDuel/observability/grafana/provisioning/datasources/datasources.yml"
    - "samples/TicTacToeDuel/observability/grafana/provisioning/dashboards/dashboards.yml"
    - "samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json"
    - "samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json"
    - "samples/TicTacToeDuel/observability/README.md"
  modified: []

key-decisions:
  - "Prometheus service omits ports: key entirely (not an empty ports: list) — Prometheus shows 9090/tcp with no host binding in docker ps, proving internal-only access; curl http://localhost:9090 connection refused (ISOLATION-OK)"
  - "OTel Collector publishes ONLY :4317 (OTLP gRPC) to host — sample app on host pushes here; Collector Prometheus exporter :8889 stays internal (not published)"
  - "Grafana anonymous admin enabled (GF_AUTH_ANONYMOUS_ENABLED + GF_AUTH_ANONYMOUS_ORG_ROLE=Admin) for dev-only local convenience — not production-suitable"
  - "Dashboard JSON hand-authored as minimal valid Grafana 13 schemaVersion:38 JSON (~100 lines each) per RESEARCH assumption A5 — no running Grafana instance needed to generate"
  - "Dashboard panels target Phase 15 metrics (gamekit.matchmaking.*, rankings) — panels show no-data until Phase 15 instrumentation lands; this is acceptable for Phase 13"
  - "Tempo 2.6.1 pinned (not 3.0.x which has breaking storage format changes per RESEARCH Pitfall 5)"
  - "Jaeger (Apache-2.0) swap documented in README as one-line compose + collector + datasource change — satisfies D-08 and OBS-08 AGPL opt-out path"

patterns-established:
  - "OBS-08 compose pair pattern: docker-compose.yml (base Postgres/Redis) + docker-compose.observability.yml (overlay) started via docker compose -f base -f overlay up"
  - "Prometheus host-isolation: service has NO ports: key, only a named network attachment — verified by ISOLATION-OK acceptance test"
  - "Grafana auto-provision: datasources.yml + dashboards.yml mounted at /etc/grafana/provisioning + dashboard JSONs at /var/lib/grafana/dashboards"

requirements-completed: [OBS-08]

duration: 5min
completed: "2026-06-14"
---

# Phase 13 Plan 04: Observability Stack Summary

**Self-hosted OTel Collector + Prometheus (host-isolated) + Grafana + Tempo compose stack with provisioned-as-code datasources and two Grafana dashboards for the TicTacToeDuel sample**

## Performance

- **Duration:** 5min
- **Started:** 2026-06-14T18:33:09Z
- **Completed:** 2026-06-14T18:38:11Z
- **Tasks:** 2
- **Files created:** 10

## Accomplishments

- Created base compose (Postgres 17.9 on host :5433 + Redis 8.6.2) and observability overlay (OTel Collector + Prometheus + Grafana + Tempo) for the TicTacToeDuel sample
- Proved Prometheus host isolation (criterion #3): Prometheus shows `9090/tcp` with NO host binding in `docker ps`; `curl http://localhost:9090` returns connection refused (ISOLATION-OK)
- Grafana auto-provisions Prometheus and Tempo datasources and both dashboards on container start with zero click-ops (GRAFANA-OK + DATASOURCE-PROMETHEUS-OK verified via Grafana API)
- All six image tags pinned explicitly (no `:latest`); Jaeger (Apache-2.0) swap documented for operators who prefer to avoid AGPLv3 Tempo

## Task Commits

1. **Task 1: Base compose + overlay + Collector/Prometheus/Tempo configs** - `26f9c20` (feat)
2. **Task 2: Grafana provisioning + dashboards + README** - `b632ef9` (feat)

## Files Created/Modified

- `samples/TicTacToeDuel/docker-compose.yml` - Base stack: Postgres 17.9 on host :5433, Redis 8.6.2
- `samples/TicTacToeDuel/docker-compose.observability.yml` - Overlay: OTel Collector (:4317 published) + Prometheus (no host port) + Tempo + Grafana (:3000 published)
- `samples/TicTacToeDuel/observability/otel-collector-config.yml` - OTLP gRPC/HTTP receivers; prometheus exporter :8889 (internal); otlp/tempo exporter
- `samples/TicTacToeDuel/observability/prometheus.yml` - Scrapes `otel-collector:8889` (internal Docker DNS)
- `samples/TicTacToeDuel/observability/tempo.yaml` - Minimal single-binary Tempo with local block+wal storage
- `samples/TicTacToeDuel/observability/grafana/provisioning/datasources/datasources.yml` - Prometheus (isDefault, http://prometheus:9090) + Tempo (http://tempo:3200)
- `samples/TicTacToeDuel/observability/grafana/provisioning/dashboards/dashboards.yml` - File provider at /var/lib/grafana/dashboards
- `samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json` - Queue depth + dropped events panels (gamekit.matchmaking.* metrics)
- `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json` - Tick duration histogram + lease acquired/lost + pool sweep duration
- `samples/TicTacToeDuel/observability/README.md` - Compose-up command, port map, criterion-#3 isolation, Jaeger swap, GPL/AGPL rationale

## Decisions Made

- Prometheus host-isolation implemented by omitting `ports:` key entirely from the `prometheus:` service block (not an empty list) — produces no host socket; criterion #3 verified via live `curl` test
- OTel Collector publishes only `:4317` (OTLP gRPC); its Prometheus exporter on `:8889` is internal only, ensuring app metrics are never exposed to host directly
- Dashboard JSON hand-authored with `schemaVersion: 38` (Grafana 13 stable format); panels target Phase 15 metric names — no-data panels are acceptable for Phase 13
- Tempo 2.6.1 chosen over 3.0.x (breaking storage format migration required in 3.0)
- Grafana anonymous Admin access enabled for local dev convenience; README does not claim this is production-suitable

## Deviations from Plan

None — plan executed exactly as written. The Redis port conflict during docker stack validation (port 6379 already allocated on the CI/dev host from a pre-existing Redis process) did not affect the observability containers; all four observability services (otel-collector, prometheus, grafana, tempo) started and passed their acceptance tests.

## Issues Encountered

- Redis port 6379 already allocated on host during both `docker compose up` runs — pre-existing host process holds :6379. The base compose Redis container could not bind its host port but this had no effect on the observability containers which were all up and healthy. ISOLATION-OK and GRAFANA-OK and DATASOURCE-PROMETHEUS-OK all confirmed.

## Known Stubs

- Dashboard panel metrics reference Phase 15 metric names (e.g., `gamekit_matchmaking_queue_depth`, `gamekit_matchmaking_tick_duration_ms_bucket`) that will not exist until Phase 15 ships full OTel instrumentation. Panels show "No data" intentionally until then. This is documented in each dashboard's `description` field and in the README.

## Threat Flags

| Flag | File | Description |
|------|------|-------------|
| threat_flag: information_disclosure | docker-compose.observability.yml | `GF_AUTH_ANONYMOUS_ENABLED=true` with `GF_AUTH_ANONYMOUS_ORG_ROLE=Admin` — grants anonymous full admin in Grafana; acceptable for dev-only local sample but NOT suitable for production. Documented in README. |

T-13-METRICS (Prometheus host-isolation) and T-13-SC-IMG (pinned image tags) from the plan's threat model are both mitigated — verified by ISOLATION-OK and grep -c :latest == 0 acceptance criteria respectively.

## Next Phase Readiness

- OBS-08 complete: self-hosted observability stack ships with the TicTacToeDuel sample
- Phase 15 instrumentation (OBS-04/05/06) will populate the pre-provisioned dashboards with real data
- Operators who need to avoid AGPLv3 have a documented one-line Jaeger swap path

---
*Phase: 13-observability-foundations*
*Completed: 2026-06-14*
