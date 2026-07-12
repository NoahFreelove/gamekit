// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
//
// tests/k6/matchmaking-burst.js
//
// PERF-03: 500-VU matchmaking-burst + auth-throughput + match-formed polling scenario.
//
// PURPOSE:
//   Validates matchmaking under a realistic 500-player surge against the LOCAL GameKit
//   stack. Three sub-scenarios run together:
//     1. `burst`         — 500 VUs ramp up and enqueue via POST /api/mm/queue
//     2. `auth_throughput` — 100 VUs sustain POST /auth/login/{provider} to measure BCrypt
//                           throughput under load (BCrypt wf=12 ~202ms per verify)
//     3. `match_poll`    — After the burst, a smaller cohort of VUs polls
//                          GET /api/mm/queue/{ticketId}/status and records the
//                          enqueue-to-matched duration in a Trend metric with a p99 threshold
//
// IMPORTANT: this scenario targets the LOCAL Testcontainers / SpikeHost stack ONLY.
//   NEVER run this against a production or staging environment.
//   See tests/k6/README.md for full invocation instructions.
//
// INVOCATION (Linux, --network host):
//   docker run --rm -i --network host \
//     -e BASE_URL=http://localhost:5100 \
//     -e JWT=<player_jwt> \
//     -e LADDER_ID=<ladder_uuid> \
//     grafana/k6:latest run - < tests/k6/matchmaking-burst.js
//
// On macOS/Windows Docker Desktop, replace 'localhost' with 'host.docker.internal'.
//
// ENVIRONMENT VARIABLES (all required at runtime; nothing hardcoded):
//   BASE_URL   — HTTP base URL of the local stack (e.g. http://localhost:5100)
//   JWT        — Short-lived bearer JWT issued by the local stack (never a production token)
//   LADDER_ID  — UUID of a seeded ladder in the local stack
//   LOGIN_PROVIDER — Provider name for the auth-throughput scenario (default: "password")
//   LOGIN_USERNAME — Username for auth-throughput login requests
//   LOGIN_PASSWORD — Password for auth-throughput login requests
//   MATCH_P99_MS   — p99 match-formation threshold in milliseconds (default: 5000)
//                    The matchmaking ticker fires every ~500ms; proposal flow adds latency.
//
// k6 LICENSING NOTE:
//   k6 (grafana/k6) is AGPLv3. It is used here as an EXTERNAL Docker process — never as a
//   library dependency, never linked into any GameKit package or test project. The scenario
//   scripts (.js files) are GameKit repository files licensed under Apache-2.0. The
//   AGPLv3 copyleft applies to the k6 binary distribution, not to test scripts that invoke
//   the binary as a subprocess. See tests/k6/README.md for the full licensing note.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

// ----- custom metrics -----

/** Time from enqueue response to matched status (in milliseconds). */
const matchFormationTime = new Trend('match_formation_time_ms', true);

/**
 * WR-04 fix: count VUs that exit the matchPoll function early (enqueue failed or returned no
 * ticketId).  When all 20 match_poll VUs miss, the match_formation_time_ms Trend has zero data
 * points, and k6 evaluates a zero-sample Trend threshold as PASS — a fully broken matchmaking
 * stack would silently green the run.  This counter gives us a hard gate: if more than 5 of
 * 20 VUs miss, the run fails even if Trend has no samples.
 */
const matchPollEnqueueMiss = new Counter('match_poll_enqueue_miss');

// ----- environment config -----

const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5100';
const JWT = __ENV.JWT;
const LADDER_ID = __ENV.LADDER_ID;
const LOGIN_PROVIDER = __ENV.LOGIN_PROVIDER || 'password';
const LOGIN_USERNAME = __ENV.LOGIN_USERNAME || '';
const LOGIN_PASSWORD = __ENV.LOGIN_PASSWORD || '';
const MATCH_P99_MS = parseInt(__ENV.MATCH_P99_MS || '5000', 10);

// ----- k6 options -----

export const options = {
  scenarios: {
    // Scenario 1: 500-VU enqueue burst
    // Ramp 0→500 VUs over 10s, sustain for 30s, ramp down over 10s.
    // Each VU enqueues once (POST /api/mm/queue), tagged `name:enqueue` for threshold targeting.
    burst: {
      executor: 'ramping-vus',
      stages: [
        { duration: '10s', target: 500 },
        { duration: '30s', target: 500 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '5s',
    },

    // Scenario 2: Auth throughput — sustained login requests measuring BCrypt wf=12 latency.
    // 100 VUs for 30s (offset 5s to let burst ramp stabilise first).
    // Threshold: p99 < 1500ms (BCrypt wf=12 ~202ms mean; 100 VUs over 1 CPU ~1-2 s p99 under load).
    auth_throughput: {
      executor: 'constant-vus',
      vus: 100,
      duration: '30s',
      startTime: '5s',
      exec: 'authThroughput',
    },

    // Scenario 3: Match-formed polling
    // After burst VUs have enqueued, a 20-VU cohort polls their ticket status until matched
    // (or until 15s elapses). Records enqueue→matched latency into `match_formation_time_ms`.
    // Offset 15s so tickets from the burst phase are already in the queue when polling starts.
    match_poll: {
      executor: 'constant-vus',
      vus: 20,
      duration: '30s',
      startTime: '15s',
      exec: 'matchPoll',
    },
  },

  thresholds: {
    // Enqueue p99 must be under 2 seconds (endpoint overhead only; not match-formation time).
    'http_req_duration{name:enqueue}': ['p(99)<2000'],

    // WR-01 fix: auth_throughput scenario p99 threshold — was documented in comments but never
    // enforced.  BCrypt wf=12 ~202ms mean; 100 VUs over 1 CPU → ~1–2 s p99 under load.
    // Without this threshold a BCrypt cost-factor regression would be measured but never fail k6.
    'http_req_duration{name:auth_login}': ['p(99)<1500'],

    // Overall HTTP error rate must stay below 1%.
    // (409 Conflict = already queued is an acceptable outcome per EnqueueOutcome.AlreadyEnqueued
    //  and is NOT counted as a failure by k6's default error rate — only network/5xx errors are.)
    'http_req_failed': ['rate<0.01'],

    // Match-formation p99: ticker at ~500ms + proposal flow → allow up to MATCH_P99_MS.
    [`match_formation_time_ms`]: [`p(99)<${MATCH_P99_MS}`],

    // WR-04 fix: guard against a vacuous Trend pass.  If all match_poll VUs fail to enqueue
    // (e.g. JWT not set, spike host down), match_formation_time_ms has zero data points and
    // k6 evaluates its threshold as PASS.  This counter ensures at most 5 of 20 VUs may
    // take the early-exit path before the run fails.
    'match_poll_enqueue_miss': ['count<5'],
  },
};

// ----- Scenario 1 default: enqueue burst -----

/**
 * Default VU function — executed by the `burst` scenario.
 * Each VU POSTs to /api/mm/queue once per iteration.
 * Accepted outcomes: 200 (queued) or 409 (already queued per MATCH-01 idempotency).
 */
export default function () {
  if (!JWT) {
    console.warn('JWT env var not set — enqueue will 401. Set -e JWT=<token> at k6 invocation.');
  }

  const payload = JSON.stringify({
    ladderId: LADDER_ID,
    poolName: null,
  });

  const res = http.post(`${BASE_URL}/api/mm/queue`, payload, {
    headers: {
      'Authorization': `Bearer ${JWT}`,
      'Content-Type': 'application/json',
    },
    tags: { name: 'enqueue' },
  });

  // 200 = Queued; 409 = AlreadyEnqueued (idempotent — not an error).
  // 403 = cooldown or ban; 400 = bad request. Any 5xx counts as failure via http_req_failed.
  check(res, {
    'enqueue: 200 queued or 409 already-queued': (r) =>
      r.status === 200 || r.status === 409,
  });

  // Brief pause between iterations (avoids hammering rate limiter in burst for same VU).
  sleep(0.1);
}

// ----- Scenario 2: auth throughput -----

/**
 * Auth-throughput VU function — executed by the `auth_throughput` scenario.
 * Posts to POST /auth/login/{provider} and measures BCrypt-under-load throughput.
 * Threshold: p(99) < 1500ms at 100 VUs (BCrypt wf=12 ~202ms mean per BASELINES.md).
 */
export function authThroughput() {
  if (!LOGIN_USERNAME || !LOGIN_PASSWORD) {
    console.warn('LOGIN_USERNAME / LOGIN_PASSWORD not set — auth-throughput requests will 400/401.');
  }

  const payload = JSON.stringify({
    username: LOGIN_USERNAME,
    password: LOGIN_PASSWORD,
  });

  const res = http.post(
    `${BASE_URL}/auth/login/${LOGIN_PROVIDER}`,
    payload,
    {
      headers: { 'Content-Type': 'application/json' },
      tags: { name: 'auth_login' },
    }
  );

  // 200 = token issued; 401 = wrong credentials (non-fatal for throughput measurement).
  // Only 5xx or network failures are real failures.
  check(res, {
    'auth: 200 or 401': (r) => r.status === 200 || r.status === 401 || r.status === 400,
  });

  sleep(0.05);
}

// ----- Scenario 3: match-formed polling -----

/**
 * Match-poll VU function — executed by the `match_poll` scenario.
 *
 * Each VU:
 *  1. Enqueues a ticket (POST /api/mm/queue) and records the enqueue timestamp.
 *  2. Polls GET /api/mm/queue/{ticketId}/status in a tight loop (max 15s / 1s interval).
 *  3. Records the elapsed enqueue-to-matched time into the `match_formation_time_ms` Trend.
 *
 * The matchmaking ticker fires every ~500ms. A typical 2-player match at 500 VUs should
 * form within 1-3 ticker cycles (~1-2s). The p99 threshold accounts for queue depth + proposal
 * flow (accept step) under burst conditions.
 *
 * DESIGN NOTE (k6/websockets event model): because this scenario uses plain HTTP polling
 * (not WebSocket), the standard sleep() pattern works correctly here. No event-model gotcha.
 */
export function matchPoll() {
  if (!JWT) {
    console.warn('JWT env var not set — match-poll will 401.');
    matchPollEnqueueMiss.add(1); // WR-04: count early-exit so Trend cannot vacuously pass
    sleep(1);
    return;
  }

  const enqueuePayload = JSON.stringify({
    ladderId: LADDER_ID,
    poolName: null,
  });

  // Step 1: enqueue and obtain ticketId.
  const enqueueRes = http.post(`${BASE_URL}/api/mm/queue`, enqueuePayload, {
    headers: {
      'Authorization': `Bearer ${JWT}`,
      'Content-Type': 'application/json',
    },
    tags: { name: 'enqueue_poll' },
  });

  if (enqueueRes.status !== 200 && enqueueRes.status !== 409) {
    // Could not enqueue — skip polling for this iteration.
    matchPollEnqueueMiss.add(1); // WR-04: count early-exit so Trend cannot vacuously pass
    sleep(1);
    return;
  }

  let ticketId = null;
  if (enqueueRes.status === 200) {
    try {
      const body = JSON.parse(enqueueRes.body);
      ticketId = body.ticketId;
    } catch (_) {
      matchPollEnqueueMiss.add(1); // WR-04: count early-exit so Trend cannot vacuously pass
      sleep(1);
      return;
    }
  }

  if (!ticketId) {
    // 409 means already enqueued but we don't have the ticket id — skip polling.
    matchPollEnqueueMiss.add(1); // WR-04: count early-exit so Trend cannot vacuously pass
    sleep(1);
    return;
  }

  // Step 2: poll status until matched (non-queued) or timeout.
  const enqueueEpochMs = Date.now();
  const maxWaitMs = MATCH_P99_MS + 5000; // generous outer deadline (p99 threshold + 5s buffer)
  let matched = false;

  for (let attempt = 0; attempt < 30; attempt++) {
    sleep(1); // 1s between polls — ticker fires every ~500ms so 1s poll gives 2 ticker cycles

    const statusRes = http.get(
      `${BASE_URL}/api/mm/queue/${ticketId}/status`,
      {
        headers: { 'Authorization': `Bearer ${JWT}` },
        tags: { name: 'ticket_status' },
      }
    );

    if (statusRes.status !== 200) {
      // Non-200 (404 if ticket expired, 401 if token expired) — stop polling.
      break;
    }

    let statusBody;
    try {
      statusBody = JSON.parse(statusRes.body);
    } catch (_) {
      break;
    }

    // Match formed when status transitions away from "queued".
    // Possible terminal values: "matched", "proposal", "accepted", "cancelled", "expired".
    const status = statusBody.status || statusBody.Status || '';
    if (status !== 'queued' && status !== '' && status !== 'Queued') {
      const elapsed = Date.now() - enqueueEpochMs;
      matchFormationTime.add(elapsed);
      matched = true;
      break;
    }

    // Guard outer deadline.
    if (Date.now() - enqueueEpochMs > maxWaitMs) {
      break;
    }
  }

  // If not matched within the polling window, record the maximum wait time as a signal
  // (the p99 threshold will catch this as a performance regression).
  if (!matched) {
    matchFormationTime.add(maxWaitMs);
  }
}
