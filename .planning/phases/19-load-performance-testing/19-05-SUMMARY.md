---
phase: 19-load-performance-testing
plan: "05"
subsystem: k6-load-scenarios + performance-tuning
status: complete
tags: [k6, load-testing, signalr, matchmaking, performance-tuning, documentation]
requires:
  - "19-02: SignalR spike (GO decision + helpers)"
  - "19-04: BenchmarkDotNet baselines (BASELINES.md)"
provides:
  - "tests/k6/matchmaking-burst.js: 500-VU matchmaking + auth throughput + match polling"
  - "tests/k6/lobby-signalr-fanout.js: N-client SignalR fan-out delivery distribution"
  - "docs/performance-tuning.md: BCrypt/Argon2 tables, Npgsql sizing, hot-query index guide"
affects:
  - "tests/k6/: k6 load scenario directory"
  - "docs/: operator documentation"
tech-stack:
  added:
    - "per-vu-iterations executor (k6 scenario config) for SignalR fan-out"
    - "k6 Trend+Counter metrics: match_formation_time_ms, signalr_delivery_time_ms"
  patterns:
    - "k6/websockets single-sleep event model: WS connects during sleep, callbacks fire at function exit in one sweep"
    - "SignalR wire target includes Async suffix: ReceiveChatMessageAsync not ReceiveChatMessage"
    - "per-vu-iterations bounds WS-backed iterations by maxDuration, not iteration count"
key-files:
  created:
    - tests/k6/matchmaking-burst.js
    - tests/k6/lobby-signalr-fanout.js
    - docs/performance-tuning.md
  modified: []
decisions:
  - "Use per-vu-iterations executor for SignalR fan-out (shared-iterations keeps VU alive until WS closes, causing iteration to never complete)"
  - "Single sleep SESSION_S in fan-out VU function — all protocol callbacks (open/message/close) fire in one sweep at function exit"
  - "SESSION_S=10 must be < SignalR handshake timeout (~15 s); open callback fires immediately when WS connects, not after sleep"
  - "Wire target for typed hub clients includes 'Async' suffix: LobbyHub.ILobbyClient.ReceiveChatMessageAsync fires as 'ReceiveChatMessageAsync'"
metrics:
  duration: "~4 hours (including k6/websockets event model debugging)"
  completed: "2026-06-23"
  tasks_completed: 3
  files_changed: 4
---

# Phase 19 Plan 05: PERF-03 Matchmaking Burst + PERF-04b SignalR Fan-out + PERF-05 Tuning Guide

Delivers the three remaining PERF requirements: the k6 matchmaking-burst scenario with auth throughput and match-formed polling (PERF-03), the k6 Lobby SignalR fan-out delivery distribution over the real Redis backplane (PERF-04), and the performance-tuning operator guide (PERF-05).

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | k6 matchmaking burst + auth throughput + match-formed polling (PERF-03) | 2bcd8c1 | tests/k6/matchmaking-burst.js |
| 2 | k6 Lobby SignalR fan-out delivery-time distribution (PERF-04b) | 62ca50d | tests/k6/lobby-signalr-fanout.js |
| 3 | docs/performance-tuning.md (PERF-05) | 8c02baf | docs/performance-tuning.md |

## Actual k6 Run Results

### Task 1: Matchmaking Burst (PERF-03)

Run command (local SpikeHost, SESSION_S environment):
```
docker run --rm -i --network host \
  -e BASE_URL=http://localhost:5100 \
  -e JWT=<spike_jwt> \
  -e LADDER_ID=<spike_ladder_id> \
  grafana/k6:latest run - < tests/k6/matchmaking-burst.js
```

Verified run output (50 VUs burst, 15s, with real SpikeHost):
- `http_req_duration{name:enqueue}`: avg=3.33ms, p(99)=**36.71ms** — threshold p(99)<2000ms: PASS
- `http_req_failed`: 0.00% — threshold rate<0.01: PASS
- `match_formation_time_ms`: p(99) within MATCH_P99_MS threshold: PASS
- 6571 enqueue iterations in ~15s at 50 VUs

All thresholds passed. Three scenarios confirmed: burst (enqueue POST), auth_throughput (login POST), match_poll (status GET).

### Task 2: SignalR Fan-out (PERF-04b)

Run command (local SpikeHost with volume mount):
```
docker run --rm --network host \
  -v /path/to/gamekit/tests/k6:/tests/k6 \
  -e BASE_URL=http://localhost:5100 \
  -e WS_URL=ws://localhost:5100 \
  -e JWT=<spike_jwt> \
  -e LOBBY_ID=<spike_lobby_id> \
  -e CLIENTS=50 \
  -e SESSION_S=10 \
  grafana/k6:latest run /tests/k6/lobby-signalr-fanout.js
```

Verified run output (50 VUs, 10s session, real SpikeHost with Redis backplane):
```
signalr_deliveries_received: 50  (all 50 VUs received ReceiveChatMessageAsync)
signalr_delivery_time_ms: avg=2.74ms  min=0ms  med=1ms  max=20ms
                           p(90)=5.3ms  p(95)=15.39ms
```
- Threshold `count>25`: 50 received — PASS
- Threshold `p(80)<5000ms`: p(80)=3.2ms — PASS
- 150/150 checks passed (handshake + join + delivery for all 50 VUs)
- `ws_msgs_received: 289`, `ws_msgs_sent: 200`
- 50/50 iterations complete (not interrupted)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] k6/websockets multi-phase sleep model mismatch**

- **Found during:** Task 2 (SignalR fan-out)
- **Issue:** The original three-phase sleep design (`sleep(Phase1)` → check state → `sleep(Phase2)`) is fundamentally incompatible with k6/websockets v2.0.0 behavior. Event callbacks (`open`, `message`, `close`) fire AFTER the VU function returns — not between sleep calls. Between two consecutive `sleep()` calls in the same function, callbacks never fire. This caused `state.handshakeAckReceived` to always be false at the inter-sleep checkpoint, producing `ws_msgs_sent: 1` (handshake only, nothing else).
- **Root cause discovery (detailed):**
  - `new WebSocket(url)` in k6/websockets opens the WS connection immediately and fires the `open` callback in real-time (not deferred to after sleep). The `ws.send(handshake)` inside `open` goes out at t~0ms.
  - The `message` callbacks however are deferred: they fire as a sweep when the VU function returns, with micro-yields between callbacks allowing server responses.
  - Between consecutive `sleep()` calls, neither the console logs nor state mutations from callbacks are visible — they are truly deferred to function exit.
  - Single sleep (the spike model) works because: sleep(N) holds the WS connection; at function exit all deferred callbacks fire in order: ack callback → sends JoinLobby → micro-yield → join result → sends broadcast → micro-yield → ReceiveChatMessageAsync → ws.close() → close records metrics.
- **Fix:** Rewrote `lobby-signalr-fanout.js` using a single `sleep(SESSION_S)` with ALL protocol logic in message callbacks. Added `per-vu-iterations` scenario executor (replaces `shared-iterations` which waits for WS to close before marking iteration complete). Removed `ws.close()` from main function body (calling it there fires BEFORE callbacks, killing the WS before handshake sends).
- **Files modified:** `tests/k6/lobby-signalr-fanout.js`
- **Commits:** 155c311 (original broken version), 62ca50d (fixed version)

**2. [Rule 1 - Bug] SignalR wire target name includes `Async` suffix**

- **Found during:** Task 2 debugging (after fixing the sleep model)
- **Issue:** `ReceiveChatMessage` detection check used `.toLowerCase() === 'receivechatmessage'` but ASP.NET Core SignalR typed hub clients (`ILobbyClient.ReceiveChatMessageAsync`) use the FULL method name including `Async` suffix on the wire. The frame arrives as `"target":"ReceiveChatMessageAsync"`. Mismatch caused ws.close() to never be called after broadcast delivery, leaving the WS open and the iteration never completing (interrupted at maxDuration).
- **Evidence:** With 50 VUs: `ws_msgs_received: 289` (all frames including ReceiveChatMessageAsync arrived), but `signalr_deliveries_received: 0` and 0 complete iterations — confirming the delivery detection check failed silently.
- **Fix:** Changed check to `frame.target.toLowerCase() === 'receivechatmessageasync'`.
- **Files modified:** `tests/k6/lobby-signalr-fanout.js`
- **Commits:** 62ca50d

**3. [Rule 1 - Bug] Debug file cleanup**

- **Found during:** Task 2 debugging
- **Issue:** `tests/k6/simple-fanout-test.js` was committed during debugging session.
- **Fix:** Deleted and committed removal.
- **Files modified:** (deleted) `tests/k6/simple-fanout-test.js`
- **Commits:** 62ca50d (includes deletion)

## Known Stubs

None. All three artifacts are complete and functional:
- `matchmaking-burst.js`: actually ran and produced real p99 output
- `lobby-signalr-fanout.js`: actually ran with 50 VUs and produced delivery distribution
- `docs/performance-tuning.md`: cross-references actual BASELINES.md measurements

## Threat Flags

None detected. All scenarios read credentials from `__ENV` (no hardcoded tokens), use local-only invocation patterns, and the k6 AGPLv3 posture is documented in both the tuning guide and scenario headers.

## Self-Check: PASSED

All files confirmed present:
- tests/k6/matchmaking-burst.js — FOUND
- tests/k6/lobby-signalr-fanout.js — FOUND
- docs/performance-tuning.md — FOUND
- tests/k6/simple-fanout-test.js — CONFIRMED DELETED

All commits confirmed:
- 2bcd8c1 (matchmaking-burst) — FOUND
- 8c02baf (performance-tuning.md) — FOUND
- 155c311 (fanout original) — FOUND
- 62ca50d (fanout fixed + cleanup) — FOUND
