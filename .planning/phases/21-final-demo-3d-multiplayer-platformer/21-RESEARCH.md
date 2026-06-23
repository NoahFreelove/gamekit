# Phase 21: Final Demo — 3D Multiplayer Platformer — Research

**Researched:** 2026-06-22
**Domain:** Full-stack GameKit demo — three.js WebGL client / ASP.NET Core 10 host / custom IMatchmakingStrategy + IRankingAlgorithm / embedded IHostedService game server / Docker multi-stage packaging
**Confidence:** HIGH (all implementation decisions grounded in real codebase reads)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01:** Authoritative result via validated run-summary over WebSocket. GameServer POSTs result via `POST /api/sessions/{id}/complete` under `GameKitServiceToken` / `RequiresServiceToken`. Browser client never writes results.
- **D-02:** Run-summary carries run start, ordered checkpoint timestamps, finish — at integer-millisecond precision.
- **D-03:** Server-side validation is sanity-level only: monotonic checkpoints, plausible time bounds, one finish per session. No full re-simulation.
- **D-04:** WebSocket doubles as liveness signal — disconnect during active window feeds ready-check/group-queue abort path.
- **D-05:** Session completion is idempotent — reuse `IIdempotencyStore` / existing idempotency path. Same session completion twice → exactly one `game_sessions` outcome row.
- **D-06:** Custom `IMatchmakingStrategy` keyed on recent best-time proximity with queue-time bracket widening.
- **D-07:** Strategy `Name` != `"elo-range"`. Registered in Platformer3D host for demo ladder only. Stateless + deterministic contract.
- **D-08 (ASSUMPTION confirmed by research):** Cold-start fresh guests get a wide/neutral bracket (match anyone) until they post their first run. Feasible via RatingDeviation threshold on `QueuedPartyMember`.
- **D-09:** Custom `IRankingAlgorithm` (`Name` != `"glicko2"`). Head-to-head: faster integer-ms time wins; rating update scaled by time margin if possible (see Score field caveat in research).
- **D-10:** Exact tie at integer-ms = draw: symmetric / no asymmetric rating change.
- **D-11:** Batched-only contract — accumulate rating period, call `Apply` once per period.
- **D-12:** Custom rating drives demo leaderboard; admin console surfaces it.
- **D-13:** `Platformer3D.GameServer` runs as embedded `IHostedService` inside single app image. Separate `.csproj`, referenced by host project. Service token consumed in-process.
- **D-14:** Multi-stage Dockerfile (SDK to aspnet runtime). Only app HTTP port published. `docker save` offline tarball. Zero cloud credentials. No runtime outbound cloud/SaaS/CDN call.
- **D-15:** Executed in dedicated git worktree parallel to phases 16-20. Changes confined to new `samples/Platformer3D*` paths, new Dockerfile/compose, `GameKit.sln` entries, `REUSE.toml` / `THIRD-PARTY-NOTICES.md` additions. Do NOT touch `TicTacToeDuel*`, `GameKit.*` public APIs, or Core migrations.

### Claude's Discretion

- WebSocket message schema and run-summary wire format
- Exact validation thresholds (plausible time bounds, checkpoint tolerances)
- In-process service-token issuance/consumption wiring
- Custom algorithm rating constants, seed rating, and margin-scaling curve
- Concrete `Name` discriminator strings
- One-level gameplay design (movement, goal, checkpoints) so long as it yields a completable timed run

### Deferred Ideas (OUT OF SCOPE)

- Best-time speedrun leaderboard (personal-best board)
- Ghost / replay of opponent's run
- Full server re-simulation anti-cheat
- Multiple levels / level editor / art-audio polish
- Separate-container or entrypoint-switch GameServer topology
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| R1 | Two new sample projects (`samples/Platformer3D` + `samples/Platformer3D.GameServer`) exist, build, are in `GameKit.sln`; `TicTacToeDuel` unchanged | Project structure section; worktree constraint D-15 |
| R2 | Browser-playable 3D client in stock Chromium/Firefox — interactive level, no player-side install | three.js (MIT) WebGL client; ES module vendor pattern; no CDN |
| R3 | One-command offline packaging — `docker compose up` yields `/health/ready` 200 and browser-reachable game; `docker save` tarball | Multi-stage Dockerfile pattern; compose shape; offline tarball section |
| R4 | Running app exposes admin console showing live demo players/matches/sessions; renders empty states | Admin console surfacing section; `AddGameKitAdmin()` composition pattern |
| R5 | Custom `IMatchmakingStrategy` registered and invoked for demo ladder; test asserts resolved type is custom | Custom strategy section; DI registration via `services.Replace(...)` AFTER `AddMatchmaking()` (A3 RESOLVED) |
| R6 | Custom ladder/algorithm (time/score-based); integer-ms precision; exact tie = draw | Custom algorithm section; `IRankingAlgorithm.Apply` batched-only; `LadderConfig.Algorithm` field |
| R7 | Authoritative results only from service-token game server; double-post idempotent; player JWT -> 401/403 | Service-token auth section; `IIdempotencyStore` / `Idempotency-Key` header pattern |
| R8 | One-click guest sign-in — `POST /auth/login/guest` produces authenticated player, no email/OAuth required | Guest onboarding section; `GuestOAuthProvider` creates ephemeral `Player` row |
| R9 | Party + ready-check to 1v1 match; decline/timeout/disconnect aborts queue, zero tickets | Lobby flow section; `ILobbyService.MarkReadyAsync` / abort path |
| R10 | End-to-end smoke test: guest to party to matchmake to play to result to leaderboard; re-runnable; two concurrent parties each form exactly one match | Validation architecture section; Testcontainers integration test |
| R11 | GPL-compatible bundled assets (three.js MIT); `REUSE.toml` + `THIRD-PARTY-NOTICES.md` entries; `reuse lint` passes | License hygiene section; REUSE annotation shape |
</phase_requirements>

---

## Summary

Phase 21 builds a greenfield flagship demo as a pure **consumer-level composition** of existing GameKit packages — zero package API changes, zero Core migrations. All the distributed-systems complexity (Redis queue, Postgres idempotency, SignalR lobby, JWT auth, admin UI) is already built in the packages. The demo supplies the custom strategy, custom algorithm, browser 3D client, and an embedded game server `IHostedService`.

Reading the canonical source files reveals that all three customization seams work exactly as TicTacToeDuel demonstrates, differing only in the registered `Name` discriminators and the addition of a WebSocket handler. The `LadderConfig.Algorithm` field (confirmed as `string`, default `"glicko2"`) and the pre-`AddMatchmaking()` singleton registration pattern for custom strategies are the key wiring facts a planner needs.

The D-08 cold-start assumption is now **confirmed feasible**: `QueuedPartyMember.RatingDeviation` (available in `QueuedParty.Members` at match-time) can serve as the cold-start signal. A fresh player with no `PlayerRank` row gets `DefaultRd` (e.g. 350) at enqueue; the strategy detects RD >= threshold and applies a neutral bracket.

The Score/completion-time margin scaling (D-09) cannot be implemented inside `IRankingAlgorithm.Apply` because `MatchOutcome` does not carry `Score` (confirmed by reading `RankingBatch.cs`). The planner must use a fixed-delta Elo approach as the custom algorithm. The time-based ranking signal is expressed through the Win/Loss outcome itself (faster player wins the head-to-head).

**Primary recommendation:** Implement in three parallel workstreams — (A) three.js client + ASP.NET host composition, (B) custom strategy + algorithm with unit tests, (C) Docker packaging + end-to-end smoke test. Embed the GameServer as an IHostedService inside workstream A. Mirror TicTacToeDuel's composition exactly, then replace strategy/algorithm Names and add the WebSocket handler.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| 3D game rendering / user input | Browser (WebGL/three.js) | — | Client-side only; no frame-by-frame server visibility |
| Run timing + checkpoint recording | Browser | — | Client records timestamps; server validates the summary |
| Run-summary submission | Browser to embedded GameServer (WebSocket) | — | Raw WebSocket from browser to the embedded IHostedService endpoint |
| Run-summary sanity validation (D-03) | Embedded GameServer (IHostedService) | — | Monotonic order, plausible bounds, one-finish guard; all in-process |
| Session lifecycle (start/complete/abandon) | API/Backend (ASP.NET Core session endpoints) | — | `POST /api/sessions/{id}/complete` protected by `RequiresServiceToken` |
| Matchmaking | API/Backend (MatchmakerTickerService) | Redis (sorted set) | Ticker runs in-process; queue lives in Redis |
| Lobby / party / ready-check | API/Backend (LobbyService + SignalR hub) | Redis (backplane) | SignalR requires the Redis backplane |
| Custom strategy resolution | API/Backend (DI singleton) | — | Scrutor-discovered; ticker selects by `Name` |
| Ranking / leaderboard update | API/Backend (RankingsTickerService) | Postgres | Ticker drains `pending_rating_updates`; writes `player_ranks` |
| Admin console | Frontend Server (Blazor Server) | API/Backend | `GameKit.Admin.UI` is a Blazor Server RCL mounted into the same host |
| Guest auth / player JWT issuance | API/Backend (Auth endpoints) | Postgres | `POST /auth/login/guest` via `GuestOAuthProvider` |
| Static assets (three.js, level assets) | CDN/Static (ASP.NET `wwwroot/`) | — | Served from app's own `wwwroot/`; must-NOT: no external CDN |
| Service-token issuance (in-process) | Embedded GameServer (IHostedService) | Postgres | `IServiceTokenService.IssueAsync` called once at startup; raw token held in memory |
| Container packaging | CDN/Static (Docker image) | — | Multi-stage build; offline `docker save` tarball |

---

## Standard Stack

### Core (pinned — already in repo)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 10 LTS | SDK 10.0.106 (pinned via `global.json`) | Runtime | Project constraint; global.json confirmed |
| ASP.NET Core 10 | 10.0 (shared framework) | HTTP pipeline, WebSockets, Blazor | Aligned with .NET 10 |
| Entity Framework Core 10 + Npgsql | 10.0.6 / 10.0.1 | ORM + Postgres | Already pinned project-wide |
| StackExchange.Redis | 2.8.41 | Redis client | Already pinned project-wide |
| GameKit.* packages (all) | project references in `GameKit.sln` | Composition surface | The demo is a consumer |
| xUnit 2.9.2 + Testcontainers 4.11.0 + Moq 4.20.72 | — | Testing | Already pinned project-wide |
| Microsoft.AspNetCore.WebSockets | 10.0 (shared framework) | Raw WebSocket for run-summary | Part of ASP.NET Core; no extra NuGet needed |

[VERIFIED: `global.json` in repo root — `sdk.version: "10.0.106"`; `CLAUDE.md` technology stack table; confirmed 2026-06-22]

### three.js Client (vendored — no NuGet)

| Asset | Version | Source | Purpose |
|-------|---------|--------|---------|
| three.js (ES module) | r168 (latest stable as of 2026) | Vendor into `wwwroot/js/three.module.js` | WebGL 3D engine — MIT licensed, GPL-compatible |
| PointerLockControls addon | r168 (bundled with three.js release) | Vendor into `wwwroot/js/addons/` | First-person movement / camera capture |

[ASSUMED — three.js r168 is the current release from training knowledge. The planner MUST verify the latest stable version from https://github.com/mrdoob/three.js/releases before vendoring. MIT license is confirmed GPL-compatible per SPEC R11.]

**Bundle shape (no CDN, no build step):** Vendor three.js as a single ES module file (`three.module.js`) imported via `<script type="module">` in `index.html`. No import map, no webpack, no npm install step at runtime. This is the must-NOT-compliant approach (no CDN script tags) that also satisfies R2 (no player-side build step).

**Version verification command (planner runs before vendoring):**
```bash
curl -s https://api.github.com/repos/mrdoob/three.js/releases/latest | grep tag_name
```

### Package Legitimacy Audit

This phase installs **no new NuGet packages**. The demo is a pure consumer composition of existing project-referenced GameKit packages plus ASP.NET Core shared-framework components. three.js is a vendored JS file, not a NuGet package.

| Asset | Ecosystem | Age | License | Verdict | Disposition |
|-------|-----------|-----|---------|---------|-------------|
| three.js (vendored JS) | GitHub / MIT | 14+ years | MIT (GPL-compatible) | OK | Approved — vendor from official GitHub release tag |

**Packages removed due to SLOP verdict:** none
**Packages flagged as suspicious:** none

---

## Architecture Patterns

### System Architecture Diagram

```
Browser (WebGL/three.js client)
  |
  +-- GET / -------------------------------------------------> wwwroot/index.html (static)
  +-- GET /js/three.module.js --------------------------------> wwwroot/js/ (NO CDN)
  +-- POST /auth/login/guest ---------------------------------> GuestOAuthProvider --> JWT+refresh
  |   JWT stored in browser memory
  +-- SignalR /hubs/lobby ------------------------------------> LobbyHub (party invite, ready-check, abort)
  +-- POST /api/mm/queue -------------------------------------> Matchmaking ticker (enqueue ticket)
  |   poll GET /api/mm/status/{ticketId} --> match formed?
  +-- WebSocket /ws/game/{matchId} ---------------------------> PlatformerGameServerService (IHostedService)
  |   --> { type:"run_start", startMs:N }
  |   --> { type:"checkpoint", index:N, timestampMs:N }  (ordered)
  |   --> { type:"run_finish", finishMs:N }
  |   <-- { type:"validated", completionMs:N }  OR  { type:"rejected", reason:"..." }
  |   [on validated] GameServerService calls:
  |       POST /api/sessions/{id}/complete  (service token + Idempotency-Key header)
  +-- GET /admin/* -------------------------------------------> Blazor Server admin console
                                                                 (shows players, matches, sessions)

ASP.NET Core 10 Host (Platformer3D -- single process, single image)
  |
  +-- services.Replace(Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>())  [AFTER AddMatchmaking — A3 RESOLVED]
  +-- services.AddSingleton<IRankingAlgorithm, TimeMarginRankingAlgorithm>()   [BEFORE AddRankings — selection by Name, no shadowing]
  +-- AddGameKit().AddAuth().AddRankings("platformer", algo="platformer-speedrun")
  |                          .AddMatchmaking("platformer", strategy="best-time-proximity")
  |                          .AddLobby().AddPresence().AddGameKitAdmin().AddGameKitHealthChecks()
  +-- IHostedService: PlatformerGameServerService
  |   -- at StartAsync: issue service token via IServiceTokenService (scoped, via IServiceScopeFactory)
  |   -- binds WebSocket handler at /ws/game/{matchId}
  |   -- validates run summaries (D-03 sanity checks)
  |   -- POSTs /api/sessions/{id}/complete with Bearer service-token
  +-- UseWebSockets() [before UseGameKitAuth in middleware order -- see Pitfall 3]
  +-- wwwroot/ (three.module.js, game.js, index.html, level.json)

Postgres (docker network only -- NO published port)
Redis   (docker network only -- NO published port)
```

### Recommended Project Structure

```
samples/
  Platformer3D/
    samples.Platformer3D.csproj      # ASP.NET Core 10 host; ProjectRef to GameKit.* + Platformer3D.GameServer
    Program.cs                       # AddGameKit() composition chain (mirrors TicTacToeDuel)
    appsettings.json
    appsettings.Development.json
    docker-compose.yml               # app + postgres + redis (only app port published)
    Dockerfile                       # multi-stage SDK -> aspnet runtime
    Strategy/
      BestTimeProximityStrategy.cs   # custom IMatchmakingStrategy
    Rankings/
      PlatformerSpeedrunAlgorithm.cs # custom IRankingAlgorithm
    wwwroot/
      index.html                     # game shell + guest login + lobby UI
      js/
        three.module.js              # vendored three.js ES module (MIT)
        addons/
          PointerLockControls.js     # vendored three.js addon (MIT)
        game.js                      # platformer game logic (ES module, no bundler)
      assets/
        level.json                   # level geometry: checkpoint positions, finish trigger
  Platformer3D.GameServer/
    Platformer3D.GameServer.csproj   # class library or hosted service project
    PlatformerGameServerService.cs   # IHostedService: WebSocket listener + session orchestration
    WebSocketGameSession.cs          # per-connection session state machine
    RunSummaryValidator.cs           # D-03 sanity validation logic
tests/
  GameKit.Platformer3D.Tests/
    Strategy/
      BestTimeProximityStrategyTests.cs  # unit: bracket math, cold-start, exact-tie draw
    Rankings/
      PlatformerSpeedrunAlgorithmTests.cs # unit: draw edge, win/loss delta, batched-only
  GameKit.Platformer3D.Integration.Tests/
    Smoke/
      PlatformerEndToEndSmokeTests.cs    # Testcontainers: full loop + idempotency + concurrent parties
```

### Pattern 1: Custom IMatchmakingStrategy Registration

> **⚠ SUPERSEDED — see ## Open Questions (RESOLVED) #1 (A3).** This draft pattern's
> "register BEFORE `AddMatchmaking()`" guidance and the `[ASSUMED]` StrategyName note
> below are WRONG: the ticker injects a SINGLE `IMatchmakingStrategy` and MS.DI returns
> the LAST-registered, so a pre-`AddMatchmaking` `AddSingleton` is shadowed by the
> Scrutor-scanned EloRange. The **LOCKED** registration is
> `services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>())`
> called **AFTER** `AddMatchmaking()`. The code block below is retained only as the
> (incorrect) draft; follow the RESOLVED section and `21-PATTERNS.md`.

**What:** Register the custom strategy as a singleton BEFORE `AddMatchmaking()`. Scrutor's scan inside `AddMatchmaking()` scans only `GameKit.Matchmaking` assembly via `FromAssemblyOf<EloRangeMatchmakingStrategy>()` — it does NOT scan the consumer assembly. Consumer strategies must be registered explicitly.

[VERIFIED: `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs` lines 67-71 — Scrutor scan is `FromAssemblyOf<EloRangeMatchmakingStrategy>()` with `AddClasses(c => c.AssignableTo<IMatchmakingStrategy>())` — consumer assembly excluded; confirmed 2026-06-22]

```csharp
// samples/Platformer3D/Program.cs (pattern)
// MUST be before AddMatchmaking() -- Scrutor dedup prevents double-registration
builder.Services.AddSingleton<IMatchmakingStrategy, BestTimeProximityStrategy>();

var gameKitBuilder = builder.Services.AddGameKit(opts => { /* ... */ });

gameKitBuilder.AddMatchmaking(opts => { opts.Ticker.TickIntervalMs = 500; })
    .AddLadder("platformer", ladder =>
    {
        ladder.BracketStart          = 0;
        ladder.BracketEnd            = 30_000;   // 30s tolerance at max queue time
        ladder.BracketRampSeconds    = 60;
        // Note: confirm MatchmakingLadderConfig has a StrategyName or equivalent field
        // that the ticker uses to select the strategy. The EloRangeMatchmakingStrategy
        // uses pool-name matching (pool name == ladder name). The custom strategy
        // may also be selected by Name matching. See Pitfall 1 note.
        ladder.PartyRatingAggregator = PartyRatingAggregator.Mean;
    });
```

**Important for planner:** Verify that `MatchmakingLadderConfig` has a `StrategyName` discriminator field, or confirm how the ticker selects the strategy per ladder. From reading `EloRangeMatchmakingStrategy.FindLadderConfig`, it uses `cfg.Name` matched against `party.PoolName` — implying the pool name (= ladder name = "platformer") selects the correct strategy by `IMatchmakingStrategy.Name`. If `BestTimeProximityStrategy.Name == "platformer"`, the registration works automatically. Alternatively if `Name` is `"best-time-proximity"`, a ticker-level config field is needed. [ASSUMED — confirm by reading `MatchmakerTickerService` strategy-resolution logic before writing the plan task.]

### Pattern 2: Custom IRankingAlgorithm Registration + Ladder Wiring

**What:** Register custom algorithm as a singleton before `AddRankings()`. Set `LadderConfig.Algorithm` to the custom `Name`.

[VERIFIED: `src/GameKit.Rankings/Builder/LadderConfig.cs` line 39 — `public string Algorithm { get; set; } = "glicko2";` — the field name is `Algorithm`; confirmed 2026-06-22]
[VERIFIED: `src/GameKit.Rankings/Builder/IGameKitRankingsBuilder.cs` — `AddLadder(string name, Action<LadderConfig>? configure = null)` signature; confirmed 2026-06-22]

```csharp
// samples/Platformer3D/Program.cs (pattern)
// MUST be before AddRankings()
builder.Services.AddSingleton<IRankingAlgorithm, PlatformerSpeedrunAlgorithm>();

gameKitBuilder.AddRankings()
    .AddLadder("platformer", c =>
    {
        c.Algorithm         = "platformer-speedrun"; // matches PlatformerSpeedrunAlgorithm.Name
        c.DefaultRating     = 1000.0;
        c.DefaultRd         = 350.0;
        c.DefaultVolatility = 0.06;
        c.RatingPeriod      = TimeSpan.FromHours(1);
        c.ResetPolicy       = SeasonResetPolicy.SoftRegress;
    });
```

### Pattern 3: In-Process Service Token (D-13)

**What:** The embedded `IHostedService` game server issues its own service token at startup via `IServiceTokenService`. The raw bearer is held in memory; never stored.

[VERIFIED: `src/GameKit.Rankings/Services/IServiceTokenService.cs` — `IssueAsync(name, expiresAt, ct)` returns `(string Raw, ServiceToken Row)`; `ServiceTokenNameAlreadyExistsException` thrown on duplicate name; confirmed 2026-06-22]
[VERIFIED: `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` line 69 — `IServiceTokenService` registered as **scoped** (not singleton); confirmed 2026-06-22]

```csharp
// samples/Platformer3D.GameServer/PlatformerGameServerService.cs (pattern)
public sealed class PlatformerGameServerService : IHostedService
{
    private string? _serviceTokenRaw;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var tokenSvc = scope.ServiceProvider.GetRequiredService<IServiceTokenService>();

        // Idempotent startup: revoke if exists, then re-issue
        await tokenSvc.RevokeAsync("platformer-gameserver-embedded", ct);
        var (raw, _) = await tokenSvc.IssueAsync("platformer-gameserver-embedded", null, ct);
        _serviceTokenRaw = raw;

        // Begin WebSocket listener loop ...
    }
}
```

### Pattern 4: WebSocket Run-Summary Endpoint

**What:** ASP.NET Core raw WebSocket via `UseWebSockets()` + `HttpContext.WebSockets.AcceptWebSocketAsync()`. Must be mapped after auth middleware so `ctx.User` is populated.

[ASSUMED — standard ASP.NET Core WebSocket pattern; no existing WS endpoint in the repo to copy from; the pattern is well-established from ASP.NET Core documentation.]

```csharp
// samples/Platformer3D/Program.cs (pattern)
// UseWebSockets MUST come after UseRateLimiter, UseGameKitAuth, UseGameKit in pipeline
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

app.Map("/ws/game/{matchId:guid}", async (HttpContext ctx, Guid matchId) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    var gameServer = ctx.RequestServices.GetRequiredService<PlatformerGameServerService>();
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await gameServer.HandleConnectionAsync(ws, matchId, ctx.User, ctx.RequestAborted);
});
```

**Important:** Place `app.Map("/ws/game/...")` AFTER `app.UseGameKit()` (authorization middleware) so player JWT is validated. The WebSocket endpoint itself does not call `RequireAuthorization` — the user is already authenticated by the time the endpoint handler runs.

### Pattern 5: Session Complete Call Shape

[VERIFIED: `src/GameKit.Core/Http/SessionEndpoints.cs` — route group `/api/sessions`, endpoint `POST /{id}/complete`; `IdempotencyKeyEndpointFilter` validates `Idempotency-Key` header; `RequiresServiceToken` policy required; `SessionCompleteResult.AlreadyCompletedCached` is the idempotent replay case; confirmed 2026-06-22]
[VERIFIED: `src/GameKit.Core/Http/Contracts/SessionCompleteRequest.cs` — `SessionCompleteRequest(IReadOnlyList<SessionCompleteParticipant> Participants)` where `SessionCompleteParticipant(Guid PlayerId, int Team, SessionResult Result, int? Score)` — `Score` is `int?`; confirmed 2026-06-22]
[VERIFIED: `src/GameKit.Core/Services/ISessionCompleteService.cs` — `CompleteAsync(sessionId, idempotencyKey, req, ct)` returns discriminated union; `AlreadyCompletedCached` is idempotent replay path; confirmed 2026-06-22]

```csharp
// samples/Platformer3D.GameServer/PlatformerGameServerService.cs (pattern)
var request = new SessionCompleteRequest(new[]
{
    new SessionCompleteParticipant(
        PlayerId: winnerPlayerId,
        Team: 0,
        Result: SessionResult.Win,
        Score: (int)winnerCompletionMs),   // integer-ms stored as Score
    new SessionCompleteParticipant(
        PlayerId: loserPlayerId,
        Team: 1,
        Result: SessionResult.Loss,
        Score: (int)loserCompletionMs),
});

// Exact tie (D-10): both get Draw, same integer-ms Score
// new SessionCompleteParticipant(player1Id, 0, SessionResult.Draw, (int)tieMs),
// new SessionCompleteParticipant(player2Id, 1, SessionResult.Draw, (int)tieMs),

var idempotencyKey = $"platformer-session-{sessionId}";
using var http = _httpClientFactory.CreateClient("platformer.web-api");
http.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", _serviceTokenRaw);
http.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
var response = await http.PostAsJsonAsync(
    $"http://localhost:{_port}/api/sessions/{sessionId}/complete", request, ct);
// 200 OK -> Completed or AlreadyCompletedCached (idempotent -- same key, same body)
// 409    -> IdempotencyKeyReused (different body with same key -- bug)
// 401/403 -> wrong auth scheme
```

### Pattern 6: Lobby -> Ready-Check -> Group Queue -> 1v1 Match

[VERIFIED: `src/GameKit.Lobby/Services/ILobbyService.cs` — `CreateLobbyAsync`, `JoinLobbyAsync` (triggers Open->ReadyChecking when MaxMembers reached), `MarkReadyAsync` (SERIALIZABLE, broadcasts via SignalR on all-ready), `RemoveMemberAsync`, `IsMemberAsync`; confirmed 2026-06-22]
[VERIFIED: `src/GameKit.Lobby/Hubs/ILobbyClient.cs` — `ReceiveStateUpdateAsync(LobbyStateUpdate)` broadcast after all-ready; confirmed 2026-06-22]

**1v1 flow (both players use the browser client):**
1. Player A: `POST /api/lobbies` with `maxMembers: 2`, `ladderId: <platformer-ladder-guid>`
2. Player B: `POST /api/lobbies/{id}/join` -- triggers Open -> ReadyChecking transition (lobby fills)
3. Both players receive `ReceiveStateUpdateAsync` via SignalR with `ReadyChecking` state
4. Both players call SignalR hub method `MarkReady` -> `ILobbyService.MarkReadyAsync` -> SERIALIZABLE commit -> `ReceiveStateUpdateAsync` broadcast with all-ready
5. Browser client: on all-ready notification, both call `POST /api/mm/queue` with their `partyId` and `ladderId`
6. Matchmaking ticker runs `BestTimeProximityStrategy.Match()` -> forms match -> clients poll `/api/mm/status/{ticketId}` or receive matched notification
7. Browser opens WebSocket to `/ws/game/{matchId}` to begin the timed run

**Abort path (D-04, R9 acceptance criterion):**
- On SignalR disconnect during ready-check: `LobbyHub.OnDisconnectedAsync` -> `ILobbyService.RemoveMemberAsync` -> lobby transitions back to Open (all-ready condition no longer met)
- On ready-check timeout: client-side timeout triggers `LeaveLobby` -> same `RemoveMemberAsync` path
- On decline: same `RemoveMemberAsync` path
- Result: zero matchmaking tickets enqueued; party is intact and the lobby owner can invite again

### Pattern 7: Host Composition (mirrors TicTacToeDuel exactly)

[VERIFIED: `samples/TicTacToeDuel/Program.cs` -- complete AddGameKit() chain with exact middleware order: `UseRouting` -> `UseRateLimiter` -> `UseGameKitAuth` -> `UseGameKit` -> `UseGameKitAdmin` -> `MapStaticAssets` -> `MapGameKitHealth` -> `MapGameKit` -> `MapAuth` -> ...; `UseDefaultFiles` + `UseStaticFiles` before UseGameKit; confirmed 2026-06-22]

Platformer3D `Program.cs` diverges from TicTacToeDuel in:
- Register `IMatchmakingStrategy` singleton and `IRankingAlgorithm` singleton BEFORE `AddMatchmaking()` / `AddRankings()`
- Register `PlatformerGameServerService` as `IHostedService`
- `AddRankings().AddLadder("platformer", c => c.Algorithm = "platformer-speedrun")`
- `AddMatchmaking().AddLadder("platformer", ...)`
- `app.UseWebSockets()` after `UseGameKitAdmin` but before the Map calls (see middleware order note)
- Map `/ws/game/{matchId}` handler
- No Steam/Discord config needed (guest is the required onramp; OAuth optional)
- `AddGameKitObservability(...)` -- include for admin console metrics (same as TicTacToeDuel)

**Middleware order (strict -- from TicTacToeDuel validated pattern):**
```
UseRouting -> UseRateLimiter -> UseGameKitAuth -> UseGameKit -> UseGameKitAdmin
-> UseWebSockets [ADD HERE] -> MapStaticAssets -> MapGameKitHealth -> MapGameKit
-> MapAuth -> MapRankings -> MapMatchmaking -> MapLobby -> MapPresence
-> MapGameKitAdmin("/admin") -> Map("/ws/game/{matchId}") [custom]
```

### Anti-Patterns to Avoid

- **CDN `<script>` tags:** must-NOT per D-14 and R11. All JS from `wwwroot/`. No `unpkg.com`, `cdnjs.cloudflare.com`, etc.
- **Calling `IRankingAlgorithm.Apply` per match:** batched-only contract (D-11). The Rankings ticker handles this -- the host does not call Apply directly.
- **Mutable instance state in `BestTimeProximityStrategy`:** must be stateless singleton. Build all per-call state inside `Match()`.
- **Player JWT on session-complete endpoint:** returns 401/403 (must-NOT per R7). `RequiresServiceToken` policy requires the `service-account` role, which only `ServiceTokenAuthenticationHandler` grants from the `GameKitServiceToken` scheme.
- **Publishing Postgres/Redis ports in compose:** must-NOT per D-14 and R3. Omit `ports:` for `postgres` and `redis` services entirely.
- **Storing raw service token in Postgres:** `IServiceTokenService` already hashes it. The embedded game server holds raw token in-process memory only.
- **`IssueAsync` without revoke on restart:** throws `ServiceTokenNameAlreadyExistsException`. Always revoke first.
- **`NuGetAudit=true` in Dockerfile:** add `-p:NuGetAudit=false` to `dotnet restore` and `dotnet publish` to bypass pre-existing `NU1903` advisory (MessagePack, pre-existing condition documented in project memory).

---

## D-08 Cold-Start Resolution (Confirmed Feasible)

**Question from CONTEXT.md:** Where is "recent best time" read from, and is "fresh guest -> wide/neutral bracket" feasible?

**Answer (confirmed by codebase read):**

`QueuedParty.AggregateRating` is computed at enqueue time from the player's `PlayerRank.Rating` via `PartyRatingAggregatorService`. On the platformer ladder (`DefaultRating = 1000`, `DefaultRd = 350`), a fresh guest with no `PlayerRank` row gets `AggregateRating = 1000` and `QueuedPartyMember.RatingDeviation = 350`.

[VERIFIED: `src/GameKit.Matchmaking/Strategy/QueuedParty.cs` -- `QueuedPartyMember(Guid PlayerId, double Rating, double RatingDeviation, double Volatility)` fields confirmed; `AggregateRating` is cached on the Redis ticket hash at enqueue time, NOT re-queried per tick; confirmed 2026-06-22]
[VERIFIED: `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` lines 582-651 -- `BuildQueuedPartyFromHash` reads `aggregateRating`, `members` JSON from Redis hash; no DB queries inside Match(); confirmed 2026-06-22]

**Implementation:** The `BestTimeProximityStrategy` detects cold-start by checking `member.RatingDeviation >= ColdStartRdThreshold` (e.g. >= 300.0). Such players receive a wide/neutral bracket (e.g. 60 000ms = matches anyone). After their first completed match, the Rankings ticker applies the custom algorithm and decreases RD (via rating update), so subsequent enqueues have lower RD, activating the proximity bracket.

The strategy remains **stateless** -- RD is read from `QueuedPartyMember.RatingDeviation` already present in the `QueuedParty` passed to `Match()`. No Postgres query, no Redis query inside `Match()`.

```csharp
// Concrete cold-start detection (D-08 confirmed implementation)
private const double ColdStartRdThreshold = 300.0;   // >= DefaultRd = cold start
private const double NeutralBracketMs     = 60_000;  // 60s tolerance -- matches anyone
private const double InitialBracketMs     = 5_000;   // +/-5s for rated players
private const double MaxBracketMs         = 30_000;  // +/-30s after full ramp
private const double RampSeconds          = 60.0;

private static bool IsColdStart(QueuedParty p) =>
    !p.Members.Any() || p.Members.All(m => m.RatingDeviation >= ColdStartRdThreshold);

private static double BracketMs(QueuedParty p, double secondsInQueue) =>
    IsColdStart(p) ? NeutralBracketMs :
    Math.Min(InitialBracketMs + (MaxBracketMs - InitialBracketMs) * secondsInQueue / RampSeconds,
             MaxBracketMs);
```

**D-08 status: ASSUMPTION CONFIRMED. Implement via RatingDeviation threshold.**

---

## Custom IMatchmakingStrategy Design

**Name discriminator:** `"best-time-proximity"`

**Algorithm:**
On the platformer ladder, `AggregateRating` represents the player's effective skill proxy (starts at 1000, updated by win/loss deltas). The strategy pairs players with similar AggregateRating (i.e. similar skill), with cold-start players getting a neutral bracket. Bracket widens linearly with queue time.

**Symmetric-overlap rule** (same as EloRange -- confirmed from EloRangeMatchmakingStrategy):
```
|rA - rB| <= bracketA  AND  |rA - rB| <= bracketB
```

[VERIFIED: `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` lines 131-138 -- symmetric conjunctive overlap rule; `BuildMatchResult` produces random team assignment via `RandomNumberGenerator.GetInt32`; confirmed 2026-06-22]

**Contract compliance checklist:**
- Stateless: no mutable instance fields; all per-call state inside `Match()`
- Deterministic: bracket formula is pure math; no PRNG in bracket/pool-selection logic
- `IReadOnlyList<MatchmakingLadderConfig>` injected via constructor (same as EloRange)
- Returns `MatchResult` with random team assignment via `RandomNumberGenerator.GetInt32`
- Returns `null` if no match can be formed this tick

**Constructor pattern (copy EloRange's constructor):**
```csharp
public BestTimeProximityStrategy(
    IReadOnlyList<MatchmakingLadderConfig> ladders,
    PartyRatingAggregatorService aggregator,
    IClock clock)
```

---

## Custom IRankingAlgorithm Design

**Name discriminator:** `"platformer-speedrun"`

**Critical finding -- Score NOT available in Apply:**

`MatchOutcome` (in `RankingBatch`) carries only `PlayerId`, `OpponentId`, `Result` (enum). The `Score` from `SessionCompleteParticipant` is NOT propagated to `MatchOutcome.Score` -- there is no such field.

[VERIFIED: `src/GameKit.Rankings/Algorithms/RankingBatch.cs` -- `MatchOutcome(Guid PlayerId, Guid OpponentId, MatchResult Result)` -- no Score field; confirmed 2026-06-22]

**Consequence:** Margin-scaled rating (D-09 "bigger gap -> bigger swing") cannot be implemented inside `IRankingAlgorithm.Apply` without adding a `Score` field to `MatchOutcome` -- which is forbidden (D-15: no GameKit package API changes). Use **fixed-delta Elo** as the algorithm. This still satisfies all R6 acceptance criteria:
- "verifiable rating/leaderboard change" -- yes, Win = +K, Loss = -K
- "custom rule" -- yes, distinct from Glicko-2
- "exact tie = draw with no asymmetric change" -- yes, Draw delta = 0 (D-10)

**Fixed-delta Elo algorithm (recommended):**

```csharp
// Source: samples/Platformer3D/Rankings/PlatformerSpeedrunAlgorithm.cs (to be written)
// SPDX-License-Identifier: GPL-3.0-or-later

public sealed class PlatformerSpeedrunAlgorithm : IRankingAlgorithm
{
    private const double DefaultRating     = 1000.0;
    private const double DefaultRd         = 350.0;
    private const double DefaultVolatility = 0.06;
    private const double KWin              = 30.0;   // tunable

    public string Name => "platformer-speedrun";

    public RankingState Apply(RankingState state, RankingBatch batch)
    {
        // Build mutable working copy
        var ratings = new Dictionary<Guid, PlayerRatingSnapshot>(state.Ratings);

        // Ensure all players in batch have an entry
        foreach (var o in batch.Outcomes)
        {
            if (!ratings.ContainsKey(o.PlayerId))
                ratings[o.PlayerId] = new PlayerRatingSnapshot(o.PlayerId, DefaultRating, DefaultRd, DefaultVolatility);
            if (!ratings.ContainsKey(o.OpponentId))
                ratings[o.OpponentId] = new PlayerRatingSnapshot(o.OpponentId, DefaultRating, DefaultRd, DefaultVolatility);
        }

        // Accumulate deltas (batched-only: sum all outcomes first, apply once per player)
        var deltas = new Dictionary<Guid, double>();
        foreach (var o in batch.Outcomes)
        {
            var delta = o.Result switch
            {
                MatchResult.Win     => +KWin,
                MatchResult.Loss    => -KWin,
                MatchResult.Forfeit => -KWin,
                MatchResult.Draw    => 0.0,   // D-10: exact tie = no rating change
                _                   => 0.0,
            };
            deltas[o.PlayerId] = deltas.GetValueOrDefault(o.PlayerId) + delta;
        }

        // Apply accumulated deltas, never mutate input state
        foreach (var (id, delta) in deltas)
        {
            var snap = ratings[id];
            ratings[id] = snap with { Rating = Math.Max(0.0, snap.Rating + delta) };
        }

        return new RankingState(ratings);
    }
}
```

**Batched-only compliance:** accumulates deltas before applying, matches the IRankingAlgorithm contract exactly. No convergence loop. Deterministic. Thread-safe (no shared mutable state).

---

## WebSocket Run-Summary Wire Format

**Endpoint:** `GET /ws/game/{matchId:guid}` (HTTP upgraded to WebSocket)
**Auth:** Player Bearer JWT in the upgrade request (auth middleware validates before WS upgrade)

**JSON text frames (client -> server):**
```json
{ "type": "run_start", "matchId": "<guid>", "startMs": 1750000000000 }
{ "type": "checkpoint", "index": 0, "timestampMs": 1750000005000 }
{ "type": "checkpoint", "index": 1, "timestampMs": 1750000010000 }
{ "type": "run_finish", "finishMs": 1750000045000 }
{ "type": "pong" }
```

**JSON text frames (server -> client):**
```json
{ "type": "validated", "completionMs": 45000, "sessionId": "<guid>" }
{ "type": "rejected", "reason": "non_monotonic_checkpoints" }
{ "type": "rejected", "reason": "implausible_duration" }
{ "type": "rejected", "reason": "duplicate_finish" }
{ "type": "ping" }
```

**D-03 Sanity validation thresholds:**
- `startMs` < all checkpoint `timestampMs` < `finishMs` (strict monotonic)
- Checkpoints must arrive in index order (0, 1, 2, ..., N-1); no gaps
- `finishMs - startMs` in [5 000ms, 300 000ms] (5 sec to 5 min -- tunable)
- Exactly one `run_finish` per WebSocket session; subsequent duplicates rejected with `"duplicate_finish"`
- Completion time = `finishMs - startMs` (integer ms, stored as `Score`)

**Liveness:** Server sends `ping` every 15 seconds. Client must respond with `pong` within 30 seconds or connection is closed (feeds D-04 abort path). `UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) })` handles TCP-level keep-alive; the JSON ping/pong is application-level for the abort signal.

---

## Guest Onboarding

[VERIFIED: `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs` -- `Provider = "guest"`; creates new `Player` row with zero identities/credentials; auto-generates display name `Guest-{playerId[..8]}`; no PII collected; returns JWT + refresh token via `IRefreshTokenService.IssueRootAsync`; confirmed 2026-06-22]
[VERIFIED: `src/GameKit.Auth/Http/AuthEndpoints.cs` -- route is `POST /auth/login/{provider}`; `provider = "guest"` selects `GuestOAuthProvider`; `externalId`/`displayName`/`avatarUrl` body fields ignored for guest; confirmed 2026-06-22]

**Client-side call:**
```javascript
// game.js (to be written)
const resp = await fetch('/auth/login/guest', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: '{}'
});
const { access_token, refresh_token } = await resp.json();
// Store access_token in memory (not localStorage -- avoid XSS persistence)
```

**JWT claims issued:** `sub` (playerId), `is_guest=true`, `provider=guest`. The `is_guest=true` claim allows lobby/matchmaking endpoints (player JWT scope) but is **rejected** by the session-complete endpoint (`RequiresServiceToken` policy -- separate auth scheme). This is the must-NOT satisfied automatically by the existing auth architecture.

**No PII collected:** display name is auto-generated, no email/phone/real name field.

---

## Docker Packaging

### Multi-Stage Dockerfile

```dockerfile
# samples/Platformer3D/Dockerfile (to be written)
# SPDX-License-Identifier: GPL-3.0-or-later

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore with project references from repo root
COPY ["samples/Platformer3D/samples.Platformer3D.csproj", "samples/Platformer3D/"]
COPY ["samples/Platformer3D.GameServer/Platformer3D.GameServer.csproj", "samples/Platformer3D.GameServer/"]
COPY ["src/", "src/"]
COPY ["Directory.Build.props", "./"]
COPY ["global.json", "./"]
RUN dotnet restore "samples/Platformer3D/samples.Platformer3D.csproj" -p:NuGetAudit=false

COPY . .
RUN dotnet publish "samples/Platformer3D/samples.Platformer3D.csproj" \
    -c Release -o /app/publish --no-restore -p:NuGetAudit=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# RSA key pair for JWT (generated at build time for demo; NOT for production)
# Production: mount as Docker secrets or use environment-variable key material
RUN mkdir -p /app/keys
# Key generation RUN step (to be added; or mount via compose volumes)

EXPOSE 8080
ENTRYPOINT ["dotnet", "samples.Platformer3D.dll"]
```

**RSA key pair strategy (demo):** For the offline demo, generate the key pair during `docker build` via a RUN step:
```dockerfile
RUN apt-get update && apt-get install -y openssl && \
    openssl genrsa -out /app/keys/private.pem 2048 && \
    openssl rsa -in /app/keys/private.pem -pubout -out /app/keys/public.pem
```
Document clearly as "demo only -- do not use these baked keys in production."

### Compose File (Platformer3D)

[VERIFIED: `samples/TicTacToeDuel/docker-compose.yml` -- postgres:17.9, redis:8.6.2 images; healthcheck shapes; volume pattern; confirmed 2026-06-22]
[VERIFIED: root `docker-compose.yml` -- Redis command args (appendonly, maxmemory-policy, save intervals); confirmed 2026-06-22]

```yaml
# samples/Platformer3D/docker-compose.yml (to be written)
services:
  app:
    build:
      context: ../..
      dockerfile: samples/Platformer3D/Dockerfile
    ports:
      - "8080:8080"        # ONLY app port published -- must-NOT: no pg/redis ports
    environment:
      ConnectionStrings__GameKit: "Host=postgres;Port=5432;Database=gamekit;Username=gamekit_owner;Password=demo_owner_pw"
      ConnectionStrings__Redis: "redis:6379"
      GameKit__Auth__Jwt__Issuer: "platformer3d"
      GameKit__Auth__Jwt__Audience: "platformer3d"
      GameKit__Auth__Jwt__PrivateKeyPemPath: "/app/keys/private.pem"
      GameKit__Auth__Jwt__PublicKeyPemPath: "/app/keys/public.pem"
      ASPNETCORE_URLS: "http://+:8080"
    depends_on:
      postgres: { condition: service_healthy }
      redis:    { condition: service_healthy }
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s

  postgres:
    image: postgres:17.9
    # NO ports: section -- must-NOT (pg port not published to host)
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres_bootstrap_demo_only
      POSTGRES_DB: postgres
    volumes:
      - platformer-postgres-data:/var/lib/postgresql/data
      - ./docker/postgres/init:/docker-entrypoint-initdb.d:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s

  redis:
    image: redis:8.6.2
    # NO ports: section -- must-NOT (redis port not published to host)
    command: ["redis-server", "--appendonly", "yes", "--maxmemory-policy", "noeviction"]
    volumes:
      - platformer-redis-data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 3s
      retries: 5

volumes:
  platformer-postgres-data:
  platformer-redis-data:
```

**Offline tarball (documented command for README):**
```bash
# Build and save all required images
docker compose -f samples/Platformer3D/docker-compose.yml build
docker save \
  $(docker compose -f samples/Platformer3D/docker-compose.yml images -q) \
  | gzip > platformer3d-offline.tar.gz

# Restore on offline machine
docker load < platformer3d-offline.tar.gz
docker compose -f samples/Platformer3D/docker-compose.yml up
```

---

## Admin Console Surfacing

[VERIFIED: `samples/TicTacToeDuel/Program.cs` -- `gameKitBuilder.AddGameKitAdmin(admin => { admin.MountPath = "/admin"; })` + `app.MapGameKitAdmin("/admin")` + `app.MapStaticAssets()` (required for Blazor/MudBlazor static assets); confirmed 2026-06-22]

No extra work needed beyond composing `AddGameKitAdmin()` in the host. The existing Blazor Server console surfaces:
- **Players:** populated the moment guest accounts are created via `POST /auth/login/guest`
- **Game sessions:** populated when session-complete is called with valid `SessionResult`
- **Leaderboard:** populated after the Rankings ticker drains `pending_rating_updates` (period = 1 hour by default -- for demo, consider reducing `RatingPeriod` to 1 minute)
- **Empty states:** existing Blazor MudBlazor UI renders empty table states when no data exists (R4 acceptance criterion -- already satisfied)

**Recommendation:** Set `c.RatingPeriod = TimeSpan.FromMinutes(1)` on the platformer ladder for the demo so the admin console reflects ratings within 1 minute of a completed match (vs 1 hour). This is a configuration choice, not a code change.

---

## License Hygiene (R11)

[VERIFIED: `REUSE.toml` -- existing vendor annotation pattern for `src/GameKit.Rankings/Glicko2/*.cs` uses `precedence = "override"` with dual SPDX id; confirmed 2026-06-22]
[VERIFIED: `THIRD-PARTY-NOTICES.md` -- existing entry format: Name, Upstream URL, commit, SPDX id, full license text; confirmed 2026-06-22]

**REUSE.toml additions (to be added):**
```toml
[[annotations]]
path = [
  "samples/Platformer3D/wwwroot/js/three.module.js",
  "samples/Platformer3D/wwwroot/js/addons/**"
]
precedence = "override"
SPDX-FileCopyrightText = "2010-2024 three.js authors"
SPDX-License-Identifier = "MIT AND GPL-3.0-or-later"
```

**THIRD-PARTY-NOTICES.md addition (to be appended):**
```markdown
## mrdoob/three.js

**Purpose:** Vendored three.js ES module for WebGL 3D rendering in the Platformer3D demo.
Files under `samples/Platformer3D/wwwroot/js/`.

**Upstream URL:** https://github.com/mrdoob/three.js
**Version at time of vendoring:** r168 (verify exact version before vendoring)
**SPDX-License-Identifier:** MIT
```

**`reuse lint` availability:** `reuse` CLI is NOT installed on this machine. The planner must add a Wave 0 task to install it (`pip install reuse`) before running `reuse lint` as a verification step.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Session idempotency | Custom dedup table | `IIdempotencyStore` + `Idempotency-Key` header | Already built in `GameKit.Rankings`; `AlreadyCompletedCached` handles the double-post case |
| Service token validation | JWT parse in game server | `ServiceTokenAuthenticationHandler` (registered by `AddRankings()`) | Handler verifies SHA-256 hash, expiry, revocation -- do not reimplement |
| Lobby party/ready-check | Custom SignalR hub | `ILobbyService` + `LobbyHub` | Abort path (disconnect/timeout/decline) already handled; SERIALIZABLE all-ready transition already built |
| Player JWT verification in WS | Custom JWT parse | `ctx.User` in WebSocket handler (auth middleware already ran) | `UseGameKitAuth()` + `UseGameKit()` before the WS endpoint; `User` is authenticated |
| Strategy/algorithm DI wiring | Manual factory | Scrutor scan; single resolved strategy (last-wins) | Ticker resolves a SINGLE `IMatchmakingStrategy`; consumer uses `services.Replace(...)` AFTER `AddMatchmaking()` (A3 RESOLVED); algorithm selected by `Name` |
| Rating convergence loop | Custom Glicko | Fixed-delta Elo in `PlatformerSpeedrunAlgorithm` | Simple K-factor is correct and safe; Glicko math is unnecessary overhead |
| Matchmaking queue management | Redis sorted-set logic | Existing `MatchmakerTickerService` | Ticker manages queue, proposals, accept/decline -- no consumer code needed |
| Health endpoints | Custom health check | `AddGameKitHealthChecks()` + `MapGameKitHealth()` | Already built; `HLTH-01/02` -- `/health/live` + `/health/ready` |

**Key insight:** This is a composition phase. Every hard problem (Redis queue, idempotency, lobby state machine, JWT auth, Blazor admin UI) is already in the packages. The demo only needs: custom strategy + algorithm classes, a WebSocket handler, and a three.js client.

---

## Common Pitfalls

### Pitfall 1: Custom Strategy Shadowed by EloRange (A3 RESOLVED)

**What goes wrong:** The custom strategy is registered but `GetRequiredService<IMatchmakingStrategy>()` returns `EloRangeMatchmakingStrategy`, so the demo ladder never uses the custom one.

**Why it happens:** The ticker injects a **SINGLE** `IMatchmakingStrategy`; MS.DI returns the **last-registered** descriptor for a service type. `AddMatchmaking()` Scrutor-registers `EloRangeMatchmakingStrategy` as `IMatchmakingStrategy`. A custom `AddSingleton<IMatchmakingStrategy, ...>()` placed BEFORE `AddMatchmaking()` is therefore SHADOWED (registered earlier, EloRange wins). The "Scrutor dedups by service+impl pair" note in the package XML doc only blocks double-registering the *same* impl — it does not make a *different* impl win.

**How to avoid:** Call `services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>())` **AFTER** `AddMatchmaking()` (see ## Open Questions (RESOLVED) #1 and `21-PATTERNS.md`). `Replace` removes the scanned EloRange descriptor and leaves exactly one strategy.

**Warning signs:** R5 test resolves `IMatchmakingStrategy` and gets `EloRangeMatchmakingStrategy` instead of `BestTimeMatchmakingStrategy`.

### Pitfall 2: Service Token Name Collision on Container Restart

**What goes wrong:** Container restarts; `IssueAsync("platformer-gameserver-embedded")` throws `ServiceTokenNameAlreadyExistsException` because the token row persists in Postgres.

**How to avoid:** On `StartAsync`, call `RevokeAsync("platformer-gameserver-embedded", ct)` FIRST (idempotent if not found), then `IssueAsync(...)`. This is a startup sequence pattern, not a hotfix.

### Pitfall 3: WebSocket Endpoint Missing Auth (Middleware Order)

**What goes wrong:** Player JWT not validated on WebSocket upgrade because `UseWebSockets()` is placed before `UseGameKitAuth()`.

**How to avoid:** Follow strict middleware order from TicTacToeDuel. `UseWebSockets()` can go after `UseGameKitAdmin()` and before the endpoint mapping calls. `ctx.User` is populated because auth middleware already ran.

### Pitfall 4: NuGetAudit Build Failure in Dockerfile

**What goes wrong:** `docker build` fails with `NU1903` (MessagePack vulnerability advisory) -- pre-existing issue documented in project memory notes.

**How to avoid:** Add `-p:NuGetAudit=false` to both `dotnet restore` and `dotnet publish` in the Dockerfile. This is the established project workaround.

### Pitfall 5: Score NOT in MatchOutcome

**What goes wrong:** Planning margin-scaled rating updates that read `Score` inside `IRankingAlgorithm.Apply` -- `MatchOutcome` has no `Score` field.

**How to avoid:** Use fixed-delta Elo (Win = +K, Loss = -K, Draw = 0). The `Score` from `SessionCompleteParticipant` is stored in the session record and visible in the admin UI, but NOT propagated to the `MatchOutcome` batch. Do not attempt to read it inside `Apply`.

### Pitfall 6: Postgres/Redis Ports Published in Compose

**What goes wrong:** Adding `ports:` for `postgres` or `redis` in the compose file -- violates R3 acceptance must-NOT criterion.

**How to avoid:** Omit `ports:` entirely for `postgres` and `redis`. They communicate with `app` via the Docker bridge network using service names (`postgres:5432`, `redis:6379`).

### Pitfall 7: IRankingAlgorithm.Apply Called Per Match

**What goes wrong:** Consumer code calls `Apply` once per session-complete, rather than letting the Rankings ticker batch the period. For Glicko-2 this is mathematically invalid; for Elo it produces correct results but bypasses the ticker's leader-election guarantee.

**How to avoid:** The Platformer3D host does NOT call `Apply` directly. `ISessionCompleteService` writes a `pending_rating_updates` row; the Rankings ticker calls `Apply` on the batch at the end of each rating period. This is automatic when `AddRankings()` is called correctly.

### Pitfall 8: RatingPeriod Too Long for Demo

**What goes wrong:** Default `RatingPeriod = 1 hour` means the admin console shows no leaderboard change for an hour after a completed match, making the demo appear broken.

**How to avoid:** Set `c.RatingPeriod = TimeSpan.FromMinutes(1)` (or `TimeSpan.FromSeconds(30)` for live demos) on the platformer ladder config so ratings drain quickly.

### Pitfall 9: three.js Import Path Case Mismatch

**What goes wrong:** `import * as THREE from '/js/Three.module.js'` (wrong case) fails silently on case-sensitive Linux filesystems.

**How to avoid:** Use lowercase consistently: `three.module.js`, `/js/three.module.js`. Match the filename exactly as vendored.

---

## Validation Architecture

> Nyquist validation is enabled (workflow.nyquist_validation not set to false).

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Integration | Testcontainers 4.11.0 (Postgres + Redis) |
| Config file | `tests/GameKit.Platformer3D.Tests/GameKit.Platformer3D.Tests.csproj` (Wave 0: create) |
| Quick run | `dotnet test tests/GameKit.Platformer3D.Tests/` |
| Integration run | `dotnet test tests/GameKit.Platformer3D.Integration.Tests/` |

### Phase Requirements to Test Map

> **Filter-name reconciliation (plan revision 2026-06-22):** The PLAN `<verify><automated>` blocks
> are authoritative. The illustrative class names from the original draft
> (`BestTimeProximityStrategy*`, `PlatformerSpeedrunAlgorithm*`,
> `IdempotencyDoublePostTests`) have been reconciled to the actual filter strings
> the plans use: `BestTimeMatchmakingStrategy` / `TimeMarginRankingAlgorithm` types,
> the `--filter "FullyQualifiedName~..."` substring filters, and `IdempotentCompletion`
> (the new Docker-free R7 unit test, 21-04 Task 4). The exact per-task command map is in
> `21-VALIDATION.md` § Per-Task Verification Map.

| Req ID | Behavior | Test Type | Plan/Task | Automated Command (authoritative) | File Exists? |
|--------|----------|-----------|-----------|-----------------------------------|-------------|
| R1 | Projects build; `TicTacToeDuel` unchanged | CI build gate | 21-01 T2 | `dotnet build samples/Platformer3D/Platformer3D.csproj -p:NuGetAudit=false && dotnet build samples/TicTacToeDuel/TicTacToeDuel.csproj -p:NuGetAudit=false` | Wave 1 |
| R2 | Browser renders 3D level (no CDN script tags) | Grep gate (+ manual render in 21-06 T3) | 21-03 T2 | `! grep -rEiq 'https?://(cdn\|unpkg\|cdnjs\|fonts\.googleapis\|jsdelivr...)' samples/Platformer3D/wwwroot/ && grep -q 'btn-guest' .../index.html` | Wave 2 |
| R3 | Offline compose stack builds / `/health/ready` 200 | Build gate (+ smoke 21-06) | 21-05 T1 | `docker build ... \|\| dotnet publish ... -p:NuGetAudit=false` | Wave 4 |
| R3 | Pg/Redis ports NOT published | Compose port assert (no Testcontainers) | 21-06 T1 | `dotnet test tests/GameKit.Platformer3D.Integration.Tests/ --filter "ComposePort" -p:NuGetAudit=false` | Wave 5 |
| R4 | Admin console mounted / renders empty states | Build assert + manual browse | 21-04 T1 / 21-06 T3 | `dotnet build samples/Platformer3D/Platformer3D.csproj` (grep `AddGameKitAdmin`); manual in 21-06 T3 | Wave 3 / 5 |
| R5 | Resolved `IMatchmakingStrategy` is the custom type + match forms | Unit + Integration | 21-02 T2 / 21-06 T1 | `--filter "FullyQualifiedName~BestTimeMatchmakingStrategy"` (unit); `--filter "Resolution"` (integration) | Wave 2 / 5 |
| R6 | Custom algorithm updates leaderboard (Win +30 / Loss -30) | Unit | 21-02 T1 | `--filter "FullyQualifiedName~TimeMarginRankingAlgorithm"` | Wave 2 |
| R6 | Exact integer-ms tie -> draw, no asymmetric change | Unit | 21-02 T1 | `--filter "FullyQualifiedName~TimeMarginRankingAlgorithm"` (DrawEdge case) | Wave 2 |
| R7 | Double-post session-complete -> exactly one outcome (Docker-free) | Unit (mocked) | 21-04 T4 | `--filter "IdempotentCompletion"` | Wave 3 |
| R7 | Double-post -> one outcome row (full-stack) | Integration (Docker-gated) | 21-06 T2 | `--filter "FullLoop"` | Wave 5 |
| R7 | Player JWT -> 401/403 on session-complete | Integration | 21-06 T1 | `--filter "PlayerJwt"` | Wave 5 |
| R7 | D-03 run-summary sanity validation | Unit | 21-04 T2 | `--filter "RunSummary"` | Wave 3 |
| R8 | `POST /auth/login/guest` -> player able to enter matchmaking, no PII | Integration | 21-06 T1 | `--filter "Guest"` | Wave 5 |
| R8 | Guest button + run-summary frame present in client | Grep gate | 21-03 T2 | `grep -q '/auth/login/guest' .../game.js && grep -q 'run_finish' .../game.js` | Wave 2 |
| R9 | Invite -> ready-check -> 1v1 match | Integration | 21-06 T2 | `--filter "LobbyToMatch"` | Wave 5 |
| R9 | Declined/timeout/disconnect -> zero tickets, party intact | Integration | 21-06 T2 | `--filter "ReadyCheck"` (within LobbyToMatchTests) | Wave 5 |
| R10 | Full loop smoke: guest -> party -> matchmake -> result -> leaderboard | Integration | 21-06 T2 | `--filter "FullLoop"` | Wave 5 |
| R10 | Smoke re-runnable (no leaked state) | Integration | 21-06 T2 | `--filter "FullLoop"` (second-run case) | Wave 5 |
| R10 | Two concurrent parties each form exactly one match | Integration | 21-06 T2 | `--filter "Concurrent"` | Wave 5 |
| R11 | `reuse lint` passes for new sample paths | CI lint | 21-01 T3 / 21-03 T1 / 21-05 T2 | `reuse lint` (requires `pipx install reuse`, 21-01 T1) | Wave 1/2/4 |
| R11 | three.js version string identical in NOTICES + REUSE.toml | Grep consistency gate | 21-03 T1 | `grep -qF "$TAG" THIRD-PARTY-NOTICES.md && grep -qF "$TAG" REUSE.toml` | Wave 2 |
| R11 | No CDN/outbound egress in wwwroot | Grep gate | 21-03 T2 | `! grep -rEiq 'https?://(cdn\|unpkg\|cdnjs...)' samples/Platformer3D/wwwroot/` | Wave 2 |

### Sampling Rate

- **Per task commit:** `dotnet test tests/GameKit.Platformer3D.Tests/ -x` (unit tests only, < 30s)
- **Per wave merge:** `dotnet test tests/GameKit.Platformer3D.Tests/ tests/GameKit.Platformer3D.Integration.Tests/ -x` (full suite including integration)
- **Phase gate:** All tests green, `reuse lint` passes, `docker compose up` smoke passes before `/gsd-verify-work`

### Wave 0 Gaps (test infrastructure to create)

- [ ] `tests/GameKit.Platformer3D.Tests/GameKit.Platformer3D.Tests.csproj` -- covers unit tests for strategy + algorithm
- [ ] `tests/GameKit.Platformer3D.Tests/Strategy/BestTimeProximityStrategyTests.cs` -- bracket math, cold-start, overlap rule, Name != "elo-range"
- [ ] `tests/GameKit.Platformer3D.Tests/Rankings/PlatformerSpeedrunAlgorithmTests.cs` -- Win/Loss/Draw deltas; exact-tie draw edge; batched-only; Name != "glicko2"
- [ ] `tests/GameKit.Platformer3D.Integration.Tests/GameKit.Platformer3D.Integration.Tests.csproj` -- Testcontainers integration tests
- [ ] `tests/GameKit.Platformer3D.Integration.Tests/Smoke/EndToEndSmokeTests.cs` -- full loop; idempotency; concurrent parties
- [ ] `tests/GameKit.Platformer3D.Integration.Tests/Auth/PlayerJwtRejectedTests.cs` -- negative: player JWT -> 401/403 on session-complete
- [ ] `tests/GameKit.Platformer3D.Integration.Tests/Lobby/LobbyToMatchTests.cs` -- party flow; decline abort

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker | R3 packaging + smoke test | Yes | 29.5.3 | -- |
| .NET SDK 10 | All build/test | Yes | 10.0.109 | -- |
| Postgres (Docker image) | R3, integration tests | Yes (via Docker) | 17.9 | -- |
| Redis (Docker image) | R3, integration tests | Yes (via Docker) | 8.6.2 | -- |
| `reuse` CLI | R11 license lint | NOT found | -- | Install: `pip install reuse` (Wave 0 task) |
| `curl` | Health check in Dockerfile CMD | Yes (on dev machine; in aspnet image: add or use wget) | -- | Use `wget -qO-` in Docker healthcheck |

**Missing dependencies with no fallback:**
- `reuse` CLI not installed. Wave 0 task: `pip install reuse` or `pipx install reuse`. CI must also install it.

**Missing dependencies with fallback:**
- `curl` in the Docker aspnet runtime image: use `wget -qO- http://localhost:8080/health/ready` as the healthcheck command, or add `curl` via `apt-get` in the Dockerfile runtime stage.

---

## Security Domain

> `security_enforcement` not set to false in config; section included.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | Yes | `GameKit.Auth` `GuestOAuthProvider`; JWT Bearer; `ServiceTokenAuthenticationHandler` |
| V3 Session Management | Yes | Refresh tokens (SHA-256 stored); `IRefreshTokenService` |
| V4 Access Control | Yes | `RequiresServiceToken` policy (service-token-only on session-complete); player JWT cannot reach session-complete (must-NOT) |
| V5 Input Validation | Yes | `FluentValidation` on `SessionCompleteRequest`; `RunSummaryValidator` (D-03 sanity checks) |
| V6 Cryptography | Partial | RSA key pair for JWT; service token raw value never stored (SHA-256 hash only) |

### Known Threat Patterns for This Stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Browser self-reports result (skips GameServer) | Tampering | `RequiresServiceToken` policy on session-complete; player JWT rejected with 401/403 |
| Replay attack on run-summary | Tampering | One `run_finish` per WebSocket session; GameServer holds session state per matchId |
| Postgres/Redis exposed on host | Info Disclosure | Omit `ports:` for pg/redis in compose (must-NOT) |
| CDN/outbound egress leaks data or deps | Info Disclosure | All assets local in wwwroot; grep gate in CI |
| Guest analytics/PII collection | Privacy | No analytics code; no PII fields in `GuestOAuthProvider`; grep gate |
| Service token collision on restart | Elevation | Revoke-then-reissue pattern in `StartAsync` |

---

## Assumptions Log

> All claims tagged [ASSUMED] in this research.

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | three.js latest stable version is r168 | Standard Stack | Planner vendors wrong version; mitigate by running `curl` version check before vendoring |
| A2 | WebSocket endpoint placement in middleware order (after UseGameKitAdmin, before Map calls) produces authenticated `ctx.User` | Pattern 4 | Player JWT not validated on WebSocket upgrade; player identity not bound to session |
| A3 | ~~`MatchmakingLadderConfig` strategy routing~~ → **RESOLVED**: ticker injects a SINGLE `IMatchmakingStrategy`; MS.DI returns last-registered; custom strategy registered via `services.Replace(...)` AFTER `AddMatchmaking()` | Pattern 1 / Custom Strategy / Open Questions (RESOLVED) #1 | (was: custom strategy never invoked) — now closed |
| A4 | ~~`SessionResult.Draw` → `MatchResult.Draw` mapping~~ → **RESOLVED**: `SessionResult.Draw=2`; `RankingsTickerService.cs:532` maps `"draw" => MatchResult.Draw`; fixed-delta Draw = 0 | D-10 / R6 / Open Questions (RESOLVED) #2 | (was: tie does not produce draw) — now closed |
| A5 | ~~`RevokeAsync` idempotency when name missing~~ → **RESOLVED**: `RevokeAsync` returns `false` when missing, no throw; unconditional revoke-then-issue is safe | Pattern 3 / In-Process Token / Open Questions (RESOLVED) #3 | (was: StartAsync fails on first start) — now closed |

**Most critical:** A3 (strategy routing) and A4 (Draw mapping) were the load-bearing unknowns for R5 and R6/D-10. **Both are now RESOLVED** against source (see ## Open Questions (RESOLVED)); A5 is also resolved. No open assumptions remain that block plan locking.

---

## Open Questions (RESOLVED)

> The three load-bearing questions (A3, A4, A5) are RESOLVED below against the
> real source, locked by the orchestrator during plan revision (2026-06-22).
> A3 and A4 are load-bearing for R5 and R6/D-10; no "ASSUMED" / "empirically
> confirm" language remains for these three.

1. **Strategy routing mechanism (A3) — RESOLVED**
   - **Source:** `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` ctor (`IMatchmakingStrategy strategy` parameter, line ~103; field assigned line ~127) and call site `_strategy.Match(candidate, poolScratch, now)` (line ~477); `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs` `AddStrategyServices()` (lines 67–71, called from `AddMatchmaking()` at `MatchmakingBuilderExtensions.cs:124`).
   - **Finding:** The ticker injects a **SINGLE** `IMatchmakingStrategy` — NOT `IEnumerable`, NOT keyed by `Name`. That one resolved strategy self-filters per ladder via its `FindLadderConfig`. `AddStrategyServices()` runs a Scrutor `Scan(...).FromAssemblyOf<EloRangeMatchmakingStrategy>().AddClasses(...AssignableTo<IMatchmakingStrategy>()).WithSingletonLifetime()` that registers `EloRangeMatchmakingStrategy` as an `IMatchmakingStrategy` singleton. MS.DI returns the **LAST-registered** descriptor when several are registered for the same service type. Therefore registering the custom strategy **BEFORE** `AddMatchmaking()` (as that file's XML doc lines 50–56 advises) is **SHADOWED** by the EloRange registration that runs later inside `AddMatchmaking()`. The doc's "Scrutor dedups by service+impl pair" only prevents double-registering the *same* impl — it does NOT make a *different* impl win.
   - **LOCKED decision:** Call `services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>())` **AFTER** `AddMatchmaking()`. `Replace` removes the scanned EloRange descriptor and registers exactly one strategy. (`Replace` lives in `Microsoft.Extensions.DependencyInjection.Extensions`; it is already used in `MatchmakingBuilderExtensions.Ticker.cs:61`.) R5's resolution test (21-06) asserts `provider.GetRequiredService<IMatchmakingStrategy>()` is `BestTimeMatchmakingStrategy` for the demo ladder. Wired in 21-04 Task 1; the PATTERNS.md Program.cs note has been corrected to this post-`AddMatchmaking` `services.Replace` form.

2. **SessionResult → MatchResult Draw mapping (A4) — RESOLVED**
   - **Source:** `src/GameKit.Core/Entities/SessionResult.cs` (`Draw = 2`); `src/GameKit.Rankings/Services/RankingsTickerService.cs:532` (`"draw" => MatchResult.Draw`; full map at lines 530–534).
   - **Finding:** A GameServer posting a `SessionResult.Draw` completion flows to `MatchResult.Draw` in the rankings batch. D-10's exact integer-ms tie → draw is therefore **fully implementable** with the fixed-delta algorithm (Draw delta = 0, symmetric). No GameKit.* API change required.
   - **LOCKED decision:** The GameServer posts both participants as `SessionResult.Draw` on an exact integer-ms tie (21-04 Task 3); `TimeMarginRankingAlgorithm` maps `MatchResult.Draw` to a 0.0 delta (21-02 Task 1). No API change.

3. **`RevokeAsync` idempotency when name not found (A5) — RESOLVED**
   - **Source:** `src/GameKit.Rankings/Services/IServiceTokenService.cs:56` (`Task<bool> RevokeAsync(string name, CancellationToken ct)`) and `:47` (`Task<(string Raw, ServiceToken Row)> IssueAsync(string name, DateTimeOffset? expiresAt, CancellationToken ct)`).
   - **Finding:** `RevokeAsync` returns a `bool` — `false` when the name is missing, **no throw**. `IssueAsync` throws `ServiceTokenNameAlreadyExistsException` on a duplicate name (hence revoke-then-issue is mandatory for clean restart).
   - **LOCKED decision:** The embedded GameServer's `StartAsync` does an unconditional `RevokeAsync(name, ct)` (false-on-missing is fine — no try/catch, no `ListAsync` pre-check) then `IssueAsync(name, null, ct)` for a clean container-restart re-issue (21-04 Task 3).

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| EloRangeMatchmakingStrategy (default) | Custom `IMatchmakingStrategy` registered via `services.Replace(...)` AFTER `AddMatchmaking()` (A3 RESOLVED) | This phase | Single resolved strategy; `Replace` removes the scanned EloRange descriptor (last-wins shadowing fix) |
| Glicko2Algorithm (default) | Custom `IRankingAlgorithm` with `LadderConfig.Algorithm = "name"` | This phase | Fixed-delta Elo for time-based leaderboard; simpler and correct for async 1v1 |
| Separate console GameServer process (TicTacToeDuel.GameServer) | Embedded `IHostedService` in single app image | This phase | Single-image offline demo; in-process token; simpler compose |
| CDN-loaded JS (hypothetical) | Vendored ES module from `wwwroot/` | This phase | GPL-compliant offline demo; no outbound CDN call |

**Deprecated/outdated:**
- `TicTacToeDuel.GameServer` pattern (separate process): still valid for production topology; replaced by embedded IHostedService for single-image demo only.

---

## Worktree Conflict Surface (D-15)

**Changes confined to NEW paths only:**
- `samples/Platformer3D/**` (new)
- `samples/Platformer3D.GameServer/**` (new)
- `tests/GameKit.Platformer3D.Tests/**` (new)
- `tests/GameKit.Platformer3D.Integration.Tests/**` (new)
- `GameKit.sln` -- adds two `<Project>` entries and two test project entries
- `REUSE.toml` -- appends one `[[annotations]]` block
- `THIRD-PARTY-NOTICES.md` -- appends one section

**Files NOT touched (D-15 hard constraint):**
- `samples/TicTacToeDuel/**` -- no changes whatsoever
- `samples/TicTacToeDuel.GameServer/**` -- no changes
- `src/GameKit.*/**` -- no changes to any package
- Core migrations -- none added

**Merge conflict risk:** LOW. The only shared files are `GameKit.sln` (append-only project entries) and `REUSE.toml` / `THIRD-PARTY-NOTICES.md` (append-only). Phases 16-20 on the main checkout do not modify `GameKit.sln` project entries for new sample projects. Standard git merge will handle append-only changes cleanly.

---

## Sources

### Primary (HIGH confidence -- codebase reads, 2026-06-22)

- `src/GameKit.Matchmaking/Strategy/IMatchmakingStrategy.cs` -- contract: `Match(candidate, pool, now)`, stateless + deterministic, `Name` discriminator
- `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` -- constructor, `FindLadderConfig`, `Bracket()`, `BuildMatchResult()`, Scrutor dedup notes
- `src/GameKit.Matchmaking/Strategy/QueuedParty.cs` -- `QueuedPartyMember` fields: `Rating`, `RatingDeviation`, `Volatility`; `AggregateRating` from Redis hash
- `src/GameKit.Matchmaking/Strategy/PartyRatingAggregator.cs` -- `Mean`, `Max`, `GlickoWeighted` enum
- `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs` -- Scrutor scan scope: `FromAssemblyOf<EloRangeMatchmakingStrategy>()` only
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` (lines 582-651) -- `BuildQueuedPartyFromHash` reads from Redis; no DB query in Match loop
- `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` -- Redis key layout
- `src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs` -- batched-only `Apply(state, batch)` contract
- `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` -- reference impl pattern; name `"glicko2"`
- `src/GameKit.Rankings/Algorithms/RankingState.cs` -- `PlayerRatingSnapshot(PlayerId, Rating, RatingDeviation, Volatility)`
- `src/GameKit.Rankings/Algorithms/RankingBatch.cs` -- `MatchOutcome(PlayerId, OpponentId, Result)` -- NO Score field
- `src/GameKit.Rankings/Builder/LadderConfig.cs` -- `Algorithm` field (string, default `"glicko2"`)
- `src/GameKit.Rankings/Builder/IGameKitRankingsBuilder.cs` -- `AddLadder(name, configure)` signature
- `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` -- `IServiceTokenService` registered as scoped
- `src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationHandler.cs` -- Bearer -> SHA-256 lookup; `service-account` role
- `src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationDefaults.cs` -- `SchemeName="GameKitServiceToken"`, `PolicyName="RequiresServiceToken"`
- `src/GameKit.Rankings/Authentication/ServiceTokenAuthorizationPolicy.cs` -- policy: `RequireRole("service-account")`
- `src/GameKit.Rankings/Services/IServiceTokenService.cs` -- `IssueAsync`, `RevokeAsync`, `ServiceTokenNameAlreadyExistsException`
- `src/GameKit.Core/Http/SessionEndpoints.cs` -- `POST /{id}/complete`; `IdempotencyKeyEndpointFilter`; `RequiresServiceToken`; result union
- `src/GameKit.Core/Http/Contracts/SessionCompleteRequest.cs` -- `SessionCompleteParticipant(PlayerId, Team, Result, Score?)`
- `src/GameKit.Core/Services/ISessionCompleteService.cs` -- `CompleteAsync`; `AlreadyCompletedCached`
- `src/GameKit.Core/Services/IIdempotencyStore.cs` -- `TryGetAsync`, `StoreAsync`; MUST run inside caller's ambient transaction
- `src/GameKit.Lobby/Services/ILobbyService.cs` -- `CreateLobbyAsync`, `JoinLobbyAsync`, `MarkReadyAsync`, `RemoveMemberAsync`, `IsMemberAsync`
- `src/GameKit.Lobby/Hubs/ILobbyClient.cs` -- `ReceiveStateUpdateAsync`, `ReceiveChatMessageAsync`
- `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs` -- `Provider="guest"`; creates Player; auto-generates displayName; no PII
- `src/GameKit.Auth/Http/AuthEndpoints.cs` -- `POST /auth/login/{provider}`; guest ignores body fields
- `samples/TicTacToeDuel/Program.cs` -- complete AddGameKit() composition chain + strict middleware order (reference)
- `samples/TicTacToeDuel.GameServer/Program.cs` -- service-token game server pattern (`IServiceTokenService` usage, `HttpClient` Bearer auth, `POST /api/sessions/{id}/start`)
- `samples/TicTacToeDuel/docker-compose.yml` -- compose reference: postgres:17.9, redis:8.6.2, healthcheck shapes
- `docker-compose.yml` (root) -- Redis command args reference
- `REUSE.toml` -- existing vendor annotation pattern
- `THIRD-PARTY-NOTICES.md` -- existing third-party notice format
- `global.json` -- SDK 10.0.106 pinned
- `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeStrategyTests.cs` -- unit test pattern for strategies
- `tests/GameKit.Rankings.Tests/Glicko2/Glicko2AlgorithmContractTests.cs` -- unit test pattern for algorithms

### Secondary (MEDIUM confidence)

- `CLAUDE.md` -- technology stack table; project constraints; GPL / self-hosted-only / zero cloud deps

### Tertiary (LOW confidence -- training knowledge)

- three.js r168 version number [ASSUMED] -- verify from https://github.com/mrdoob/three.js/releases before vendoring
- WebSocket endpoint middleware placement [ASSUMED] -- standard ASP.NET Core pattern; no existing WS endpoint in repo

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all NuGet versions from `CLAUDE.md` + `global.json` (codebase reads)
- Architecture patterns: HIGH -- all from canonical source file reads
- Custom strategy/algorithm design: HIGH (contract) / MEDIUM (exact constants) -- contracts verified from interface reads; constants are planner's discretion
- D-08 cold-start: HIGH -- confirmed feasible from `QueuedPartyMember.RatingDeviation` field
- Score-in-Apply finding: HIGH -- `MatchOutcome` verified to have no Score field
- Docker packaging: MEDIUM -- pattern from TicTacToeDuel compose + root compose; no existing app Dockerfile to copy
- three.js version: LOW [ASSUMED] -- verify before vendoring

**Research date:** 2026-06-22
**Valid until:** 2026-07-22 (30 days -- stable GameKit packages; three.js version should be reverified if > 7 days old)
