---
phase: 15-per-package-otel-instrumentation
plan: "06"
subsystem: telemetry
tags: [otel, metrics, tracing, observability, prometheus, grafana, collector, dashboard, obs-04, obs-05, obs-06]
depends_on: ["15-01", "15-02", "15-03", "15-04", "15-05"]
provides:
  - AddGameKitObservability registers LobbySourceName (GameKit.Lobby ActivitySource) in WithTracing
  - AddGameKitObservability registers RankingsMeterName (GameKit.Rankings Meter) in WithMetrics
  - AddGameKitObservability registers LobbyMeterName (GameKit.Lobby Meter) in WithMetrics
  - OTel Collector prometheus exporter with namespace=gamekit prefix (all metrics appear as gamekit_* in Prometheus)
  - matchmaking-queue-depth.json: dropped-events panel re-prefixed to gamekit_matchmaking_analytics_dropped_events_total
  - ticker-health.json: ticker-lag bucket corrected to gamekit_matchmaking_ticker_lag_ms_bucket
  - ticker-health.json: rankings decay bucket corrected to gamekit_rankings_decay_duration_ms_bucket
affects:
  - src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs
  - samples/TicTacToeDuel/observability/otel-collector-config.yml
  - samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json
  - samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json
  - tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs
tech_stack:
  added: []
  patterns:
    - OTel Collector prometheus exporter namespace key for metric prefixing
    - AddGameKitObservability() chained AddSource/AddMeter covering all Phase-15 sources and meters
key_files:
  created: []
  modified:
    - src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs
    - samples/TicTacToeDuel/observability/otel-collector-config.yml
    - samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json
    - samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json
    - tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs
key-decisions:
  - "Approach A (collector namespace) chosen over Approach B (strip prefix from dashboards) — lower dashboard-change count, validates the full Collector→Prometheus pipeline"
  - "Panel titles updated alongside PromQL: 'Ticker Tick Duration' → 'Ticker Lag'; 'Drain Ladder Duration' → 'Decay Duration' — accurate descriptions after Phase-15 instrument rename"
  - "Matchmaking.Tests: 3 previously-skipped W3C stub tests now pass (no skips in 115-test run) — Phase-03 W3C stubs resolved in a prior execution"
requirements-completed: [OBS-04, OBS-05, OBS-06]
duration: 3min
completed: 2026-06-22T21:30:00Z
tasks_completed: 3
files_changed: 5
status: complete
---

# Phase 15 Plan 06: End-to-End OBS Wiring Summary

**`AddGameKitObservability()` extended to register Lobby source + Rankings/Lobby meters; OTel Collector gains `namespace: gamekit`; both matchmaking dashboards corrected to actual emitted instrument names — Phase 15 criterion #3 + criterion #4 scrape path complete.**

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Register Lobby source + Rankings/Lobby meters in AddGameKitObservability + update smoke test comment | 04d4e45 | `GameKitObservabilityBuilderExtensions.cs`, `GameKitTelemetryConstantsTests.cs` |
| 2 | Add namespace:gamekit to collector + correct both dashboard PromQL expressions | 684a765 | `otel-collector-config.yml`, `matchmaking-queue-depth.json`, `ticker-health.json` |
| 3 | Full-suite regression gate (build + test run) | — (no code change) | — |

## Verification

- `dotnet test tests/GameKit.Core.Tests --filter "AddGameKitObservability|GameKitTelemetryConstantsTests"`: 23/23 pass — registration smoke tests + all reflection Facts green
- `grep "namespace: gamekit" samples/TicTacToeDuel/observability/otel-collector-config.yml`: FOUND
- `grep -E "tick_duration_ms_bucket|drain_ladder_duration_ms_bucket" ticker-health.json`: nothing (stale names gone)
- `grep gamekit_matchmaking_analytics_dropped_events_total matchmaking-queue-depth.json`: FOUND (re-prefixed)
- Plan verification check (`DASHBOARD-NAMES-OK`): PASSED
- **Full affected-package regression gate:**
  - `dotnet test tests/GameKit.Core.Tests`: 156/156 passed
  - `dotnet test tests/GameKit.Matchmaking.Tests`: 115/115 passed (0 skipped — W3C stubs resolved)
  - `dotnet test tests/GameKit.Rankings.Tests`: 24/24 passed
  - `dotnet test tests/GameKit.Lobby.Integration.Tests`: 25/25 passed
- **Live stack criterion #4**: manual live-stack run (`docker compose -f docker-compose.yml -f docker-compose.observability.yml up`) deferred to operator — queue-depth + ticker-health dashboards are now wired with correct PromQL against real instruments; criterion #4 requires a running sample with traffic to verify visually.

## Deviations from Plan

### Auto-fixed Issues

None. Plan executed exactly as specified. The only addition beyond the minimal PromQL fix list was updating two stale panel titles/descriptions in `ticker-health.json` to match the actual Phase-15 instrument names ("Ticker Tick Duration" → "Ticker Lag"; "Drain Ladder Duration" → "Decay Duration") — this is cosmetic and consistent with the done criteria.

## Key Decisions Made

1. **Approach A (collector namespace) confirmed:** `otel/opentelemetry-collector-contrib:0.154.0` supports `exporters.prometheus.namespace` (the key has been present in the contrib Prometheus exporter for many years; Research Open Q1 resolved in favor of Approach A). No fallback to Approach B needed.

2. **Panel title updates:** When correcting the PromQL bucket names in `ticker-health.json`, updated the stale panel titles ("Ticker Tick Duration" / "Drain Ladder Duration") and descriptions to reflect the actual Phase-15 instrument names. This avoids operator confusion when the panels render real data.

3. **Smoke test comment vs. new assertion:** Plan asked to add a comment (not a new test assertion) to `GameKitTelemetryConstantsTests` documenting the extended registration. A brief in-line comment was added to `AddGameKitObservability_DoesNotThrow_WithDefaultOptions` — no new test file or assertion added, keeping the smoke contract identical.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes. Changes are:
- In-process OTel SDK registration (AddSource/AddMeter) — no outbound network
- OTel Collector YAML config (collector-internal; Prometheus exporter port :8889 remains on internal Docker network per Phase-13 D-09)
- Grafana dashboard JSON (static config, no code execution)

No threat flags to report.

## Known Stubs

None. All instruments have been registered in `AddGameKitObservability()`, the collector is configured to prefix them as `gamekit_*`, and both dashboards query the actual emitted names. The only remaining gap is criterion #4 live validation — this requires a running sample stack with matchmaking traffic, deferred to operator manual run.

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` — LobbySourceName added | FOUND |
| `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` — RankingsMeterName/LobbyMeterName added | FOUND |
| `samples/TicTacToeDuel/observability/otel-collector-config.yml` — namespace: gamekit | FOUND |
| `samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json` — gamekit_matchmaking_analytics_dropped_events_total | FOUND |
| `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json` — gamekit_matchmaking_ticker_lag_ms_bucket | FOUND |
| `samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json` — gamekit_rankings_decay_duration_ms_bucket | FOUND |
| Commit 04d4e45 (Task 1) | FOUND |
| Commit 684a765 (Task 2) | FOUND |
| Core.Tests 156/156 | PASSED |
| Matchmaking.Tests 115/115 | PASSED |
| Rankings.Tests 24/24 | PASSED |
| Lobby.Integration.Tests 25/25 | PASSED |
