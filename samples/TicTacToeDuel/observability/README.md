# TicTacToeDuel Observability Stack

Self-hosted observability for the GameKit sample app: OTel Collector, Prometheus, Grafana, and Tempo.
No SaaS dependencies — everything runs locally via Docker Compose.

## Quick Start

From the `samples/TicTacToeDuel` directory:

```bash
# Start base stack (Postgres + Redis) + observability overlay (Collector + Prometheus + Grafana + Tempo):
docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d

# Stop and remove all containers + volumes:
docker compose -f docker-compose.yml -f docker-compose.observability.yml down -v
```

## Port Map

| Port | Service | Access |
|------|---------|--------|
| `:5433` | Postgres (host mapping) | `postgres://postgres:postgres_bootstrap_dev_only@localhost:5433/gamekit` |
| `:6379` | Redis | `redis://localhost:6379` |
| `:4317` | OTel Collector OTLP gRPC | `http://localhost:4317` (sample app pushes telemetry here) |
| `:3000` | Grafana UI | `http://localhost:3000` (open in browser — anonymous admin) |
| `:9090` | Prometheus | **NOT published** — internal network only (see Criterion #3 below) |

## Criterion #3: Prometheus Host Isolation

Prometheus is intentionally **NOT published** to the host network. This prevents app metrics
from being accessible to anything on the host (other processes, network scans, etc.).

Verify isolation is working correctly:

```bash
curl http://localhost:9090/-/healthy
# Expect: connection refused (not reachable from host)
```

Within the `obs-internal` Docker bridge network, Grafana accesses Prometheus at
`http://prometheus:9090` via Docker internal DNS.

## Grafana Dashboards

Grafana auto-provisions two dashboards on container start (zero click-ops required):

1. **GameKit: Matchmaking Queue Depth** — Queue depth and analytics dropped events
   (`gamekit.matchmaking.*` metrics from OTel Collector)
2. **GameKit: Ticker Health** — Ticker tick duration, lease acquired/lost, pool sweep duration

> **Note:** Dashboard panels will show "No data" until Phase 15 ships the full metric
> instrumentation. The dashboards are pre-built with the correct Prometheus queries for
> when the metrics start flowing.

## Grafana Access

Open `http://localhost:3000` in your browser. Anonymous access with Admin role is enabled
(for local dev convenience only — not suitable for production).

Datasources are provisioned automatically:
- **Prometheus** (default) — `http://prometheus:9090`
- **Tempo** — `http://tempo:3200`

## Jaeger Swap (Apache-2.0 vs AGPLv3 Tempo)

Tempo (the default trace store) is licensed under AGPLv3. GameKit does not link or
distribute Tempo — it is an operator-pulled container. However, if you prefer an
Apache-2.0-licensed alternative, you can swap Tempo for Jaeger in one step:

**In `docker-compose.observability.yml`**, replace the `tempo:` service:

```yaml
# REMOVE:
  tempo:
    image: grafana/tempo:2.6.1
    command: ["-config.file=/etc/tempo.yaml"]
    volumes:
      - ./observability/tempo.yaml:/etc/tempo.yaml:ro
      - tempo-data:/var/tempo
    networks:
      - obs-internal

# ADD:
  jaeger:
    image: jaegertracing/all-in-one:1.58   # Apache-2.0
    ports:
      - "16686:16686"                        # Jaeger UI
    networks:
      - obs-internal
```

Also update `otel-collector-config.yml` — replace the `otlp/tempo` exporter:

```yaml
# REMOVE:
  otlp/tempo:
    endpoint: "tempo:4317"
    tls:
      insecure: true

# ADD:
  jaeger:
    endpoint: "jaeger:14250"
    tls:
      insecure: true

# And update the traces pipeline:
service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [jaeger]   # was: [otlp/tempo]
```

And update `grafana/provisioning/datasources/datasources.yml` — replace the Tempo datasource:

```yaml
# REMOVE:
  - name: Tempo
    type: tempo
    url: http://tempo:3200
    access: proxy

# ADD:
  - name: Jaeger
    type: jaeger
    url: http://jaeger:16686
    access: proxy
```

### AGPL Licensing Note (third-party observability stack)

GameKit is Apache-2.0-licensed. Grafana and Tempo are AGPLv3-licensed. Running them as separately
containerized processes does NOT create a "combined work" under the AGPL. GameKit does not
link against, modify, or distribute Grafana/Tempo — it only references them by container
image name in a Compose file. Operators who run this stack own their own deployment.

If you need to be certain, use the Jaeger (Apache-2.0) swap above to avoid AGPL entirely.

## Architecture

```
Host Machine
┌────────────────────────────────────────────────────────────────┐
│  dotnet run (TicTacToeDuel sample app)                          │
│  OtlpExporter → push OTLP/gRPC → localhost:4317                │
└───────────────────────────┬────────────────────────────────────┘
                            │ host port 4317
┌───────────────────────────▼────────────────────────────────────┐
│  Docker internal network: obs-internal                          │
│                                                                 │
│  otel-collector :4317 ──→ prometheus exporter :8889 (internal) │
│                       ──→ tempo:4317                            │
│                                                                 │
│  prometheus (scrapes otel-collector:8889) — NO host port       │
│  tempo (trace store)                                            │
│  grafana :3000 ──→ reads prometheus:9090 + tempo:3200           │
└─────────────────────────────────────────────────────────────────┘

Host: curl http://localhost:9090 → connection refused (ISOLATION-OK)
Host: curl http://localhost:3000 → Grafana UI
Host: OTLP push → localhost:4317 → Collector
```

## Image Versions

All image tags are pinned explicitly (no `:latest`):

| Image | Version | License |
|-------|---------|---------|
| `postgres` | `17.9` | PostgreSQL License |
| `redis` | `8.6.2` | BSD-3-Clause |
| `otel/opentelemetry-collector-contrib` | `0.154.0` | Apache-2.0 |
| `prom/prometheus` | `v3.11.2` | Apache-2.0 |
| `grafana/tempo` | `2.6.1` | AGPLv3 (operator-pulled) |
| `grafana/grafana` | `13.0.2` | AGPLv3 (operator-pulled) |
