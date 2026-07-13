# Phase 21 — Demo Functional Overhaul

**Date:** 2026-06-26 · **Branch/worktree:** `phase-21-demo` (unmerged) · **Status:** ✅ implemented + verified end-to-end in a real two-player headless browser.

## Why
User feedback: the demo was not functional enough — "press Play and I just go straight into single-player; there's no matchmaking because it's impossible to get into a queue; when both players in a party ready up it just doesn't do anything; there's no after-match rating visual or clear ladder/leaderboard." This overhaul makes the demo a real, navigable game.

## Root-cause bugs found (not just UX)
1. **/ws/game WebSocket was anonymous.** Browsers can't set an `Authorization` header on a WS upgrade, and GameKit.Lobby's query-token extraction (`LobbyJwtBearerPostConfigure`) only covers `/hubs/lobby`. So the run-summary WS authenticated as nobody → the run was never recorded → **1v1 matches never completed.** (Solo "worked" only because it skips the WS.)
2. **No solo ranked queue existed in the UI.** The only multiplayer path was party create/join; there was no "find a match" button, and a solo lobby (MaxMembers=1) can't reach ReadyChecking via the public API.
3. **Parties lingered after matches.** A player stays in their (solo or friend) party after a match; the next match hits `PartyConflictException` ("already a member of an active party") inside the lobby ready-check → the friend match silently failed.
4. **Ratings updated on the production 60s cadence** (TickInterval 60s + 1-min RatingPeriod) → an after-match rating delta could take ~2 minutes.
5. **A 1v1 hung forever if one player didn't finish** (completion required both run-summaries).

## Changes
### Backend (host — `samples/Platformer3D/Program.cs`, all D-15 host-only)
- **`/ws/game` query-token auth**: a chained `JwtBearer.OnMessageReceived` that reads `?access_token=` for `/ws/game` (mirrors the lobby's `/hubs/lobby` extraction, preserves it). *This is the keystone fix that makes matches complete.*
- **`POST /demo/quick-match`** — ranked solo matchmaking: dissolve lingering parties → create a solo party → enqueue on the platformer ladder default pool → return the ticket id.
- **`GET /demo/my-rank`** — caller rating + W/L (menu header + rating delta baseline).
- **`GET /demo/session-result/{id}`** — participants' result + time once completed (results screen).
- **`POST /demo/leave-party`** + shared `DissolveActivePartiesAsync` — clears lingering parties (dissolves via each party's real `OwnerPlayerId` so member-only players are cleaned too).
- **Responsive rankings**: `Ticker.TickIntervalSeconds = 3`, ladder `RatingPeriod = 5s` → rating + leaderboard update within seconds (demo-only).

### GameServer (`Platformer3D.GameServer/PlatformerGameServerService.cs`)
- **DNF timeout**: track connected players per match; once the first finishes, give the opponent 30s, else complete with the finisher as winner (opponent = DNF loss). DB fallback to find the opponent; idempotent vs. a late real completion.

### Client (`wwwroot/`)
- **`index.html`**: screen state machine — sign-in → menu → searching → game → results → leaderboard.
- **Main menu** with rating/W-L header and four explicit modes: Ranked Match, Play with a Friend, Solo Practice, Leaderboard.
- **`app.js`** (new flow controller): ranked quick-match (searching screen + live timer + cancel), friend party (create/join by code, Ready gated to ReadyChecking, unranked), solo, **results screen** (your time vs opponent, win/loss, rating before→after delta or "unranked"), and a **leaderboard** that highlights you.
- **`game.js`**: refactored engine + auth — clean dispose (no leaked listeners/loops), live HUD timer, the `?access_token=` WS URL, an `onFinish` callback, and a `window.__debugFinish` test hook (synthesises a valid run). Removed the auto-solo bootstrap.
- Deleted `lobby.js` (folded into `app.js`).

## Verification
- **Two-player headless-browser e2e** (`tests/e2e-browser.mjs`, Playwright + chrome-headless-shell): sign-in→menu (not auto-solo); ranked match pairs two players → results show Victory/Defeat + `1000 → 1162 (+162)` rating delta; leaderboard renders; friend party → both ready → unranked match, no rating change. **All assertions pass.**
- Regression: `GameKit.Platformer3D.Tests` 48 + `GameKit.Platformer3D.Integration.Tests` 27 green (no GameKit.* package code changed this round).

## Commits
- `f5eff66` host backend (endpoints + WS auth + rating timing + party cleanup)
- `a9fd779` GameServer DNF timeout
- `b6caca2` menu-driven client rewrite

## Follow-up fixes (commit `113461a`)
- **Leaderboard showed 0 ratings** (user-reported): `ILeaderboardService` hides a rating (null) while a player is in placement (RANK-16), and the default `PlacementMatchCount` is 10 — so every player in a short demo showed 0 while `/demo/my-rank` + the results screen showed the real rating. Rewrote `/demo/leaderboard` to read `PlayerRank` directly (raw ratings, joined to `players` for the display name); set the demo `PlacementMatchCount = 1`. Note: placement is decremented in `PendingRatingUpdatesAdapter` only once the rank row exists, and the row is created during the drain — so the first match never decrements (exit is ~count+1 matches); the raw leaderboard sidesteps this entirely.
- **`/demo/quick-match` 500 on simultaneous clicks**: two players clicking Ranked at once collided on the parties tables under SERIALIZABLE (Postgres 40001) past the package retry → the client got no ticket → never matched. Wrapped dissolve→create→enqueue in a transient-retry loop (dissolve-first each attempt so retries never orphan a party).
- e2e strengthened to assert non-zero leaderboard ratings; ran twice back-to-back, both ALL PASSED (`ratings: [1162, 838]`).

## Notes
- This round touched only `samples/` (host + GameServer + client) — no GameKit.* package changes.
- Headless verification: snap chromium can't sandbox here, but Playwright's `chrome-headless-shell` + `--no-sandbox` works (`npx playwright install chromium` + `npm i playwright-core`).
- Demo rebuilt with `down -v` (5s rating period reseeded; demo superadmin auto-reseeds). Play at `http://localhost:8080`; two players = two browser **profiles** (guest id is per-localStorage).
