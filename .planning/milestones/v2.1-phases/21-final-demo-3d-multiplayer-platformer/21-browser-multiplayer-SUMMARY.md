---
phase: "21"
plan: "browser-multiplayer"
subsystem: "samples/Platformer3D"
tags: ["demo", "browser-client", "signalr", "lobby", "matchmaking", "leaderboard"]
dependency_graph:
  requires: ["21-01", "21-02", "21-03", "21-04", "21-05", "21-06"]
  provides: ["browser-lobby-flow", "signalr-vendored", "demo-endpoints", "bug-a-fix"]
  affects: ["samples/Platformer3D/wwwroot/**", "samples/Platformer3D/Program.cs"]
tech_stack:
  added:
    - "@microsoft/signalr@10.0.0 (IIFE browser bundle, vendored MIT)"
  patterns:
    - "window.signalR global from IIFE bundle + ES module lobby.js consumer"
    - "REST poll for ticket discovery after SignalR InGame broadcast"
    - "Static-method EF LINQ (EntityFrameworkQueryableExtensions.FirstOrDefaultAsync)"
key_files:
  created:
    - "samples/Platformer3D/wwwroot/js/signalr.min.js"
    - "samples/Platformer3D/wwwroot/js/lobby.js"
    - "tests/e2e-lobby-protocol.mjs"
  modified:
    - "samples/Platformer3D/wwwroot/js/game.js"
    - "samples/Platformer3D/wwwroot/index.html"
    - "samples/Platformer3D/Program.cs"
    - "REUSE.toml"
    - "THIRD-PARTY-NOTICES.md"
decisions:
  - "Used IIFE browser bundle (signalr.min.js) not ESM build; avoids vendoring 20+ individual ESM files"
  - "window.initLobbyControls pattern: lobby.js sets window property, game.js bootstrap calls it"
  - "LobbyState compared as integer (1=ReadyChecking, 3=InGame) per default STJ numeric enum serialization"
  - "Proposal accept step (POST /api/mm/proposal/{id}/accept) included in poll loop per spec Phase F"
  - "npm pack + tar extraction used to download signalr without leaving node_modules in repo"
metrics:
  duration: "8 minutes"
  completed_date: "2026-06-23"
  tasks_completed: 6
  files_modified: 7
status: complete
---

# Phase 21 Browser Multiplayer: Full Demo Flow SUMMARY

**One-liner:** Guest sign-in → SignalR lobby → ready-check → InGame → ticket poll → proposal accept → game WS → leaderboard; plus Bug A fix and no-CDN SignalR vendor.

## Tasks Completed

| Task | Description | Commit | Files |
|------|-------------|--------|-------|
| 1 - Bug A Fix | Hide #auth-screen (not #auth-panel) after guest sign-in | 0d00d2f | game.js |
| 2 - Vendor SignalR | Extract signalr.min.js from @microsoft/signalr@10.0.0 npm pack; REUSE+notices | 5edfb61 | signalr.min.js, REUSE.toml, THIRD-PARTY-NOTICES.md |
| 3 - Demo Endpoints | /demo/my-ticket + /demo/leaderboard in Program.cs | e72c2e7 | Program.cs |
| 4+5 - lobby.js + index.html | Full lobby/party/matchmaking browser wiring + UI additions | 4caaefc | lobby.js, index.html |
| 6 - E2E Protocol Test | Node script verifying REST+SignalR negotiate, 19/19 assertions pass | aca2e99 | tests/e2e-lobby-protocol.mjs |

## Implementation Details

### Bug A Fix (game.js)

The `DOMContentLoaded` handler was calling `authPanel.classList.add('hidden')` where `authPanel = getElementById('auth-panel')`. The `#auth-panel` is a child `<section>` inside `#auth-screen` — hiding the child left the screen's background visible, blocking the game canvas below the fold.

Fix: target `document.getElementById('auth-screen')` directly.

Additional wiring: `window.initLobbyControls` is called after sign-in, and `window.startGame = initGame` is exposed so lobby.js can trigger the 3D game start when a match is formed.

### SignalR Vendoring

- Downloaded via `npm pack @microsoft/signalr@10.0.0` + `tar xzf` in a temp dir (no node_modules in repo)
- Used IIFE browser bundle (`dist/browser/signalr.min.js`) which exposes `window.signalR` global
- Avoids vendoring all 20+ individual ESM files — single 47KB artifact
- Loaded via `<script src="/js/signalr.min.js">` before the ES module scripts
- REUSE.toml override annotation: `MIT AND GPL-3.0-or-later` (same pattern as three.js)
- THIRD-PARTY-NOTICES.md section with verbatim MIT license text

### Demo Endpoints in Program.cs

**`GET /demo/my-ticket`** (RequireAuthorization):
- Extracts player ID from JWT `sub` claim
- EF LINQ join: `MatchmakingTicket JOIN PartyMember WHERE Status IN (Queued=0, Proposed=1) AND PlayerId == caller`
- Returns `{ ticketId: guid }` or 404 `{ error: "no_active_ticket" }`
- Uses static-method form of `EntityFrameworkQueryableExtensions.FirstOrDefaultAsync` (matches existing demo endpoint pattern)

**`GET /demo/leaderboard`** (anonymous):
- Resolves platformer ladder by name via EF
- Calls `ILeaderboardService.TopAsync(ladderId, 20, null, ct)`
- Returns `IReadOnlyList<LeaderboardRowDto>` as JSON array

### lobby.js Browser Flow

Full ES module wiring the lobby panel UI to the SignalR hub and matchmaking REST:

1. `initLobbyControls(getToken)` — called by game.js after sign-in; enables Create/Join buttons
2. **Create Party**: `POST /api/lobbies` → show lobbyId as invite code → `connectHub()`
3. **Join**: `POST /api/lobbies/{id}/join` → `connectHub()`
4. **connectHub()**: `new signalR.HubConnectionBuilder().withUrl('/hubs/lobby', { accessTokenFactory })` → `startAsync()` → `invoke('JoinLobbyAsync', lobbyId)` → enable Ready button
5. **ReceiveStateUpdateAsync handler**: State===1 → enable Ready; State===3 → `startMatchPoll()`
6. **startMatchPoll()**: poll `/demo/my-ticket` (retry 500ms×20) → poll `/api/mm/queue/{id}/status` → on `proposed` POST accept → on `matched` call `window.startGame(sessionId)`
7. **Post-match**: `fetchAndShowLeaderboard()` → render top-10 in `#leaderboard-area`

### index.html Additions

- "Create Party" button (`#btn-create-lobby`) with invite code display (`#lobby-code-display`)
- Leaderboard panel (`#leaderboard-area`) — hidden until post-match
- `<script src="/js/signalr.min.js">` (IIFE, loads before modules)
- `<script type="module" src="/js/lobby.js">` added before `game.js`

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build Platformer3D.csproj -p:NuGetAudit=false` | 0 errors, 0 warnings |
| `dotnet test GameKit.Platformer3D.Tests -p:NuGetAudit=false` | 45/45 passed |
| `dotnet test GameKit.Platformer3D.Integration.Tests -p:NuGetAudit=false` | 26/26 passed |
| No-CDN gate (`! grep -rEiq 'cdn\|unpkg\|...'`) | PASS |
| `/health/ready` | HTTP 200 |
| `GET /js/signalr.min.js` | HTTP 200 (47KB) |
| `GET /demo/leaderboard` | HTTP 200 (JSON array) |
| `GET /demo/my-ticket` (guest JWT, no ticket) | HTTP 404 `{"error":"no_active_ticket"}` (correct) |
| `POST /hubs/lobby/negotiate` (guest×2) | HTTP 200 with connectionToken |
| e2e-lobby-protocol.mjs | 19/19 assertions passed |

## E2E Protocol Test Coverage

`tests/e2e-lobby-protocol.mjs` covers:
- Guest sign-in ×2
- Ladder ID resolution
- Create lobby (200, returns lobbyId)
- SignalR negotiate ×2 (200, returns connectionToken — proves auth works)
- Join lobby (200, state=ReadyChecking)
- `/demo/my-ticket` before matchmaking (404, correct behavior)
- `/demo/leaderboard` (200, JSON array)
- `/health/ready` (200)

**Not auto-verified** (covered by integration tests):
- Full WS SignalR flow: `JoinLobbyAsync` invoke → `MarkReadyAsync` invoke → `ReceiveStateUpdateAsync` InGame broadcast → ticket creation timing → proposal/accept → session creation. All covered by `LobbyToMatchTests` and `EndToEndSmokeTests` (26 tests passing).

## D-15 Compliance

All changes confined to:
- `samples/Platformer3D/wwwroot/js/` — new/modified client files
- `samples/Platformer3D/wwwroot/index.html` — UI additions
- `samples/Platformer3D/Program.cs` — two demo-only endpoints
- `REUSE.toml` — SPDX annotation for vendored signalr.min.js
- `THIRD-PARTY-NOTICES.md` — MIT license text entry
- `tests/e2e-lobby-protocol.mjs` — verification script

No `src/GameKit.*` package changes. No migrations. No TicTacToeDuel changes.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Build error: `FirstOrDefaultAsync` extension method ambiguity**
- **Found during:** Task 3 build verification
- **Issue:** `.FirstOrDefaultAsync(ct)` resolves to `System.Linq.AsyncEnumerable` on .NET 10 when called on `IQueryable<T>`, not `Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions`. Used the static method form (matching the existing `/demo/ladder-id` endpoint pattern in Program.cs).
- **Fix:** Switched to `EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(query, ct)` static invocation.
- **Files modified:** `samples/Platformer3D/Program.cs`
- **Commit:** e72c2e7

**2. [Orchestrator correction applied] SignalR version 10.0.0 (not 10.0.8)**
- The research doc assumed 10.0.8 on npm; the orchestrator confirmed only 10.0.0 is available.
- Used `@microsoft/signalr@10.0.0` throughout (all references updated).

**3. [Design adjustment] IIFE bundle over ESM build**
- Research doc recommended the ESM build (`dist/esm/signalr.js`) renamed to `signalr.module.js`.
- The orchestrator preferred the IIFE bundle (`dist/browser/signalr.min.js`) as simpler (no bare imports, single file).
- lobby.js accesses `window.signalR` global set by the IIFE, consistent with loading `<script src="...">` before the module.

## Known Stubs

None — all data flows from live server endpoints. The leaderboard returns an empty array initially (no matches played yet); this is correct server behavior, not a stub.

## Threat Flags

None. New endpoints:
- `/demo/my-ticket` is `RequireAuthorization()` — player JWT required, returns only the caller's own ticket
- `/demo/leaderboard` is read-only public data, no PII exposed (displayName from player profile, may be null)

## Self-Check: PASSED

- `samples/Platformer3D/wwwroot/js/signalr.min.js` — exists (47,729 bytes)
- `samples/Platformer3D/wwwroot/js/lobby.js` — exists (7,802 bytes)
- `tests/e2e-lobby-protocol.mjs` — exists, 19/19 assertions pass against live stack
- Commits 0d00d2f, 5edfb61, e72c2e7, 4caaefc, aca2e99 — all present in `git log`
- Stack running: `platformer3d-app-1` healthy on port 8080
