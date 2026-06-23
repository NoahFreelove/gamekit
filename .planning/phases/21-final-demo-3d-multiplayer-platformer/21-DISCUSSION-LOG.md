# Phase 21: Final Demo — 3D Multiplayer Platformer - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-22
**Phase:** 21-final-demo-3d-multiplayer-platformer
**Areas discussed:** Run model & adjudication, Custom matchmaking strategy, Custom ladder / ranking rule, GameServer topology & Docker

> SPEC.md (11 requirements) locked the WHAT. This discussion covered implementation (HOW) decisions only. Several big technical choices were already locked before discussion (carried forward, not re-asked): three.js (WebGL, MIT) engine; async 1v1, no live netcode; the existing `GameKitServiceToken` auth primitive; .NET in-stack game server; single capstone phase.

---

## Run model & adjudication

| Option | Description | Selected |
|--------|-------------|----------|
| Validated summary / WS | Client sends a run-summary (start, checkpoint timestamps, finish at integer-ms) over a WebSocket; server sanity-validates then posts the authoritative completion via service token. Live disconnect detection for the ready-check abort path. | ✓ |
| Run-summary / REST | Client POSTs the run-summary over plain HTTP; simplest, no socket lifecycle; loses live-liveness signal. | |
| Full server re-simulation | Client streams inputs; server re-runs deterministic physics to derive the time. Most cheat-proof, overkill for a one-level async demo. | |

**User's choice:** Validated summary / WS
**Notes:** Reconciles the user's earlier "raw WebSockets" instinct with the locked async model — WS carries a validated run-summary rather than live position sync. Server validation is sanity-level (monotonic checkpoints, plausible bounds), not full re-sim. Result posted via the existing `GameKitServiceToken` → `POST /api/sessions/{id}/complete`, idempotent.

---

## Custom matchmaking strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Recent best-time proximity | Pair players whose recent best completion times are closest, widening with queue time (uses now/queuedAt flex). Most platformer-appropriate; pairs with the time ladder. | ✓ |
| Ladder-rating bracket + time-widening | Like elo-range but keyed on the custom ladder rating. Structurally familiar, simplest port. | |
| Hybrid: best-time + rating tiebreak | Primary best-time, secondary rating. Most realistic, most moving parts. | |

**User's choice:** Recent best-time proximity
**Notes:** `Name` ≠ `"elo-range"`; registered for the demo ladder only. Cold-start (fresh guest, no recorded time) assumed to fall into a wide/neutral "match anyone" bracket until first run — flagged for the researcher to confirm rather than re-asked.

---

## Custom ladder / ranking rule

| Option | Description | Selected |
|--------|-------------|----------|
| Head-to-head + time margin | Each async 1v1 → win/loss/draw (faster wins; equal integer-ms = draw); custom IRankingAlgorithm updates rating scaled by time margin. Fits the batched Apply contract; verifiable rating change (R6). | ✓ |
| Best-time speedrun board | Ladder ranks by personal-best time; the 1v1 is flavor. Dead simple but a min-time projection, not really an IRankingAlgorithm. | |
| Both board + rating | PB board AND a head-to-head rating. Richest, most surface to build/verify. | |

**User's choice:** Head-to-head + time margin
**Notes:** Custom `IRankingAlgorithm`, `Name` ≠ `"glicko2"`. Exact integer-ms tie = symmetric draw with no asymmetric rating change. Must honor the BATCHED-ONLY `Apply` contract.

---

## GameServer topology & Docker

| Option | Description | Selected |
|--------|-------------|----------|
| Embedded IHostedService | GameServer runs as a BackgroundService inside the single Platformer3D image (separate .csproj, referenced by host). One image satisfies "single app image", simplest compose + offline tarball. | ✓ |
| Separate container | Host + game server as two images plus pg+redis. Cleaner separation, truer to prod, but two images to build/save — tension with "single app image". | |
| Same image, entrypoint switch | One image, compose runs it twice with different entrypoints. Single image, two runtime roles, more config. | |

**User's choice:** Embedded IHostedService
**Notes:** Multi-stage Dockerfile (SDK build → aspnet runtime); single compose (app+pg+redis) publishing only the app HTTP port; `docker save` offline tarball; no runtime cloud/SaaS/CDN call.

---

## Claude's Discretion

- WebSocket message schema and run-summary wire format
- Server-side validation thresholds (plausible-time bounds, checkpoint tolerances)
- In-process service-token issuance/consumption wiring
- Custom ranking algorithm constants, seed rating, and time-margin scaling curve
- Concrete `Name` discriminator strings for the custom strategy/algorithm
- The one-level gameplay design (movement, goal, checkpoints), so long as the run is completable and timed

## Deferred Ideas

- Best-time speedrun (personal-best) leaderboard — future enhancement
- Ghost / replay of the opponent's run — out of scope per SPEC; natural follow-on
- Full server re-simulation anti-cheat — rejected for the async demo
- Multiple levels / level editor / art-audio polish — out of scope
- Separate-container or entrypoint-switch GameServer topology — revisit if the demo grows toward a deployable template

## Process note

- **Execution constraint:** the user is running `gsd-autonomous` on phases 16→20 on the main checkout; Phase 21 is to be executed in a **dedicated git worktree in parallel** and merged later. Captured as D-15 in CONTEXT.md so planning/execution keep the merge conflict surface minimal (new `samples/Platformer3D*` paths, Dockerfile/compose, sln entries, REUSE/notices only).
