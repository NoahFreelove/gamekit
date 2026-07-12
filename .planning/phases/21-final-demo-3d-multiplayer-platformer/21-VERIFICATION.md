---
phase: 21-final-demo-3d-multiplayer-platformer
verified: 2026-07-12T00:00:00Z
status: passed
score: 6/6 must-haves verified
behavior_unverified: 0
overrides_applied: 1
overrides:
  - must_have: "The demo composes existing packages only — no changes to any GameKit.* package public API (SPEC Constraints / D-15)"
    reason: "The inter-party 1v1 fix required 3 surgical edits to src/GameKit.Matchmaking (MatchmakerTickerService single-candidate gates, TeamAssignmentService lone-party round-robin split, ProposalService inter-party null-LadderId). The package changes were explicitly authorized by the user (recorded in 21-inter-party-1v1-SUMMARY.md: 'Package changes were explicitly authorized by the user for this — overrides D-15 / SPEC'). Regression evidence: GameKit.Matchmaking.Tests 117 green + GameKit.Matchmaking.Integration.Tests 84 green at the time; changes merged to master via e1acdeb."
    accepted_by: "Noah Freelove"
    accepted_at: "2026-06-24T00:00:00Z"
gaps: []
---

# Phase 21: Final Demo — 3D Multiplayer Platformer Verification Report

**Phase Goal:** A single, loadable container image showcases GameKit end-to-end — someone runs the image and immediately plays a 3D multiplayer game in their web browser. The image is the GameKit-enabled admin server bundled with a *fully customized* example GameKit integration (not the bare sample), proving the library's composability and self-host story in one artifact.
**Verified:** 2026-07-12 (retroactive — formal artifact produced post-hoc; the v2.1 milestone audit flagged its absence as tech debt)
**Status:** passed
**Re-verification:** No — initial formal verification (the phase itself was behaviorally verified 2026-06-23..26 during execution)

## Verification Method

This is a **retroactive, static** goal-backward verification against the master checkout (commit lineage includes merge `e1acdeb`, confirmed ancestor of master). Constraints of this run:

- **Run today (2026-07-12):** artifact existence + wiring greps, no-CDN egress grep, compose port-mapping inspection, solution-membership check, license-record greps, and the fast unit tier — `dotnet test tests/GameKit.Platformer3D.Tests -c Release` → **48/48 passed, 0 skipped, 1s**.
- **Not re-run today (by instruction — no servers, no Docker):** Testcontainers integration suite (27 tests), docker build/compose bring-up, and the browser e2e.
- **Accepted existing behavioral evidence:** the phase was verified 2026-06-23..26 via a **two-player headless-browser e2e** (`tests/e2e-browser.mjs`, Playwright chrome-headless-shell — ranked quick-match pairs two players, results show Victory/Defeat + `1000 → 1162 (+162)` rating delta, leaderboard renders non-zero ratings, friend-party unranked match; run twice back-to-back, ALL PASSED), plus `tests/e2e-lobby-protocol.mjs` 19/19, plus 21 (later 27) Testcontainers integration tests green, plus the 21-06 T3 human-verify checkpoint ("live verified"). Recorded in the phase SUMMARYs, project memory, ROADMAP `[x]` (completed 2026-06-26), and merged to master.

## Goal Achievement

### Observable Truths (ROADMAP Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | **Single loadable image** — one image via `docker load`/`docker compose up` reaches a playable game with zero manual config; Postgres + Redis brought up by the same compose file | VERIFIED | `samples/Platformer3D/Dockerfile` (multi-stage SDK→aspnet, repo-root context, EXPOSE 8080), `docker-compose.yml` (app + postgres:17.9 + redis:8.6.2), `docker-compose.release.yml` (published image `ghcr.io/noahfreelove/gamekit`), `README.md` documents `docker save`/`docker load` (lines 114/123). Behavioral: docker build exit 0 / 400 MB image (21-05), stack `/health/ready` 200 + full browser play verified through 2026-06-26. |
| 2 | **Play in the browser** — 3D multiplayer client served by the image, stock browser, no native install/engine download | VERIFIED | `wwwroot/index.html` + `js/game.js` (three.js game loop, guest auth at `fetch('/auth/login/guest')` game.js:32, WS run-summary `run_finish` game.js:125) + `js/app.js` (menu-driven flow controller) + vendored `three.module.js`/`three.core.js`/`addons/PointerLockControls.js` (r184, MIT) + `signalr.min.js` (vendored) + `assets/level.json`. No-CDN grep re-run today: clean. Behavioral: two-player headless-browser e2e (2026-06-26). |
| 3 | **Admin server IS the image** — running container is the GameKit admin server; operator sees live players/matches/sessions | VERIFIED | `AddGameKitAdmin` wired in `Program.cs:156`; `DemoAdminSeederHostedService.cs` seeds a usable admin account on first boot (config-gated, non-Production-guarded; 45 unit tests per 21-06 T3). Behavioral: admin console live-data + empty-state acceptance was the 21-06 T3 human-verify checkpoint, recorded "Complete — live verified". |
| 4 | **Fully customized GameKit example** — custom matchmaking strategy / ranking config / lobby flow, not the vanilla sample | VERIFIED | `Strategy/BestTimeMatchmakingStrategy.cs` (`Name="best-time"`, `MatchPlayerCount=2` self-match, `BuildSelfMatchResult`) replaces the default via `services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>())` at `Program.cs:131` (A3); `Algorithms/TimeMarginRankingAlgorithm.cs` (fixed-delta, `Name="time-margin"`) selected via `ladder.Algorithm = "time-margin"` at `Program.cs:97`; custom lobby/party flow (quick-match, friend party, inter-party 1v1 unranked). Unit tier re-run today: 48/48 green (includes strategy + algorithm + self-match + validator + idempotency tests). Integration `BestTimeStrategyResolutionTests` asserts the resolved type is not `EloRangeMatchmakingStrategy`. `TicTacToeDuel` untouched. |
| 5 | **Real server↔GameKit auth** — a real (non-stub) game server authenticates with the service-to-service primitive and drives the session API | VERIFIED | `Platformer3D.GameServer/PlatformerGameServerService.cs` — embedded `IHostedService` using the existing `GameKitServiceToken` primitive: revoke-then-issue via `IServiceTokenService` at StartAsync (lines 99–114), deterministic `Idempotency-Key: platformer-session-{sessionId}`, Pending→Active start call, DNF 30s timeout, tie→Draw mapping. Negative gate: `PlayerJwtRejectedTests.cs` asserts player JWT / unauthenticated / guest JWT all rejected 401/403 on `POST /api/sessions/{id}/complete` (`RequiresServiceToken`). Docker-free idempotency unit tests re-run green today. |
| 6 | **One-command demo** — full stack from a single command, reproducible offline, zero cloud credentials | VERIFIED | Single `docker compose up` file; compose inspected today: only `8080:8080` published on `app`, postgres/redis have **no** `ports:` sections (asserted by `Packaging/ComposePortMappingTests.cs` + `InitSqlGrantTests.cs`); demo RSA keypair generated at image build (no external secrets); no cloud/SaaS/CDN references in `wwwroot/` (grep clean today). Offline `docker save`/`load` documented in README. Behavioral bring-up verified repeatedly through 2026-06-26 (`down -v && up -d --build`, `/health/ready` 200). |

**Score:** 6/6 truths verified (0 present-but-behavior-unverified)

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `samples/Platformer3D/Platformer3D.csproj` | ASP.NET host referencing real GameKit packages | VERIFIED | ProjectReferences to GameKit packages at lines 14–23 (confirmed by the integration checker); full builder chain in `Program.cs:50-245` |
| `samples/Platformer3D/Program.cs` | Full host composition | VERIFIED | Custom strategy Replace (l.131), time-margin ladder (l.97), AddGameKitAdmin (l.156), `/ws/game` `?access_token=` JWT extraction (l.197), demo endpoints quick-match/my-rank/session-result/leaderboard/leave-party (l.315–542) |
| `samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs` | Custom `IMatchmakingStrategy` | VERIFIED | Substantive: bracket ramp, cold-start, self-match, equal-size guard; wired via Replace; unit-tested |
| `samples/Platformer3D/Algorithms/TimeMarginRankingAlgorithm.cs` | Custom `IRankingAlgorithm` | VERIFIED | Fixed-delta (D-09 amendment documented), draw-symmetric; wired via ladder Algorithm; unit-tested |
| `samples/Platformer3D.GameServer/` | Real embedded game server | VERIFIED | `PlatformerGameServerService.cs`, `WebSocketGameSession.cs`, `RunSummary.cs`, `RunSummaryValidator.cs` (D-13 IHostedService pattern). `GameServerPlaceholder.cs` remains but is inert leftover scaffold, not on the runtime path |
| `samples/Platformer3D/wwwroot/` | Browser 3D client, no CDN | VERIFIED | index.html, game.js, app.js, vendored three.js r184 + SignalR 10.0.0 + level.json; CDN grep clean |
| `samples/Platformer3D/Dockerfile` + `.dockerignore` + `docker/postgres/init/01-init.sql` | Image packaging | VERIFIED | Multi-stage, repo-root context, healthcheck curl, demo RSA keygen |
| `samples/Platformer3D/docker-compose.yml` (+ `.release.yml`) | One-command stack, app port only | VERIFIED | 3 services; only `8080:8080` published; release variant pulls `ghcr.io/noahfreelove/gamekit` |
| `tests/GameKit.Platformer3D.Tests/` | Unit tier | VERIFIED | **48/48 passed today** (Strategy, Rankings, GameServer, Admin seeder) |
| `tests/GameKit.Platformer3D.Integration.Tests/` | Testcontainers integration tier | VERIFIED (exists; not re-run today) | Strategy resolution, GuestOnboarding, PlayerJwtRejected, ComposePortMapping, InitSqlGrant, LobbyToMatch (incl. `InterParty_TwoMemberParty_SelfMatchesIntoOneVsOne`), EndToEndSmokeTests (FullLoop / DoublePost idempotent / Rerun / ConcurrentParties). Last recorded run: 27/27 green (2026-06-26) |
| `GameKit.sln` | All 4 new projects wired | VERIFIED | 4 Platformer3D project entries present |
| `LICENSES/MIT.txt` + `REUSE.toml` + `THIRD-PARTY-NOTICES.md` | License hygiene for vendored assets | VERIFIED | three.js and SignalR both recorded in REUSE.toml and THIRD-PARTY-NOTICES.md; LICENSES/ has GPL-3.0-or-later, BSD-3-Clause, MIT |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| Program.cs | BestTimeMatchmakingStrategy | `services.Replace(...)` after `AddMatchmaking()` (A3) | WIRED | Program.cs:131; gate = BestTimeStrategyResolutionTests |
| Program.cs | TimeMarginRankingAlgorithm | `ladder.Algorithm = "time-margin"` | WIRED | Program.cs:97 matches the algorithm's `Name` |
| Browser client | GameKit.Auth | `POST /auth/login/guest` | WIRED | game.js:32; one-click guest, token in module scope |
| Browser client | GameServer WS | `/ws/game/{id}?access_token=` | WIRED | game.js:117 → Program.cs:197 `OnMessageReceived` query-token extraction (keystone fix from demo-functional-overhaul) |
| GameServer | Session API | Bearer service token + `Idempotency-Key` | WIRED | Revoke-then-issue at StartAsync; `RequiresServiceToken` enforced (negative tests) |
| Match result | Leaderboard | `IRankingsTicker` → `/demo/leaderboard` (raw `PlayerRank` read) | WIRED | 3s tick / 5s rating period for demo responsiveness; e2e asserted non-zero ratings `[1162, 838]` |
| Inter-party self-match | 1v1 unranked session | MatchmakerTickerService gates relaxed + TeamAssignmentService round-robin + ProposalService null-`LadderId` | WIRED | src/GameKit.Matchmaking (user-authorized package change — see override); `isInterPartyMatch` at ProposalService.cs:325-331 |

### Behavioral Spot-Checks (run today)

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Unit tier green | `dotnet test tests/GameKit.Platformer3D.Tests -c Release` | Failed: 0, Passed: 48, Skipped: 0 (1s) | PASS |
| No CDN/analytics egress (must-NOT) | `grep -rEin 'cdn|unpkg|cdnjs|fonts.googleapis|jsdelivr|google-analytics|googletagmanager' samples/Platformer3D/wwwroot/` | no matches | PASS |
| Only app port published (must-NOT) | compose inspection | app `8080:8080` only; pg/redis have no `ports:` | PASS |
| e1acdeb merged | `git merge-base --is-ancestor e1acdeb master` | ancestor confirmed | PASS |
| Integration/e2e/docker tiers | — | not re-run (static verification per instruction) | SKIP — covered by recorded runs 2026-06-23..26 |

### Prohibitions (SPEC must-NOTs) — 4/4

| Prohibition | Tier | Status | Evidence |
|-------------|------|--------|----------|
| No runtime outbound cloud/SaaS/CDN call | test | VERIFIED | Grep re-run today clean; all engine/assets vendored locally |
| Browser client cannot write results — player JWT → 401/403 on session-complete | test | VERIFIED | `PlayerJwtRejectedTests.cs` (3 negative tests) exists and wired; last recorded run green; `RequiresServiceToken` policy on the endpoint |
| Compose publishes only the app HTTP port | test | VERIFIED | Compose inspected today + `ComposePortMappingTests.cs` (5 YAML-parse tests, Docker-free) |
| Guest onboarding collects no PII; no analytics/tracking | judgment + test | VERIFIED | `GuestOnboardingTests` asserts no `player_identities`/`player_credentials` rows; tracker grep clean; guest identity is a per-localStorage device id only |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `samples/Platformer3D.GameServer/GameServerPlaceholder.cs` | all | Leftover Wave-1 scaffold file | Info | Inert — real server is `PlatformerGameServerService.cs`; placeholder not on any runtime path. Cosmetic cleanup candidate only. |

No TBD/FIXME/XXX debt markers found in the phase's runtime files.

### Deviations Accepted

1. **D-15 / SPEC "no GameKit.* package changes" overridden** for the inter-party 1v1 self-match (3 surgical edits to `src/GameKit.Matchmaking`). Explicitly user-authorized, regression-tested (MM unit 117 + MM integration 84 green), behaviour-preserving for the default `EloRange` strategy. Recorded as a formal override in frontmatter.
2. **D-09 margin-scaled rating → fixed-delta** (21-02 amendment): `MatchOutcome` has no margin field and adding one would have violated the package-API boundary. SPEC R6 still satisfied (custom non-glicko2 rule, draw-symmetric tie).
3. **`reuse lint` exits non-zero repo-wide** for pre-existing, pre-Phase-21 reasons (uncovered bin/obj artifacts + SPDX strings in planning markdown). None of the Platformer3D files appear in the violations; the phase's own license records (three.js, SignalR, MIT.txt) are complete.

### Gaps Summary

None. All 6 ROADMAP success criteria are satisfied in the merged codebase; all 4 SPEC prohibitions hold; the unit tier is green as of today. The behavioral tiers (integration, docker bring-up, browser e2e) were not re-executed in this static run but have recorded green runs from 2026-06-23..26 (including a twice-repeated two-player headless-browser e2e) that shipped to master via `e1acdeb`. This document closes the v2.1 audit tech-debt item: Phase 21 previously lacked a formal VERIFICATION.md despite being behaviorally verified.

---

_Verified: 2026-07-12T00:00:00Z_
_Verifier: Claude (gsd-verifier) — retroactive static verification_
