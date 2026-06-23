// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// tests/k6/spike-signalr.js
//
// PERF-04 Part A — SignalR handshake GO/NO-GO spike.
//
// Purpose: Proves that stock grafana/k6 v2.0.0 (with the stable `k6/websockets` module)
// can complete the full SignalR JSON protocol handshake against the real Lobby hub at
// /hubs/lobby. This is the Open Q2 gate: if the spike passes, the full fan-out scenario
// (plan 19-05) proceeds with stock k6. If it fails, an xk6 extension build is required.
//
// Six-step GO/NO-GO sequence (per 19-RESEARCH.md §k6 SignalR Spike):
//   1. HTTP POST to /hubs/lobby/negotiate?negotiateVersion=1&access_token=<jwt>
//   2. Open WebSocket to /hubs/lobby?id=<connectionToken>&access_token=<jwt>
//   3. Send {"protocol":"json","version":1}\x1e handshake frame
//   4. Assert {} handshake-ack frame is received
//   5. Invoke JoinLobbyAsync with LOBBY_ID from __ENV
//   6. Assert a response (or well-formed SignalR error frame) arrives within 2 s
//
// Run against a LOCAL stack only — NEVER against production.
//
// Usage (Linux, --network host):
//   docker run --rm -i --network host \
//     -e BASE_URL=http://localhost:5000 \
//     -e WS_URL=ws://localhost:5000 \
//     -e JWT=<short_lived_player_jwt> \
//     -e LOBBY_ID=<lobby_guid> \
//     grafana/k6:latest run - < tests/k6/spike-signalr.js
//
// Usage (macOS/Windows, host.docker.internal):
//   docker run --rm -i \
//     -e BASE_URL=http://host.docker.internal:5000 \
//     -e WS_URL=ws://host.docker.internal:5000 \
//     -e JWT=<short_lived_player_jwt> \
//     -e LOBBY_ID=<lobby_guid> \
//     grafana/k6:latest run - < tests/k6/spike-signalr.js
//
// All of BASE_URL, WS_URL, JWT, LOBBY_ID are read from __ENV — nothing is hardcoded.
// Never commit a real JWT. Tokens must be short-lived and minted against the LOCAL stack.

import { check, sleep } from 'k6';
import { negotiateSignalR, connectSignalR, invoke } from './helpers/signalr.js';

// ---------------------------------------------------------------------------
// k6 scenario options: single VU, single iteration — this is a spike, not a load test.
// ---------------------------------------------------------------------------
export const options = {
  vus: 1,
  iterations: 1,
  thresholds: {
    // The spike must complete without errors — checks must all pass.
    checks: ['rate==1.0'],
  },
};

// ---------------------------------------------------------------------------
// Environment variables — ALL required; none hardcoded.
// ---------------------------------------------------------------------------
const BASE_URL = __ENV.BASE_URL || '';   // e.g. "http://localhost:5000"
const WS_URL   = __ENV.WS_URL   || '';  // e.g. "ws://localhost:5000"
const JWT      = __ENV.JWT      || '';  // Short-lived player JWT from LOCAL stack
const LOBBY_ID = __ENV.LOBBY_ID || '';  // Valid lobby GUID from LOCAL stack

// ---------------------------------------------------------------------------
// Spike default function
// ---------------------------------------------------------------------------
export default function () {
  // Guard: fail early with a clear message if env vars are missing.
  if (!BASE_URL || !WS_URL || !JWT || !LOBBY_ID) {
    throw new Error(
      'Missing required environment variables. Set BASE_URL, WS_URL, JWT, and LOBBY_ID via -e at invocation. ' +
      'Example: docker run --rm -i --network host -e BASE_URL=http://localhost:5000 -e WS_URL=ws://localhost:5000 ' +
      '-e JWT=<jwt> -e LOBBY_ID=<guid> grafana/k6:latest run - < tests/k6/spike-signalr.js'
    );
  }

  // --------------------------------------------------------------------------
  // Step 1: Negotiate — POST /hubs/lobby/negotiate?negotiateVersion=1
  // --------------------------------------------------------------------------
  let negotiateBody;
  try {
    negotiateBody = negotiateSignalR(BASE_URL, JWT, '/hubs/lobby');
  } catch (e) {
    check(false, { 'step1: negotiate succeeded (HTTP 200)': () => false });
    console.error(`Step 1 (negotiate) failed: ${e}`);
    return;
  }

  const connectionToken = negotiateBody.connectionToken || negotiateBody.connectionId;
  check(negotiateBody, {
    'step1: negotiate returned connectionToken': (b) =>
      !!(b.connectionToken || b.connectionId),
  });

  console.log(`Step 1 OK — connectionToken=${connectionToken.substring(0, 8)}...`);

  // --------------------------------------------------------------------------
  // Steps 2–6: Open WebSocket, handshake, invoke, assert response.
  // --------------------------------------------------------------------------
  let handshakeAckReceived = false;
  let invocationResponseReceived = false;
  let invocationError = null;

  // collectFrames accumulates all frames received; we check state at the end.
  const ws = connectSignalR(WS_URL, JWT, connectionToken, function (frame, _ws) {
    // connectSignalR dispatches ping internally. Everything else arrives here.

    if (!handshakeAckReceived) {
      // The first non-ping frame should be the handshake ack: `{}` (no `type` field).
      if (frame.type === undefined || Object.keys(frame).length === 0) {
        handshakeAckReceived = true;
        console.log('Step 4 OK — handshake ack {} received');

        // --------------------------------------------------------------------------
        // Step 5: Invoke JoinLobbyAsync with LOBBY_ID
        // --------------------------------------------------------------------------
        console.log(`Step 5 — invoking JoinLobbyAsync("${LOBBY_ID}")`);
        invoke(_ws, 'JoinLobbyAsync', [LOBBY_ID], '1');
        return;
      }
    }

    // Step 6: Response to JoinLobbyAsync invocation.
    // type=3 = completion (result or error).
    // type=1 = incoming hub method call (unexpected here; log and treat as response).
    if (frame.type === 3 && frame.invocationId === '1') {
      invocationResponseReceived = true;
      if (frame.error) {
        invocationError = frame.error;
        console.warn(`Step 6 — JoinLobbyAsync returned a hub error: ${frame.error}`);
      } else {
        console.log(`Step 6 OK — JoinLobbyAsync completion received (result=${JSON.stringify(frame.result)})`);
      }
      _ws.close();
    }
  }, '/hubs/lobby');

  // Wait up to 3 s for all steps to complete (handshake + invocation response).
  // The k6/websockets event loop processes messages while we sleep.
  sleep(3);

  // --------------------------------------------------------------------------
  // Assertions — these determine GO/NO-GO.
  // --------------------------------------------------------------------------

  // Step 3+4: handshake ack
  check(handshakeAckReceived, {
    'step3+4: SignalR JSON handshake ack received ({})': (v) => v === true,
  });

  // Step 6: invocation response (a hub error is acceptable — it means the protocol worked)
  check(invocationResponseReceived || invocationError !== null, {
    'step5+6: JoinLobbyAsync invocation got a response (result or error) within 3s': (v) => v === true,
  });

  // If a hub error was returned, log it — it may indicate a lobby membership failure
  // (expected if LOBBY_ID is not a lobby the test player belongs to) rather than a
  // protocol failure.
  if (invocationError) {
    console.warn(
      `JoinLobbyAsync hub error (may be expected if player is not a lobby member): ${invocationError}`
    );
    // Protocol-level success: the error came back as a well-formed type=3 frame.
    check(true, { 'step6: hub error is a well-formed type=3 SignalR frame (protocol OK)': () => true });
  }

  // Ensure the WebSocket is closed.
  try { ws.close(); } catch (_) { /* already closed */ }
}
