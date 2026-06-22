---
status: testing
phase: 15-per-package-otel-instrumentation
source: [15-VERIFICATION.md]
started: 2026-06-22T21:35:00Z
updated: 2026-06-22T21:35:00Z
---

## Current Test

number: 1
name: Live Grafana dashboard rendering against the sample stack (Criterion #4)
expected: |
  Both dashboards show non-zero data; no panel displays "No data". The
  matchmaking-queue-depth dashboard renders gamekit_matchmaking_queue_depth and
  gamekit_matchmaking_budget_bail_total; the ticker-health dashboard renders
  gamekit_matchmaking_ticker_lag_ms_bucket with real p50/p99 values. The Rankings
  Decay Duration panel legend reading "p50 ms ()" with an empty ladder_id variable
  is the known WR-01 cosmetic defect, not a failure of criterion #4.
awaiting: user response

## Tests

### 1. Live Grafana dashboard rendering against the sample stack (Criterion #4)
expected: Start the TicTacToeDuel sample stack (`docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d`), drive matchmaking traffic (enqueue two tickets), then open Grafana and confirm: (a) matchmaking-queue-depth renders gamekit_matchmaking_queue_depth and gamekit_matchmaking_budget_bail_total; (b) ticker-health renders gamekit_matchmaking_ticker_lag_ms_bucket with real p50/p99 values. Panels show non-zero data, not "No data".
result: [pending]

### 2. Live Tempo trace descent for a matchmaking enqueue (Criterion #2)
expected: Enqueue a matchmaking ticket in the sample app with the stack capturing traces. In Grafana Explore → Tempo, the enqueue trace shows the MatchFormation span as a descendant (child) of the HTTP enqueue span — a single causal trace timeline. For a 2-player match, the second ticket's traceparent appears as an ActivityLink on the MatchFormation span. (In-process proxy W3CTracePropagationTests is 3/3 passing.)
result: [pending]

### 3. Lobby connected-clients gauge does not leak on a failed connect (CR-01)
expected: With a Postgres/backplane failure injected during an active OnConnectedAsync, the lobby.connected_clients gauge does NOT drift upward — it matches the actual number of live connections. NOTE: this requires the CR-01 fix first (a try/catch in LobbyHub.OnConnectedAsync that Decrements before rethrowing); without it the gauge over-counts permanently under sustained connect failures.
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps
