# Phase 21: Final Demo — 3D Multiplayer Platformer - Context

**Gathered:** 2026-06-22
**Status:** Ready for planning

<domain>
## Phase Boundary

A greenfield flagship demo (`samples/Platformer3D` + `samples/Platformer3D.GameServer`) that proves GameKit end-to-end as a *fully customized* integration — not the vanilla-defaults sample. With one `docker compose up` (app image + Postgres + Redis), a visitor opens a stock browser, guest-signs-in, parties + ready-checks with a friend, gets matchmade into a **1v1 async** 3D-platformer match, and an **authoritative** result (posted by the service-token game server) updates a **custom** GameKit ladder. The admin console surfaces the live demo players/matches/sessions. `TicTacToeDuel` stays untouched as the minimal teaching sample.

This discussion locked the **HOW** (implementation decisions). The **WHAT** is locked by `21-SPEC.md`.

</domain>

<spec_lock>
## Requirements (locked via SPEC.md)

**11 requirements are locked.** See `21-SPEC.md` for full requirements, boundaries, acceptance criteria, edge coverage, and prohibitions (must-NOTs).

Downstream agents MUST read `21-SPEC.md` before planning or implementing. Requirements are not duplicated here.

**In scope (from SPEC.md):**
- New `samples/Platformer3D` ASP.NET host (GameKit + admin UI + serves the browser 3D client)
- New `samples/Platformer3D.GameServer` authoritative game server (reuses existing `GameKitServiceToken` auth)
- A WebGL/three.js 3D platformer browser client — one playable level
- A custom `IMatchmakingStrategy` for the demo ladder
- A custom ranking/ladder configuration (time/score-based), exact-tie = draw at integer-ms precision
- One-click guest onboarding via `GameKit.Auth`
- Party + ready-check via `GameKit.Lobby` (SignalR) → 1v1 async match between the two partied players
- Authoritative match-result submission via the service-token session API (idempotent)
- A `Dockerfile` + single `docker compose` file (app image + Postgres + Redis) + a `docker save` offline tarball
- Admin console surfacing live demo players/matches/sessions
- An automated smoke/integration test of the full loop
- `REUSE.toml` + `THIRD-PARTY-NOTICES.md` entries for any bundled engine/asset

**Out of scope (from SPEC.md):**
- Live real-time position sync / authoritative physics netcode (async model chosen)
- N-player free-for-all matches (locked to 1v1 async)
- Multiple levels, level editor, or art/audio polish (one level proves the loop)
- Ghost/replay of the opponent's run
- Mobile or native desktop client (browser-only)
- A new server-to-server auth primitive (reuse the existing service-account token system)
- Any modification to `TicTacToeDuel` / `TicTacToeDuel.GameServer`
- Steam/Discord OAuth as a *required* path to play (guest is the required onramp; OAuth buttons optional)
- Production hardening (public TLS, multi-replica, autoscaling) — Phases 16/18 own that; demo is local-first
- Changes to any `GameKit.*` package public API or Core migrations (demo composes existing packages only)

</spec_lock>

<decisions>
## Implementation Decisions

### Run Model & Adjudication
- **D-01:** Authoritative result is established from a **validated run-summary** the client sends to the GameServer over a **WebSocket** (not full server re-simulation, not plain REST). The GameServer then posts the authoritative completion through the existing service-token-protected session API — `POST /api/sessions/{id}/complete` under the `GameKitServiceToken` scheme / `RequiresServiceToken` policy. The browser client never writes the result (must-NOT: player JWT to session-complete → 401/403).
- **D-02:** The run-summary carries the timing primitives the ladder needs: run start, ordered checkpoint timestamps, and finish — completion time at **integer-millisecond** precision (matches R6).
- **D-03:** Server-side validation is **sanity-level, not full re-sim**: monotonic/ordered checkpoints, plausible time bounds, one finish per session. Implausible runs are rejected. Anti-cheat depth beyond this is explicitly *not* a goal for the demo (the async model trades full input re-simulation for simplicity — see SPEC "Out of scope: authoritative physics netcode").
- **D-04:** The WebSocket connection doubles as a **liveness signal** — a disconnect during the active window feeds the ready-check/group-queue abort path (R9: decline/timeout/disconnect aborts the group queue, party returns to lobby, zero tickets).
- **D-05:** Submission is **idempotent** (R7): the same session completion posted twice yields exactly one `game_sessions` outcome row. Reuse `IIdempotencyStore` / the existing session-complete idempotency path rather than inventing a new one.

### Custom Matchmaking Strategy
- **D-06:** A bespoke `IMatchmakingStrategy` keyed on **recent best-time proximity** — pair players whose recent best completion times are closest, **widening the window as queue time grows** (uses the `now` / `queuedAt` flex the `Match(candidate, pool, now)` signature already exposes). This is the "platformer-appropriate" signal that pairs naturally with the time-based ladder and visibly differs from the default `EloRangeMatchmakingStrategy`.
- **D-07:** `Name` ≠ `"elo-range"`. Registered in the **Platformer3D host** for the demo ladder (replacing the default for that ladder only). Must satisfy the interface's **stateless + deterministic** contract (build per-call state inside `Match`, no mutable instance fields). R5 test asserts the resolved strategy for the demo ladder is the custom type and that a match forms through it.
- **D-08 (ASSUMPTION — researcher to confirm):** **Cold-start** — a fresh guest has no recorded best time. Default: such players get a **wide/neutral bracket (match anyone)** until they post their first run, then narrow on subsequent queues. Flagged as the assumed default, not a re-asked question; researcher confirms feasibility against where "recent best time" is read from (ladder/ranking store vs. Redis queue metadata).

### Custom Ladder / Ranking Rule
- **D-09:** A **custom `IRankingAlgorithm`** (`Name` ≠ `"glicko2"`) drives the demo leaderboard. Each async 1v1 produces a **head-to-head outcome**: the faster integer-ms completion **wins**; the rating update is **scaled by the time margin** (bigger gap → bigger swing). Chosen over a pure best-time speedrun board because it satisfies R6's "verifiable rating/leaderboard change via the custom rule" while cleanly exercising the `IRankingAlgorithm` strategy seam.
- **D-10:** **Exact tie** at integer-ms = **draw**: symmetric / no asymmetric rating change (R6 acceptance: feed two equal integer-ms times → assert a draw with no asymmetric change).
- **D-11:** Honor the interface's **BATCHED-ONLY** contract — accumulate the rating period and call `Apply` once per period; never call `Apply` per individual match. Custom convergence/iteration (if any) must be bounded and deterministic per the interface's numerical-stability note.
- **D-12:** The resulting rating drives the demo leaderboard, which the **admin console** surfaces (R4) and which is verifiably changed after a completed match.

### GameServer Topology & Docker
- **D-13:** `samples/Platformer3D.GameServer` runs as an **embedded `IHostedService`** inside the **single** Platformer3D app image — kept as a separate `.csproj` for clarity and referenced by the host project. This satisfies SPEC "single app image", keeps the compose surface minimal (app + pg + redis), and makes the offline `docker save` tarball straightforward. The game server consumes a service token **in-process**.
- **D-14:** **Multi-stage Dockerfile** (SDK build → `aspnet` runtime). One compose file brings up the app image + `postgres` + `redis`. **Only the app HTTP port is published** to the host (must-NOT: Postgres/Redis ports not mapped). A documented `docker save` command produces an offline tarball that `docker load` restores; the stack needs zero cloud credentials and makes **no runtime outbound cloud/SaaS/CDN call** (engine + assets served locally — must-NOT).

### Execution / Process
- **D-15:** This phase is **executed in a dedicated git worktree, in parallel** with the `gsd-autonomous` run currently working phases 16→20 on the main checkout; it is **merged later**. Planning and execution must assume an isolated worktree and keep the merge conflict surface minimal — changes are confined to **new** `samples/Platformer3D*` paths, the new `Dockerfile`/compose, `GameKit.sln` project entries, and `REUSE.toml` / `THIRD-PARTY-NOTICES.md` additions. Do **not** touch `TicTacToeDuel*`, `GameKit.*` package public APIs, or Core migrations (also a SPEC boundary).

### Claude's Discretion
Left to research/planning within the decisions above: the WebSocket message schema and run-summary wire format; exact validation thresholds (plausible-time bounds, checkpoint tolerances); in-process service-token issuance/consumption wiring; the custom algorithm's rating constants, seed rating, and margin-scaling curve; concrete `Name` discriminator strings; and the one-level gameplay design (movement, goal, checkpoints) so long as it yields a completable, timed run.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements (read first)
- `.planning/phases/21-final-demo-3d-multiplayer-platformer/21-SPEC.md` — **Locked requirements (11), boundaries, acceptance criteria, edge coverage, must-NOT prohibitions. MUST read before planning.**
- `.planning/ROADMAP.md` — Phase 21 detail entry (capstone success criteria; promoted from Backlog 999.1).

### Matchmaking (custom strategy — D-06/07/08)
- `src/GameKit.Matchmaking/Strategy/IMatchmakingStrategy.cs` — strategy contract: `Match(candidate, pool, now)`, stateless + deterministic, `Name` discriminator, queue-time flex via `now`/`queuedAt`.
- `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` — default impl to base the custom one on (bracket-widening reference).
- `src/GameKit.Matchmaking/Strategy/PartyRatingAggregator.cs` — how a party's rating is aggregated for matchmaking (relevant to party → 1v1).
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs` — how strategies are registered/selected per ladder.

### Rankings (custom ladder — D-09/10/11/12)
- `src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs` — **BATCHED-ONLY** `Apply(state, batch)` contract; determinism + numerical-stability requirements.
- `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` — default impl reference.
- `src/GameKit.Rankings/GameKitRankingsOptions.cs` + `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` — ladder/algorithm configuration + registration.

### Service-token auth + session lifecycle (authoritative result — D-01/05)
- `src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationHandler.cs`, `ServiceTokenAuthorizationPolicy.cs`, `ServiceTokenAuthenticationDefaults.cs`, `ServiceTokenAuthenticationOptions.cs` — `GameKitServiceToken` scheme / `RequiresServiceToken` policy (the fixed auth primitive).
- `src/GameKit.Core/Http/SessionEndpoints.cs` — session lifecycle endpoints (complete/abandon).
- `src/GameKit.Core/Services/ISessionCompleteService.cs` + `src/GameKit.Core/Http/Contracts/SessionCompleteRequest.cs` / `SessionCompleteResponse.cs` — the complete call shape.
- `src/GameKit.Core/Services/IIdempotencyStore.cs` — idempotency seam for duplicate submissions (D-05 / R7).

### Lobby (party + ready-check → 1v1 — R9 / D-04)
- `src/GameKit.Lobby/Services/ILobbyService.cs`, `src/GameKit.Lobby/Hubs/ILobbyClient.cs` (+ `Hubs/`) — party, ready-check, SignalR hub used for the group-queue flow and the abort path.

### Sample / packaging references
- `samples/TicTacToeDuel/Program.cs` — host wiring reference: the `AddGameKit().AddAuth().AddRankings().AddMatchmaking().AddLobby().AddGameKitAdmin().AddGameKitObservability()...` chain to mirror (then swap in the custom strategy + algorithm).
- `samples/TicTacToeDuel.GameServer/Program.cs` — existing service-token game server pattern to reuse (auth + authoritative POST).
- `samples/TicTacToeDuel/docker-compose.yml` and root `docker-compose.yml` — compose layout references (note: these define pg+redis only; this phase adds the app **image**).
- `templates/GameKit.Templates/content/GameKit.SampleGame/docker-compose.yml` — additional compose reference.
- `REUSE.toml` + `THIRD-PARTY-NOTICES.md` (repo root) — where the three.js (and any other bundled asset) SPDX entries go (R11).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **Service-token auth + session-complete pipeline:** fully built in `GameKit.Rankings/Authentication/` + `GameKit.Core` session endpoints, with idempotency already supported — the GameServer reuses this verbatim (no new auth primitive).
- **`TicTacToeDuel.GameServer`:** a working, non-stub service-token game server to copy the auth+POST pattern from.
- **`TicTacToeDuel/Program.cs`:** a complete `AddGameKit()` composition to mirror; the demo diverges by registering the custom `IMatchmakingStrategy` + custom `IRankingAlgorithm` for its ladder.
- **`GameKit.Lobby` SignalR hub + ready-check:** the party/ready-check/abort flow is already implemented; the demo wires the demo flow through it.
- **Strategy/algorithm seams:** both `IMatchmakingStrategy` and `IRankingAlgorithm` are Scrutor-discovered, selected by `Name` per ladder — dropping a custom impl into the sample assembly is the intended extension path.

### Established Patterns
- **Strategy contracts are stateless + deterministic** — the custom matchmaking strategy and ranking algorithm must hold no mutable instance state and be safe under concurrent singleton invocation.
- **`IRankingAlgorithm` is batched-only** — accumulate the rating period; never `Apply` per match.
- **Per-package migration boundary** — the demo adds no Core migrations and modifies no `GameKit.*` package; it is purely a consumer/composition.

### Integration Points
- New `Platformer3D` host registers custom strategy + custom algorithm for the demo ladder; mounts `AddGameKitAdmin()`; serves the three.js client from `wwwroot/`.
- Embedded `Platformer3D.GameServer` (`IHostedService`) terminates the client WebSocket, validates the run-summary, and posts to the session-complete endpoint via service token.
- New `Dockerfile` + single compose (app + pg + redis) packages the whole thing; `GameKit.sln` gains the two new projects.

</code_context>

<specifics>
## Specific Ideas

- **Engine:** three.js (WebGL, MIT) — user-confirmed 2026-06-22, matches the SPEC default; pure-JS WebGL, no engine binary/WASM bundle to vendor (smallest image, cleanest GPL story). Served locally — no CDN `<script>`.
- **Transport instinct:** the user originally leaned toward raw WebSockets for the game server; under the locked *async* model that becomes the validated-summary-over-WS choice (D-01/04) rather than a live position-sync socket.
- **Auth phrasing reconciliation:** an earlier note described server↔GameKit auth as "dedicated server credential → scoped JWT." The SPEC supersedes the loose phrasing — it is specifically the existing `GameKitServiceToken` scheme + `gamekit service-token issue` (same underlying JWT-issuance mechanism, no new primitive).

</specifics>

<deferred>
## Deferred Ideas

- **Best-time speedrun leaderboard (personal-best board):** considered for the ladder rule; not chosen for the core loop (head-to-head + time-margin won). A PB board is a clean future enhancement.
- **Ghost / replay of the opponent's run:** explicitly out of scope per SPEC; a natural follow-on once run-summaries are captured.
- **Full server re-simulation anti-cheat:** rejected for the demo (async, sanity-validation only); a real-time/authoritative-netcode milestone would revisit it.
- **Multiple levels / level editor / art-audio polish:** out of scope — one level proves the loop.
- **Separate-container or entrypoint-switch GameServer topology:** rejected in favor of the embedded `IHostedService` for the single-image requirement; the separate-container shape is the more production-realistic variant to revisit if the demo grows toward a deployable template.

</deferred>

---

*Phase: 21-final-demo-3d-multiplayer-platformer*
*Context gathered: 2026-06-22*
