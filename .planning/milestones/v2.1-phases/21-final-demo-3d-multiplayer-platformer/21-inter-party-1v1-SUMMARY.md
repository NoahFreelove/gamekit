# Phase 21 — Inter-Party 1v1 (full-party self-match)

**Date:** 2026-06-24 · **Branch/worktree:** `phase-21-demo` (unmerged) · **Status:** ✅ implemented + automated-verified; pending user 2-tab browser confirmation.

## Goal
Two friends who queue together as one party (Create Party → invite → both Ready) must play a **1v1 against each other** — the console-style "inter-party" match. Previously both browsers hung forever on "Ready sent! Waiting for all players…".

## Root cause
A 2-member party enqueues **one** matchmaking ticket whose `members` array holds **both** players. The matcher only ever paired a candidate ticket against **another** ticket, and the ticker skipped any pool with `< 2` tickets — so a lone party ticket was never even offered to the strategy. Even if it had been, `TeamAssignmentService` is *party-cohesive* (all members of one party → same team), which would put both friends on team 0 instead of opposing each other.

## Decision: Option B (minimal matchmaking self-match) — not Option C (lobby-direct session)
Option B reuses the **entire** proven `atomic-claim → proposal → accept → session → publish` pipeline (the atomic-claim Lua and accept/complete Lua are already generic over `n ≥ 1` tickets) and needs **zero client changes** — the browser already polls a ticket through `queued → proposed → matched(sessionId)`. Option C (lobby creates the session directly) would have required a new Core session-create service, a Lobby branch, and a new client notification channel. Option B's blast radius is smaller and keeps the demo's matchmaking story real.

Package changes were explicitly authorized by the user for this (overrides D-15 / SPEC "no GameKit.* changes").

## Changes (3 surgical edits + tests)
1. **`src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs`** (`ProcessPoolAsync`) — relax the three single-candidate gates (`entries<2`, `candidates<2`, `poolScratch==0`) so a lone candidate is offered to `IMatchmakingStrategy.Match` with an empty pool. The strategy contract already documents the pool "may be empty"; the default `EloRange` strategy returns null on an empty pool, so this is **behaviour-preserving** for every existing consumer — only a strategy that opts in to self-matching forms a match. (Mechanism.)
2. **`src/GameKit.Matchmaking/Services/TeamAssignmentService.cs`** (`AssignTeams`) — when the input is a single party with > 1 member (a full party filling the whole roster on its own), split its members round-robin across teams 0/1 instead of party-cohesively. Only fires for the self-match path; a normal multi-party match (≥ 2 parties) is untouched. (Mechanism.)
3. **`samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs`** — add `MatchPlayerCount = 2`. A party with ≥ `MatchPlayerCount` members self-matches into a 1v1 immediately (`BuildSelfMatchResult`, members on opposing teams), regardless of pool. The pool scan gains an **equal-size guard** so a lone solo never pairs with a full party (would overflow into a 1v2). (Game policy — the package provides the mechanism, the strategy decides when to use it.)

The strategy's `TeamAssignments` are (as before) dropped on the wire; `TeamAssignmentService` is the authority for the final split at session-creation time.

## Verification (all green)
- **Unit:** `GameKit.Matchmaking.Tests` 117 (incl. new lone-party team-split tests); `GameKit.Platformer3D.Tests` 48 (incl. new self-match + roster-fit-guard tests).
- **Integration:** `GameKit.Matchmaking.Integration.Tests` 84 (regression check — shared ticker change under the default `EloRange` strategy, no regression); `GameKit.Platformer3D.Integration.Tests` 27, including the new
  **`LobbyToMatchTests.InterParty_TwoMemberParty_SelfMatchesIntoOneVsOne`**: one 2-member lobby readies → a **single** party ticket → self-match → both members land in the **same** `game_session` as participants on **opposing teams** {0,1}.
- **Protocol:** `node tests/e2e-lobby-protocol.mjs` 19/19 against the rebuilt stack.
- Build with `-p:NuGetAudit=false` (pre-existing transitive MessagePack NU1903 in this pre-Phase-18 worktree).

## Commits
- `c9cf0b2` feat(21): matchmaking self-match mechanism for a full party (package)
- `de4f9e8` feat(21): demo strategy self-matches a full 2-member party (samples)
- `f22e0cf` test(21): cover inter-party 1v1 self-match (unit + e2e integration)

## How to 2-tab test
1. Stack is rebuilt + healthy at `http://localhost:8080` (`docker compose -f samples/Platformer3D/docker-compose.yml up -d --build`).
2. Open two **different** browser profiles (normal + Incognito) — guest identity is per-localStorage device id, so two tabs in the same profile are the SAME player.
3. Tab A: switch to Multiplayer/Party → **Create Party** (note the invite code). Tab B: **Join by invite code**.
4. Both click **Ready**. They should now drop into the same 3D run as opponents; faster integer-ms time wins → leaderboard updates.
5. Hard-refresh (Ctrl+Shift+R) after any client change (cache).

## Follow-up: inter-party matches are UNRANKED (no-elo) — commit `75d8077`
An inter-party 1v1 is an elo-farming vector ("party up, friend AFKs, you win → free elo"). Fix chosen (user picked the targeted option over a full casual queue): **auto-unrank inter-party matches.** Reuses the codebase's existing *"unranked = null `LadderId`"* model — no schema change, no new rating logic:
- `ProposalService.CreateSessionAsync` flags a match as inter-party when two **opposing** participants came from the **same** party/ticket (only the self-match path splits one ticket across teams). Such a session is created with a **null `LadderId`**. `SessionCompleteService` then builds null-ladder snapshots and `PendingRatingUpdatesAdapter` skips the `PlayerRank` read **and** the rating update → fully unranked (no elo, no W/L). Normal stranger matchmaking keeps each party wholly on one team (party cohesion) → never trips → stays ranked.
- Tests: inter-party session asserts `LadderId IS NULL`; two-solo session asserts `LadderId == platformer` (still ranked). MM integration 84 green (normal matches still rated); P3D integration 27 green.
- **Not done (deferred):** a player-choosable Casual queue (Ranked/Casual toggle + matchmaking `Rated` flag + UI). The same null-LadderId mechanism would back it; revisit if a general "for fun" mode is wanted.

## Notes / non-blocking
- **Concurrent ready on one lobby:** the integration test marks the two members ready *sequentially* (same lobby) so the all-ready gate fires once. A truly simultaneous double-MarkReady on one lobby can surface a server-side error; this is a pre-existing Lobby concern (the `SerializationFailureRetry` path), out of scope here, and not hit by the real human/browser cadence.
- **Secondary finding (RD=0):** enqueued members snapshot `RatingDeviation: 0` (fresh guests have no `PlayerRank` row). It does **not** block either path — the self-match keys off member count, not rating, and the two-solo path pairs on equal ratings. Left as-is.
