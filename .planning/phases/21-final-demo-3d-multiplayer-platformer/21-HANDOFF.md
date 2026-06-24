# Phase 21 — Continuation Handoff (resume in a fresh session)

**Written:** 2026-06-24 · **Branch/worktree:** `phase-21-demo` at `/home/noah/Desktop/projects/gamekit/.claude/worktrees/phase-21-demo` (NOT merged to master; master has the parallel phases-16→20 autonomous run — keep them isolated, merge later per D-15).

## TL;DR of the open task
Build **real inter-party 1v1**: a 2-member party (invite a friend → both ready) must form a **1v1 match between those two members**. The user has **explicitly authorized changing `GameKit.*` packages** for this — overriding D-15 and the SPEC "no GameKit.* package changes" must-NOT. Direct quote: *"Inter-party matches are quite common on console, so lets do it here, i dont care if it volates this phase's rules."* So D-15/SPEC boundary is intentionally relaxed for this work.

## Why it's needed (root cause — confirmed via live Redis/Postgres)
The browser party flow (Create Party + Join by invite code) makes ONE lobby with 2 members → `LobbyService.TryStartMatchmakingAsync` enqueues **ONE ticket whose `members` array holds BOTH players** (Redis `mm:ticket:…`, `mm:queue:{ladderId}:default` ZCARD=**1**). But `BestTimeMatchmakingStrategy.Match(candidate, pool, now)` only pairs a candidate against **another** ticket in the pool — it never self-pairs a 2-member party into a 1v1. So the single party ticket waits forever → both browsers stuck on "Ready sent! Waiting for all players…". `LobbyToMatchTests` passes only because it uses **two separate solo lobbies** (2 tickets) — a different semantic than "two friends in one party".

The matchmaking ticker IS working (heartbeat alive; acquire-tick-release lease per 500ms; the "matchmaking-leader Degraded: not leader; lock unheld" health check is cosmetic — it samples the lock between ticks). Do NOT chase the leader/lock thing; it's a red herring.

## Next step: research → implement (the research call was about to run; rerun it)
Spawn `gsd-phase-researcher` with this brief (output to `21-RESEARCH-party-1v1.md`):
- **PRIMARY QUESTION:** Can the demo's CUSTOM `IMatchmakingStrategy` (`samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs`, freely editable) produce a "full-party self-match" — when the candidate ticket is a party of match-size (2) members, return a match formed from its OWN two members — that the existing matcher→proposal/accept→session-creation pipeline honors into a real `game_session` with the two members as opposing participants? If YES → samples-only, no package change. If NO → minimal `GameKit.Matchmaking` (or `Lobby`) change.
- Trace the FULL pipeline: strategy result shape (`IMatchmakingStrategy.cs`, `QueuedParty`/`QueuedPartyMember`) → match formation in `MatchmakerTickerService.RunOnceAsync` (atomic-claim Lua; does it assume exactly 2 tickets?) → proposal/accept (`ProposalSweeper`, accept/decline endpoints) → **what creates the `game_session` + participants** (find the creator in `GameKit.Core`/`Matchmaking`) → how the GameServer completes it via `POST /api/sessions/{id}/complete`.
- Evaluate options ranked by smallest surface + lowest merge risk: (A) samples-only via custom strategy and/or a host-side handler that directly creates the session for a full party; (B) minimal `GameKit.Matchmaking` self-match path (prefer internal, avoid public-API/migration churn); (C) `GameKit.Lobby` `TryStartMatchmakingAsync` directly creates the session for a full party. Note per-package migration boundary.
- End state required: two partied browser players land in the SAME `game_session` as opponents; both play + submit run-summaries over `/ws/game/{sessionId}`; faster integer-ms time wins → leaderboard updates.

Then implement the chosen option (executor), rebuild, and have the user 2-tab test.

## Secondary finding to address
Enqueued ticket members show `RatingDeviation: 0` (not the ~350 `DefaultRd` the cold-start RD≥300 logic expects). Fresh guests have no `PlayerRank` row → the matchmaking rating snapshot defaults to 0, so the cold-start "match anyone" bracket won't trigger and proximity uses rating 0. For two equal-rating players it still pairs, but confirm whether this needs fixing for the party path / general matchmaking.

## What already works (Phase 21 executed + verified; do NOT redo)
Single loadable image (multi-stage Dockerfile + one compose, app-port-only, offline tarball); custom `best-time` strategy + fixed-delta `time-margin` algorithm (D-09 amended — `MatchOutcome` has no score field, margin-scaling infeasible without package change, documented in 21-02); embedded authoritative GameServer (in-process service token, WS run-summary, D-03 validation, idempotent session-complete); admin console with auto-seeded demo superadmin; **solo play** full-screen with timer + replay; three.js r184 + @microsoft/signalr@10.0.0 vendored locally (no CDN). Tests: 45 unit + 26 integration green; `node tests/e2e-lobby-protocol.mjs` (REST+SignalR-negotiate) 19/19. ROADMAP marks Phase 21 complete (on this branch).

## Post-execution fixes already committed (this is why master rules were bent before)
- `5b57b04` init-SQL grants `gamekit_app` schema privileges (compose boot was crash-looping).
- `98cddb2`/`f82275d` auto-seed demo superadmin (`root` / `platformer-demo-admin`); demo runs in **Staging** (Production fail-closes on no superadmin).
- `0d00d2f` hide `#auth-screen` (game was below the fold).
- `5edfb61`/`e72c2e7`/`4caaefc`/`aca2e99` browser multiplayer wiring (lobby.js + signalr + `/demo/my-ticket` + `/demo/leaderboard` + e2e test) — **this is the flow that needs the inter-party fix**.
- `60a1f1d` full-screen solo + timer + replay; multiplayer reachable via in-game "Multiplayer / Party" toggle.
- `aa88751` SignalR JS `start()` not `startAsync()`.
- `ba87dbc` GameServer issues a UNIQUE per-start service-token name (fixed-name collided on restart with persisted DB — `ServiceTokenNameAlreadyExistsException`; the token name is just a label, auth validates by hash).

## How to resume (practical)
- **Work in the worktree** `/home/noah/Desktop/projects/gamekit/.claude/worktrees/phase-21-demo`, branch `phase-21-demo`. Do NOT switch to master or merge yet.
- **Build/test gotcha:** always pass `-p:NuGetAudit=false` (pre-existing transitive MessagePack NU1903).
- **Run the stack:** `docker compose -f samples/Platformer3D/docker-compose.yml up -d --build` (no `down -v` needed now — restart-safe). Play at `http://localhost:8080`. Admin: `/admin` → `root` / `platformer-demo-admin`.
- **Tests:** unit `dotnet test tests/GameKit.Platformer3D.Tests/ -p:NuGetAudit=false`; integration `tests/GameKit.Platformer3D.Integration.Tests/`; protocol `node tests/e2e-lobby-protocol.mjs`.
- **Browser testing:** hard-refresh (Ctrl+Shift+R) after any client change (cache). Two players = **two browser profiles** (normal + Incognito) — guest identity is per-localStorage device id, so two same-profile tabs are the SAME player.
- Headless browser verification is NOT available here (snap chromium can't init its sandbox); rely on the e2e/integration tests + the user's browser for visual checks.

## Key files
- Strategy (samples, editable): `samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs`
- Matcher pipeline (package): `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs`, `ProposalSweeper.cs`, `Strategy/IMatchmakingStrategy.cs`, `Strategy/QueuedParty.cs`, `Strategy/QueuedPartyMember.cs`, `Http/**` (enqueue `POST /api/mm/queue`, status `GET /api/mm/queue/{ticketId}/status`)
- Lobby (package): `src/GameKit.Lobby/Services/LobbyService.cs` (`TryStartMatchmakingAsync`), `Hubs/LobbyHub.cs` (`JoinLobbyAsync`/`MarkReadyAsync`), `Hubs/ILobbyClient.cs` (`ReceiveStateUpdateAsync`; LobbyState int over wire: Open=0, ReadyChecking=1, Closed=2, InGame=3)
- Session creation (package): find the `game_session` creator in `src/GameKit.Core` / matchmaking; `src/GameKit.Core/Http/SessionEndpoints.cs`, `ISessionCompleteService`
- Client (samples): `samples/Platformer3D/wwwroot/js/lobby.js`, `game.js`, `index.html`; host demo endpoints in `samples/Platformer3D/Program.cs` (`/demo/my-ticket`, `/demo/leaderboard`, `/demo/ladder-id/platformer`)
- Reference: `21-RESEARCH-browser-multiplayer.md` (the prior wiring spec), `21-SPEC.md` (R9), `LobbyToMatchTests.cs` (the 2-solo-ticket proven path)
