---
phase: "21"
plan: "ux-fixes"
subsystem: "Platformer3D browser client"
tags: [demo, three.js, browser, ux, solo-play, replay, full-screen]
dependency_graph:
  requires: []
  provides: [full-screen-solo-play, timed-finish, replay, multiplayer-reachable]
  affects: [samples/Platformer3D/wwwroot]
tech_stack:
  added: []
  patterns: [fixed-position-viewport-overlay, solo-practice-mode, in-game-multiplayer-panel]
key_files:
  created: []
  modified:
    - samples/Platformer3D/wwwroot/index.html
    - samples/Platformer3D/wwwroot/js/game.js
    - samples/Platformer3D/wwwroot/js/lobby.js
decisions:
  - "Solo practice as default: Play as Guest immediately calls initGame(null) rather than waiting for party/match"
  - "In-game multiplayer panel: toggle button + slide-out panel in #game-section with -mp suffixed IDs; lobby.js inGameMode param wires the correct elements"
  - "Replay stays solo: after any finished run (match or solo), replay always uses matchId=null to avoid re-submitting to a consumed match"
  - "canvas.click unconditional: remove _runFinished gate; resetRun() re-arms the overlay before player can lock, preventing a frozen locked state"
metrics:
  duration: "~25 minutes"
  completed: "2026-06-23"
  tasks_completed: 1
  files_modified: 3
status: complete
---

# Phase 21 UX Fixes: Full-Screen Solo Play, Timer, Replay, Multiplayer Reachable

One atomic commit fixes three confirmed UX bugs in the Platformer3D browser demo without touching any `src/GameKit.*` library code (D-15 honored).

## What Was Delivered

### Bug 1 Fixed: Full-Screen, No Scroll

`#game-section` changed from `position: relative; height: 100vh` (normal flow — scrollable) to `position: fixed; inset: 0; z-index: 100` (viewport overlay — no scroll possible). `body.game-active` class added to suppress `overflow` on the root. `#auth-screen` is hidden via `.hidden` class when the game section activates.

### Bug 2 Fixed: Immediate Solo Path + Time Displayed

`btnGuest` click handler now calls `initGame(null)` immediately after sign-in (solo practice run), rather than waiting for the lobby/party flow. The `submitRunSummary` null-matchId branch now shows the completion time: `"Run complete! Your time: X.XXs"` in both the status bar and the overlay hint. `showFinishOverlay()` helper added to set the overlay text and make the replay button visible.

### Bug 3 Fixed: Replay After Finish

- `canvas.click` listener no longer gates on `_runFinished` — it always calls `controls.lock()`
- `resetRun()` function: clears `_runFinished`, `runStarted`, `_runStartMs`, `_checkpoints`; resets checkpoint marker colors/opacity; respawns player at `level.spawn`; zeroes velocity; resets `nextCpIdx`; calls `resetOverlay()` (hides replay button, restores "Click to capture mouse" text)
- R key (`KeyR`) triggers `resetRun()` when `_runFinished && !controls.isLocked`
- "Play Again (R)" button in overlay triggers `resetRun()` (with `stopPropagation` to avoid re-locking immediately)
- After a match run, replay is always solo practice (`_currentMatchId` set to null in `resetRun()`)

### Multiplayer Party Flow Preserved and Reachable

- "Multiplayer / Party" toggle button (top-right HUD corner, `z-index: 110`) shows/hides `#multiplayer-panel`
- `#multiplayer-panel` contains Create Party / invite-code Join / Ready controls with `-mp` suffixed IDs
- `lobby.js` `initLobbyControls(token, inGameMode=true)` wires to these in-game elements
- When a match forms, `window.startGame(sessionId)` calls `initGame(sessionId)` which re-runs the level as a competitive timed run and submits the WS run-summary
- Leaderboard (`window.showLeaderboard`) shown after `validated` WS message
- Direct UUID bypass (`#match-id-input`) still works

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build Platformer3D.csproj -p:NuGetAudit=false` | Build succeeded, 0 errors, 0 warnings |
| `docker compose down -v && up -d --build` | Stack rebuilt and running |
| `curl /health/ready` | 200 |
| `curl /js/game.js` | 200 |
| `curl /js/lobby.js` | 200 |
| `curl /js/signalr.min.js` | 200 |
| `node tests/e2e-lobby-protocol.mjs` | 19/19 passed |
| No-CDN grep | PASS (no CDN references in wwwroot) |
| `#game-section` uses `position: fixed` | PASS |
| Solo finish emits "Your time: X.XXs" | PASS |
| `_runFinished` cleared in `resetRun()` | PASS |
| R key + replay button present | PASS |
| `canvas.click` not gated by `_runFinished` | PASS |

## Deviations from Plan

None — plan executed exactly as specified. All three bugs fixed, multiplayer flow preserved, D-15 boundary honored.

## Known Stubs

None. The solo finish time is wired to the real `performance.now()` elapsed computation already present in the finish detection block.

## Threat Flags

None. No new network endpoints, auth paths, or schema changes introduced. Client-side JS only.

## Self-Check: PASSED

- `/home/noah/Desktop/projects/gamekit/.claude/worktrees/phase-21-demo/samples/Platformer3D/wwwroot/index.html` — FOUND
- `/home/noah/Desktop/projects/gamekit/.claude/worktrees/phase-21-demo/samples/Platformer3D/wwwroot/js/game.js` — FOUND
- `/home/noah/Desktop/projects/gamekit/.claude/worktrees/phase-21-demo/samples/Platformer3D/wwwroot/js/lobby.js` — FOUND
- Commit `60a1f1d` — exists on `phase-21-demo` branch
