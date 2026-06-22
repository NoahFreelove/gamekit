---
status: complete
phase: 15-per-package-otel-instrumentation
source: [15-VERIFICATION.md]
started: 2026-06-22T21:35:00Z
updated: 2026-06-22T23:05:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Live Grafana dashboard rendering against the sample stack (Criterion #4)
expected: Start the TicTacToeDuel sample stack (`docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d`), drive matchmaking traffic (enqueue two tickets), then open Grafana and confirm: (a) matchmaking-queue-depth renders gamekit_matchmaking_queue_depth and gamekit_matchmaking_budget_bail_total; (b) ticker-health renders gamekit_matchmaking_ticker_lag_ms_bucket with real p50/p99 values. Panels show non-zero data, not "No data".
result: pass
note: "Initially failed (0 gamekit_* series — sample never called AddGameKitObservability). Gap closed: sample wired (commit 826f751) + dashboard PromQL corrected to the real OTel→Prometheus names (commit a86f3be). Re-verified live against a real 2-distinct-player match in the default pool: 8/12 dashboard targets PASS with real values (ticker_lag p50=2.5ms/p99=4.95ms, pool_sweep p50/p99, lease_acquired 0.85/s, queue_depth=1, matches_formed increase=3.21, raw matches_formed=5); the other 4 targets are documented-absent counters (lease_lost, rankings_decay, dropped_events, budget_bail) whose triggering events did not occur in a clean short run. Authoritative Prometheus __name__ dump confirmed every PASS target's metric name matches exactly — no target references a wrong/nonexistent name."

### 2. Live Tempo trace descent for a matchmaking enqueue (Criterion #2)
expected: Enqueue a matchmaking ticket in the sample app with the stack capturing traces. In Grafana Explore → Tempo, the enqueue trace shows the MatchFormation span as a descendant (child) of the HTTP enqueue span — a single causal trace timeline. For a 2-player match, the second ticket's traceparent appears as an ActivityLink on the MatchFormation span. (In-process proxy W3CTracePropagationTests is 3/3 passing.)
result: pass
note: "Initially failed (0 traces — no SDK pipeline). Gap closed: sample now wires AddGameKitObservability + ASP.NET Core trace instrumentation (commit 826f751). Re-verified live against a real 2-distinct-player match — Tempo trace d0223a6a... shows: POST /api/mm/queue (HTTP SERVER span, scope Microsoft.AspNetCore) → MatchFormation (INTERNAL, scope GameKit.Matchmaking.Ticker) as a true descendant (parentSpanId chain matches), and the second ticket's enqueue context attached as an ActivityLink (trace 761c94b3.../span 576ad22a...). Exactly the OBS-06 fan-in design."

### 3. Lobby connected-clients gauge does not leak on a failed connect (CR-01)
expected: With a Postgres/backplane failure injected during an active OnConnectedAsync, the lobby.connected_clients gauge does NOT drift upward — it matches the actual number of live connections. NOTE: this requires the CR-01 fix first (a try/catch in LobbyHub.OnConnectedAsync that Decrements before rethrowing); without it the gauge over-counts permanently under sustained connect failures.
result: pass
note: "CR-01 fix applied (commit bb570fe): try/catch in LobbyHub.OnConnectedAsync decrements before rethrowing. Regression test LobbyConnectionGaugeLeakTests (2/2) asserts the gauge returns to 0 when the connect-path dependency throws, and counts exactly once + decrements to 0 on the clean path. Full Lobby suite 27/27."

## Summary

total: 3
passed: 3
issues: 0
pending: 0
skipped: 0
blocked: 0

## Notes

- All three live-stack items passed after the gap fix + automated re-verification. Criterion #1 (PII tag-key tests) and #3 (metrics namespace) were already verified in 15-VERIFICATION.md.
- Criterion #3 (Prometheus host-isolation) independently re-confirmed during the live run: `curl http://localhost:9090` from the host is refused (curl_rc=7); only :4317 (collector), :3000 (Grafana), :5433 (Postgres), :6379 (Redis) are host-published.
- WR-01 (Rankings decay panel legend referencing a non-existent ladder_id tag) fixed in commit 83e679f.
- Matchmaking-pairing observation RESOLVED to a sample doc/UX gap (not a telemetry defect): the ticker's GetPoolNamesForLadder() only sweeps the `default` pool (the sample configures no AllowedRegions), but the README walkthrough tells players to enqueue with poolName="tictactoe" — those tickets land in a pool the ticker never scans, so they never pair. Enqueuing with NO poolName (→ default) pairs two distinct players in ~1s. Flagged as a separate sample-docs follow-up; out of Phase-15 (instrumentation) scope.

## Gaps

- truth: "Emitted GameKit instruments are observable end-to-end: matchmaking/lobby/rankings metrics reach Prometheus and render in the Grafana dashboards (criterion #4)"
  status: resolved
  reason: "Closed by commits 826f751 (sample wires gameKitBuilder.AddGameKitObservability(o => o.OtlpEndpoint = config) + 3 OTel SDK package refs + ASP.NET Core trace instrumentation) and a86f3be (dashboard PromQL corrected to the actual OTel→Prometheus names: histograms use the _milliseconds unit suffix, lease/budget counters use _events). Re-verified live: 8/12 dashboard targets PASS with real values; the 4 empty targets are documented-absent counters (events that did not occur in a short clean run). Authoritative Prometheus __name__ dump confirmed all PASS names exact."
  severity: major
  test: 1
  artifacts: [samples/TicTacToeDuel/Program.cs, samples/TicTacToeDuel/TicTacToeDuel.csproj, samples/TicTacToeDuel/appsettings.Development.json, samples/TicTacToeDuel/observability/grafana/dashboards/ticker-health.json, samples/TicTacToeDuel/observability/grafana/dashboards/matchmaking-queue-depth.json]
  missing: []
- truth: "A matchmaking enqueue trace shows MatchFormation as a descendant of the enqueue span, visible end-to-end in Tempo (criterion #2)"
  status: resolved
  reason: "Closed by commit 826f751 (sample wires AddGameKitObservability + ASP.NET Core HTTP trace instrumentation so the enqueue server span exists and the MatchmakingActivitySource is exported). Re-verified live: Tempo trace d0223a6a... shows POST /api/mm/queue (SERVER) → MatchFormation (INTERNAL, GameKit.Matchmaking.Ticker) as a true descendant + the co-matched ticket attached as an ActivityLink."
  severity: major
  test: 2
  artifacts: [samples/TicTacToeDuel/Program.cs, samples/TicTacToeDuel/TicTacToeDuel.csproj]
  missing: []
