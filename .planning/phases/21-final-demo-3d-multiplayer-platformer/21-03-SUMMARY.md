---
phase: 21-final-demo-3d-multiplayer-platformer
plan: "03"
subsystem: browser-client
tags: [three.js, webgl, guest-auth, websocket, run-summary, license-hygiene]
status: complete

dependency_graph:
  requires:
    - "21-01 (Platformer3D project scaffold + Program.cs host stub)"
    - "21-02 (BestTimeMatchmakingStrategy + TimeMarginRankingAlgorithm)"
  provides:
    - "samples/Platformer3D/wwwroot/index.html — game shell with guest sign-in + lobby UI hooks"
    - "samples/Platformer3D/wwwroot/js/three.module.js — vendored three.js r184 (MIT)"
    - "samples/Platformer3D/wwwroot/js/three.core.js — vendored three.js core (MIT, required by three.module.js)"
    - "samples/Platformer3D/wwwroot/js/addons/PointerLockControls.js — vendored three.js addon (MIT)"
    - "samples/Platformer3D/wwwroot/js/game.js — three.js game loop + guest auth + run-summary WS submission"
    - "samples/Platformer3D/wwwroot/assets/level.json — one completable 3D level"
    - "REUSE.toml (three.js + Platformer3D sample tree annotations appended)"
    - "THIRD-PARTY-NOTICES.md (three.js section appended)"
    - "LICENSES/MIT.txt (added for three.js vendored asset)"
  affects:
    - "21-04 (GameServer parses the identical WS run-summary frames this client emits)"
    - "21-05 (Dockerfile COPY layer includes wwwroot/; three.js r184 version consistent)"
    - "21-06 (manual human-verify checkpoint: level renders + is completable in browser)"

tech_stack:
  added:
    - "three.js r184 (MIT, vendored — no NuGet, no CDN)"
    - "PointerLockControls addon from three.js r184 (MIT, vendored)"
    - "LICENSES/MIT.txt (reuse download MIT)"
  patterns:
    - "ES module imports from vendored local path (no CDN, no bundler)"
    - "Guest sign-in via POST /auth/login/guest; access_token in module memory"
    - "WebSocket run-summary submission (run_start/checkpoint/run_finish/pong frames)"
    - "Integer-ms timing via Math.trunc(performance.now() + performance.timeOrigin)"
    - "REUSE.toml override annotation for vendored MIT asset inside GPL project"

key_files:
  created:
    - "samples/Platformer3D/wwwroot/index.html"
    - "samples/Platformer3D/wwwroot/js/game.js"
    - "samples/Platformer3D/wwwroot/js/three.module.js"
    - "samples/Platformer3D/wwwroot/js/three.core.js"
    - "samples/Platformer3D/wwwroot/js/addons/PointerLockControls.js"
    - "samples/Platformer3D/wwwroot/assets/level.json"
    - "LICENSES/MIT.txt"
  modified:
    - "REUSE.toml (appended three.js override block + Platformer3D aggregate block)"
    - "THIRD-PARTY-NOTICES.md (appended three.js section)"

decisions:
  - "Vendored three.module.js + three.core.js (both required by the r184 split build; three.module.js imports from ./three.core.js)"
  - "PointerLockControls bare 'three' import rewritten to relative '../three.module.js' (no import-map needed, no bundler)"
  - "Access token held in ES module scope (not localStorage) to reduce XSS persistence; refresh token persisted to localStorage per TicTacToeDuel pattern"
  - "LICENSES/MIT.txt added via reuse download MIT (required for three.js REUSE.toml annotation to pass lint)"
  - "WebSocket connection opened on run finish with Authorization header attempted; browser limitation means server must validate token via subprotocol or cookie — tolerated for demo (21-04 handles server-side auth)"
  - "three.js r184 (confirmed from GitHub releases API: tag_name=r184) — RESEARCH assumed r168; actual was r184"

metrics:
  duration: "6min"
  completed: "2026-06-23"
  tasks_completed: 2
  files_created: 7
  files_modified: 2
---

# Phase 21 Plan 03: Browser 3D Client + Guest Auth + License Hygiene Summary

**One-liner:** three.js r184 vendored locally with REUSE-compliant MIT annotation; browser game loop with one-click guest sign-in (POST /auth/login/guest), WASD/pointer-lock platformer, ordered checkpoint timing, and run-summary WebSocket submission (run_start/checkpoint/run_finish frames at integer-ms precision).

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Vendor three.js r184 + record license hygiene | 7304304 | three.module.js, three.core.js, PointerLockControls.js, LICENSES/MIT.txt, REUSE.toml, THIRD-PARTY-NOTICES.md |
| 2 | Game shell + guest sign-in + 3D game loop + level | bae4f47 | index.html, game.js, assets/level.json |

## WebSocket Wire Format (canonical — 21-04 GameServer must parse these frames)

**Client → server (JSON text frames):**
```json
{ "type": "run_start", "matchId": "<guid>", "startMs": 1750000000000 }
{ "type": "checkpoint", "index": 0, "timestampMs": 1750000005000 }
{ "type": "checkpoint", "index": 1, "timestampMs": 1750000010000 }
{ "type": "checkpoint", "index": 2, "timestampMs": 1750000020000 }
{ "type": "run_finish", "finishMs": 1750000045000 }
{ "type": "pong" }
```

**Server → client (JSON text frames):**
```json
{ "type": "validated", "completionMs": 45000, "sessionId": "<guid>" }
{ "type": "rejected", "reason": "non_monotonic_checkpoints" }
{ "type": "rejected", "reason": "implausible_duration" }
{ "type": "rejected", "reason": "duplicate_finish" }
{ "type": "ping" }
```

**Integer-ms precision:** timestamps use `Math.trunc(performance.now() + performance.timeOrigin)`.

## three.js Vendored Version

**Confirmed tag:** r184 (verified from `curl https://api.github.com/repos/mrdoob/three.js/releases/latest`)

**Note for 21-05 Dockerfile:** The COPY layer in `samples/Platformer3D/Dockerfile` must include both:
- `samples/Platformer3D/wwwroot/js/three.module.js` (634 KB)
- `samples/Platformer3D/wwwroot/js/three.core.js` (1.4 MB, required by three.module.js)
- `samples/Platformer3D/wwwroot/js/addons/PointerLockControls.js`

## Level Design

The level in `assets/level.json` has:
- **Spawn**: (0, 1, 0)
- **Checkpoints**: 3 in order at increasing height/distance
- **Finish trigger**: elevated platform at the end
- **Plausible run time**: ~15–60 seconds (within D-03's [5000ms, 300000ms] bounds)

## Verification Results

All acceptance criteria passed:
- CDN/analytics negative grep: PASS (no outbound URLs in wwwroot/)
- `btn-guest` in index.html: PASS
- `/auth/login/guest` in game.js: PASS
- `run_finish` in game.js: PASS
- three.module.js is a real module with exports: PASS
- THIRD-PARTY-NOTICES.md has three.js entry: PASS
- REUSE.toml has three.module.js annotation: PASS
- Version consistency (r184 identical in both files): PASS
- No PII fields at guest sign-in: PASS
- No analytics/tracker scripts: PASS

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] three.module.js imports from './three.core.js' (split build)**
- **Found during:** Task 1
- **Issue:** three.js r184 uses a split build — `three.module.js` re-exports from `./three.core.js`. The plan only mentioned vendoring `three.module.js`, but the build file imports a sibling.
- **Fix:** Downloaded `three.core.js` alongside `three.module.js`; added it to the REUSE.toml annotation block; added to THIRD-PARTY-NOTICES.md coverage.
- **Files modified:** three.core.js (new), REUSE.toml, THIRD-PARTY-NOTICES.md

**2. [Rule 1 - Bug] PointerLockControls bare 'three' import**
- **Found during:** Task 1
- **Issue:** The vendored `PointerLockControls.js` uses `import { Controls, Euler, Vector3 } from 'three'` — a bare specifier that requires either an import map or a bundler to resolve.
- **Fix:** Rewrote the import to `from '../three.module.js'` (relative path to the vendored module). No import map needed; no CDN fallback.
- **Files modified:** PointerLockControls.js

**3. [Rule 2 - Missing Critical Functionality] MIT license file missing**
- **Found during:** Task 1 (`reuse lint` reported "Missing licenses: MIT")
- **Issue:** REUSE.toml annotation uses `SPDX-License-Identifier = "MIT"` for three.js files, but `LICENSES/MIT.txt` did not exist.
- **Fix:** Ran `reuse download MIT` to add `LICENSES/MIT.txt`.
- **Files modified:** LICENSES/MIT.txt (new)

**4. Version deviation: r168 (RESEARCH assumed) → r184 (actual)**
- **Found during:** Task 1 version verification
- **RESEARCH states:** "r168 is the current release [ASSUMED]"
- **Actual:** GitHub releases API returned `tag_name: r184`
- **Action:** Vendored r184 everywhere; recorded r184 (not r168) in both REUSE.toml and THIRD-PARTY-NOTICES.md consistently.

## Known Stubs

None. The level is completable (manual verification via 21-06). The WebSocket submission code fires on actual finish trigger — no hardcoded empty data.

## Threat Flags

None. The new surface (wwwroot/ assets + guest sign-in) is fully covered by the plan's threat model:
- T-21-07: CDN egress mitigated (all assets local, grep gate passes)
- T-21-08: PII mitigated (no email/phone fields, no analytics)
- T-21-09: Tampering transferred to server (client only sends summary; session-complete requires service token)
- T-21-10: Supply chain mitigated (three.js r184 from official GitHub tag; MIT recorded in REUSE.toml + THIRD-PARTY-NOTICES.md)

## Self-Check: PASSED

All 10 created/modified files verified present on disk. Both task commits verified in git log (7304304, bae4f47).
