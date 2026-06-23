# Phase 21: Final Demo — 3D Multiplayer Platformer — Specification

**Created:** 2026-06-22
**Ambiguity score:** 0.14 (gate: ≤ 0.20)
**Requirements:** 11 locked

## Goal

A new greenfield demo (`samples/Platformer3D` + `samples/Platformer3D.GameServer`) ships as a single app image that, with one `docker compose up` (app + Postgres + Redis), lets a visitor open a stock web browser, guest-sign-in, party with a friend, get matchmade into a 1v1 async 3D-platformer match, and have the authoritative result update a custom GameKit ranking — proving GameKit end-to-end as a *fully customized* integration, not the vanilla-defaults sample.

## Background

GameKit ships 14 packages (`src/`) and two existing samples:

- **`samples/TicTacToeDuel/`** — an ASP.NET host that wires up the full GameKit surface (`AddGameKit().AddAuth().AddRankings().AddMatchmaking().AddPresence().AddLobby().AddGameKitOpenApi().AddGameKitAdmin("/admin").AddGameKitObservability().AddGameKitHealthChecks()`) and serves a **vanilla-JS 2D** tic-tac-toe browser client from `wwwroot/`. It uses **all GameKit defaults** — default `EloRangeMatchmakingStrategy`, default `Glicko2Algorithm`, no custom strategies. The admin console is mounted in-process at `/admin`.
- **`samples/TicTacToeDuel.GameServer/`** — a real (non-stub) console game server that authenticates to GameKit via the **existing service-account token system** (`GameKitServiceToken` auth scheme + `RequiresServiceToken` policy, defined in `src/GameKit.Rankings/Authentication/`; tokens minted by `gamekit service-token issue`) and POSTs authoritative session-lifecycle calls.

What does **not** exist today: any 3D engine/client, any multiplayer game logic, any `Dockerfile` for the app, any single loadable image, any standalone admin host, and any *custom* `IMatchmakingStrategy`/`IRankingAlgorithm` in a sample. This phase builds the flagship 3D demo greenfield while reusing the proven service-token auth pattern. TicTacToeDuel stays untouched as the minimal teaching sample.

This phase was promoted from Backlog Phase 999.1 (captured 2026-06-14) and is the v2.1 capstone.

## Requirements

1. **Greenfield demo projects**: Two new sample projects exist and build.
   - Current: Only `TicTacToeDuel` + `TicTacToeDuel.GameServer` exist; no 3D demo.
   - Target: `samples/Platformer3D` (ASP.NET host wiring GameKit + admin UI + serving the browser 3D client) and `samples/Platformer3D.GameServer` (authoritative game server using the existing `GameKitServiceToken` scheme) both exist, build, and are in `GameKit.sln`. `TicTacToeDuel` is unchanged.
   - Acceptance: `dotnet build` succeeds for both new projects; `TicTacToeDuel` still builds and its 2D client still loads; `git` shows no functional change to `samples/TicTacToeDuel/`.

2. **Browser-playable 3D client**: A 3D platformer playable in a stock browser with no native install.
   - Current: No 3D client; `wwwroot/` holds only 2D vanilla-JS tic-tac-toe.
   - Target: `samples/Platformer3D/wwwroot/` serves a WebGL 3D platformer (default engine: three.js, MIT) requiring no per-player install and no per-player build step; one playable level is sufficient.
   - Acceptance: Loading the app root URL in a current Chromium/Firefox renders an interactive 3D level the player can move through and complete; no native binary or engine download is required of the player.

3. **One-command, offline packaging**: A single `docker compose up` brings up the whole stack, reproducible offline.
   - Current: No `Dockerfile` for the app; root + sample compose files define only Postgres + Redis; no shippable image.
   - Target: A `Dockerfile` builds the Platformer3D app image; one compose file brings up that image + `postgres` + `redis`; the stack needs zero cloud credentials; a `docker save` tarball of all required images is produced for offline `docker load` transfer.
   - Acceptance: On a machine with images pre-pulled/loaded and **no network**, `docker compose up` yields a stack where `/health/ready` returns 200 and the game is reachable in the browser; a documented `docker save` command produces a tarball that `docker load` restores.

4. **The image is the admin server**: The running app exposes the GameKit admin console showing live demo data.
   - Current: Admin UI is mounted into TicTacToeDuel at `/admin`; no Platformer3D admin.
   - Target: The Platformer3D app mounts `AddGameKitAdmin()` so an operator opens the admin console in the browser and sees the players, matches, and game sessions produced by demo play.
   - Acceptance: After at least one demo match completes, the admin console lists the participating player(s), the match, and the completed session; with zero activity the admin console renders its empty states without error.

5. **Custom matchmaking strategy**: The demo registers and uses a bespoke `IMatchmakingStrategy`.
   - Current: Samples use the default `EloRangeMatchmakingStrategy`; no custom strategy exists in any sample.
   - Target: A platformer-specific `IMatchmakingStrategy` implementation is registered in the Platformer3D host (replacing the default for the demo ladder) and is the strategy matchmaking actually invokes.
   - Acceptance: A test asserts the resolved `IMatchmakingStrategy` for the demo ladder is the custom type (not `EloRangeMatchmakingStrategy`); a match is formed through it.

6. **Custom ranking/ladder**: The demo ranks by a platformer-appropriate, non-default rating.
   - Current: Samples use the default win/loss `Glicko2Algorithm` ladder.
   - Target: A custom ladder configuration (time/score-based) or custom `IRankingAlgorithm` tuned for the platformer is registered and drives the demo leaderboard. Completion time is stored at integer-millisecond precision. An exact tie is recorded as a **draw** (no rating change / symmetric draw path).
   - Acceptance: Completing a match updates the demo ladder using the custom rule (verifiable rating/leaderboard change); a test feeds two equal integer-ms times and asserts a draw outcome with no asymmetric rating change.

7. **Server-authoritative results**: Match outcomes are recorded only by the service-token game server.
   - Current: No 3D match results; session-lifecycle endpoints already require the `RequiresServiceToken` policy.
   - Target: `samples/Platformer3D.GameServer` validates and posts the authoritative match result via the service-token-protected session API (e.g. `POST /api/sessions/{id}/complete`); duplicate/double submissions for the same session are idempotent (exactly one recorded outcome).
   - Acceptance: A test posts the same session completion twice and asserts exactly one `game_sessions` outcome row; the result visible in the leaderboard originates from the game server, not the browser client.

8. **One-click guest onboarding**: A visitor can start playing in seconds via guest sign-in.
   - Current: TicTacToeDuel exposes guest + password + Steam + Discord providers; no streamlined onramp.
   - Target: The Platformer3D client offers a one-click guest sign-in wired through `GameKit.Auth` that yields a playable player identity without external credentials.
   - Acceptance: From a fresh browser session, a single guest action produces an authenticated player able to enter matchmaking; no email/password/OAuth is required to play.

9. **Party + ready-check → 1v1 match**: Two friends can party up and be placed into the same 1v1 async match.
   - Current: `GameKit.Lobby` provides party/ready-check + SignalR hub; no demo flow uses it.
   - Target: A player invites a friend to a party, both complete a ready-check, and queue together via `GameKit.Lobby`; the two partied players are placed into the same 1v1 async match. A ready-check decline, timeout, or disconnect aborts the group queue and returns the party to the lobby (nobody is enqueued).
   - Acceptance: An integration test drives invite → ready-check → queue and asserts both players land in one 1v1 match; a second test asserts a declined/timed-out ready-check leaves zero matchmaking tickets and the party intact.

10. **End-to-end loop smoke test**: The full loop runs green in CI against the composed stack.
    - Current: No end-to-end demo test exists.
    - Target: An automated smoke/integration test exercises guest sign-in → party + ready-check → matchmaking → match → authoritative result → ranking/leaderboard update against the composed stack, and is re-runnable.
    - Acceptance: The smoke test passes from a clean stack; re-running it on the same stack passes again (idempotent / no leaked state that breaks the second run); when two parties queue concurrently the test asserts each forms exactly one match.

11. **License + dependency hygiene**: Bundled third-party assets are GPL-compatible and disclosed; no new cloud dependency.
    - Current: `REUSE.toml` + `THIRD-PARTY-NOTICES.md` track vendored code (e.g. Glicko-2); no JS engine is bundled yet.
    - Target: Any vendored browser engine/asset (e.g. three.js) is GPL-compatible (MIT/Apache/BSD/LGPL), recorded in `REUSE.toml` and `THIRD-PARTY-NOTICES.md`; the demo adds no cloud/SaaS runtime dependency.
    - Acceptance: `reuse lint` passes for the new sample paths; `THIRD-PARTY-NOTICES.md` lists each bundled engine/asset with name, URL, and SPDX license id; no GPL-incompatible (SSPL/AGPL-only/proprietary) asset is vendored.

## Boundaries

**In scope:**
- New `samples/Platformer3D` ASP.NET host (GameKit + admin UI + serves the browser 3D client)
- New `samples/Platformer3D.GameServer` authoritative game server (reuses existing `GameKitServiceToken` auth)
- A WebGL/three.js 3D platformer browser client — one playable level
- A custom `IMatchmakingStrategy` for the demo ladder
- A custom ranking/ladder configuration (time/score-based), with exact-tie = draw at integer-ms precision
- One-click guest onboarding via `GameKit.Auth`
- Party + ready-check via `GameKit.Lobby` (SignalR) → 1v1 async match between the two partied players
- Authoritative match-result submission via the service-token session API (idempotent)
- A `Dockerfile` + single `docker compose` file (app image + Postgres + Redis) + a `docker save` offline tarball
- Admin console surfacing live demo players/matches/sessions
- An automated smoke/integration test of the full loop
- `REUSE.toml` + `THIRD-PARTY-NOTICES.md` entries for any bundled engine/asset

**Out of scope:**
- Live real-time position sync / authoritative physics netcode — the async model was chosen; live netcode is realistically its own milestone
- N-player free-for-all matches — locked to 1v1 async
- Multiple levels, level editor, or art/audio polish — one level proves the loop
- Ghost/replay of the opponent's run — not selected for the core loop; a separate enhancement
- Mobile or native desktop client — browser-only is the requirement
- A new server-to-server auth primitive — reuse the existing service-account token system
- Any modification to `TicTacToeDuel` / `TicTacToeDuel.GameServer` — they stay as the minimal sample
- Steam/Discord OAuth as a *required* path to play — guest is the required onramp (OAuth buttons optional)
- Production hardening of the demo (public TLS, multi-replica scale, autoscaling) — covered by Phases 16/18; the demo is local-first
- Changes to any `GameKit.*` package public API or Core migrations — the demo composes existing packages only (per the project migration-boundary constraint)

## Constraints

- **Self-hosted / GPL / no phone-home**: zero cloud-service or SaaS runtime dependency; the full demo runs offline on the operator's hardware (CLAUDE.md core constraint).
- **Runtime**: .NET 10 LTS + ASP.NET Core 10; Postgres (Npgsql) + Redis (StackExchange.Redis), matching the rest of the repo.
- **Auth primitive is fixed**: reuse the existing `GameKitServiceToken` scheme / `RequiresServiceToken` policy and `gamekit service-token issue` — do not invent a new one.
- **Engine default**: three.js (MIT) / WebGL is the locked default for the browser client; overridable during discuss-phase if a GPL-compatible alternative is preferred.
- **Packaging form**: one published app image + Postgres + Redis via a single `docker compose up`; a `docker save` tarball is also provided for offline `docker load`. (A truly single-container variant was considered and rejected in spec for v1.)
- **Migration boundary**: the demo adds no migrations to Core tables and modifies no `GameKit.*` package; it is purely a consumer/composition.
- **No regression to the minimal sample**: `TicTacToeDuel` remains the simple, defaults-based example.

## Acceptance Criteria

- [ ] `samples/Platformer3D` and `samples/Platformer3D.GameServer` build and are in `GameKit.sln`; `TicTacToeDuel` is functionally unchanged
- [ ] The app root URL renders an interactive, completable 3D level in a stock browser with no player-side install or build
- [ ] `docker compose up` (offline, images pre-loaded) yields `/health/ready` = 200 and a browser-reachable game; a documented `docker save` tarball restores via `docker load`
- [ ] The admin console lists demo players/matches/sessions after a match, and renders empty states with zero activity
- [ ] The resolved demo-ladder `IMatchmakingStrategy` is the custom type (not `EloRangeMatchmakingStrategy`) and forms matches
- [ ] Completing a match updates a custom (time/score-based) ladder; two equal integer-ms times produce a draw with no asymmetric rating change
- [ ] Posting the same session completion twice yields exactly one outcome row; leaderboard results originate from the game server
- [ ] A single guest action from a fresh browser session produces a player able to enter matchmaking (no email/OAuth required)
- [ ] Invite → ready-check → queue places both partied players into one 1v1 match; a declined/timed-out ready-check leaves zero tickets and the party intact
- [ ] The end-to-end smoke test passes from a clean stack and again on re-run; two concurrent parties each form exactly one match
- [ ] `reuse lint` passes for new sample paths; `THIRD-PARTY-NOTICES.md` lists each bundled asset with name/URL/SPDX id
- [ ] **must-NOT**: no runtime outbound cloud/SaaS/CDN call — engine + assets served locally, no CDN `<script>`, no SaaS OTLP, no fonts/analytics CDN (grep/egress check)
- [ ] **must-NOT**: a player JWT calling the session-complete endpoint returns 401/403 — only the `GameKitServiceToken` role is accepted (route-auth test)
- [ ] **must-NOT**: the demo compose publishes only the app HTTP port — Postgres/Redis ports are not mapped to the host
- [ ] **must-NOT**: guest onboarding collects no PII and the demo adds no analytics/tracking

## Edge Coverage

**Coverage:** 17/17 applicable edges resolved · 0 unresolved (6 covered · 2 backstop · 9 dismissed)

| Category | Requirement | Status | Resolution / Reason |
|----------|-------------|--------|---------------------|
| boundary | R6 | ✅ covered | Exact tie in completion time → draw, no asymmetric rating change (AC: equal-times test) |
| precision | R6 | ✅ covered | Completion time stored/compared at integer-ms precision; rounding/tie-break moot (draw on equality) |
| concurrency | R7 | ✅ covered | Duplicate/double session-complete is idempotent — exactly one outcome row (AC: double-post test) |
| idempotency | R3 | ✅ covered | `docker compose up` re-run reconciles to same healthy stack; GameKit migrations already idempotent |
| empty | R4 | ✅ covered | Admin console renders empty states with zero players/matches/sessions |
| (flow) | R9 | ✅ covered | Ready-check decline/timeout/disconnect aborts group queue, party returns to lobby, zero tickets |
| idempotency | R10 | 🧪 backstop | Smoke test must be re-runnable on the same stack — held-out re-run assertion for plan-phase |
| concurrency | R10 | 🧪 backstop | Two parties queueing concurrently each form exactly one match — held-out concurrency test |
| adjacency | R3 | ⛔ dismissed | False-positive cue match — R3 is container packaging, not a merge/interval algorithm |
| ordering | R3 | ⛔ dismissed | No collection output to order — packaging requirement |
| empty | R3 | ⛔ dismissed | No input collection — packaging requirement |
| concurrency | R3 | ⛔ dismissed | Single-operator bring-up; not a concurrent operation |
| unclassified | R1 | ⛔ dismissed | Existence/scaffolding requirement; no input-boundary edge (its own AC covers it) |
| unclassified | R2 | ⛔ dismissed | Browser-serve existence requirement; no input-boundary edge |
| unclassified | R5 | ⛔ dismissed | DI registration requirement; verified by resolution test, no input edge |
| unclassified | R8 | ⛔ dismissed | Guest onboarding existence; repeated guest sign-ins yield distinct ephemeral identities (acceptable) |
| unclassified | R11 | ⛔ dismissed | License/disclosure compliance owned by `reuse lint`; folded into R11 acceptance |

## Prohibitions (must-NOT)

**Coverage:** 4/4 applicable prohibitions resolved · 0 unresolved

| Prohibition (must-NOT statement) | Requirement | Status | Verification / Reason |
|----------------------------------|-------------|--------|------------------------|
| The demo MUST NOT make any runtime outbound call to a cloud/SaaS/CDN (no CDN `<script>`, no SaaS OTLP, no fonts/analytics) — engine + assets served locally | R2, R3, R11 | resolved | verification: test (grep/egress check over `wwwroot/` + config; complements Phase 18 SEC-05 SaaS-OTLP grep gate) |
| The browser client MUST NOT write match results/ratings — only the service-token game server posts outcomes; a player JWT to session-complete returns 401/403 | R7 | resolved | verification: test (route-auth test asserting player JWT is rejected) |
| The demo compose MUST NOT publish Postgres/Redis ports to the host/public — only the app HTTP port is exposed | R3 | resolved | verification: test (assert compose port mappings expose app port only) |
| Guest onboarding MUST NOT collect PII and the demo MUST NOT add analytics/tracking | R8 | resolved | verification: judgment (+ grep for trackers / PII fields at guest signin) |

## Ambiguity Report

| Dimension          | Score | Min  | Status | Notes                                              |
|--------------------|-------|------|--------|----------------------------------------------------|
| Goal Clarity       | 0.90  | 0.75 | ✓      | Single, measurable end-to-end loop locked          |
| Boundary Clarity   | 0.85  | 0.70 | ✓      | Async model, 1v1, greenfield, TicTacToe untouched  |
| Constraint Clarity | 0.82  | 0.65 | ✓      | Packaging, auth primitive, engine, GPL all fixed   |
| Acceptance Criteria| 0.85  | 0.70 | ✓      | 15 pass/fail criteria incl. 4 negative             |
| **Ambiguity**      | 0.14  | ≤0.20| ✓      |                                                    |

Status: ✓ = met minimum, ⚠ = below minimum (planner treats as assumption)

## Interview Log

| Round | Perspective        | Question summary                          | Decision locked                                                        |
|-------|--------------------|-------------------------------------------|-----------------------------------------------------------------------|
| 1     | Researcher         | Multiplayer / netcode depth?              | Async / shared-world — matchmaking + rankings, **no live position sync** |
| 1     | Researcher         | What "loadable image" means?              | One `docker compose up` (app image + Postgres + Redis siblings)        |
| 1     | Researcher         | Build greenfield or extend existing?      | New `samples/Platformer3D` (+ GameServer); TicTacToeDuel stays minimal |
| 2     | Simplifier         | Irreducible core loop?                    | Guest → **party + ready-check** → matchmake → play → authoritative result → rank |
| 2     | Researcher         | What "fully customized" must demonstrate? | Custom matchmaking strategy + custom ladder + authoritative server + guest onboarding |
| 2     | Simplifier         | Match size?                               | 1v1 async                                                             |
| 5.5   | Failure Analyst    | Exact-tie ranking edge?                   | Draw — no asymmetric rating change; integer-ms time precision         |
| 5.5   | Failure Analyst    | Ready-check decline/timeout/disconnect?   | Abort group queue, return party to lobby, nobody enqueued             |
| 5.6   | Boundary Keeper    | Values/integrity must-NOTs?               | No cloud/CDN egress; client can't self-report; DB/Redis not published; no PII/analytics |

---

*Phase: 21-final-demo-3d-multiplayer-platformer*
*Spec created: 2026-06-22*
*Next step: /gsd-discuss-phase 21 — implementation decisions (engine wiring, custom-strategy design, Dockerfile/compose layout, run recording for results)*
