---
phase: 11-gamekit-lobby
plan: "03"
subsystem: lobby
tags: [lobby, signalr, redis-backplane, jwt, websocket, hub, ready-check, chat, serializable]

requires:
  - phase: 11-02
    provides: Lobby/LobbyMember entities, LobbyState enum (Open=0/ReadyChecking=1/Closed=2/InGame=3), lobbies + lobby_members schema

provides:
  - GameKitLobbyOptions (DefaultMaxMembers=8, MaxChatMessageLength=500, DefaultPoolName="default")
  - LobbyOptionsValidator (fail-fast at startup)
  - ILobbyClient (typed hub interface: ReceiveChatMessageAsync, ReceiveStateUpdateAsync)
  - LobbyStateUpdate record (LobbyId, State, Detail?)
  - LobbyJwtBearerPostConfigure (IPostConfigureOptions<JwtBearerOptions> chaining OnMessageReceived for /hubs/lobby access_token — SC#2)
  - LobbyRedisBackplanePostConfigure (IPostConfigureOptions<RedisOptions> deferring IConnectionMultiplexer into ConnectionFactory)
  - AddLobby() extension (SignalR+Redis backplane ChannelPrefix="GameKit", JWT WS auth, services)
  - MapLobby() extension (MapHub<LobbyHub>("/hubs/lobby") + REST endpoints)
  - ILobbyService (CreateLobbyAsync, JoinLobbyAsync, RemoveMemberAsync, IsMemberAsync, MarkReadyAsync, GetLobbyAsync, GetPlayerLobbyIdsAsync)
  - LobbyService (CRUD + SERIALIZABLE MarkReadyAsync + IHubContext broadcast + TryStartMatchmakingAsync stub for Plan 04)
  - LobbyHub ([Authorize] Hub<ILobbyClient>; JoinLobbyAsync + SendChatMessageAsync gated on IsMemberAsync; OnConnectedAsync group re-add)
  - ILobbyMessageHandler relay/gate-only seam (OnMessageAsync returns bool; NO persistence method)
  - NullLobbyMessageHandler (default no-op returning true)
  - REST endpoints (POST /api/lobbies, GET /api/lobbies/{id}, DELETE /api/lobbies/{id}/members/{pid})
  - LobbyExceptions (LobbyNotFoundException, LobbyFullException, AlreadyMemberException, NotAMemberException, LobbyAuthorizationException)
  - ValidationEndpointFilter<T> (DRY clone for Lobby namespace)

affects:
  - 11-04 (Plan 04: two-TestServer integration tests + TryStartMatchmakingAsync real wiring via IPartyService + IMatchmakingService)

tech-stack:
  added: []
  patterns:
    - "LobbyJwtBearerPostConfigure: IPostConfigureOptions<JwtBearerOptions> chaining (NOT replacing) existing OnMessageReceived — mirrors DiscordBackchannelPostConfigure shell; registered via TryAddEnumerable"
    - "LobbyRedisBackplanePostConfigure: IPostConfigureOptions<RedisOptions> deferring IConnectionMultiplexer resolution to startup — avoids BuildServiceProvider() at registration time"
    - "LobbyHub.GetPlayerId(): reads Context.User.FindFirst(ClaimTypes.NameIdentifier) ?? Context.User.FindFirst('sub') — NOT ICurrentPlayer (HttpContext is null in hub invocations)"
    - "LobbyHub.OnConnectedAsync: queries lobby_members from Postgres to re-add new ConnectionId to groups (SignalR group membership is per-connection, lost on reconnect)"
    - "LobbyService.MarkReadyAsync: SerializationFailureRetry.Build (reused from Matchmaking via InternalsVisibleTo grant) + SERIALIZABLE tx + broadcast AFTER commit via IHubContext"
    - "TryStartMatchmakingAsync: documented stub annotated TODO(11-04) — transitions state to InGame only; no party/matchmaking calls until Plan 04"
    - "ILobbyMessageHandler: relay/gate-only seam — XML doc enforces MUST NOT persist (LOBBY-04 anti-feature)"
    - "using-alias pattern (LobbyEntity, LobbyMemberEntity) for GameKit.Lobby namespace/class ambiguity — mirrors Plan 11-02 deviation"

key-files:
  created:
    - src/GameKit.Lobby/GameKitLobbyOptions.cs
    - src/GameKit.Lobby/LobbyOptionsValidator.cs
    - src/GameKit.Lobby/Hubs/ILobbyClient.cs
    - src/GameKit.Lobby/Hubs/LobbyStateUpdate.cs
    - src/GameKit.Lobby/Hubs/LobbyHub.cs
    - src/GameKit.Lobby/LobbyJwtBearerPostConfigure.cs
    - src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs
    - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
    - src/GameKit.Lobby/Builder/LobbyApplicationBuilderExtensions.cs
    - src/GameKit.Lobby/Services/ILobbyService.cs
    - src/GameKit.Lobby/Services/LobbyService.cs
    - src/GameKit.Lobby/Services/ILobbyMessageHandler.cs
    - src/GameKit.Lobby/Services/NullLobbyMessageHandler.cs
    - src/GameKit.Lobby/Services/Exceptions/LobbyExceptions.cs
    - src/GameKit.Lobby/Http/LobbyEndpoints.cs
    - src/GameKit.Lobby/Http/Contracts/CreateLobbyRequest.cs
    - src/GameKit.Lobby/Http/Contracts/JoinLobbyRequest.cs
    - src/GameKit.Lobby/Http/EndpointFilters/ValidationEndpointFilter.cs
  modified:
    - src/GameKit.Matchmaking/AssemblyInfo.cs (added InternalsVisibleTo("GameKit.Lobby") for SerializationFailureRetry reuse)

key-decisions:
  - "LobbyHub does NOT reference ICurrentPlayer — Context.User.FindFirst is used directly; HttpContext is null in hub invocations (T-11-03-05)"
  - "SerializationFailureRetry reused from Matchmaking via InternalsVisibleTo grant — no duplication of the Polly pipeline"
  - "TryStartMatchmakingAsync is a documented stub (TODO(11-04)) — only sets State=InGame; real IPartyService + IMatchmakingService calls land in Plan 04"
  - "ILobbyMessageHandler has no Save/Persist method — relay/gate-only (LOBBY-04 anti-feature enforcement at interface level)"
  - "ValidationEndpointFilter DRY-cloned into GameKit.Lobby namespace (not shared) — consistent with Matchmaking pattern"
  - "GetPlayerLobbyIdsAsync added to ILobbyService for OnConnectedAsync group re-add (not in plan spec but required for RESEARCH Pitfall 2 compliance)"
  - "FluentValidation validators registered in AddLobby() (not MapLobby()) — consistent with Auth/Admin/Matchmaking DI registration pattern"

requirements-completed: [LOBBY-02, LOBBY-03, LOBBY-04, LOBBY-06]

duration: 9min
completed: 2026-06-06
---

# Phase 11 Plan 03: LobbyHub (SignalR) + JWT-WS + Redis Backplane + LobbyService Summary

**[Authorize] LobbyHub on Redis backplane (ChannelPrefix "GameKit") with chained JWT WebSocket query-string token extraction (SC#2), SERIALIZABLE all-ready MarkReadyAsync with Polly retry + post-commit IHubContext broadcast (LOBBY-03), relay-only chat seam enforcing LOBBY-04 at both interface and runtime level, and full REST endpoint surface.**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-06-06T23:59:43Z
- **Completed:** 2026-06-06T00:09:00Z
- **Tasks:** 3
- **Files modified:** 19

## Accomplishments

- LobbyHub: [Authorize]-gated Hub<ILobbyClient> with JoinLobbyAsync (IsMemberAsync gate), SendChatMessageAsync (relay-only, zero Postgres writes, MaxChatMessageLength check), MarkReadyAsync (delegates to LobbyService for SERIALIZABLE state machine), OnConnectedAsync (group re-add from Postgres)
- LobbyService: full CRUD + SERIALIZABLE MarkReadyAsync using SerializationFailureRetry.Build (reused from Matchmaking via InternalsVisibleTo) + post-commit IHubContext broadcast; TryStartMatchmakingAsync clearly stubbed for Plan 04
- AddLobby/MapLobby: SignalR+Redis backplane wired correctly (AddStackExchangeRedis chained on ISignalRServerBuilder, ChannelPrefix="GameKit"); LobbyRedisBackplanePostConfigure defers IConnectionMultiplexer resolution; LobbyJwtBearerPostConfigure chains OnMessageReceived without replacing existing Auth handler

## Task Commits

1. **Task 1: Options, post-configures, ILobbyClient, AddLobby/MapLobby** — `3b3be20` (feat)
2. **Task 2: ILobbyService + LobbyService (SERIALIZABLE + broadcast)** — `4e018b7` (feat)
3. **Task 3: LobbyHub + relay-only ILobbyMessageHandler + REST endpoints** — `68f3e13` (feat)

## Files Created/Modified

- `src/GameKit.Lobby/GameKitLobbyOptions.cs` — Options with DefaultMaxMembers=8, MaxChatMessageLength=500, DefaultPoolName="default"
- `src/GameKit.Lobby/LobbyOptionsValidator.cs` — IValidateOptions<GameKitLobbyOptions> fail-fast
- `src/GameKit.Lobby/Hubs/ILobbyClient.cs` — Typed hub interface: ReceiveChatMessageAsync + ReceiveStateUpdateAsync
- `src/GameKit.Lobby/Hubs/LobbyStateUpdate.cs` — Record carrying LobbyId + LobbyState + Detail?
- `src/GameKit.Lobby/Hubs/LobbyHub.cs` — [Authorize] Hub<ILobbyClient>; Context.User player id; IsMemberAsync gates; SendChatMessageAsync zero-Postgres; OnConnectedAsync group re-add
- `src/GameKit.Lobby/LobbyJwtBearerPostConfigure.cs` — Chains OnMessageReceived for /hubs/lobby access_token extraction (SC#2)
- `src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs` — Defers IConnectionMultiplexer into ConnectionFactory at startup
- `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` — AddLobby(): SignalR+Redis backplane + JWT WS + services + NullLobbyMessageHandler TryAdd
- `src/GameKit.Lobby/Builder/LobbyApplicationBuilderExtensions.cs` — UseGameKitLobby (no-op) + MapLobby (hub + REST)
- `src/GameKit.Lobby/Services/ILobbyService.cs` — Full service interface including GetPlayerLobbyIdsAsync for reconnect
- `src/GameKit.Lobby/Services/LobbyService.cs` — SERIALIZABLE MarkReadyAsync + broadcast + TryStartMatchmakingAsync stub
- `src/GameKit.Lobby/Services/ILobbyMessageHandler.cs` — Relay/gate-only seam; XML doc prohibits persistence (LOBBY-04)
- `src/GameKit.Lobby/Services/NullLobbyMessageHandler.cs` — Returns Task.FromResult(true)
- `src/GameKit.Lobby/Services/Exceptions/LobbyExceptions.cs` — 5 exception types
- `src/GameKit.Lobby/Http/LobbyEndpoints.cs` — POST /api/lobbies, GET /api/lobbies/{id}, DELETE members
- `src/GameKit.Lobby/Http/Contracts/CreateLobbyRequest.cs` — With FluentValidation validator
- `src/GameKit.Lobby/Http/Contracts/JoinLobbyRequest.cs` — With FluentValidation validator
- `src/GameKit.Lobby/Http/EndpointFilters/ValidationEndpointFilter.cs` — DRY clone (Lobby namespace)
- `src/GameKit.Matchmaking/AssemblyInfo.cs` — Added InternalsVisibleTo("GameKit.Lobby") for SerializationFailureRetry

## Decisions Made

- LobbyHub does NOT reference ICurrentPlayer — all player identity comes from Context.User.FindFirst directly; HttpContext is null inside SignalR hub invocations.
- SerializationFailureRetry reused from Matchmaking via a new InternalsVisibleTo("GameKit.Lobby") grant rather than duplicating the Polly pipeline. This is the correct pattern per PATTERNS.md.
- TryStartMatchmakingAsync is a documented stub annotated `// TODO(11-04)` — sets State=InGame only. The real IPartyService.CreateAsync + IPartyService.JoinAsync + IMatchmakingService.EnqueueAsync wiring lands in Plan 04 (IPartyService.CreateAsync takes only ownerPlayerId per PATTERNS.md A1 resolution).
- GetPlayerLobbyIdsAsync added to ILobbyService (not in plan spec) to support OnConnectedAsync group re-add per RESEARCH Pitfall 2. This is a correctness requirement, not scope creep.
- ValidationEndpointFilter DRY-cloned into GameKit.Lobby.Http.EndpointFilters — consistent with Matchmaking pattern (open Q4 decision from Plan 04-07).
- FluentValidation validators (CreateLobbyRequestValidator, JoinLobbyRequestValidator) registered in AddLobby() so consumers only call AddLobby() without needing to wire validators separately.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] using-alias workaround for namespace/class ambiguity in Services**
- **Found during:** Task 2 (build)
- **Issue:** `ILobbyService.cs` and `LobbyService.cs` are in namespace `GameKit.Lobby.Services`. The identifier `Lobby` is ambiguous — resolves to both the namespace `GameKit.Lobby` and the entity class `GameKit.Lobby.Entities.Lobby`. CS0118 compilation error.
- **Fix:** Added `using LobbyEntity = GameKit.Lobby.Entities.Lobby;` and `using LobbyMemberEntity = GameKit.Lobby.Entities.LobbyMember;` and `using LobbyState = GameKit.Lobby.Entities.LobbyState;` aliases. Mirrors the Plan 11-02 deviation fix in the configuration files.
- **Files modified:** `ILobbyService.cs`, `LobbyService.cs`
- **Committed in:** 4e018b7 (Task 2 commit)

**2. [Rule 1 - Bug] CS1587 XML comments on record parameters**
- **Found during:** Task 3 (build)
- **Issue:** XML `<summary>` doc comments inside positional record parameter declarations (e.g., `/// <summary>...description...</summary>\n int? MaxMembers = null`) are not valid C# syntax — CS1587.
- **Fix:** Moved parameter descriptions to `<param name="...">` on the record's type-level `<summary>` doc comment.
- **Files modified:** `CreateLobbyRequest.cs`, `JoinLobbyRequest.cs`
- **Committed in:** 68f3e13 (Task 3 commit)

**3. [Rule 2 - Missing Critical] Added GetPlayerLobbyIdsAsync to ILobbyService**
- **Found during:** Task 3 (LobbyHub.OnConnectedAsync implementation)
- **Issue:** The plan specifies OnConnectedAsync must "query the player's lobby memberships and re-add the new ConnectionId to each lobby:{id} group" (RESEARCH Pitfall 2), but ILobbyService had no method to retrieve player lobby memberships.
- **Fix:** Added `GetPlayerLobbyIdsAsync(Guid playerId, CancellationToken ct)` to both ILobbyService and LobbyService. This is a correctness requirement for the RESEARCH Pitfall 2 mitigation — without it, reconnected players would not receive broadcasts.
- **Files modified:** `ILobbyService.cs`, `LobbyService.cs`, `LobbyHub.cs`
- **Committed in:** 68f3e13 (Task 3 commit)

**4. [Rule 3 - Blocking] Added InternalsVisibleTo("GameKit.Lobby") to Matchmaking**
- **Found during:** Task 2 (build)
- **Issue:** `SerializationFailureRetry` is `internal static` in `GameKit.Matchmaking.Services` — the CS0122 "inaccessible due to protection level" error blocked compilation.
- **Fix:** Added `[assembly: InternalsVisibleTo("GameKit.Lobby")]` to `src/GameKit.Matchmaking/AssemblyInfo.cs`. PATTERNS.md specifies "Reuse Matchmaking's SerializationFailureRetry.Build() directly (Lobby has a ProjectReference to Matchmaking). Do NOT duplicate the Polly pipeline."
- **Files modified:** `src/GameKit.Matchmaking/AssemblyInfo.cs`
- **Committed in:** 4e018b7 (Task 2 commit)

---

**Total deviations:** 4 auto-fixed (2 bug, 1 missing critical, 1 blocking)
**Impact on plan:** All fixes required for correctness. No scope creep.

## TryStartMatchmakingAsync Stub Location

The stub for Plan 04 replacement is at:
`src/GameKit.Lobby/Services/LobbyService.cs` — method `TryStartMatchmakingAsync`

Annotated with `// TODO(11-04): real party submission via IPartyService + IMatchmakingService.`

Plan 04 must call `IPartyService.CreateAsync(lobby.OwnerId)` (single owner — verified from actual interface), then `IPartyService.JoinAsync(code, memberId)` for each non-owner member, then `IMatchmakingService.EnqueueAsync(ownerId, ladderId, poolName, partyId, ct)`.

## Chat Persistence Verification

No `lobby_messages` entity exists. `SendChatMessageAsync` in LobbyHub contains ZERO calls to `_ctx`, `SaveChangesAsync`, or any DbSet. `ILobbyMessageHandler` has no Save/Persist method. The Plan 11-02 `No_Chat_Message_Table_Exists` schema test (LOBBY-04 anti-feature at DB level) remains green.

## Known Stubs

| File | Location | Purpose | Plan that wires it |
|------|----------|---------|-------------------|
| `LobbyService.cs` | `TryStartMatchmakingAsync` | Transitions State=InGame only; no actual matchmaking submission | 11-04 |

## Threat Surface Scan

New network surfaces introduced in this plan:

| Flag | File | Description |
|------|------|-------------|
| threat_flag: websocket_endpoint | `Builder/LobbyApplicationBuilderExtensions.cs` | New WebSocket endpoint `/hubs/lobby` — gated by [Authorize] + LobbyJwtBearerPostConfigure 401-before-handshake (T-11-03-01 mitigated) |
| threat_flag: rest_endpoints | `Http/LobbyEndpoints.cs` | POST /api/lobbies, GET /api/lobbies/{id}, DELETE members — all RequireAuthorization() |

All threat register entries (T-11-03-01 through T-11-03-06) mitigated as specified in the plan's `<threat_model>`.

## Self-Check: PASSED

- [x] `src/GameKit.Lobby/GameKitLobbyOptions.cs` — FOUND
- [x] `src/GameKit.Lobby/Hubs/LobbyHub.cs` — FOUND
- [x] `src/GameKit.Lobby/LobbyJwtBearerPostConfigure.cs` — FOUND
- [x] `src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs` — FOUND
- [x] `src/GameKit.Lobby/Services/ILobbyService.cs` — FOUND
- [x] `src/GameKit.Lobby/Services/LobbyService.cs` — FOUND
- [x] `src/GameKit.Lobby/Services/ILobbyMessageHandler.cs` — FOUND
- [x] `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` — FOUND
- [x] Commits 3b3be20, 4e018b7, 68f3e13 — all present
- [x] `dotnet build GameKit.sln -warnaserror` — 0 errors, 0 warnings
- [x] ICurrentPlayer absent from LobbyHub.cs — CONFIRMED
- [x] IsMemberAsync called in LobbyHub (4 times: JoinLobbyAsync x2 + SendChatMessageAsync x2) — CONFIRMED
- [x] IsolationLevel.Serializable in LobbyService.cs — CONFIRMED
- [x] AddStackExchangeRedis in LobbyBuilderExtensions.cs — CONFIRMED
- [x] ConnectionFactory in LobbyRedisBackplanePostConfigure.cs — CONFIRMED
- [x] OnMessageAsync in ILobbyMessageHandler.cs — CONFIRMED

---
*Phase: 11-gamekit-lobby*
*Completed: 2026-06-06*
