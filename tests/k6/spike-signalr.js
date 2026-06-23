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
//   6. Assert a response (or well-formed SignalR error frame) arrives within 5 s
//
// IMPORTANT k6/websockets behavior note:
//   In k6's WHATWG WebSocket implementation, event callbacks (open, message) fire
//   AFTER the current "sleep" window completes — not concurrently during sleep.
//   All protocol logic (handshake, invoke, assertion) must live INSIDE the callbacks.
//   Use sleep() only as a deadline for the WS session (the session is torn down when
//   the VU iteration ends, approximately deadline = sleep duration after open fires).
//
// Run against a LOCAL stack only — NEVER against production.
//
// Usage (Linux, --network host, volume-mount for local helpers):
//   docker run --rm --network host \
//     -v "$(pwd)/tests/k6:/scripts" \
//     -e BASE_URL=http://localhost:5100 \
//     -e WS_URL=ws://localhost:5100 \
//     -e JWT=<short_lived_player_jwt> \
//     -e LOBBY_ID=<lobby_guid> \
//     grafana/k6:latest run /scripts/spike-signalr.js
//
// Usage (macOS/Windows, host.docker.internal):
//   docker run --rm \
//     -v "$(pwd)/tests/k6:/scripts" \
//     -e BASE_URL=http://host.docker.internal:5100 \
//     -e WS_URL=ws://host.docker.internal:5100 \
//     -e JWT=<short_lived_player_jwt> \
//     -e LOBBY_ID=<lobby_guid> \
//     grafana/k6:latest run /scripts/spike-signalr.js
//
// All of BASE_URL, WS_URL, JWT, LOBBY_ID are read from __ENV — nothing is hardcoded.
// Never commit a real JWT. Tokens must be short-lived and minted against the LOCAL stack.

import { check, sleep } from 'k6';
import { negotiateSignalR, RECORD_SEP } from './helpers/signalr.js';
import { WebSocket } from 'k6/websockets';

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
const BASE_URL = __ENV.BASE_URL || '';   // e.g. "http://localhost:5100"
const WS_URL   = __ENV.WS_URL   || '';  // e.g. "ws://localhost:5100"
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
      'Example: docker run --rm --network host -v "$(pwd)/tests/k6:/scripts" ' +
      '-e BASE_URL=http://localhost:5100 -e WS_URL=ws://localhost:5100 ' +
      '-e JWT=<jwt> -e LOBBY_ID=<guid> grafana/k6:latest run /scripts/spike-signalr.js'
    );
  }

  // --------------------------------------------------------------------------
  // Step 1: Negotiate — POST /hubs/lobby/negotiate?negotiateVersion=1
  // --------------------------------------------------------------------------
  let negotiateBody;
  try {
    negotiateBody = negotiateSignalR(BASE_URL, JWT, '/hubs/lobby');
  } catch (e) {
    check(null, { 'step1: negotiate succeeded (HTTP 200)': () => false });
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
  // State shared across event handlers (modified in-place by callbacks).
  // IMPORTANT: In k6/websockets, events fire after sleep() completes.
  // All protocol logic runs inside the callbacks below.
  // --------------------------------------------------------------------------
  const state = {
    handshakeAckReceived: false,
    invocationResponseReceived: false,
    invocationError: null,
    allChecksRun: false,
  };

  // --------------------------------------------------------------------------
  // Steps 2–6: Open WebSocket, handshake, invoke, assert response.
  // All logic lives in the message handler — invoked after sleep().
  // --------------------------------------------------------------------------
  const url = `${WS_URL}/hubs/lobby?id=${connectionToken}&access_token=${JWT}`;
  const ws = new WebSocket(url);

  ws.addEventListener('open', function () {
    // Step 3: Send the SignalR JSON protocol handshake frame.
    console.log('Step 2+3: WebSocket open — sending SignalR handshake...');
    ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RECORD_SEP);
  });

  ws.addEventListener('message', function (event) {
    // Split on RECORD_SEP — a single WS message can carry multiple SignalR frames.
    const parts = event.data.split(RECORD_SEP);
    for (let i = 0; i < parts.length; i++) {
      const part = parts[i];
      if (!part || part.length === 0) continue;

      let frame;
      try {
        frame = JSON.parse(part);
      } catch (e) {
        console.warn(`Failed to parse SignalR frame: ${part} — ${e}`);
        continue;
      }

      // Ping — respond with pong.
      if (frame.type === 6) {
        ws.send(JSON.stringify({ type: 6 }) + RECORD_SEP);
        continue;
      }

      if (!state.handshakeAckReceived) {
        // Step 4: Handshake ack — server sends {} (empty object, no type field).
        if (frame.type === undefined || Object.keys(frame).length === 0) {
          state.handshakeAckReceived = true;
          console.log('Step 4 OK — handshake ack {} received');

          // Step 5: Invoke JoinLobbyAsync.
          console.log(`Step 5 — invoking JoinLobbyAsync("${LOBBY_ID}")`);
          ws.send(JSON.stringify({
            type: 1,
            target: 'JoinLobbyAsync',
            arguments: [LOBBY_ID],
            invocationId: '1',
          }) + RECORD_SEP);
          continue;
        }
      }

      // Step 6: JoinLobbyAsync invocation response (type=3).
      if (frame.type === 3 && frame.invocationId === '1') {
        state.invocationResponseReceived = true;
        if (frame.error) {
          state.invocationError = frame.error;
          console.warn(`Step 6 — JoinLobbyAsync hub error (protocol OK): ${frame.error}`);
        } else {
          console.log(`Step 6 OK — JoinLobbyAsync result: ${JSON.stringify(frame.result)}`);
        }
        // All steps complete — close the WebSocket.
        ws.close();
        return;
      }
    }
  });

  ws.addEventListener('error', function (event) {
    console.error(`WebSocket error: ${JSON.stringify(event)}`);
  });

  ws.addEventListener('close', function (event) {
    console.log(`WebSocket closed: code=${event.code}`);
    // Perform k6 checks INSIDE the close handler so they run after all events.
    // In k6/websockets, close fires after all message events for that session.
    check(state.handshakeAckReceived, {
      'step3+4: SignalR JSON handshake ack received ({})': (v) => v === true,
    });
    check(state.invocationResponseReceived || state.invocationError !== null, {
      'step5+6: JoinLobbyAsync invocation got a response (result or error)': (v) => v === true,
    });
    if (state.invocationError) {
      // Hub error is protocol-level success (type=3 was received).
      console.warn(`Hub error: "${state.invocationError}" — this is protocol-OK if the player is not a lobby member.`);
      check(true, { 'step6: hub error is well-formed type=3 SignalR frame (protocol OK)': () => true });
    }
    state.allChecksRun = true;
  });

  // Allow up to 10 s for the WebSocket session to complete all six steps.
  // IMPORTANT: In k6/websockets, all event callbacks (open, message, close) fire AFTER
  // sleep() completes — at the end of the VU iteration. The sleep() duration acts as the
  // deadline for how long we wait for the server to respond. All assertions are in the
  // close handler (below), which runs after all message events have been processed.
  sleep(10);

  // No post-sleep checks here — all assertions run in the 'close' event handler above,
  // which fires after sleep() and after all 'message' events are processed.
  // If the close handler did not fire (e.g. the WS never connected), we won't have
  // checks — that is acceptable; the test will exit with 0 checks = threshold failure.
}
