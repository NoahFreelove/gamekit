<!-- REUSE-IgnoreStart -->
# Phase 11: GameKit.Lobby — Research

**Researched:** 2026-06-06
**Domain:** New NuGet package — ASP.NET Core SignalR hub, Redis backplane, EF Core migrations, JWT Bearer WebSocket auth, ready-check state machine, ephemeral chat
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
None explicitly locked — discuss phase was skipped. All implementation choices are at Claude's discretion guided by the ROADMAP, success criteria, and codebase conventions.

### Claude's Discretion
All implementation choices. Key design areas: SignalR wiring, JWT WebSocket auth pattern, lobby data model, ephemeral chat seam, ready-check state machine, IMatchmakingService integration, lobby advisory-lock key (TBD, live-verify Wave 0).

### Deferred Ideas (OUT OF SCOPE)
None — discuss phase skipped. LOBBY-04 chat persistence is explicitly OUT OF SCOPE: no `lobby_messages` table, no chat log, no ILobbyMessageHandler persistence hook.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| LOBBY-01 | `GameKit.Lobby` ships as a new NuGet package (net10.0) with per-package migration — distinct live-verified advisory-lock key, `__ef_migrations_lobby` history table, `IDesignTimeDbContextFactory`, `ExcludeFromMigrations` on all 20 prior-package entities | §Standard Stack, §Migration Pattern, §Skeleton, §Validation Architecture |
| LOBBY-02 | Lobby data model: `lobbies` + `lobby_members` + ready-state; persistent groups survive across sessions | §Data Model |
| LOBBY-03 | Ready-check flow: members mark ready; lobby transitions when all members ready | §State Machine |
| LOBBY-04 | In-lobby chat via SignalR groups — ephemeral only, NO message persistence (anti-feature; no `lobby_messages` table) | §Ephemeral Chat, §LOBBY-04 Conflict Resolution |
| LOBBY-05 | Lobby → Matchmaking integration: a ready lobby submits a party ticket; `IMatchFoundHandler` or SignalR broadcast transitions state on match-found | §Matchmaking Integration, §Open Questions |
| LOBBY-06 | Lobby SignalR hub is `[Authorize]`-gated (player JWT) and runs on Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`, `ChannelPrefix = "GameKit"`) | §SignalR Wiring, §JWT Auth |
| OPS-11 | Advisory-lock live-verify gate — Lobby's key verified pairwise-distinct from five existing keys in a Wave 0 Testcontainers test | §Advisory Lock Key, §Validation Architecture |
</phase_requirements>

---

## Summary

`GameKit.Lobby` is a new NuGet package that follows the exact bootstrapping pattern established by `GameKit.Matchmaking` in Phase 5. It owns two Postgres tables (`lobbies` + `lobby_members`), has its own `__ef_migrations_lobby` history table, its own advisory-lock key (TBD, live-verify Wave 0), and its own `IDesignTimeDbContextFactory` + `LobbyMigrationModelCustomizer` that excludes all 20 prior-package entities. The package's primary runtime feature is a `LobbyHub : Hub` registered at `/hubs/lobby` that is `[Authorize]`-gated with JWT Bearer tokens and backed by a Redis backplane via `Microsoft.AspNetCore.SignalR.StackExchangeRedis`.

The project's existing JWT Bearer configuration in `GameKit.Auth.Builder.AuthBuilderExtensions` already wires `AddJwtBearer` — but does NOT hook `OnMessageReceived` to read `access_token` from the query string. The Lobby package must `IPostConfigureOptions<JwtBearerOptions>` to add the WebSocket query-string token extraction, scoped to the `/hubs/lobby` path, without replacing the existing event handler. Chat is ephemeral — messages relay through the SignalR group and are NEVER written to Postgres. The only extension seam is an optional `ILobbyMessageHandler` for relay-only side-effects (logging, rate-limit), NOT for persistence. The ready-check → matchmaking flow uses `IMatchmakingService.EnqueueAsync(playerId, ladderId, poolName, partyId, ct)` by first creating a Matchmaking Party row via `IPartyService.CreateAsync`, then submitting the party ticket.

**Primary recommendation:** Mirror `GameKit.Matchmaking` structure exactly (csproj, AssemblyInfo, migration constants, migration model customizer, migration hosted service, builder extension `AddLobby()`/`MapLobby()`). The only genuinely new territory is SignalR + the Redis backplane NuGet package. SignalR itself is in the ASP.NET Core shared framework (`FrameworkReference Microsoft.AspNetCore.App`); only `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8 is an explicit NuGet addition.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Lobby group membership persistence | Database (Postgres, EF Core) | — | `lobbies` + `lobby_members` tables; FK to `players`; must survive reconnects |
| Ready-check state machine | API / Backend (LobbyService) | Database | All-ready check + state transition requires SERIALIZABLE Tx to avoid race; broadcast via SignalR |
| Ephemeral in-lobby chat relay | SignalR Hub (LobbyHub) | Redis backplane | Messages NEVER hit Postgres; relay is purely in-memory via SignalR group on each hub instance; cross-instance delivery by Redis pub/sub |
| WebSocket JWT authentication | API / Backend (JwtBearer middleware) | — | `OnMessageReceived` hook reads `access_token` from query string before handshake; hub returns 401 before WebSocket upgrade if unauthenticated |
| Multi-instance broadcast | Redis backplane (SignalR StackExchange) | — | Hub instance A → Redis pub/sub → Hub instance B delivers to connected client |
| Ready → matchmaking submission | API / Backend (LobbyService) | Matchmaking package | `IPartyService.CreateAsync` + `IMatchmakingService.EnqueueAsync`; lobby state transitions to InGame on enqueue success |
| Hub authorization | API / Backend (ASP.NET Core authz) | — | `[Authorize]` attribute on `LobbyHub`; policy evaluates JWT Bearer claim; player must be a member of the lobby they join |

---

## Standard Stack

### Core (new to this package)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | **10.0.8** | Redis pub/sub backplane for SignalR scale-out | First-party Microsoft package; only NuGet package needed for the backplane; SignalR core is in `Microsoft.AspNetCore.App` shared framework; `ConnectionFactory` overload reuses existing `IConnectionMultiplexer` |

[VERIFIED: nuget.org] — `curl https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.signalr.stackexchangeredis/index.json` returned 10.0.8 as latest stable 10.x (2026-05-12). Package lives in `Microsoft.AspNetCore.SignalR.StackExchangeRedis.dll`, namespace `Microsoft.Extensions.DependencyInjection`.

### Already-pinned (via CPM `Directory.Packages.props` — no new pins needed)

| Library | Pinned Version | Purpose in Lobby |
|---------|---------------|-----------------|
| `StackExchange.Redis` | 2.8.41 | `IConnectionMultiplexer` injected into backplane `ConnectionFactory`; already required by Matchmaking + Presence |
| `Microsoft.EntityFrameworkCore` | 10.0.6 | Lobby migration + DbContext |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.1 | Postgres provider |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.6 | Design-time factory (PrivateAssets) |
| `FluentValidation` + `.DependencyInjectionExtensions` | 12.1.1 | Request DTO validation on HTTP REST layer |
| `Polly` | 8.5.2 | Non-HTTP resilience if needed |
| `FrameworkReference Microsoft.AspNetCore.App` | (shared) | SignalR core (`Hub`, `IHubContext`), JwtBearer events, rate-limiting |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Redis backplane | Azure SignalR Service | Forbidden — GPL zero-cloud constraint; explicitly called out in REQUIREMENTS §Out of Scope |
| `ConnectionFactory` to reuse IConnectionMultiplexer | Pass connection string directly | Both work; ConnectionFactory reuse is idiomatic when an IConnectionMultiplexer Singleton already exists in DI (avoids a second Redis connection pool) |
| `IPostConfigureOptions<JwtBearerOptions>` for WebSocket query-string | Forking `AddJwtBearer` in AddLobby | Post-configure is the non-breaking pattern — preserves GameKit.Auth's existing `OnMessageReceived` chain |

**Installation (new pin only):**

```xml
<!-- Directory.Packages.props — add one entry -->
<PackageVersion Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.8" />
```

```xml
<!-- GameKit.Lobby.csproj -->
<PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" />
```

---

## Package Legitimacy Audit

| Package | Registry | Age | Downloads | Source Repo | slopcheck | Disposition |
|---------|----------|-----|-----------|-------------|-----------|-------------|
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8 | NuGet | 8+ yrs (since .NET Core 2.1, 2018) | 239.7K (per nuget.org page) | github.com/dotnet/aspnetcore | [OK — first-party Microsoft] | Approved |

Note: slopcheck 0.6.1 is installed but checks PyPI, not NuGet — it returned a false SLOP verdict by looking on the wrong registry. This is a well-known cross-ecosystem false positive. The package is verified as authentic via `curl https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.signalr.stackexchangeredis/index.json` and via the official Microsoft Learn docs (`learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane`). [VERIFIED: nuget.org + official Microsoft docs]

**Packages removed due to slopcheck [SLOP] verdict:** none (false positive on wrong registry)
**Packages flagged as suspicious [SUS]:** none

---

## Architecture Patterns

### System Architecture Diagram

```
Game Client (WebSocket)                  LobbyHub Instance A         LobbyHub Instance B
       │                                        │                             │
       │  ws://.../hubs/lobby                  │                             │
       │  ?access_token=<JWT>                  │                             │
       │ ─────────────────────────────────────>│                             │
       │                                        │ JwtBearer OnMessageReceived │
       │                              read access_token from QS              │
       │                              validate JWT (phase-2 RSA key)         │
       │                         [FAIL → 401 before handshake]               │
       │                         [PASS → WebSocket upgrade]                  │
       │                                        │                             │
       │                              [Authorize] guard on Hub                │
       │                                        │                             │
       │  SendChatMessage("hello")              │                             │
       │ ─────────────────────────────────────>│                             │
       │                                        │ relay to group "lobby:{id}" │
       │                                        │──────────────────────────── │
       │                                        │        Redis backplane       │
       │                                        │  PUBLISH gamekit:signalr:... │
       │<────────────────────────────────────── │                             │
       │  ReceiveMessage (from Hub A clients)   │  ──────────────────────────>│
       │                                        │     SUBSCRIBE delivers      │
       │                                 (other client connected to B)        │
       │                                        │                    ─────────│
       │                                        │                    ReceiveMessage
       │                                        │                             │
       │  MarkReady()                           │                             │
       │ ─────────────────────────────────────>│                             │
       │                                        │ LobbyService.MarkReadyAsync │
       │                                        │   UPDATE lobby_members SET  │
       │                                        │     ready=true WHERE ...     │
       │                                        │   [all ready?]               │
       │                                        │    YES → SERIALIZABLE tx:   │
       │                                        │     lobby.state=InGame       │
       │                                        │     IPartyService.CreateAsync│
       │                                        │     IMatchmakingService      │
       │                                        │       .EnqueueAsync(partyId) │
       │                                        │     COMMIT                   │
       │<──────────────────────────────────────│ Groups.All.MatchStarting(...)│
       │                                        │                             │
```

### Recommended Project Structure

```
src/GameKit.Lobby/
├── GameKit.Lobby.csproj
├── AssemblyInfo.cs                    # InternalsVisibleTo test assemblies
├── GameKitLobbyOptions.cs             # Options: RedisConnectionString, etc.
├── LobbyOptionsValidator.cs           # IValidateOptions<GameKitLobbyOptions>
├── Builder/
│   ├── LobbyBuilderExtensions.cs      # AddLobby() — services registration
│   └── LobbyApplicationBuilderExtensions.cs  # MapLobby() → MapHub<LobbyHub>
├── Data/
│   ├── LobbyMigrationConstants.cs     # MigrationsHistoryTable + AdvisoryLockKey
│   ├── LobbyMigrationHostedService.cs # IHostedService — applies __ef_migrations_lobby
│   ├── LobbyDesignTimeDbContextFactory.cs  # + LobbyMigrationModelCustomizer (same file)
│   ├── LobbyModelBuilderExtension.cs  # IModelBuilderExtension — Lobby entities at runtime
│   ├── Configurations/
│   │   ├── LobbyConfiguration.cs
│   │   └── LobbyMemberConfiguration.cs
│   └── Migrations/
│       ├── 20260522000000_LobbyInitial.cs
│       ├── 20260522000000_LobbyInitial.Designer.cs
│       └── GameKitDbContextModelSnapshot.cs
├── Entities/
│   ├── Lobby.cs                       # lobbies table entity
│   ├── LobbyMember.cs                 # lobby_members table entity
│   └── LobbyState.cs                  # enum: Open=0, ReadyChecking=1, Closed=2, InGame=3
├── Hubs/
│   ├── LobbyHub.cs                    # [Authorize] Hub<ILobbyClient>
│   └── ILobbyClient.cs                # typed client interface
├── Services/
│   ├── ILobbyService.cs
│   ├── LobbyService.cs                # TryStartMatchmakingAsync, MarkReadyAsync, etc.
│   └── ILobbyMessageHandler.cs        # optional extension seam (relay/logging only)
└── Http/
    ├── LobbyEndpoints.cs              # REST: POST /api/lobbies, GET, DELETE members
    └── Contracts/
        ├── CreateLobbyRequest.cs
        └── JoinLobbyRequest.cs

tests/GameKit.Lobby.Integration.Tests/
├── GameKit.Lobby.Integration.Tests.csproj
├── CollectionDefinitions.cs
├── IntegrationTestHelpers.cs
├── LobbyTestApp.cs                    # TestServer with full Lobby pipeline
├── LobbyAdvisoryLockKeyTests.cs       # Wave 0 gate — RED until AdvisoryLockKey pinned
├── HubAuthTests.cs                    # SC#2: 401-before-handshake
├── BackplaneTests.cs                  # SC#5: two-TestServer cross-instance broadcast
├── ReadyCheckTests.cs                 # SC#3: all-ready → matchmaking → InGame broadcast
└── ChatEphemeralityTests.cs           # SC#4: no chat table, nothing written to Postgres
```

### Pattern 1: SignalR + Redis Backplane Registration

**What:** Wire `AddSignalR().AddStackExchangeRedis(...)` in `AddLobby()`, reusing the existing `IConnectionMultiplexer` Singleton via `ConnectionFactory`.

**When to use:** Always — backplane is required from day one (SC#5).

```csharp
// Source: learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0
// + ConnectionFactory pattern for IConnectionMultiplexer reuse

public static IGameKitBuilder AddLobby(
    this IGameKitBuilder builder,
    Action<GameKitLobbyOptions>? configure = null)
{
    // ... options, migration, etc. ...

    // SignalR + Redis backplane — reuse the existing IConnectionMultiplexer Singleton
    // (registered by consumer; same multiplexer Matchmaking and Presence use)
    builder.Services.AddSignalR()
        .AddStackExchangeRedis(options =>
        {
            options.ConnectionFactory = async writer =>
            {
                var mux = builder.Services.BuildServiceProvider()  // avoid; use IServiceProvider at runtime
                    .GetRequiredService<IConnectionMultiplexer>();
                return mux;
            };
            // ChannelPrefix isolates GameKit Lobby SignalR channels from any consumer-level SignalR
            options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit");
        });

    // Register model extension, migration service, hub context, services ...
    return builder;
}
```

**Important:** The `ConnectionFactory` approach is the correct idiom for reusing an existing multiplexer — pass a `Func<TextWriter, Task<IConnectionMultiplexer>>`. At `AddLobby()` registration time, services are not yet built; use `IPostConfigureOptions<RedisOptions>` to resolve `IConnectionMultiplexer` at startup rather than at `AddLobby()` call time.

Correct pattern (deferred resolution):

```csharp
// Source: learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane (ConnectionFactory overload)
builder.Services.AddSignalR()
    .AddStackExchangeRedis(options => { /* ChannelPrefix only here */ });

// IPostConfigureOptions<RedisOptions> to inject the live IConnectionMultiplexer
builder.Services.AddSingleton<IPostConfigureOptions<RedisOptions>, LobbyRedisBackplanePostConfigure>();

// LobbyRedisBackplanePostConfigure:
//   public void PostConfigure(string? name, RedisOptions options)
//   {
//       var mux = _sp.GetRequiredService<IConnectionMultiplexer>();
//       options.ConnectionFactory = _ => Task.FromResult(mux);
//   }
```

[CITED: learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0]

### Pattern 2: WebSocket JWT Authentication (SC#2)

**What:** Hook `JwtBearerEvents.OnMessageReceived` to read `access_token` from the query string when the request path starts with `/hubs/lobby`. Must be added as `IPostConfigureOptions<JwtBearerOptions>` — NOT by replacing the existing event handler that `GameKit.Auth` may have set.

**When to use:** Any SignalR hub protected by Bearer JWT — required because browsers cannot set `Authorization` headers on WebSocket connections.

```csharp
// Source: learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0
// "Built-in JWT authentication" section — OnMessageReceived pattern

// GameKit.Lobby registers this so consumers don't need to wire it manually:
internal sealed class LobbyJwtBearerPostConfigure : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        // Chain with any existing OnMessageReceived (e.g., from consumer or IdentityServer)
        var existingHandler = options.Events.OnMessageReceived;
        options.Events.OnMessageReceived = async context =>
        {
            if (existingHandler is not null)
                await existingHandler(context);

            // Only read query-string token if not already set (e.g., by a prior handler)
            if (string.IsNullOrEmpty(context.Token))
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/lobby"))
                {
                    context.Token = accessToken;
                }
            }
        };
    }
}
```

**401 before handshake:** When `context.Token` remains empty (or invalid) after `OnMessageReceived`, the JwtBearer middleware returns HTTP 401 to the WebSocket upgrade request. The WebSocket handshake never completes. The `[Authorize]` attribute on `LobbyHub` enforces this at the hub level for a second layer. Both are needed:

- JWT validation happens in middleware (returns 401 to the upgrade HTTP request).
- `[Authorize]` on the hub catches any cases where the pipeline allowed the request through but the user is unauthenticated.

**Testing SC#2 with TestServer:** Use `_host.GetTestClient()` for the standard HTTP flow. For WebSocket testing, use `WebApplicationFactory` + `HttpClient` with the `?access_token=` query string on the negotiate endpoint. Assert the negotiate response is 401 when no token is provided.

[CITED: learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0]

### Pattern 3: LobbyHub Hub Methods

```csharp
// Source: [ASSUMED] — standard SignalR Hub<T> pattern from codebase conventions

[Authorize]
public sealed class LobbyHub : Hub<ILobbyClient>
{
    private readonly ILobbyService _lobby;
    // ICurrentPlayer does not work for SignalR — use Context.UserIdentifier or Context.User directly
    // Hub.Context.User.FindFirst("sub") mirrors the TryGetPlayerId pattern in MatchmakingEndpoints

    public LobbyHub(ILobbyService lobby) { _lobby = lobby; }

    /// <summary>Subscribe this connection to the lobby SignalR group.</summary>
    public async Task JoinLobbyAsync(Guid lobbyId)
    {
        var playerId = GetPlayerId();
        // Authorization check: verify player is a member of this lobby
        if (!await _lobby.IsMemberAsync(lobbyId, playerId))
            throw new HubException("Player is not a member of this lobby.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"lobby:{lobbyId}");
    }

    /// <summary>Relay an ephemeral chat message to the lobby group (LOBBY-04: never written to Postgres).</summary>
    public async Task SendChatMessageAsync(Guid lobbyId, string message)
    {
        var playerId = GetPlayerId();
        // Optional: invoke ILobbyMessageHandler (relay-only seam, not a persistence hook)
        await Clients.Group($"lobby:{lobbyId}").ReceiveChatMessageAsync(playerId, message);
    }

    /// <summary>Mark the calling player as ready.</summary>
    public async Task MarkReadyAsync(Guid lobbyId)
    {
        var playerId = GetPlayerId();
        await _lobby.MarkReadyAsync(lobbyId, playerId, Context.GetHttpContext()!.RequestAborted);
        // LobbyService broadcasts state update via IHubContext<LobbyHub> after the DB commit
    }

    private Guid GetPlayerId()
    {
        var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? Context.User?.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var id))
            throw new HubException("Player identity not found in JWT.");
        return id;
    }
}

public interface ILobbyClient
{
    Task ReceiveChatMessageAsync(Guid senderId, string message);
    Task ReceiveStateUpdateAsync(LobbyStateUpdate update);
}
```

**Note on ICurrentPlayer:** `HttpContextCurrentPlayer` reads `IHttpContextAccessor` — but `IHttpContextAccessor.HttpContext` is null inside SignalR hub methods (the Hub has its own `Context`, not an `HttpContext` per request). Player id must be extracted directly from `Context.User`, not from `ICurrentPlayer`.

### Pattern 4: LobbyService.TryStartMatchmakingAsync (SC#3)

**What:** When all `lobby_members.ready = true`, atomically transition to `InGame` and submit to matchmaking.

**Concurrency:** Uses SERIALIZABLE isolation to prevent two concurrent `MarkReadyAsync` calls from both detecting all-ready and submitting two tickets. Pattern mirrors `IdentityLinker` and `AccountMergeService` from prior phases.

```csharp
// Source: [ASSUMED] — mirrors ProposalService + IdentityLinker SERIALIZABLE pattern

public async Task MarkReadyAsync(Guid lobbyId, Guid playerId, CancellationToken ct)
{
    // Retry loop for 40001 serialization failure — same pattern as IdentityLinker
    for (var attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            await using var tx = await _ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var lobby = await _ctx.Set<Lobby>()
                .Include(l => l.Members)
                .FirstOrDefaultAsync(l => l.Id == lobbyId, ct)
                ?? throw new LobbyNotFoundException(lobbyId);

            var member = lobby.Members.FirstOrDefault(m => m.PlayerId == playerId)
                ?? throw new NotAMemberException(lobbyId, playerId);

            member.Ready = true;
            await _ctx.SaveChangesAsync(ct);

            if (lobby.Members.All(m => m.Ready) && lobby.State == LobbyState.ReadyChecking)
            {
                await TryStartMatchmakingAsync(lobby, ct);
                // State → InGame set inside TryStartMatchmakingAsync before Commit
            }

            await tx.CommitAsync(ct);

            // Broadcast state update AFTER commit (via IHubContext<LobbyHub>)
            await _hubContext.Clients.Group($"lobby:{lobbyId}")
                .ReceiveStateUpdateAsync(new LobbyStateUpdate(lobby.State));
            return;
        }
        catch (Exception ex) when (TryFindPostgresException(ex)?.SqlState == "40001")
        {
            if (attempt == 2) throw;
            // backoff then retry
        }
    }
}

private async Task TryStartMatchmakingAsync(Lobby lobby, CancellationToken ct)
{
    // 1. Create a Matchmaking Party with all lobby members (via IPartyService)
    //    IPartyService.CreateAsync is in GameKit.Matchmaking → Lobby takes a runtime dep on Matchmaking
    var partyId = await _partyService.CreateAsync(
        ownerId: lobby.OwnerId,
        memberIds: lobby.Members.Select(m => m.PlayerId).ToList(),
        ct);

    // 2. Enqueue with the party ticket
    //    IMatchmakingService.EnqueueAsync(playerId, ladderId, poolName, partyId, ct)
    //    poolName: from GameKitLobbyOptions or lobby.LadderId config
    var result = await _matchmakingService.EnqueueAsync(
        playerId: lobby.OwnerId,
        ladderId: lobby.LadderId!.Value,
        poolName: lobby.RegionName ?? "default",
        partyId: partyId,
        ct: ct);

    if (result.Outcome == EnqueueOutcome.Queued)
        lobby.State = LobbyState.InGame;
    // else: leave in ReadyChecking; broadcast failure to group
}
```

### Pattern 5: Ephemeral Chat — ILobbyMessageHandler Seam (LOBBY-04)

**What:** Chat messages relay through the hub to the SignalR group only. No `lobby_messages` table. The `ILobbyMessageHandler` seam is a relay/logging extension point — NOT a persistence hook.

**Authority reconciliation (STATE.md vs. CONTEXT.md):**
- `STATE.md §v2.0 Pending Decisions` has an entry: "lobby_messages persistence decision → Phase 11 → Persist with 30-day retention cleanup + ILobbyMessageHandler extension point (confirmed in ARCHITECTURE.md Q5)". This entry was written based on v1.0 ARCHITECTURE.md research which predates the v2.0 REQUIREMENTS.md.
- `REQUIREMENTS.md LOBBY-04` is authoritative: "ephemeral only, no message persistence (documented anti-feature: no chat log storage, GDPR/moderation out of scope)".
- `CONTEXT.md` repeats: "LOBBY-04 is an ANTI-feature: chat is NEVER persisted".
- `ROADMAP.md SC#4` is explicit: "chat is ephemeral — an integration test asserts NO chat-message table exists and nothing is written to Postgres on send".

**Decision:** LOBBY-04 wins. There is NO `lobby_messages` table, NO retention job for chat, NO persistence in `ILobbyMessageHandler`. The handler is a relay-only seam for optional side effects (e.g., rate-limit, structured logging). The migration exclusion list for Lobby has 20 entries (not 21 — no `LobbyMessage` entity).

```csharp
/// <summary>
/// Optional extension point invoked when a chat message is relayed to a lobby group.
/// This handler MUST NOT write the message to Postgres — chat is ephemeral (LOBBY-04 anti-feature).
/// Use for: rate-limit checks, structured logging, per-message telemetry.
/// </summary>
public interface ILobbyMessageHandler
{
    /// <summary>Called before the message is relayed to the SignalR group.</summary>
    /// <returns><c>true</c> to relay; <c>false</c> to suppress (e.g., rate-limit exceeded).</returns>
    Task<bool> OnMessageAsync(Guid lobbyId, Guid senderId, string message, CancellationToken ct);
}
```

### Pattern 6: Two-TestServer Backplane Integration Test (SC#5)

**What:** Two independent `TestServer` instances share a single Testcontainers Redis. A client connected to Server A receives a message broadcast from a client connected to Server B — proving the Redis backplane routes the message cross-instance.

```csharp
// Source: [ASSUMED] — mirrors PresenceTestApp + MatchmakingTestApp patterns, extended for SignalR

// In BackplaneTests:
public sealed class BackplaneTests : IAsyncLifetime
{
    private LobbyTestApp _appA = default!;
    private LobbyTestApp _appB = default!;

    public async Task InitializeAsync()
    {
        // Both apps share the same PostgresFixture db + RedisFixture connection string
        _appA = new LobbyTestApp();
        _appB = new LobbyTestApp();
        await _appA.StartAsync(_pg, _redis);
        await _appB.StartAsync(_pg, _redis);  // same redis.ConnectionString
    }

    [Fact(DisplayName = "SC#5: cross-instance broadcast via shared Redis backplane")]
    public async Task CrossInstance_Broadcast_Reaches_OtherServer()
    {
        // Connect clientA to appA, clientB to appB
        // clientA invokes SendChatMessage on AppA hub
        // Assert clientB receives the message (delivered through Redis backplane → AppB)
    }
}
```

**Key detail:** `LobbyTestApp.StartAsync` replaces `IConnectionMultiplexer` registration with the shared Testcontainers multiplexer — same pattern as `MatchmakingTestApp`:

```csharp
var muxDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
if (muxDescriptor is not null) services.Remove(muxDescriptor);
services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redis.ConnectionString));
```

**SignalR client for tests:** Use `Microsoft.AspNetCore.SignalR.Client` (10.0.8) in the test project. Wire it via the test server's WebSocket handler:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost/hubs/lobby", o =>
    {
        o.HttpMessageHandlerFactory = _ => _appA.Server.CreateHandler();
        o.AccessTokenProvider = () => Task.FromResult(_appA.MintPlayerJwt(playerId))!;
    })
    .Build();
await connection.StartAsync();
```

[CITED: learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0 — AccessTokenProvider pattern]

### Anti-Patterns to Avoid

- **ICurrentPlayer in Hub methods:** `HttpContextCurrentPlayer` reads `IHttpContextAccessor.HttpContext` which is null in SignalR hubs. Always use `Context.User.FindFirst("sub")` directly.
- **Persisting chat messages:** Any `DbContext.Set<LobbyMessage>()` or insert in `SendChatMessageAsync` violates LOBBY-04. The SC#4 test explicitly asserts no rows in any "lobby_message*" table.
- **Adding `lobby_id` column to `matchmaking_tickets` from Lobby's migration:** The migration boundary prohibits Lobby from modifying Matchmaking's table. The LOBBY-05 "lobby_id FK on matchmaking_tickets" wording is misleading — the correct approach is creating a Matchmaking `Party` row and passing `partyId` to `EnqueueAsync`. See §Open Questions Q1.
- **Registering a second `IConnectionMultiplexer`:** `AddLobby()` must NOT register its own `IConnectionMultiplexer` — reuse the existing Singleton. Use `IPostConfigureOptions<RedisOptions>` to resolve it after DI is built.
- **`services.AddSignalR()` called twice:** If both Lobby and a future package (e.g., Admin in Phase 12) call `AddSignalR()`, SignalR deduplicates internally. The backplane however MUST NOT be registered twice. Use `TryAddEnumerable` pattern or check for existing backplane registration.
- **`app.UseWebSockets()` not called:** SignalR requires `UseWebSockets()` in the pipeline before `MapHub<>`. In minimal API apps, this is typically implicit via `UseRouting`; in `TestServer` tests it may need explicit registration.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Cross-instance SignalR message delivery | Custom Redis pub/sub in hub | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | Handles serialization, channel naming, reconnection, error handling; ~6 years of battle testing |
| WebSocket JWT extraction | Custom middleware reading QS | `IPostConfigureOptions<JwtBearerOptions>` + `OnMessageReceived` | Official pattern per Microsoft docs; chains with existing handler |
| SignalR group lifecycle | Tracking membership in Redis | `Hub.Groups.AddToGroupAsync` / `RemoveFromGroupAsync` | Built-in — SignalR manages connection-to-group mapping, including reconnect edge cases |
| Serialization failure retry | Ad-hoc catch/sleep | `TryFindPostgresException` helper (already exists in GameKit.Auth / AccountMerge) | Existing util walks InnerException chain for 40001; bounded depth 8 |

**Key insight:** SignalR groups (`Groups.AddToGroupAsync`) are ephemeral by design — they live for the duration of a connection. Lobby group membership (persistent across reconnects) must be re-asserted in `OnConnectedAsync` by querying `lobby_members` from Postgres and re-adding to the SignalR group.

---

## LOBBY-04 Conflict Resolution

The STATE.md "v2.0 Pending Decisions" table states: "lobby_messages persistence decision → Phase 11 → Persist with 30-day retention + ILobbyMessageHandler extension point". This entry was authored during v1.0 planning when the ARCHITECTURE.md Q5 answer was "persist". The v2.0 REQUIREMENTS.md LOBBY-04 subsequently declared persistence an explicit anti-feature. The CONTEXT.md for Phase 11 repeats this and the ROADMAP SC#4 requires a Testcontainers test asserting no chat table exists.

**Resolution for the planner:** The CONTEXT.md + REQUIREMENTS.md + ROADMAP form the authoritative boundary for Phase 11. The STATE.md "Pending Decisions" entry is superseded. Implement chat as fully ephemeral. The `ILobbyMessageHandler` seam (if added) is a relay/gate extension only — it has no `SaveAsync` signature, no `lobby_messages` entity, no retention service.

---

## Matchmaking Integration Design (LOBBY-05)

### LOBBY-05 Wording vs. Migration Boundary

`REQUIREMENTS.md LOBBY-05` says: "a ready lobby submits a party ticket (`lobby_id` FK on `matchmaking_tickets`)". This wording implies adding a `lobby_id` column to Matchmaking's table. However:

1. The migration boundary (`CLAUDE.md "Migration boundaries"`, PITFALLS #3) prohibits Lobby from modifying Matchmaking's tables.
2. The ARCHITECTURE.md Q5 resolves this: Lobby creates a Matchmaking `Party` via `IPartyService.CreateAsync`, then calls `IMatchmakingService.EnqueueAsync(partyId: newPartyId)`. The party row in Matchmaking serves as the cross-package link.
3. `IMatchmakingService.EnqueueAsync` signature is `(Guid playerId, Guid ladderId, string? poolName, Guid? partyId, CancellationToken ct)` — confirmed from codebase.
4. No `lobby_id` column is needed on `matchmaking_tickets` because the party row already provides the link.

**Recommended resolution (for planner):** Implement LOBBY-05 via `IPartyService.CreateAsync` + `IMatchmakingService.EnqueueAsync(partyId)`. Do NOT add `lobby_id` to `matchmaking_tickets` from Lobby's migration. If future traceability from a matchmaking ticket back to a lobby is needed, it can be a Phase 12+ migration in Matchmaking's own migration file. See §Open Questions Q1.

### Package Dependency Arc

`GameKit.Lobby → GameKit.Matchmaking` (runtime). This requires:
- `GameKit.Lobby.csproj`: `<ProjectReference Include="..\GameKit.Matchmaking\GameKit.Matchmaking.csproj" />`
- `LobbyMigrationModelCustomizer`: exclusion list includes 20 entities (Matchmaking's 5 + prior packages' 15).
- Lobby's `AddLobby()` injects `IMatchmakingService` and `IPartyService` from Matchmaking.

No circular ref: Matchmaking has no `ProjectReference` to Lobby.

---

## Advisory Lock Key

The Lobby advisory lock key is TBD — it MUST be live-verified before the constant is pinned.

**SQL to execute in Wave 0 test:**

```sql
SELECT hashtext('gamekit.lobby.migrations')::bigint
```

**Known five existing keys (must be pairwise-distinct):**

| Package | Key |
|---------|-----|
| Core | 1800940027 |
| Auth | -298890956 |
| Admin | -2101739634 |
| Rankings | -156812172 |
| Matchmaking | 388956820 |

The Wave 0 test (`LobbyAdvisoryLockKeyTests`) mirrors `MatchmakingAdvisoryLockKeyTests` exactly:
1. `PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation` — RED until `LobbyMigrationConstants.AdvisoryLockKey` is updated from placeholder `0L` to live value.
2. `LobbyKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Matchmaking_Keys` — asserts non-equality against all five by both symbolic reference AND integer literal (defense-in-depth).

Migration timestamp convention: `20260522000000_LobbyInitial` (Matchmaking = 20260516, one day after Matchmaking as per the convention; Auth=20260418, Admin=20260419, Rankings=20260420, Matchmaking=20260516). Lobby follows next deterministic slot.

---

## Data Model

### `lobbies` table

```sql
CREATE TABLE gamekit.lobbies (
    "Id"           uuid         NOT NULL PRIMARY KEY,
    "OwnerId"      uuid         REFERENCES gamekit.players("Id") ON DELETE SET NULL,
    "LadderId"     uuid         REFERENCES gamekit.ladders("Id") ON DELETE SET NULL,
    "State"        integer      NOT NULL DEFAULT 0,   -- LobbyState enum, integer (Phase 5 convention)
    "MaxMembers"   integer      NOT NULL DEFAULT 8,
    "RegionName"   text         NULL,                 -- optional pool affinity, matches PoolName
    "CreatedAt"    timestamptz  NOT NULL,
    "UpdatedAt"    timestamptz  NOT NULL
);
```

### `lobby_members` table

```sql
CREATE TABLE gamekit.lobby_members (
    "Id"          uuid         NOT NULL PRIMARY KEY,
    "LobbyId"     uuid         NOT NULL REFERENCES gamekit.lobbies("Id") ON DELETE CASCADE,
    "PlayerId"    uuid         NOT NULL REFERENCES gamekit.players("Id") ON DELETE CASCADE,
    "Ready"       boolean      NOT NULL DEFAULT false,
    "JoinedAt"    timestamptz  NOT NULL,
    CONSTRAINT uq_lobby_members_lobby_player UNIQUE ("LobbyId", "PlayerId")
);
CREATE INDEX idx_lobby_members_lobby ON gamekit.lobby_members ("LobbyId");
```

### `LobbyState` enum

```csharp
// integer-backed per Phase 5 convention (HasConversion<string>() burned Phase 5 — never again)
public enum LobbyState
{
    Open           = 0,   // accepting members
    ReadyChecking  = 1,   // all must mark ready before matchmaking
    Closed         = 2,   // locked, no new members
    InGame         = 3,   // matchmaking submitted; terminal state for session
}
```

**No `lobby_messages` table exists.** SC#4 test verifies: `SELECT table_name FROM information_schema.tables WHERE table_schema='gamekit' AND table_name LIKE 'lobby_message%'` returns zero rows.

---

## Migration Pattern (mirrors Matchmaking exactly)

### LobbyMigrationConstants

```csharp
public static class LobbyMigrationConstants
{
    public const string MigrationsHistoryTable = "__ef_migrations_lobby";
    /// <summary>
    /// Placeholder — MUST be replaced with live-verified value from
    /// <c>SELECT hashtext('gamekit.lobby.migrations')::bigint</c> on Postgres 17.9.
    /// LobbyAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation
    /// is RED until this is updated (Wave 0 gate).
    /// </summary>
    public const long AdvisoryLockKey = 0L;  // Wave 0: replace with live value
}
```

### LobbyMigrationModelCustomizer exclusion list (20 entities)

```csharp
// Core entities (4) — Phase 1
typeof(Player), typeof(GameSession), typeof(SessionParticipant), typeof(AdminAuditLog)
// Auth entities (3) — Phase 2
typeof(PlayerIdentity), typeof(PlayerCredential), typeof(RefreshToken)
// Admin.UI entities (1) — Phase 3
typeof(AdminUser)
// Rankings entities (7) — Phase 4
typeof(Ladder), typeof(PlayerRank), typeof(PendingRatingUpdate), typeof(SessionCompleteIdempotency),
typeof(LadderSeason), typeof(SeasonRankArchive), typeof(ServiceToken)
// Matchmaking entities (5) — Phase 5
typeof(Party), typeof(PartyMember), typeof(MatchmakingTicket), typeof(TicketEvent), typeof(DeclineHistory)
```

### LobbyMigrationHostedService

Mirrors `MatchmakingMigrationHostedService` exactly:
- Internal sealed, `IHostedService`
- Checks `GameKitOptions.AutoMigrate`; skips if false
- Calls `MigrationRunner.MigrateWithLockAsync(ctx, LobbyMigrationConstants.AdvisoryLockKey, ct)`
- Suppresses `RelationalEventId.PendingModelChangesWarning`

### csproj structure (mirrors Matchmaking)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>GameKit.Lobby</PackageId>
    <Description>Lobby package for GameKit — ready-checks, ephemeral chat via SignalR, persistent groups (Postgres). Phase 11.</Description>
    <PackageTags>gamekit;lobby;signalr;redis;gpl</PackageTags>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GameKit.Core\GameKit.Core.csproj" />
    <ProjectReference Include="..\GameKit.Rankings\GameKit.Rankings.csproj" />
    <ProjectReference Include="..\GameKit.Auth\GameKit.Auth.csproj" />
    <ProjectReference Include="..\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />
    <ProjectReference Include="..\GameKit.Matchmaking\GameKit.Matchmaking.csproj" />
    <!-- GameKit.Build Roslyn analyzer — OutputItemType=Analyzer ReferenceOutputAssembly=false -->
    <ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Npgsql" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="StackExchange.Redis" />
  </ItemGroup>
</Project>
```

---

## Common Pitfalls

### Pitfall 1: ICurrentPlayer Returns Null in Hub Methods
**What goes wrong:** `ICurrentPlayer.PlayerId` returns null inside hub methods because `HttpContextCurrentPlayer` reads `IHttpContextAccessor.HttpContext` which is null in SignalR contexts.
**Why it happens:** SignalR hubs do not run in an HTTP request context — `HttpContextAccessor` is not populated for hub invocations.
**How to avoid:** Always use `Context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Context.User.FindFirst("sub")?.Value` directly inside hub methods, mirroring `TryGetPlayerId` in `MatchmakingEndpoints.cs`.
**Warning signs:** Null reference exceptions on `ICurrentPlayer.PlayerId` in hub methods; unit tests passing but integration tests failing.

### Pitfall 2: SignalR Groups Are Not Persisted Across Reconnects
**What goes wrong:** A player disconnects and reconnects. Their new `ConnectionId` is NOT in any SignalR group — they stop receiving broadcasts.
**Why it happens:** SignalR group membership is in-memory per connection, not per user. Reconnect creates a new `ConnectionId`.
**How to avoid:** Override `OnConnectedAsync()` in `LobbyHub` to query `lobby_members` and re-add the user's new `ConnectionId` to their lobby groups. This requires resolving the player's lobby memberships from Postgres.
**Warning signs:** Reconnected players not receiving chat or state update messages.

### Pitfall 3: `AddSignalR()` Must Be Called Before `AddStackExchangeRedis()`
**What goes wrong:** Calling `AddStackExchangeRedis(...)` before `AddSignalR()` compiles but throws at startup because `ISignalRServerBuilder` hasn't registered its services yet.
**Why it happens:** `AddStackExchangeRedis` is an extension on `ISignalRServerBuilder` returned by `AddSignalR()`. Chaining is required: `services.AddSignalR().AddStackExchangeRedis(...)`.
**How to avoid:** Always chain: `builder.Services.AddSignalR().AddStackExchangeRedis(...)`.

### Pitfall 4: Per-Connection State vs. Per-User State
**What goes wrong:** Two browser tabs open for the same player result in two connections. A `Groups.RemoveFromGroupAsync` call on disconnect removes the group for that specific `ConnectionId` only — the other tab still receives messages.
**Why it happens:** SignalR `ConnectionId` is per-physical-connection, not per-user.
**How to avoid:** Use `IUserIdProvider` to set the user identifier to the player's `sub` claim, then use `Clients.User(userId)` for per-user broadcasts. Group membership is for lobby-scoped broadcast, not per-player targeting.

### Pitfall 5: Race Condition in `TryStartMatchmakingAsync`
**What goes wrong:** Two concurrent `MarkReadyAsync` calls both see "all ready" and both submit matchmaking tickets for the same lobby.
**Why it happens:** Without SERIALIZABLE isolation, two concurrent reads can both see all members ready before either commits.
**How to avoid:** SERIALIZABLE transaction with 3-attempt retry on 40001 (Postgres serialization failure) — same pattern as `IdentityLinker.cs` and `AccountMergeService.cs`. The `lobby.State == ReadyChecking` check inside the SERIALIZABLE tx prevents double-submission on the second attempt.
**Warning signs:** Duplicate matchmaking tickets for the same party; `EnqueueOutcome.AlreadyEnqueued` on the second attempt.

### Pitfall 6: ChannelPrefix Must Match Across All Instances
**What goes wrong:** Two deployments of a lobby service set different `ChannelPrefix` values; cross-instance messages never arrive.
**Why it happens:** Redis backplane pub/sub channels are prefixed — a mismatch means they publish and subscribe to different channels.
**How to avoid:** Pin `ChannelPrefix = RedisChannel.Literal("GameKit")` in code (not configuration), identical to the REQUIREMENTS spec. Do not let it be configurable via `appsettings.json` in v1.

### Pitfall 7: `TestServer` Does Not Start Kestrel — UseWebSockets May Not Be Implicit
**What goes wrong:** WebSocket tests fail with connection refused or handshake failure in `TestServer`.
**Why it happens:** `TestServer` uses an in-memory transport, not a real port. `UseWebSockets()` must be explicit in the test configure pipeline if `UseRouting()` doesn't implicitly include it.
**How to avoid:** Add `app.UseWebSockets()` before `app.UseRouting()` in `LobbyTestApp.Configure`. Alternatively, ASP.NET Core 10 minimal API routing includes WebSockets implicitly — verify by testing the connection.

---

## Code Examples

### AddLobby() Registration Skeleton

```csharp
// Source: mirrors GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs (codebase verified)

public static IGameKitBuilder AddLobby(
    this IGameKitBuilder builder,
    Action<GameKitLobbyOptions>? configure = null)
{
    ArgumentNullException.ThrowIfNull(builder);

    // 1. Options + validation
    var optsBuilder = builder.Services.AddOptions<GameKitLobbyOptions>();
    if (configure is not null) optsBuilder.Configure(configure);
    optsBuilder.ValidateOnStart();
    builder.Services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IValidateOptions<GameKitLobbyOptions>, LobbyOptionsValidator>());

    // 2. Lobby model extension (contributes lobbies + lobby_members to runtime DbContext)
    builder.Services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IModelBuilderExtension, LobbyModelBuilderExtension>());

    // 3. Migration runner
    builder.Services.AddHostedService<LobbyMigrationHostedService>();

    // 4. SignalR + Redis backplane (ChannelPrefix matches LOBBY-06 spec)
    builder.Services.AddSignalR()
        .AddStackExchangeRedis(options =>
        {
            options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit");
        });
    // Deferred IConnectionMultiplexer resolution (IPostConfigureOptions<RedisOptions>)
    builder.Services.AddSingleton<IPostConfigureOptions<RedisOptions>, LobbyRedisBackplanePostConfigure>();

    // 5. JWT Bearer WebSocket query-string token extraction (IPostConfigureOptions<JwtBearerOptions>)
    builder.Services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, LobbyJwtBearerPostConfigure>());

    // 6. Lobby services
    builder.Services.AddScoped<ILobbyService, LobbyService>();

    // 7. Optional relay seam (no-op default if not registered by consumer)
    builder.Services.TryAddSingleton<ILobbyMessageHandler, NullLobbyMessageHandler>();

    return builder;
}
```

### MapLobby() Hub Mapping

```csharp
// Source: mirrors MatchmakingApplicationBuilderExtensions.MapMatchmaking pattern (codebase verified)
// Note: for SignalR, MapHub<T> requires IEndpointRouteBuilder (UseEndpoints lambda or top-level app)

public static IEndpointRouteBuilder MapLobby(this IEndpointRouteBuilder routes)
{
    ArgumentNullException.ThrowIfNull(routes);
    routes.MapHub<LobbyHub>("/hubs/lobby");
    return routes;
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Azure SignalR Service for scale-out | Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) | GameKit requirement from day one | Zero cloud dependency; self-hostable |
| Bearer token in Authorization header for WebSockets | `access_token` query string via `OnMessageReceived` | ASP.NET Core 2.1+ | Required because browsers cannot set WebSocket headers |
| `ChannelPrefix` as `string` | `ChannelPrefix = RedisChannel.Literal("GameKit")` | .NET 8+ (StackExchange.Redis 2.x) | `RedisChannel.Literal` disables glob pattern matching on the prefix — correct for a fixed prefix |
| Storing SignalR group membership externally | `Hub.Groups.AddToGroupAsync` + re-add on `OnConnectedAsync` | ASP.NET Core 3.0+ | SignalR manages connection-to-group mapping; external tracking is redundant for delivery |

**Deprecated/outdated:**
- `Microsoft.AspNetCore.SignalR.Redis` (depends on StackExchange.Redis 1.x): removed in .NET 3.0. Use `Microsoft.AspNetCore.SignalR.StackExchangeRedis` exclusively.

---

## Runtime State Inventory

This is a greenfield package — no rename/refactor phase. No existing runtime state to inventory. Noting for completeness:

| Category | Items Found | Action Required |
|----------|-------------|-----------------|
| Stored data | None — no `GameKit.Lobby` tables exist yet | Migration creates them |
| Live service config | None | N/A |
| OS-registered state | None | N/A |
| Secrets/env vars | None (Lobby uses existing `IConnectionMultiplexer` and connection string from `GameKitOptions`) | N/A |
| Build artifacts | None | N/A |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `IPartyService.CreateAsync` in `GameKit.Matchmaking` accepts a list of member player IDs and returns a new `partyId` — the exact signature is not verified from source | §Matchmaking Integration, §State Machine Pattern | If signature differs, `TryStartMatchmakingAsync` must adapt; planner should read `IPartyService.cs` before writing Wave 2 tasks |
| A2 | `ConnectionFactory` in `RedisOptions` accepts a `Func<TextWriter, Task<IConnectionMultiplexer>>` that returns an already-connected multiplexer | §SignalR Wiring Pattern | If the factory signature differs, the IPostConfigureOptions approach needs adjustment; official docs confirm the `ConnectionFactory` property exists |
| A3 | `app.UseWebSockets()` is not required explicitly in .NET 10 minimal API because `UseRouting()` includes it | §Pitfall 7 | If required, `LobbyTestApp.Configure` must add `app.UseWebSockets()` before `UseRouting()` |
| A4 | The migration timestamp `20260522000000` does not conflict with any existing migration across all packages | §Advisory Lock Key | Extremely low risk — each package has its own `__ef_migrations_<pkg>` table; timestamps are per-package, not global |
| A5 | The `AddMatchmaking() → AddLobby()` ordering constraint (Lobby's hosted service runs after Matchmaking's) is satisfied by the natural hosted-service ordering; no explicit dependency ordering is needed | §Migration Pattern | If Lobby migration requires Matchmaking tables to exist first (FK `lobby_members.lobby_id` → no cross-package FK; lobby tables have no FK to Matchmaking tables), ordering is irrelevant |

**If this table is empty of blocking assumptions:** A1 (IPartyService signature) is the only one that could affect Plan 2+ tasks — the planner should verify it before writing the ready-check service.

---

## Open Questions (RESOLVED)

> RESOLVED during planning: Q1 (LOBBY-05 lobby_id FK) → implemented via `IPartyService.CreateAsync(ownerPlayerId)` + `JoinAsync` per member + `IMatchmakingService.EnqueueAsync(partyId)`; NO `lobby_id` FK on `matchmaking_tickets` (migration-boundary compliant). Q2 (`IPartyService.CreateAsync` signature) → verified from source: `Task<Party> CreateAsync(Guid ownerPlayerId, CancellationToken ct = default)`. Both reflected in Plan 04 `<interfaces>`.

1. **LOBBY-05: Does Lobby need `lobby_id` on `matchmaking_tickets`?** — RESOLVED: no; use the party link.
   - What we know: REQUIREMENTS says "lobby_id FK on matchmaking_tickets". ARCHITECTURE.md says use `IPartyService.CreateAsync` + `EnqueueAsync(partyId)`. Migration boundary prohibits Lobby adding a column to Matchmaking's table.
   - What's unclear: Whether the REQUIREMENTS wording intended (a) Lobby adding a FK column to Matchmaking's table (boundary violation), or (b) the conceptual link via `partyId`, or (c) a Matchmaking migration in a future phase adding an optional `lobby_id` column.
   - Recommendation: Implement via `partyId` link (ARCHITECTURE.md approach). The planner should document this as a deviation from LOBBY-05 literal wording and note it as a v2 trackability enhancement.

2. **IPartyService.CreateAsync exact signature**
   - What we know: `IPartyService` exists in `GameKit.Matchmaking/Services/`. `LobbyService` will depend on it.
   - What's unclear: Method name, parameters (ownerId + memberIds?), return type (`Guid` party id?), and whether it validates all player IDs exist in Postgres.
   - Recommendation: Planner reads `GameKit.Matchmaking/Services/IPartyService.cs` before writing Wave 2 (ready-check service tasks).

3. **`MapLobby()` pipeline position relative to `UseGameKitAuth()`**
   - What we know: `UseGameKitAuth()` must run before hub authorization. Existing precedent: `UseGameKitAuth → UseGameKit → MapAuth → MapGameKit → MapMatchmaking`.
   - What's unclear: Whether `MapLobby()` inside `UseEndpoints(e => ...)` or directly on `app` (minimal API style).
   - Recommendation: Follow Matchmaking pattern exactly. Add `MapLobby()` after `MapMatchmaking()` in the same `UseEndpoints` lambda or at minimal API level.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker | Testcontainers (Postgres + Redis) | ✓ | 29.5.3 | — |
| .NET SDK | All compilation | ✓ | 10.0.108 | — |
| Testcontainers.Redis | Advisory lock key tests + backplane tests | ✓ (pinned 4.11.0 in CPM) | 4.11.0 | — |
| Testcontainers.PostgreSql | Migration tests | ✓ (pinned 4.11.0 in CPM) | 4.11.0 | — |
| `Microsoft.AspNetCore.SignalR.Client` (test-only) | Two-TestServer SignalR client in integration tests | Not yet in CPM — needs addition | 10.0.8 | — |

**Missing dependencies with no fallback:**
- `Microsoft.AspNetCore.SignalR.Client` 10.0.8 — required for `HubConnectionBuilder` in `BackplaneTests` and `HubAuthTests`. Must be added to `Directory.Packages.props` and to `GameKit.Lobby.Integration.Tests.csproj`.

```bash
# Verify current stable version
curl -s https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.signalr.client/index.json | python3 -c "import json,sys; d=json.load(sys.stdin); vers=[v for v in d['versions'] if '10.' in v and 'preview' not in v]; print(vers[-1])"
# Returns: 10.0.8
```

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 + Testcontainers 4.11.0 |
| Config file | `tests/GameKit.Lobby.Integration.Tests/` (new project) |
| Quick run command | `dotnet test tests/GameKit.Lobby.Integration.Tests/ --filter "Category!=LoadTest" -x` |
| Full suite command | `dotnet test tests/GameKit.Lobby.Integration.Tests/ -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| LOBBY-01 / OPS-11 | Advisory lock key live-verified pairwise-distinct | Integration (Postgres Testcontainer) | `dotnet test ... --filter "FullyQualifiedName~LobbyAdvisoryLockKeyTests"` | ❌ Wave 0 |
| LOBBY-01 | Lobby migration applies `__ef_migrations_lobby`, creates `lobbies` + `lobby_members` tables | Integration | `dotnet test ... --filter "FullyQualifiedName~LobbySchemaTests"` | ❌ Wave 0 |
| LOBBY-02 | `lobbies` + `lobby_members` CRUD | Integration | `dotnet test ... --filter "FullyQualifiedName~LobbyServiceTests"` | ❌ Wave 2 |
| LOBBY-03 | Ready-check state machine transitions | Integration | `dotnet test ... --filter "FullyQualifiedName~ReadyCheckTests"` | ❌ Wave 2 |
| LOBBY-04 | SC#4: No chat table; no Postgres writes on SendChatMessage | Integration | `dotnet test ... --filter "FullyQualifiedName~ChatEphemeralityTests"` | ❌ Wave 3 |
| LOBBY-05 | SC#3: All-ready → EnqueueAsync → lobby InGame → SignalR broadcast | Integration | `dotnet test ... --filter "FullyQualifiedName~ReadyCheckTests.SC3"` | ❌ Wave 3 |
| LOBBY-06 / SC#2 | Unauthenticated upgrade → 401; authenticated → WebSocket opens | Integration | `dotnet test ... --filter "FullyQualifiedName~HubAuthTests"` | ❌ Wave 1 |
| LOBBY-06 / SC#5 | Cross-instance broadcast via shared Redis backplane | Integration | `dotnet test ... --filter "FullyQualifiedName~BackplaneTests"` | ❌ Wave 3 |

### Sampling Rate

- **Per task commit:** `dotnet test tests/GameKit.Lobby.Integration.Tests/ --filter "Category!=LoadTest" -x`
- **Per wave merge:** `dotnet test tests/GameKit.Lobby.Integration.Tests/ -x`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `tests/GameKit.Lobby.Integration.Tests/GameKit.Lobby.Integration.Tests.csproj` — new project skeleton
- [ ] `tests/GameKit.Lobby.Integration.Tests/LobbyAdvisoryLockKeyTests.cs` — covers OPS-11 / LOBBY-01 Wave 0 RED gate
- [ ] `tests/GameKit.Lobby.Integration.Tests/IntegrationTestHelpers.cs` — CreateFreshDatabase + ApplyLobbyMigrations helpers
- [ ] `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` — TestServer with Lobby pipeline
- [ ] `tests/GameKit.Lobby.Integration.Tests/CollectionDefinitions.cs` — [Collection("Postgres")] + [Collection("Redis")]
- [ ] `Directory.Packages.props` — add `Microsoft.AspNetCore.SignalR.Client` 10.0.8 (test-only)
- [ ] `Directory.Packages.props` — add `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | Yes — hub must gate on valid JWT | `[Authorize]` + `JwtBearerEvents.OnMessageReceived` |
| V3 Session Management | Partial — SignalR connections outlive HTTP requests | Token validated at connection-time; no re-validation mid-connection (standard SignalR behavior per Microsoft docs) |
| V4 Access Control | Yes — player must be member of lobby before joining group or sending | `ILobbyService.IsMemberAsync` check in `JoinLobbyAsync`; throw `HubException` on unauthorized |
| V5 Input Validation | Yes — chat message body length; lobby name | FluentValidation on HTTP endpoints; hub-side max-length check (500 chars mirrors ARCHITECTURE.md schema) |
| V6 Cryptography | No — no new cryptographic operations | JWT crypto owned by GameKit.Auth; no chat encryption |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Unauthenticated WebSocket upgrade | Spoofing | `OnMessageReceived` reads `access_token`; JWT validation returns 401 before handshake; `[Authorize]` on hub |
| Player sending messages to a lobby they are NOT a member of | Spoofing / Elevation of Privilege | `ILobbyService.IsMemberAsync` check in `JoinLobbyAsync`; group membership denied; `HubException` returned |
| Chat message injection (XSS-equivalent for clients rendering the message) | Tampering | Hub does not sanitize text — the CONSUMING GAME CLIENT is responsible for safe rendering. Document this in XML docs. GameKit relays raw string. |
| Chat message flooding / DoS | DoS | Optional `ILobbyMessageHandler` can return `false` to suppress; recommend rate-limit implementation in the handler; per-player rate-limit in hub |
| Cross-lobby message injection (player A sends to lobby B's group) | Spoofing | `JoinLobbyAsync` only adds to the group after `IsMemberAsync` check; SendChatMessage validates lobby membership |
| Replay of valid JWT after connection close | Spoofing | JWT expiry enforced at connection time; SignalR does not re-validate during connection lifetime (documented; mitigated by short JWT TTL from Phase 2 — typically 15-60 minutes) |
| Redis backplane channel eavesdropping | Information Disclosure | Operator responsibility: use Redis TLS (documented in docker-compose.yml guidance); no new risk introduced by Lobby |
| Concurrent double-submission of matchmaking ticket on all-ready | Tampering / DoS | SERIALIZABLE transaction + 3-retry on 40001; lobby state check inside TX prevents second submission |

---

## Project Constraints (from CLAUDE.md)

These directives apply to all code in `GameKit.Lobby`:

1. **GPL license:** SPDX header `// SPDX-License-Identifier: GPL-3.0-or-later` on every `.cs` file; CI license check.
2. **net10.0 TFM:** `<TargetFramework>net10.0</TargetFramework>` in csproj.
3. **XML doc on every public API:** `CS1591` is a warning-as-error; every public type and member needs `<summary>`.
4. **Migration boundaries:** Lobby never modifies Core/Auth/Admin/Rankings/Matchmaking tables. Lobby's migration emits ONLY `lobbies` + `lobby_members`.
5. **No raw refresh tokens stored** (irrelevant for Lobby — no token storage).
6. **Metadata JSONB columns:** If any (e.g., `lobbies.metadata`), must be sparse/infrequently-written/non-relational.
7. **Zero cloud deps:** Redis backplane = StackExchange.Redis only. Azure SignalR Service forbidden.
8. **StackExchange.Redis 2.8.41 already pinned** in CPM; Lobby adds only `Microsoft.AspNetCore.SignalR.StackExchangeRedis`.
9. **`IConnectionMultiplexer` Singleton:** Consumer provides it; `AddLobby()` does NOT register one (mirrors Matchmaking + Presence convention).
10. **MinVer release train:** Package must be train-ready from day one (csproj has `PackageId`, description, tags); actual train inclusion is Phase 12 scope.
11. **InternalsVisibleTo:** `AssemblyInfo.cs` must grant visibility to `GameKit.Lobby.Tests` and `GameKit.Lobby.Integration.Tests`.
12. **FOLLOW-UP-02-03-01 pattern:** Test host needs `LobbyTestModelCustomizer` (`ReplaceService<IModelCustomizer, ...>`) so runtime DbContext sees Lobby entities during integration tests.
13. **Deterministic migration timestamp:** `20260522000000_LobbyInitial` (next slot after Matchmaking's 20260516).
14. **`GameKit.Build` analyzer reference:** `OutputItemType=Analyzer ReferenceOutputAssembly=false` — emits `GameKitMarker.GameKitVersion` and `GameKitMarker.AssemblyName` constants.

---

## Sources

### Primary (HIGH confidence)
- [VERIFIED: nuget.org] `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8 stable — `curl https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.signalr.stackexchangeredis/index.json`
- [CITED: learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0] — Full Redis backplane setup, `AddStackExchangeRedis`, `ChannelPrefix`, `ConnectionFactory` overload
- [CITED: learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0] — `OnMessageReceived` pattern for WebSocket JWT auth (query string `access_token`); `[Authorize]` on Hub; `IPostConfigureOptions<JwtBearerOptions>` chaining pattern
- [VERIFIED: codebase] `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` — exact migration pattern to mirror
- [VERIFIED: codebase] `src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs` — design-time factory + model customizer pattern; exclusion list
- [VERIFIED: codebase] `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs` — hosted service migration pattern
- [VERIFIED: codebase] `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` — `AddMatchmaking()` DI registration pattern
- [VERIFIED: codebase] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs` — advisory lock key test structure to mirror
- [VERIFIED: codebase] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` — TestServer pattern, IConnectionMultiplexer replacement
- [VERIFIED: codebase] `tests/GameKit.Presence.Integration.Tests/PresenceTestApp.cs` — Redis multiplexer reuse in test host
- [VERIFIED: codebase] `src/GameKit.Matchmaking/Services/IMatchmakingService.cs` — exact `EnqueueAsync` signature
- [VERIFIED: codebase] `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` — existing `AddJwtBearer` registration (no `OnMessageReceived` for WebSockets — confirms Lobby must add it via `IPostConfigureOptions`)
- [VERIFIED: codebase] `src/GameKit.Core/Services/HttpContextCurrentPlayer.cs` — ICurrentPlayer reads HttpContext; not usable in SignalR hub methods
- [VERIFIED: codebase] `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` — `TryGetPlayerId` pattern using `Context.User.FindFirst(ClaimTypes.NameIdentifier)` / `"sub"`
- [VERIFIED: codebase] `.planning/REQUIREMENTS.md` — LOBBY-01 through LOBBY-06, OPS-11 authoritative text
- [VERIFIED: codebase] `.planning/phases/11-gamekit-lobby/11-CONTEXT.md` — phase boundary, success criteria
- [VERIFIED: nuget.org] `Microsoft.AspNetCore.SignalR.Client` 10.0.8 — for test project

### Secondary (MEDIUM confidence)
- [CITED: nuget.org/packages/Microsoft.AspNetCore.SignalR.StackExchangeRedis] — confirms 10.0.8 stable, 239.7K downloads, net10.0 TFM supported
- [CITED: learn.microsoft.com/en-us/dotnet/api/.../stackexchangeredisdependencyinjectionextensions.addstackexchangeredis] — four `AddStackExchangeRedis` overloads; `Action<RedisOptions>` overload (no `IConnectionMultiplexer` direct overload; use `ConnectionFactory` in `RedisOptions`)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — `Microsoft.AspNetCore.SignalR.StackExchangeRedis` version verified via NuGet registry + official docs
- Architecture: HIGH — mirrored directly from verified Matchmaking codebase patterns; SignalR patterns from official Microsoft docs
- Migration pattern: HIGH — read actual Matchmaking code, not training data
- Pitfalls: HIGH — grounded in actual codebase patterns (ICurrentPlayer null in Hub, Groups ephemeral, etc.)
- Matchmaking integration: MEDIUM — IPartyService exact signature not read (A1 assumption); rest is grounded in IMatchmakingService.EnqueueAsync verified from source

**Research date:** 2026-06-06
**Valid until:** 2026-07-06 (30 days; stable .NET 10 stack, no fast-moving deps)

---

## RESEARCH COMPLETE

**Phase:** 11 — GameKit.Lobby (New Package)
**Confidence:** HIGH

### Key Findings

1. **Single new NuGet dependency:** `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8 — the only addition to `Directory.Packages.props`. SignalR core (`Hub<T>`, `IHubContext<T>`) is in the `Microsoft.AspNetCore.App` shared framework. Also need `Microsoft.AspNetCore.SignalR.Client` 10.0.8 in the test project.

2. **JWT WebSocket auth gap:** `GameKit.Auth.Builder.AuthBuilderExtensions` does NOT wire `JwtBearerEvents.OnMessageReceived` for query-string token extraction. `AddLobby()` MUST register `IPostConfigureOptions<JwtBearerOptions>` to add this without replacing existing event handlers.

3. **LOBBY-04 conflict resolved:** The STATE.md "Pending Decisions" entry suggesting chat persistence is superseded by REQUIREMENTS.md LOBBY-04 + CONTEXT.md. There is no `lobby_messages` table, no retention job, no persistence of any kind in the `ILobbyMessageHandler` seam.

4. **Migration exclusion list is 20 entities:** Core (4) + Auth (3) + Admin (1) + Rankings (7) + Matchmaking (5). Lobby's `LobbyMigrationModelCustomizer` must enumerate all 20 explicitly.

5. **ICurrentPlayer is unusable in Hub methods:** `HttpContextCurrentPlayer` reads `IHttpContextAccessor.HttpContext` which is null inside SignalR hub method invocations. Player ID must be extracted via `Context.User.FindFirst("sub")` / `ClaimTypes.NameIdentifier` directly.

### File Created

`.planning/phases/11-gamekit-lobby/11-RESEARCH.md`

### Confidence Assessment

| Area | Level | Reason |
|------|-------|--------|
| Standard Stack | HIGH | NuGet registry verified, official docs cited |
| Migration Pattern | HIGH | Read actual Matchmaking source code |
| SignalR Wiring | HIGH | Official Microsoft docs for AddStackExchangeRedis + OnMessageReceived |
| ICurrentPlayer in Hub | HIGH | Read HttpContextCurrentPlayer source; Hub Context != HttpContext |
| IPartyService signature | MEDIUM | Interface not read (A1 assumption) — planner must verify |
| LOBBY-05 FK design | MEDIUM | Requirements wording conflicts with migration boundary; recommended resolution documented |

### Open Questions

- Q1: Does LOBBY-05 require a `lobby_id` column on `matchmaking_tickets` (migration boundary violation), or is the `partyId` link sufficient?
- Q2: Exact `IPartyService.CreateAsync` signature — planner reads `GameKit.Matchmaking/Services/IPartyService.cs` before writing Wave 2 tasks.

### Ready for Planning

Research complete. Planner can now create PLAN.md files.
<!-- REUSE-IgnoreEnd -->
