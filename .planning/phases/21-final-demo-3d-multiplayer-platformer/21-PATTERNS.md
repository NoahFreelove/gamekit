<!-- REUSE-IgnoreStart -->
# Phase 21: Final Demo — 3D Multiplayer Platformer - Pattern Map

**Mapped:** 2026-06-22
**Files analyzed:** 13 new/modified files
**Analogs found:** 12 / 13

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `samples/Platformer3D/Program.cs` | config/host | request-response | `samples/TicTacToeDuel/Program.cs` | exact |
| `samples/Platformer3D.GameServer/GameServerService.cs` | service (IHostedService) | request-response + event-driven | `samples/TicTacToeDuel.GameServer/Program.cs` | role-match |
| `samples/Platformer3D.GameServer/RunSummaryValidator.cs` | utility | transform | `src/GameKit.Core/Http/SessionEndpoints.cs` (validation filter shape) | partial |
| `samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs` | service (strategy) | CRUD / request-response | `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` | exact |
| `samples/Platformer3D/Algorithms/TimeMarginRankingAlgorithm.cs` | service (strategy) | batch/transform | `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` | exact |
| `samples/Platformer3D/wwwroot/index.html` | component (browser client entry) | request-response | `samples/TicTacToeDuel/wwwroot/index.html` | role-match |
| `samples/Platformer3D/wwwroot/game.js` | component (three.js game loop) | event-driven | `samples/TicTacToeDuel/wwwroot/index.html` (inline JS patterns) | partial |
| `samples/Platformer3D/Dockerfile` | config | file-I/O | none in repo (no existing Dockerfile) | no-analog |
| `samples/Platformer3D/docker-compose.yml` | config | file-I/O | `samples/TicTacToeDuel/docker-compose.yml` | role-match |
| `GameKit.sln` additions | config | N/A | existing `.sln` project-reference block | role-match |
| `REUSE.toml` additions | config | N/A | existing `REUSE.toml` `[[annotations]]` blocks | role-match |
| `THIRD-PARTY-NOTICES.md` additions | config | N/A | existing `THIRD-PARTY-NOTICES.md` entry for Glicko-2 | exact |
| `tests/GameKit.Platformer3D.Integration.Tests/` | test | batch | `tests/GameKit.Matchmaking.Integration.Tests/` | role-match |

---

## Pattern Assignments

### `samples/Platformer3D/Program.cs` (config/host, request-response)

**Analog:** `samples/TicTacToeDuel/Program.cs`

**Imports pattern** (lines 1–17):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Admin.UI.Builder;
using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using GameKit.Lobby.Builder;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
using GameKit.OpenApi.Builder;
using GameKit.Presence.Builder;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Entities;
using OpenTelemetry;
using OpenTelemetry.Trace;
// Add: using Platformer3D.Strategy; using Platformer3D.Algorithms;
```

**Redis singleton pattern** (lines 26–29):
```csharp
var redisCs = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Redis");
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisCs));
```

**Core GameKit builder chain pattern** (lines 34–40):
```csharp
var gameKitBuilder = builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("GameKit")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:GameKit");
    opts.MigrationsConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations");
    opts.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
});
```

**Custom strategy registration note (CORRECTED — A3):** Register the custom strategy via `services.Replace(...)` **AFTER** `gameKitBuilder.AddMatchmaking(...)`:
```csharp
using Microsoft.Extensions.DependencyInjection.Extensions; // for services.Replace

// ... after gameKitBuilder.AddMatchmaking(...).AddLadder("platformer", ...):
// Replace the Scrutor-scanned EloRange descriptor so the SINGLE resolved
// IMatchmakingStrategy is the custom one (A3 shadowing fix).
builder.Services.Replace(
    ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>());
```
**Shadowing rationale (A3):** `MatchmakerTickerService` injects a **SINGLE** `IMatchmakingStrategy` (ctor line ~103; `_strategy.Match(...)` line ~477) — not `IEnumerable`, not keyed by `Name`. `AddMatchmaking()` calls `AddStrategyServices()` (`MatchmakingBuilderExtensions.Strategy.cs` lines 67–71), which Scrutor-scans and registers `EloRangeMatchmakingStrategy` as an `IMatchmakingStrategy` singleton. MS.DI returns the **last-registered** descriptor for a service type, so a plain `AddSingleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>()` placed *before* `AddMatchmaking()` is **shadowed** by EloRange. The XML doc at `MatchmakingBuilderExtensions.Strategy.cs:50–56` ("register BEFORE … Scrutor dedups by service+impl pair") only prevents double-registering the *same* impl — it does NOT make a *different* impl win. Therefore use `services.Replace(...)` **after** `AddMatchmaking()`: it removes the scanned EloRange descriptor and leaves exactly one strategy. The R5 resolution test (21-06) — `GetRequiredService<IMatchmakingStrategy>()` is `BestTimeMatchmakingStrategy` — is the gate that proves this.

**Custom algorithm registration note:** Register before `AddRankings()` using the same pattern:
```csharp
builder.Services.AddSingleton<IRankingAlgorithm, TimeMarginRankingAlgorithm>();
```

**AddRankings + AddLadder pattern** (lines 77–101) — copy the chain, change ladder name to `"platformer"` and point the algorithm name to the custom discriminator:
```csharp
gameKitBuilder.AddRankings(opts => { })
.AddLadder("platformer", c =>
{
    c.DefaultRating     = 1500;
    c.DefaultRd         = 350;
    c.DefaultVolatility = 0.06;
    c.RatingPeriod      = System.TimeSpan.FromHours(1);
    c.ResetPolicy       = SeasonResetPolicy.SoftRegress;
    // c.Algorithm       = "time-margin"; // name must match TimeMarginRankingAlgorithm.Name
});
```

**AddMatchmaking + AddLadder pattern** (lines 106–119):
```csharp
gameKitBuilder.AddMatchmaking(opts =>
{
    opts.Ticker.TickIntervalMs = 500;
})
.AddLadder("platformer", ladder =>
{
    ladder.BracketStart          = 0;          // best-time strategy uses its own window; BracketStart is a pass-through
    ladder.BracketEnd            = int.MaxValue;
    ladder.BracketRampSeconds    = 60;
    ladder.PartyRatingAggregator = PartyRatingAggregator.Mean;
    // ladder.Strategy            = "best-time"; // name must match BestTimeMatchmakingStrategy.Name
});
```

**Middleware pipeline + mapping pattern** (lines 191–212):
```csharp
app.UseRouting();
app.UseRateLimiter();
app.UseGameKitAuth();
app.UseGameKit();
app.UseGameKitAdmin();
app.MapStaticAssets();
app.MapGameKitHealth();
app.MapGameKit();
app.MapAuth();
app.MapRankings();
app.MapMatchmaking();
app.MapLobby();
app.MapPresence();
app.MapGameKitOpenApi();
app.MapGameKitAdmin("/admin");
// Add MapGameKitWebSockets() or app.MapHub<PlatformerHub>() for the WS endpoint
```

---

### `samples/Platformer3D.GameServer/GameServerService.cs` (IHostedService, event-driven + request-response)

**Analog:** `samples/TicTacToeDuel.GameServer/Program.cs`

**Key divergence:** The TicTacToeDuel game server is a standalone console process. Platformer3D embeds the game server as an `IHostedService` inside the host project (D-13). The patterns to copy are the HTTP + auth patterns; the process boundary is replaced by DI.

**HttpClient factory + service-token auth pattern** (lines 31–32, 107–117):
```csharp
builder.Services.AddHttpClient("gamekit.web-api");

// In the hosted service ExecuteAsync:
var http = _httpClientFactory.CreateClient("gamekit.web-api");
http.BaseAddress = new Uri(_webApiBaseUrl);
http.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", _serviceJwt);

using var content = new StringContent(
    JsonSerializer.Serialize(runSummary),
    System.Text.Encoding.UTF8,
    "application/json");
using var response = await http.PostAsync(
    $"/api/sessions/{sessionId}/complete",
    content,
    ct);
```

**In-process wiring pattern:** Because it is embedded, the service token JWT is loaded from configuration the same way (lines 52–53 analog):
```csharp
var serviceJwt = config["Services:GameServer:ServiceJwt"]
    ?? throw new InvalidOperationException("Missing Services:GameServer:ServiceJwt");
```

**WebSocket accept + run-summary receive pattern** (no direct analog — see "Claude's Discretion" in CONTEXT.md; use ASP.NET Core `WebSocket.ReceiveAsync` loop):
```csharp
// In the endpoint handler (mapped via app.MapGet("/ws/match/{sessionId}", ...)):
if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
using var ws = await context.WebSockets.AcceptWebSocketAsync();
// receive loop → deserialize RunSummary → validate → POST to session-complete
```

**Idempotency-Key header pattern** (from `src/GameKit.Core/Http/SessionEndpoints.cs` lines 42–43):
```csharp
// POST /api/sessions/{id}/complete requires Idempotency-Key header (IdempotencyKeyEndpointFilter)
http.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
```

---

### `samples/Platformer3D.GameServer/RunSummaryValidator.cs` (utility, transform)

**Analog:** `src/GameKit.Core/Http/EndpointFilters/IdempotencyKeyEndpointFilter.cs` (shape), `src/GameKit.Core/Http/SessionEndpoints.cs` (validation pattern)

**Validation pattern (D-03 — sanity-level, not full re-sim):**
```csharp
// Monotonic checkpoint check:
for (int i = 1; i < summary.CheckpointTimesMs.Length; i++)
    if (summary.CheckpointTimesMs[i] <= summary.CheckpointTimesMs[i - 1])
        return ValidationResult.NonMonotonic;

// Plausible bounds:
var totalMs = summary.FinishTimeMs - summary.StartTimeMs;
if (totalMs < MinPlausibleMs || totalMs > MaxPlausibleMs)
    return ValidationResult.Implausible;

// One-finish-per-session: checked via IIdempotencyStore (D-05)
```

---

### `samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs` (service/strategy, request-response)

**Analog:** `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs`

**File header + namespace pattern** (lines 1–11):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using GameKit.Core.Services;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;

namespace Platformer3D.Strategy;
```

**Class structure + constructor pattern** (lines 39–63):
```csharp
/// <summary>
/// Custom <see cref="IMatchmakingStrategy"/> for the Platformer3D demo ladder (D-06/D-07).
/// Pairs players whose recent best completion times are closest, widening the window as
/// queue time grows. Cold-start players (no recorded best time) get a neutral wide bracket
/// until they post their first run (D-08).
/// </summary>
public sealed class BestTimeMatchmakingStrategy : IMatchmakingStrategy
{
    private readonly IReadOnlyList<MatchmakingLadderConfig> _ladders;
    // No mutable instance fields — stateless + thread-safe (IMatchmakingStrategy contract)

    public BestTimeMatchmakingStrategy(IReadOnlyList<MatchmakingLadderConfig> ladders)
    {
        ArgumentNullException.ThrowIfNull(ladders);
        _ladders = ladders;
    }

    /// <inheritdoc />
    public string Name => "best-time";   // D-07: Name ≠ "elo-range"
```

**Match method signature + stateless pattern** (lines 66–69):
```csharp
    /// <inheritdoc />
    public MatchResult? Match(QueuedParty candidate, IReadOnlyList<QueuedParty> pool, DateTimeOffset now)
    {
        // All per-call state built here — no mutable instance fields (IMatchmakingStrategy contract)
        var cfg = FindLadderConfig(candidate);
        if (cfg is null) return null;

        var queueSeconds = (now - candidate.QueuedAt).TotalSeconds;
        var windowMs = ComputeWindow(queueSeconds, cfg);   // widens over time (D-06)

        // Cold-start: if candidate has no best time, match anyone (wide bracket)
        // ...iterate pool, compare |candidateBestTime - poolBestTime| <= windowMs...
    }
```

**BuildMatchResult pattern** — copy verbatim from `EloRangeMatchmakingStrategy` lines 204–225 (CSPRNG team assignment):
```csharp
    private static MatchResult BuildMatchResult(QueuedParty a, QueuedParty b)
    {
        var allMembers = new List<Guid>(a.Members.Count + b.Members.Count);
        foreach (var m in a.Members) allMembers.Add(m.PlayerId);
        foreach (var m in b.Members) allMembers.Add(m.PlayerId);

        var teamAssignments = new Dictionary<Guid, int>(allMembers.Count);
        foreach (var pid in allMembers)
            teamAssignments[pid] = RandomNumberGenerator.GetInt32(0, 2);

        return new MatchResult(
            ProposalId: Guid.NewGuid(),
            MatchedTickets: new[] { a, b },
            TeamAssignments: teamAssignments);
    }
```

---

### `samples/Platformer3D/Algorithms/TimeMarginRankingAlgorithm.cs` (service/strategy, batch)

**Analog:** `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs`

**File header + namespace pattern** (lines 1–8):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Rankings.Algorithms;

namespace Platformer3D.Algorithms;
```

**Class + Name discriminator pattern** (lines 29–56):
```csharp
/// <summary>
/// Custom <see cref="IRankingAlgorithm"/> for the Platformer3D demo ladder (D-09/D-10/D-11).
/// Each 1v1 outcome: faster integer-ms finish wins; rating swing is scaled by time margin
/// (bigger gap → bigger swing). Exact tie at integer-ms precision = draw (D-10).
/// </summary>
public sealed class TimeMarginRankingAlgorithm : IRankingAlgorithm
{
    /// <inheritdoc/>
    public string Name => "time-margin";   // D-09: Name ≠ "glicko2"

    /// <inheritdoc/>
    public RankingState Apply(RankingState state, RankingBatch batch)
    {
        // Build fresh per-call state — no mutable instance fields (IRankingAlgorithm contract)
        var ratings = new Dictionary<Guid, double>(state.Ratings.Count);
        foreach (var (id, snap) in state.Ratings)
            ratings[id] = snap.Rating;

        // Accumulate ALL outcomes (batched-only contract D-11 / RANK-04)
        foreach (var outcome in batch.Outcomes)
        {
            // outcome.Result is MatchResult.Win / Loss / Draw / Forfeit
            // For Draw: no rating change (D-10 exact-tie)
            // For Win: delta = BaseK * (1 + marginFactor); loser: -delta
        }

        var newRatings = new Dictionary<Guid, PlayerRatingSnapshot>(ratings.Count);
        // ... build updated snapshots ...
        return new RankingState(newRatings);
    }
}
```

**Critical contract note** — copy from `Glicko2Algorithm.Apply` lines 58–61 comment structure: "Build one [calculator] per Apply call — it is stateful per period." Apply the same discipline: construct any accumulator state inside `Apply`, never on the instance.

---

### `samples/Platformer3D/wwwroot/index.html` (browser client, request-response)

**Analog:** `samples/TicTacToeDuel/wwwroot/index.html`

**File header pattern** (lines 1–2):
```html
<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->
```

**Guest sign-in button pattern** (lines 52–55):
```html
<section id="auth-panel" class="panel">
  <h2>Sign in</h2>
  <div class="row">
    <button id="btn-guest">Play as Guest</button>
    <span class="muted">No account — anonymous JWT.</span>
  </div>
```

**Demo disclaimer pattern** (lines 44–47):
```html
<div class="banner-demo">
  <strong>Demo-only client:</strong> tokens are stored in <code>localStorage</code> (XSS-vulnerable).
  Do not copy this pattern into production.
</div>
```

**No CDN script constraint (must-NOT R2/R11):** three.js must be bundled locally — no `<script src="https://...">`. Serve from `wwwroot/lib/three.module.js` (downloaded at build time, not fetched at runtime).

---

### `samples/Platformer3D/wwwroot/game.js` (three.js game loop, event-driven)

**Analog:** `samples/TicTacToeDuel/wwwroot/index.html` inline JS (auth fetch pattern only — no 3D analog exists)

**Auth fetch pattern to copy:**
```javascript
// Guest sign-in → store JWT in localStorage (demo only, same pattern as TicTacToeDuel)
async function guestSignIn() {
    const res = await fetch('/auth/guest', { method: 'POST' });
    const { token } = await res.json();
    localStorage.setItem('gk_token', token);
}

// Authenticated fetch helper:
function authFetch(url, opts = {}) {
    return fetch(url, {
        ...opts,
        headers: { ...(opts.headers || {}), Authorization: `Bearer ${localStorage.getItem('gk_token')}` },
    });
}
```

**WebSocket run-summary submission pattern** (no analog — Claude's Discretion):
```javascript
// After match completes locally, send run-summary over WS:
ws.send(JSON.stringify({
    type: 'run-summary',
    sessionId,
    startTimeMs,
    checkpointTimesMs: [...],
    finishTimeMs,         // integer-ms precision (D-02)
}));
```

---

### `samples/Platformer3D/docker-compose.yml` (config, file-I/O)

**Analog:** `samples/TicTacToeDuel/docker-compose.yml`

**Postgres + Redis service pattern** (lines 9–41) — copy both service blocks; change volume name to `platformer3d-postgres-data`.

**Critical divergence from analog:** The analog does NOT include an app image service — Platformer3D must add one:
```yaml
services:
  app:
    image: platformer3d:latest    # built by the multi-stage Dockerfile
    build:
      context: ../..
      dockerfile: samples/Platformer3D/Dockerfile
    ports:
      - "5000:8080"               # Only app port published (must-NOT: no Postgres/Redis ports)
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_started
    environment:
      ConnectionStrings__GameKit: "Host=postgres;..."
      ConnectionStrings__Redis: "redis:6379"
      ASPNETCORE_ENVIRONMENT: Production

  postgres:
    image: postgres:17.9
    # NO ports mapping to host (must-NOT R3)
    environment: { ... }
    healthcheck: { ... }
    volumes:
      - platformer3d-postgres-data:/var/lib/postgresql/data

  redis:
    image: redis:8.6.2
    # NO ports mapping to host (must-NOT R3)
    command: ["redis-server", "--appendonly", "yes", ...]
```

---

### `samples/Platformer3D/Dockerfile` (config, file-I/O)

**No analog in repo.** Standard .NET multi-stage pattern (D-14):
```dockerfile
# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish samples/Platformer3D/Platformer3D.csproj -c Release -o /app/publish

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Platformer3D.dll"]
```

Key constraints: SDK build stage, `aspnet` runtime stage, no cloud credentials baked in, no CDN calls at runtime.

---

### `REUSE.toml` additions (config)

**Analog:** `REUSE.toml` existing `[[annotations]]` blocks (lines 42–47):
```toml
[[annotations]]
path = ["src/GameKit.Rankings/Glicko2/*.cs"]
precedence = "override"
SPDX-FileCopyrightText = "2015 Maarten Staa"
SPDX-License-Identifier = "BSD-3-Clause AND GPL-3.0-or-later"
```

**Pattern to add for three.js:**
```toml
[[annotations]]
path = ["samples/Platformer3D/wwwroot/lib/three.module.js"]
precedence = "override"
SPDX-FileCopyrightText = "2010-2024 three.js authors"
SPDX-License-Identifier = "MIT"
```

And for new sample source files:
```toml
[[annotations]]
path = ["samples/Platformer3D/**", "samples/Platformer3D.GameServer/**"]
precedence = "aggregate"
SPDX-FileCopyrightText = "2026 GameKit contributors"
SPDX-License-Identifier = "GPL-3.0-or-later"
```

---

### `THIRD-PARTY-NOTICES.md` additions (config)

**Analog:** existing Glicko-2 entry (lines 1–65):
```markdown
## MaartenStaa/glicko2-csharp

**Purpose:** ...
**Upstream URL:** https://github.com/...
**Upstream commit at time of vendoring:** ...
**SPDX-License-Identifier:** `BSD-3-Clause`
```

**Pattern for three.js entry:**
```markdown
## three.js

**Purpose:** WebGL 3D engine powering the Platformer3D browser client.
Bundled locally at `samples/Platformer3D/wwwroot/lib/three.module.js`.

**Upstream URL:** https://github.com/mrdoob/three.js

**Version vendored:** rXXX (pinned at build time)

**SPDX-License-Identifier:** `MIT`
```

---

### `tests/GameKit.Platformer3D.Integration.Tests/` (test, batch + request-response)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs`

**Test project setup pattern** (lines 1–18):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests;
```

**Fresh database + migration helper pattern** (lines 27–51) — copy `CreateFreshDatabaseAsync` and `ApplyMatchmakingMigrationsAsync` verbatim, rename to `ApplyPlatformerMigrationsAsync`, add `Rankings` + `Matchmaking` + `Lobby` migrations in the same chain.

**Smoke test structure** — drive the full loop via HTTP:
```csharp
[Fact]
public async Task FullLoop_GuestSignIn_Party_Match_AuthoritativeResult_UpdatesLadder()
{
    // 1. Guest sign-in → JWT
    // 2. Invite + ready-check (SignalR or REST lobby)
    // 3. Enqueue → assert match formed (poll /api/mm/status)
    // 4. WS connect → POST run-summary → assert session completed once
    // 5. Assert ladder rating changed for both players
    // Re-run: assert idempotent (second run sees same state)
}
```

---

## Shared Patterns

### SPDX File Header
**Apply to:** every new `.cs` file in `samples/Platformer3D*/`
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

### Service-Token Auth (Bearer)
**Source:** `samples/TicTacToeDuel.GameServer/Program.cs` lines 107–117; `src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationHandler.cs` lines 69–108
**Apply to:** `GameServerService.cs` (any HTTP call to session-complete endpoint)
```csharp
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceJwt);
// + Idempotency-Key header on /api/sessions/{id}/complete
http.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
```

### Strategy/Algorithm Statelessness Contract
**Source:** `src/GameKit.Matchmaking/Strategy/IMatchmakingStrategy.cs` lines 17–26; `src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs` lines 60–72
**Apply to:** `BestTimeMatchmakingStrategy.cs`, `TimeMarginRankingAlgorithm.cs`
- No mutable instance fields
- Build all per-call state inside `Match` / `Apply`
- Registered as singletons — must be safe for concurrent invocation

### Batched-Only Contract
**Source:** `src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs` lines 11–18; `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` lines 80–82
**Apply to:** `TimeMarginRankingAlgorithm.Apply`
- Never call `Apply` per individual match
- Accumulate the full rating period batch, then compute once

### must-NOT: No CDN Script
**Source:** SPEC.md prohibitions + CONTEXT.md D-14
**Apply to:** `wwwroot/index.html`, `wwwroot/game.js`, `Dockerfile`
- No `<script src="https://...">` anywhere in `wwwroot/`
- three.js downloaded at build time, served from `wwwroot/lib/`
- Dockerfile COPY layer must include the vendored `lib/` directory

### must-NOT: Ports
**Source:** SPEC.md prohibition; `samples/TicTacToeDuel/docker-compose.yml` (note: TicTacToeDuel exposes pg+redis — the Platformer3D compose must NOT follow this)
**Apply to:** `docker-compose.yml`
- `postgres` and `redis` services: no `ports:` mapping
- Only `app` service maps a host port

---

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `samples/Platformer3D/Dockerfile` | config | file-I/O | No Dockerfile exists in the repo; use standard .NET multi-stage pattern from Microsoft docs |

---

## Metadata

**Analog search scope:** `samples/`, `src/GameKit.Matchmaking/`, `src/GameKit.Rankings/`, `src/GameKit.Core/`, `tests/`, repo root
**Files read:** 14
**Pattern extraction date:** 2026-06-22
<!-- REUSE-IgnoreEnd -->
