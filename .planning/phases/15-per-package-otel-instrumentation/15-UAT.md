---
status: complete
phase: 15-per-package-otel-instrumentation
source: [15-VERIFICATION.md]
started: 2026-06-22T21:35:00Z
updated: 2026-06-22T22:10:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Live Grafana dashboard rendering against the sample stack (Criterion #4)
expected: Start the TicTacToeDuel sample stack (`docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d`), drive matchmaking traffic (enqueue two tickets), then open Grafana and confirm: (a) matchmaking-queue-depth renders gamekit_matchmaking_queue_depth and gamekit_matchmaking_budget_bail_total; (b) ticker-health renders gamekit_matchmaking_ticker_lag_ms_bucket with real p50/p99 values. Panels show non-zero data, not "No data".
result: issue
reported: "Automated live-stack run: Prometheus has 0 gamekit_* series and the collector received 0 OTLP, despite a healthy stack and 2 tickets enqueued (ZCARD=2, ticker running). Root cause: samples/TicTacToeDuel/Program.cs never calls AddGameKitObservability(), which is the only place the OTel SDK pipeline + OTLP exporter is registered. Dashboards would show 'No data'. The library instrumentation itself is correct and unit-verified; the sample app simply never enables the SDK pipeline."
severity: major

### 2. Live Tempo trace descent for a matchmaking enqueue (Criterion #2)
expected: Enqueue a matchmaking ticket in the sample app with the stack capturing traces. In Grafana Explore → Tempo, the enqueue trace shows the MatchFormation span as a descendant (child) of the HTTP enqueue span — a single causal trace timeline. For a 2-player match, the second ticket's traceparent appears as an ActivityLink on the MatchFormation span. (In-process proxy W3CTracePropagationTests is 3/3 passing.)
result: issue
reported: "Automated live-stack run: Tempo /api/search returned 0 traces (same root cause — no SDK pipeline, nothing exported). Partial confirmation that the propagation code works: the Redis ticket hash contained otel.traceparent=00-<traceid>-<spanid>-00, the W3C context written at enqueue. Only the SDK export to Tempo is missing."
severity: major

### 3. Lobby connected-clients gauge does not leak on a failed connect (CR-01)
expected: With a Postgres/backplane failure injected during an active OnConnectedAsync, the lobby.connected_clients gauge does NOT drift upward — it matches the actual number of live connections. NOTE: this requires the CR-01 fix first (a try/catch in LobbyHub.OnConnectedAsync that Decrements before rethrowing); without it the gauge over-counts permanently under sustained connect failures.
result: pass
note: "CR-01 fix applied (commit bb570fe): try/catch in LobbyHub.OnConnectedAsync decrements before rethrowing. Regression test LobbyConnectionGaugeLeakTests (2/2) asserts the gauge returns to 0 when the connect-path dependency throws, and counts exactly once + decrements to 0 on the clean path. Full Lobby suite 27/27."

## Summary

total: 3
passed: 1
issues: 2
pending: 0
skipped: 0
blocked: 0

## Notes

- Criterion #3 (Prometheus host-isolation) independently re-confirmed during the live run: `curl http://localhost:9090` from the host is refused (curl_rc=7); only :4317 (collector), :3000 (Grafana), :5433 (Postgres), :6379 (Redis) are host-published.
- WR-01 (Rankings decay panel legend referencing a non-existent ladder_id tag) fixed in commit 83e679f.
- Secondary observation during the live run: with two equal-rating (aggregateRating=0) solo tickets in pool `tictactoe`, no match formed within ~90s. May be a separate matchmaking-pairing issue or a test-data artifact; does not affect these telemetry verdicts (no match is still scraped/traced once the SDK pipeline is wired). Flagged for separate follow-up.

## Gaps

- truth: "Emitted GameKit instruments are observable end-to-end: matchmaking/lobby/rankings metrics reach Prometheus and render in the Grafana dashboards (criterion #4)"
  status: failed
  reason: "User reported (automated run): samples/TicTacToeDuel/Program.cs never calls AddGameKitObservability(), so the running app registers no OpenTelemetry SDK pipeline and exports zero OTLP. Prometheus had 0 gamekit_* series. The library instrumentation is correct (unit-verified); the sample wiring is missing."
  severity: major
  test: 1
  artifacts: [samples/TicTacToeDuel/Program.cs, src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs]
  missing: ["gameKitBuilder.AddGameKitObservability(o => o.OtlpEndpoint = \"http://localhost:4317\") call in samples/TicTacToeDuel/Program.cs"]
- truth: "A matchmaking enqueue trace shows MatchFormation as a descendant of the enqueue span, visible end-to-end in Tempo (criterion #2)"
  status: failed
  reason: "User reported (automated run): Tempo had 0 traces — same root cause as the metrics gap (no SDK pipeline in the sample). The W3C traceparent IS correctly written to the Redis ticket hash, so the in-code propagation is sound; only the live export is missing."
  severity: major
  test: 2
  artifacts: [samples/TicTacToeDuel/Program.cs]
  missing: ["AddGameKitObservability() with tracing/OTLP wired in the sample so the MatchFormation span is exported to Tempo"]
