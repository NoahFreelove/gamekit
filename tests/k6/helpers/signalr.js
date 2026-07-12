// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
//
// tests/k6/helpers/signalr.js
//
// Reusable SignalR JSON protocol helpers for k6 load scenarios.
//
// SignalR JSON protocol reference (ASP.NET Core SignalR):
//   - Negotiate: POST /hubs/<name>/negotiate?negotiateVersion=1
//   - WS connect: ws://host/hubs/<name>?id=<connectionToken>&access_token=<jwt>
//   - Handshake send: {"protocol":"json","version":1}\x1e
//   - Handshake ack: {}\x1e
//   - Hub invocation: {"type":1,"target":"Method","arguments":[...],"invocationId":"N"}\x1e
//   - Invocation result: {"type":3,"invocationId":"N","result":...}\x1e
//   - Hub error: {"type":3,"invocationId":"N","error":"..."}\x1e
//   - Ping: {"type":6}\x1e  — server sends keepalives; client responds with same frame
//
// Usage:
//   import { negotiateSignalR, connectSignalR, invoke } from './helpers/signalr.js';
//
// NOTE: Always import from k6/websockets (the WHATWG-compatible stable module, GA in k6 v2.0.0).
//       Earlier preview modules were deprecated in k6 v2.0.0 and must not be used.

import http from 'k6/http';
import { WebSocket } from 'k6/websockets';

/** ASCII 0x1E — terminates every SignalR JSON frame. */
export const RECORD_SEP = String.fromCharCode(0x1e);

/**
 * Negotiate a SignalR connection.
 *
 * Performs an HTTP POST to `<baseUrl>/hubs/lobby/negotiate?negotiateVersion=1`.
 * The `?negotiateVersion=1` query param is REQUIRED for ASP.NET Core SignalR 8+ — omitting it
 * causes a 400 or malformed negotiate response (see 19-RESEARCH.md Pitfall §5).
 *
 * WR-02 fix: the JWT is sent in the `Authorization: Bearer` header rather than as an
 * `access_token` query parameter.  Query parameters are logged verbatim by every layer
 * (server access logs, reverse proxies, k6 summary artifacts) and can expose the token in
 * CI upload artifacts.  The negotiate POST is a plain HTTP request and supports headers;
 * using the header keeps the token out of URLs and logs.
 *
 * NOTE on the WebSocket URL: the `access_token` query parameter IS kept in the WebSocket
 * upgrade URL (see connectSignalR below) because the WebSocket upgrade HTTP request cannot
 * carry custom headers — ASP.NET Core SignalR requires this for WS auth and it is the
 * documented protocol.  This is an unavoidable protocol constraint, not a choice.
 *
 * @param {string} baseUrl - HTTP base URL of the target host (e.g. "http://localhost:5000").
 * @param {string} jwt     - Bearer JWT (short-lived; from LOCAL stack; never production).
 * @param {string} [hubPath="/hubs/lobby"] - SignalR hub path.
 * @returns {{ connectionId: string, connectionToken: string }} Negotiate response body.
 * @throws {Error} When the negotiate HTTP response is not 200.
 */
export function negotiateSignalR(baseUrl, jwt, hubPath) {
  hubPath = hubPath || '/hubs/lobby';
  // negotiateVersion=1 only — JWT goes in the Authorization header, not the URL.
  const url = `${baseUrl}${hubPath}/negotiate?negotiateVersion=1`;
  const res = http.post(url, null, {
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${jwt}`,
    },
  });
  if (res.status !== 200) {
    throw new Error(`SignalR negotiate failed: HTTP ${res.status} from ${url} — body: ${res.body}`);
  }
  const body = JSON.parse(res.body);
  // SignalR .NET 5+ returns connectionToken (may differ from connectionId).
  // Use connectionToken when present; fall back to connectionId for older servers.
  if (!body.connectionToken && !body.connectionId) {
    throw new Error(`SignalR negotiate: missing connectionToken/connectionId in response: ${res.body}`);
  }
  return body;
}

/**
 * Open a WebSocket to the SignalR hub and perform the JSON protocol handshake.
 *
 * The WebSocket URL is constructed as:
 *   `<wsUrl><hubPath>?id=<connectionToken>&access_token=<jwt>`
 *
 * On open, the handshake frame `{"protocol":"json","version":1}\x1e` is sent.
 * Incoming frames are split on RECORD_SEP and dispatched:
 *   - `{}` (empty object) = handshake ack → signals handshake complete
 *   - `{"type":6}` = server ping → automatically pong'd with `{"type":6}\x1e`
 *   - All other frames → forwarded to `onMessage(parsedFrame, ws)`
 *
 * @param {string}   wsUrl          - WebSocket base URL (e.g. "ws://localhost:5000").
 * @param {string}   jwt            - Bearer JWT.
 * @param {string}   connectionToken - `connectionToken` (or `connectionId`) from negotiate.
 * @param {function} onMessage      - Called with (parsedFrame, ws) for every non-handshake,
 *                                    non-ping frame received.
 * @param {string}   [hubPath="/hubs/lobby"] - SignalR hub path.
 * @returns {WebSocket} The opened WebSocket instance.
 */
export function connectSignalR(wsUrl, jwt, connectionToken, onMessage, hubPath) {
  hubPath = hubPath || '/hubs/lobby';
  // The access_token query parameter is required here: the WebSocket upgrade HTTP request
  // cannot carry custom headers (browsers and the WebSocket protocol do not allow it), so
  // ASP.NET Core SignalR accepts the bearer token via this query param for WS connections.
  // This is documented SignalR behaviour and is NOT the same issue as WR-02 (negotiate POST),
  // where headers ARE available and the token no longer appears in the URL.
  const fullUrl = `${wsUrl}${hubPath}?id=${connectionToken}&access_token=${jwt}`;

  const ws = new WebSocket(fullUrl);

  ws.addEventListener('open', function () {
    // Send the SignalR JSON protocol handshake frame.
    ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RECORD_SEP);
  });

  ws.addEventListener('message', function (event) {
    // A single WebSocket message may carry multiple SignalR frames (batched by the server).
    // Split on RECORD_SEP and parse each non-empty frame.
    const raw = event.data;
    const parts = raw.split(RECORD_SEP);
    for (let i = 0; i < parts.length; i++) {
      const part = parts[i];
      if (!part || part.length === 0) continue;

      let frame;
      try {
        frame = JSON.parse(part);
      } catch (e) {
        // Malformed frame — log and skip.
        console.warn(`SignalR: failed to parse frame: ${part} — ${e}`);
        continue;
      }

      // SignalR message type dispatch:
      if (frame.type === 6) {
        // Ping — respond with pong (same frame).
        ws.send(JSON.stringify({ type: 6 }) + RECORD_SEP);
        continue;
      }

      // Handshake ack: the server sends `{}` (an object with no `type` field) as the
      // first frame to confirm protocol negotiation. Forward it to onMessage so the
      // spike can assert it.
      onMessage(frame, ws);
    }
  });

  ws.addEventListener('error', function (event) {
    console.error(`SignalR WebSocket error: ${JSON.stringify(event)}`);
  });

  return ws;
}

/**
 * Send a SignalR hub method invocation frame (type=1).
 *
 * @param {WebSocket} ws           - Open WebSocket returned from `connectSignalR`.
 * @param {string}    target       - Hub method name (e.g. "JoinLobbyAsync").
 * @param {Array}     args         - Positional arguments for the hub method.
 * @param {string|number} invocationId - Unique invocation id string (caller tracks this).
 */
export function invoke(ws, target, args, invocationId) {
  const frame = JSON.stringify({
    type: 1,
    target: target,
    arguments: args,
    invocationId: String(invocationId),
  }) + RECORD_SEP;
  ws.send(frame);
}
