// SPDX-License-Identifier: GPL-3.0-or-later
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
//   Measures the time from broadcast-send to per-client message-receipt across N SignalR
//   clients connected to /hubs/lobby. When clients are distributed across server replicas
//   the messages cross the Redis backplane — this scenario exercises that fan-out path.
//
//   Delivery-time distribution is recorded into the `signalr_delivery_time_ms` Trend metric.
//   The k6 end-of-run summary prints p50, p95, p99 for this metric.
//
// SCENARIO MODEL (single-iteration, shared setup):
//   1. All N VUs (CLIENTS) negotiate + connect to /hubs/lobby and perform the SignalR
//      handshake. They then join the shared lobby via JoinLobbyAsync.
//   2. VU #0 (the broadcaster) triggers ONE broadcast via SendChatMessageAsync.
//   3. All N VUs record the time from broadcast-send to ReceiveChatMessage frame receipt
//      into the `signalr_delivery_time_ms` Trend.
//   4. All VUs disconnect cleanly.
//
// DESIGN NOTE — k6/websockets event dispatch timing model (19-02 deviation #1):
//   In k6/websockets, ALL event callbacks (open, message, close) fire AFTER sleep() completes,
//   NOT concurrently during sleep. Therefore all protocol state machine logic (handshake
//   detection, invoke calls, delivery time recording) lives inside event callbacks. sleep()
//   is used only as a session deadline — it determines when the WebSocket closes.
//
// IMPORTANT: exercises the REAL Redis backplane (fan-out crosses the backplane when clients
//   connect to different replicas). Run against the LOCAL SpikeHost or Testcontainers stack
//   with a real Redis instance. NEVER run against production or CI-vs-production.
//   See tests/k6/README.md for full invocation instructions.
//
// INVOCATION (Linux, --network host):
//   docker run --rm -i --network host \
//     -e BASE_URL=http://localhost:5100 \
//     -e WS_URL=ws://localhost:5100 \
//     -e JWT=<player_jwt> \
//     -e LOBBY_ID=<lobby_uuid> \
//     -e CLIENTS=50 \
//     grafana/k6:latest run - < tests/k6/lobby-signalr-fanout.js
//
// On macOS/Windows Docker Desktop: replace 'localhost' with 'host.docker.internal'.
//
// ENVIRONMENT VARIABLES:
//   BASE_URL  — HTTP base URL for SignalR negotiate (e.g. http://localhost:5100)
//   WS_URL    — WebSocket base URL (e.g. ws://localhost:5100)
//   JWT       — Short-lived bearer JWT from LOCAL stack (never a production token)
//   LOBBY_ID  — UUID of a lobby the test player is a member of in the local stack
//   CLIENTS   — Number of concurrent SignalR clients (default: 50)
//   SESSION_S — WebSocket session duration in seconds (default: 20)
//               Must be long enough for all clients to connect, join, receive broadcast, and record.
//
// k6 LICENSING NOTE:
//   k6 (grafana/k6) is AGPLv3. Used here as an EXTERNAL Docker process only — never as a
//   NuGet/library dependency, never shipped in any GameKit package. See tests/k6/README.md.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { negotiateSignalR, connectSignalR, invoke, RECORD_SEP } from './helpers/signalr.js';

// ----- custom metrics -----

/**
 * Per-client delivery latency (ms): time from broadcast-send to ReceiveChatMessage receipt.
 * The k6 run summary prints p50/p95/p99 for this Trend automatically.
 */
const deliveryTime = new Trend('signalr_delivery_time_ms', true);

/** Count of clients that successfully received the broadcast. */
const deliveriesReceived = new Counter('signalr_deliveries_received');

/** Count of clients that did NOT receive the broadcast within the session window. */
const deliveriesMissed = new Counter('signalr_deliveries_missed');

// ----- environment config -----

const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5100';
const WS_URL = (__ENV.WS_URL || BASE_URL).replace(/^http/, 'ws');
const JWT = __ENV.JWT;
const LOBBY_ID = __ENV.LOBBY_ID;
const NUM_CLIENTS = parseInt(__ENV.CLIENTS || '50', 10);
const SESSION_S = parseInt(__ENV.SESSION_S || '20', 10);

// ----- k6 options -----

export const options = {
  scenarios: {
    // N VUs each represent one SignalR client. All VUs start simultaneously (preAllocatedVUs).
    // The `shared-iterations` executor distributes work so each VU handles one client slot.
    fanout: {
      executor: 'shared-iterations',
      vus: NUM_CLIENTS,
      iterations: NUM_CLIENTS,
      maxDuration: `${SESSION_S + 30}s`,
    },
  },

  thresholds: {
    // At least 80% of clients must receive the broadcast within SESSION_S * 1000ms.
    [`signalr_delivery_time_ms`]: [`p(80)<${SESSION_S * 1000}`],
    // Less than 20% of clients miss the broadcast (network timeouts on local stack are tolerable).
    'signalr_deliveries_missed': ['count<' + Math.ceil(NUM_CLIENTS * 0.2)],
  },
};

// ----- broadcast send-time registry -----
// Because k6 VUs share a module scope but run in isolated goroutines, we use a plain object
// to hold the broadcast send-time. VU #0 writes it; all other VUs read it.
// NOTE: In k6, __VU is the VU index (1-based). We elect VU index 1 as the broadcaster.
const sharedState = {
  broadcastSentAtMs: 0,
};

// ----- default VU function -----

/**
 * Each VU:
 *  1. Negotiates a SignalR connection to /hubs/lobby.
 *  2. Opens a WebSocket and completes the JSON protocol handshake.
 *  3. Joins the shared LOBBY_ID via JoinLobbyAsync.
 *  4. VU #1 additionally sends a SendChatMessageAsync broadcast and records the send-time.
 *  5. All VUs wait for a ReceiveChatMessage frame and record delivery latency.
 *
 * All protocol logic lives inside event callbacks to handle the k6/websockets
 * events-fire-after-sleep timing model (19-02 deviation #1).
 */
export default function () {
  if (!JWT) {
    console.warn('JWT env var not set — WebSocket connections will 401.');
    sleep(SESSION_S);
    return;
  }
  if (!LOBBY_ID) {
    console.warn('LOBBY_ID env var not set — JoinLobbyAsync will fail.');
    sleep(SESSION_S);
    return;
  }

  // Step 1: HTTP negotiate.
  let negotiateBody;
  try {
    negotiateBody = negotiateSignalR(BASE_URL, JWT, '/hubs/lobby');
  } catch (e) {
    console.error(`VU ${__VU}: negotiate failed: ${e}`);
    sleep(SESSION_S);
    return;
  }

  const connectionToken = negotiateBody.connectionToken || negotiateBody.connectionId;

  // Per-VU state (local — not shared across VUs).
  let handshakeDone = false;
  let joinDone = false;
  let broadcastSent = false; // only VU #1 sets this
  let deliveryRecorded = false;
  const vuIdx = __VU;

  // Step 2–5: WebSocket session (all logic inside callbacks).
  const ws = connectSignalR(
    WS_URL,
    JWT,
    connectionToken,
    function onMessage(frame, socket) {
      // Handshake ack is the first frame (empty object, no `type` field).
      if (!handshakeDone && typeof frame.type === 'undefined') {
        // Handshake ack received.
        handshakeDone = true;

        // Step 3: Join the shared lobby.
        invoke(socket, 'JoinLobbyAsync', [LOBBY_ID], `join-${vuIdx}`);
        return;
      }

      // JoinLobbyAsync completion result (type=3).
      if (frame.type === 3 && !joinDone) {
        const invId = frame.invocationId || '';
        if (invId.startsWith('join-')) {
          if (frame.error) {
            console.error(`VU ${vuIdx}: JoinLobbyAsync error: ${frame.error}`);
            socket.close();
            return;
          }
          joinDone = true;

          // Step 4: VU #1 sends the broadcast after all VUs have (approximately) joined.
          // We use a short sleep-equivalent by recording the send immediately —
          // the shared-iterations model means all VUs start near-simultaneously,
          // and VU #1 is guaranteed to reach this point after its own handshake + join.
          if (vuIdx === 1 && !broadcastSent) {
            broadcastSent = true;
            // Record send-time BEFORE sending (avoids double-counting any callback latency).
            sharedState.broadcastSentAtMs = Date.now();
            invoke(socket, 'SendChatMessageAsync', [LOBBY_ID, 'perf-fanout-probe'], `bcast-${vuIdx}`);
          }
        }
        return;
      }

      // ReceiveChatMessage server-push (type=1, target='ReceiveChatMessage').
      // This is the delivery signal — record latency for every VU including the broadcaster.
      if (frame.type === 1 &&
          typeof frame.target === 'string' &&
          frame.target.toLowerCase() === 'receivechatmessage' &&
          !deliveryRecorded) {
        const sentAt = sharedState.broadcastSentAtMs;
        if (sentAt > 0) {
          const latencyMs = Date.now() - sentAt;
          deliveryTime.add(latencyMs);
          deliveriesReceived.add(1);
          deliveryRecorded = true;
          // Close after recording so the VU's session ends cleanly.
          socket.close();
        }
        return;
      }

      // Broadcast result for the sender (type=3, invocationId=bcast-1): no delivery record needed.
      // (The sender records delivery via the ReceiveChatMessage push like all other clients.)
    },
    '/hubs/lobby'
  );

  // sleep() = session deadline. All callbacks fire AFTER sleep() returns (k6/websockets model).
  // SESSION_S must be long enough for connect + join + broadcast + receive on the local stack.
  sleep(SESSION_S);

  // Post-session: record miss if delivery was not recorded.
  if (!deliveryRecorded) {
    deliveriesMissed.add(1);
  }
}
