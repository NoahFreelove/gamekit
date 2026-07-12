// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
//
// tests/k6/lobby-signalr-fanout.js
//
// PERF-04 (part B): Lobby SignalR fan-out delivery-time distribution over the real Redis backplane.
//
// PREREQUISITE:
//   Plan 19-02 (spike) returned GO — stock grafana/k6 v2.0.0 with k6/websockets is sufficient
//   for the SignalR JSON protocol. This scenario reuses the helpers from tests/k6/helpers/signalr.js.
//
// PURPOSE:
//   Measures the end-to-end delivery latency of a SignalR chat broadcast across N concurrently
//   connected clients.
//
// k6/WEBSOCKETS EVENT MODEL (critical for understanding this script):
//
//   In grafana/k6 v2.0.0, `new WebSocket(url)` schedules a connection but event callbacks
//   (open, message, close) are NOT called during sleep(). Instead:
//
//     1. `sleep(SESSION_S)` — k6 keeps the WS connection alive for SESSION_S real seconds.
//        During this period the actual network I/O runs, server messages are buffered.
//     2. After sleep() returns, the VU function continues synchronously (any code after sleep).
//     3. When the VU function RETURNS (exits), k6 flushes the event queue and fires all
//        buffered callbacks in order: open → message (N times) → close.
//
//   CONSEQUENCE:
//     All protocol logic (handshake, join, broadcast, delivery measurement) MUST live
//     inside event callbacks. Code after sleep() cannot safely read state set by callbacks
//     because the callbacks haven't run yet. Metrics and checks must be recorded inside
//     the `close` callback (the last event to fire).
//
//   TIMEOUT:
//     SESSION_S must be long enough for the full protocol flow to complete BEFORE sleep
//     ends. The handshake is sent in the `open` callback which fires at k6-function-exit
//     time. The server has a handshake timeout (default 15 s). SESSION_S must be < 15 s
//     to avoid the server closing with {"error":"Handshake was canceled."}.
//     Recommended: SESSION_S=10 (matches the spike). Reduce for tighter delivery windows
//     if the server responds quickly (observed latency ~2 ms on a local stack).
//
// PROTOCOL FLOW (all inside callbacks, triggered at function exit after sleep):
//
//   open     → send {"protocol":"json","version":1}\x1e  (handshake)
//   message  → {} ack received → send JoinLobbyAsync(lobbyId)
//   message  → type=3 join result → record broadcastSentAtMs, send SendChatMessageAsync
//   message  → type=1 ReceiveChatMessage push → compute deliveryMs, ws.close()
//   close    → record signalr_delivery_time_ms + signalr_deliveries_received/missed
//
// REDIS BACKPLANE NOTE:
//   When clients connect to DIFFERENT server replicas, the ReceiveChatMessage push crosses
//   the Redis backplane. On a single-replica local stack, it exercises SignalR's in-process
//   group dispatch. Use the SpikeHost with a real Redis backplane (see tests/k6/SpikeHost/)
//   to test the full fan-out path.
//
// IMPORTANT: Run against the LOCAL SpikeHost or Testcontainers stack. NEVER production.
//   See tests/k6/README.md for full invocation instructions.
//
// INVOCATION (Linux, --network host, volume-mount for helper import):
//   docker run --rm --network host \
//     -v /path/to/gamekit/tests/k6:/tests/k6 \
//     -e BASE_URL=http://localhost:5100 \
//     -e WS_URL=ws://localhost:5100 \
//     -e JWT=<player_jwt> \
//     -e LOBBY_ID=<lobby_uuid> \
//     -e CLIENTS=50 \
//     grafana/k6:latest run /tests/k6/lobby-signalr-fanout.js
//
// ENVIRONMENT VARIABLES:
//   BASE_URL    — HTTP base URL for SignalR negotiate (e.g. http://localhost:5100)
//   WS_URL      — WebSocket base URL (defaults to BASE_URL with http→ws)
//   JWT         — Short-lived bearer JWT from LOCAL stack (never a production token)
//   LOBBY_ID    — UUID of a lobby the test player is a member of in the local stack
//   CLIENTS     — Number of concurrent SignalR VUs (default: 50)
//   SESSION_S   — Seconds per VU session (default: 10; must be < SignalR handshake timeout ~15 s)
//
// k6 LICENSING NOTE:
//   k6 (grafana/k6) is AGPLv3. Used here as an EXTERNAL Docker process only — never as a
//   NuGet/library dependency, never shipped in any GameKit package. See tests/k6/README.md.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { negotiateSignalR, RECORD_SEP } from './helpers/signalr.js';
import { WebSocket } from 'k6/websockets';

// ----- custom metrics -----

/**
 * Per-VU delivery round-trip time (ms): wall-clock time from when SendChatMessageAsync was
 * sent to when ReceiveChatMessage is received. Captured inside the `message` callback.
 *
 * p50/p95/p99 printed automatically in the k6 run summary.
 */
const deliveryTime = new Trend('signalr_delivery_time_ms', true);

/** Count of VUs that received the ReceiveChatMessage echo. */
const deliveriesReceived = new Counter('signalr_deliveries_received');

/** Count of VUs that did NOT receive the echo (failed handshake/join/timeout). */
const deliveriesMissed = new Counter('signalr_deliveries_missed');

// ----- environment config -----

const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5100';
const WS_URL = (__ENV.WS_URL || BASE_URL).replace(/^http/, 'ws');
const JWT = __ENV.JWT;
const LOBBY_ID = __ENV.LOBBY_ID;
const NUM_CLIENTS = parseInt(__ENV.CLIENTS || '50', 10);

// SESSION_S: duration each VU keeps the WS connection alive.
// Must be LESS than the ASP.NET Core SignalR handshake timeout (~15 s).
// The handshake frame is sent when the k6 'open' callback fires — which happens at function
// exit, not during sleep. The server must receive it before its timeout fires.
// Recommended: 10 s (= spike default). Reduce only if delivery latency is consistently < 5 s.
const SESSION_S = parseInt(__ENV.SESSION_S || '10', 10);

// ----- k6 options -----

// SESSION_DURATION_S: how long each VU runs its WS session, including sleep + callback sweep.
// Add 5 s headroom beyond SESSION_S for the callback sweep (open → message → close) that
// fires at function exit. The scenario duration governs how long VUs run; the sleep inside
// the VU function governs how long the WS connection is held open.
const SCENARIO_DURATION_S = SESSION_S + 5;

export const options = {
  scenarios: {
    // Each VU runs ONE SignalR session (connect → join → broadcast → receive).
    // `per-vu-iterations` ensures exactly one iteration per VU — no load distribution.
    // Scenario duration = SESSION_S (WS hold time) + 5s (callback sweep headroom).
    fanout: {
      executor: 'per-vu-iterations',
      vus: NUM_CLIENTS,
      iterations: 1,
      maxDuration: `${SCENARIO_DURATION_S}s`,
    },
  },

  thresholds: {
    // At least 50% of VUs must receive the broadcast echo.
    'signalr_deliveries_received': [`count>${Math.floor(NUM_CLIENTS * 0.5)}`],
    // p80 delivery time under 5 s (local single-replica stack; loosen for multi-replica / WAN).
    'signalr_delivery_time_ms': ['p(80)<5000'],
  },
};

// ----- default VU function -----

/**
 * Single-sleep SignalR fan-out protocol cycle.
 *
 * k6/websockets event model (grafana/k6 v2.0.0):
 *   - Callbacks (open, message, close) fire AFTER the VU function returns, NOT during sleep().
 *   - sleep(SESSION_S) keeps the WS connection open for SESSION_S seconds of real I/O.
 *   - All protocol logic and metric recording must live inside the event callbacks.
 *   - State checked AFTER sleep() (before function returns) will NOT reflect callback updates.
 *
 * Protocol state machine (all inside message callback):
 *   ack received    → send JoinLobbyAsync
 *   join result     → record broadcastSentAtMs, send SendChatMessageAsync
 *   ReceiveChatMessage → compute deliveryMs, ws.close()
 *   close           → record metrics/checks
 */
export default function () {
  if (!JWT) {
    console.warn('JWT env var not set — connections will 401.');
    deliveriesMissed.add(1);
    return;
  }
  if (!LOBBY_ID) {
    console.warn('LOBBY_ID env var not set — JoinLobbyAsync will fail.');
    deliveriesMissed.add(1);
    return;
  }

  // HTTP negotiate — this runs synchronously before sleep.
  let negotiateBody;
  try {
    negotiateBody = negotiateSignalR(BASE_URL, JWT, '/hubs/lobby');
  } catch (e) {
    console.error(`VU ${__VU}: negotiate failed: ${e}`);
    deliveriesMissed.add(1);
    return;
  }

  const connectionToken = negotiateBody.connectionToken || negotiateBody.connectionId;

  // Per-VU protocol state — mutated inside callbacks (which fire at function exit).
  const state = {
    handshakeDone: false,
    handshakeError: null,
    joinDone: false,
    joinError: null,
    broadcastSentAtMs: 0,
    deliveryMs: null,
  };

  // Open WebSocket. Connection is established immediately (before sleep).
  const url = `${WS_URL}/hubs/lobby?id=${connectionToken}&access_token=${JWT}`;
  const ws = new WebSocket(url);

  // open: fires at function exit (after sleep). Send handshake immediately.
  ws.addEventListener('open', function () {
    ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RECORD_SEP);
  });

  // message: fires at function exit for each server frame received during sleep.
  // Implements the full SignalR state machine in sequence.
  ws.addEventListener('message', function (event) {
    const parts = event.data.split(RECORD_SEP);
    for (let i = 0; i < parts.length; i++) {
      const part = parts[i];
      if (!part || part.length === 0) continue;

      let frame;
      try { frame = JSON.parse(part); } catch (_) { continue; }

      // Ping — pong.
      if (frame.type === 6) {
        ws.send(JSON.stringify({ type: 6 }) + RECORD_SEP);
        continue;
      }

      // Handshake ack: server sends {} (no type field, no error field).
      if (!state.handshakeDone) {
        if (frame.error) {
          // Handshake canceled (e.g., SESSION_S too long, exceeded server timeout).
          state.handshakeError = frame.error;
          ws.close();
          return;
        }
        if (typeof frame.type === 'undefined') {
          state.handshakeDone = true;
          // Immediately send JoinLobbyAsync — response will arrive in a subsequent
          // message callback (k6 yields between callbacks for network I/O).
          ws.send(JSON.stringify({
            type: 1,
            target: 'JoinLobbyAsync',
            arguments: [LOBBY_ID],
            invocationId: 'join-1',
          }) + RECORD_SEP);
        }
        continue;
      }

      // JoinLobbyAsync result (type=3).
      if (!state.joinDone && state.joinError === null &&
          frame.type === 3 && frame.invocationId === 'join-1') {
        if (frame.error) {
          state.joinError = frame.error;
          ws.close();
          return;
        }
        state.joinDone = true;
        // Record send timestamp and fire broadcast immediately.
        state.broadcastSentAtMs = Date.now();
        ws.send(JSON.stringify({
          type: 1,
          target: 'SendChatMessageAsync',
          arguments: [LOBBY_ID, `perf-fanout-probe-vu${__VU}`],
          invocationId: 'bcast-1',
        }) + RECORD_SEP);
        continue;
      }

      // ReceiveChatMessageAsync push (type=1) — the broadcast echo.
      // Note: ASP.NET Core SignalR sends the full method name including the 'Async' suffix
      // as the wire target (e.g. "ReceiveChatMessageAsync", NOT "ReceiveChatMessage").
      if (state.joinDone && state.broadcastSentAtMs > 0 && state.deliveryMs === null &&
          frame.type === 1 &&
          typeof frame.target === 'string' &&
          frame.target.toLowerCase() === 'receivechatmessageasync') {
        state.deliveryMs = Date.now() - state.broadcastSentAtMs;
        ws.close();
        return;
      }
    }
  });

  ws.addEventListener('error', function (event) {
    console.error(`VU ${__VU}: WebSocket error: ${JSON.stringify(event)}`);
  });

  // close: fires last, after all message callbacks. Safe place for metrics + checks.
  ws.addEventListener('close', function () {
    if (state.deliveryMs !== null) {
      deliveryTime.add(state.deliveryMs);
      deliveriesReceived.add(1);
    } else {
      deliveriesMissed.add(1);
    }

    check(state, {
      'handshake completed': (s) => s.handshakeDone,
      'join succeeded': (s) => s.joinDone,
      'delivery received': (s) => s.deliveryMs !== null,
    });

    if (state.handshakeError) {
      console.warn(`VU ${__VU}: handshake error — SESSION_S (${SESSION_S}) may exceed server timeout: ${state.handshakeError}`);
    }
    if (state.joinError) {
      console.warn(`VU ${__VU}: JoinLobbyAsync error: ${state.joinError}`);
    }
  });

  // Keep the WS connection alive for SESSION_S seconds. During this time, the real
  // network I/O runs: the WS connects, the server sends frames, the k6 event loop
  // buffers them. When this function RETURNS after sleep(), k6 fires all queued
  // callbacks: open → sends handshake → micro-yield → message (ack) → sends JoinLobby
  // → micro-yield → message (join result) → sends broadcast → micro-yield →
  // message (ReceiveChatMessage) → ws.close() → close (records metrics).
  //
  // DO NOT call ws.close() here — that would close the WS BEFORE callbacks run,
  // preventing the handshake, join, and broadcast sequence from completing.
  // ws.close() is called inside the message callback when ReceiveChatMessage arrives
  // (or on error paths). The scenario maxDuration provides a hard upper bound.
  sleep(SESSION_S);
}
