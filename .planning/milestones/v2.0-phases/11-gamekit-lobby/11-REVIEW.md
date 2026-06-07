---
phase: 11-gamekit-lobby
reviewed: 2026-06-06T00:00:00Z
depth: standard
files_reviewed: 18
files_reviewed_list:
  - src/GameKit.Lobby/Hubs/LobbyHub.cs
  - src/GameKit.Lobby/Services/LobbyService.cs
  - src/GameKit.Lobby/Services/ILobbyService.cs
  - src/GameKit.Lobby/Services/ILobbyMessageHandler.cs
  - src/GameKit.Lobby/Services/NullLobbyMessageHandler.cs
  - src/GameKit.Lobby/Services/Exceptions/LobbyExceptions.cs
  - src/GameKit.Lobby/LobbyJwtBearerPostConfigure.cs
  - src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs
  - src/GameKit.Lobby/LobbyOptionsValidator.cs
  - src/GameKit.Lobby/GameKitLobbyOptions.cs
  - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
  - src/GameKit.Lobby/Builder/LobbyApplicationBuilderExtensions.cs
  - src/GameKit.Lobby/Http/LobbyEndpoints.cs
  - src/GameKit.Lobby/Http/Contracts/CreateLobbyRequest.cs
  - src/GameKit.Lobby/Http/Contracts/JoinLobbyRequest.cs
  - src/GameKit.Lobby/Entities/Lobby.cs
  - src/GameKit.Lobby/Entities/LobbyMember.cs
  - src/GameKit.Lobby/Data/Configurations/LobbyConfiguration.cs
  - src/GameKit.Lobby/Data/Configurations/LobbyMemberConfiguration.cs
  - src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.cs
findings:
  critical: 3
  warning: 3
  info: 3
  total: 9
status: issues_found
---

# Phase 11: Code Review Report

**Reviewed:** 2026-06-06
**Depth:** standard
**Files Reviewed:** 18
**Status:** issues_found

## Summary

Reviewed the GameKit.Lobby package: SignalR hub, LobbyService, HTTP endpoints, EF entities and
configurations, the Redis backplane wiring, and the JWT Bearer post-configurator. The security
plumbing (WebSocket token extraction scoped to `/hubs/lobby`, chain-not-replace of the existing
`OnMessageReceived` handler, `[Authorize]` gate on the hub, membership verification before
`AddToGroupAsync` and chat relay) is well-constructed. The migration boundary, advisory-lock key,
integer enum storage, and absence of a chat persistence path are all correct.

Three blockers were found: (1) a missing REST endpoint for the fully-implemented
`JoinLobbyAsync` service method that leaves the join path completely inaccessible via HTTP,
(2) a partial-state correctness bug in `TryStartMatchmakingAsync` that permanently strands the
lobby in `InGame` when any `IPartyService.JoinAsync` call throws, and (3) the hub's
`MarkReadyAsync` method lacks the `IsMemberAsync` pre-check present on every other hub method,
causing non-`HubException` errors to surface as opaque connection terminations rather than
clean `HubException` messages.

---

## Critical Issues

### CR-01: Missing REST endpoint for JoinLobbyAsync — join flow is unreachable via HTTP

**File:** `src/GameKit.Lobby/Http/LobbyEndpoints.cs:31-46`

**Issue:** `ILobbyService.JoinLobbyAsync` is fully implemented, `JoinLobbyRequest` and
`JoinLobbyRequestValidator` exist, and the validator is registered in DI
(`LobbyBuilderExtensions.cs:96`). However, `MapLobbyEndpoints` maps only three routes:
`POST /api/lobbies` (create), `GET /api/lobbies/{id}` (get), and
`DELETE /api/lobbies/{id}/members/{pid}` (remove member). There is no `POST /api/lobbies/{id}/join`
or equivalent. Players have no HTTP path to join a lobby; the only remaining path would be a
future direct service call that bypasses the authorization layer entirely. The validator
registration in `LobbyBuilderExtensions.cs:96` is dead code as a result.

**Fix:**
```csharp
// In LobbyEndpoints.MapLobbyEndpoints, after the CreateLobby route:
routes.MapPost("/api/lobbies/{lobbyId:guid}/join", JoinLobbyAsync)
    .RequireAuthorization();

// Add handler:
private static async Task<IResult> JoinLobbyAsync(
    Guid lobbyId,
    HttpContext http,
    ILobbyService svc,
    CancellationToken ct)
{
    if (!TryGetPlayerId(http, out var playerId))
        return Results.Unauthorized();

    try
    {
        var lobby = await svc.JoinLobbyAsync(lobbyId, playerId, ct).ConfigureAwait(false);
        return Results.Ok(new { lobbyId = lobby.Id, state = lobby.State.ToString() });
    }
    catch (LobbyNotFoundException)      { return Results.NotFound(new { error = "lobby_not_found" }); }
    catch (LobbyFullException ex)       { return Results.Conflict(new { error = "lobby_full", maxMembers = ex.MaxMembers }); }
    catch (AlreadyMemberException)      { return Results.Conflict(new { error = "already_member" }); }
}
```

---

### CR-02: `TryStartMatchmakingAsync` — partial party state leaves lobby permanently stranded in `InGame`

**File:** `src/GameKit.Lobby/Services/LobbyService.cs:330-365`

**Issue:** `TryStartMatchmakingAsync` creates a party (`_partyService.CreateAsync`) and then
calls `_partyService.JoinAsync` for each non-owner member in a bare `foreach` loop with no
try/catch. If any `JoinAsync` call throws (e.g., the member is already in another party,
`PartyConflictException`, or any transient error), execution aborts:

1. The party row already exists in Postgres with partial membership.
2. `_matchmakingService.EnqueueAsync` is never reached.
3. The exception propagates through `MarkReadyAsync` (line 239-242) where there is also no
   catch, then through the hub's `MarkReadyAsync` (hub line 168), which has no catch.
4. SignalR converts the non-`HubException` to an opaque `"An unexpected error occurred"` client
   message and may terminate the connection.
5. Critically, `RevertToReadyCheckingAsync` is **never called** — the lobby state remains
   `InGame` (set optimistically inside the committed SERIALIZABLE transaction at line 223), and
   the ready-check cycle is permanently broken for that lobby.

**Fix:** Wrap the full party-create-and-join block in a try/catch that reverts the lobby state
and re-throws (or returns `ReadyChecking`) on failure:

```csharp
Party party;
try
{
    party = await _partyService.CreateAsync(ownerId, ct).ConfigureAwait(false);
    foreach (var member in nonOwnerMembers)
        await _partyService.JoinAsync(party.PartyCode, member.PlayerId, ct).ConfigureAwait(false);
}
catch (Exception ex)
{
    _logger.LogError(ex,
        "Lobby {LobbyId} party creation/join failed — reverting InGame to ReadyChecking.", lobbyId);
    await RevertToReadyCheckingAsync(lobbyId, ct).ConfigureAwait(false);
    return LobbyState.ReadyChecking;
}
```

---

### CR-03: `LobbyHub.MarkReadyAsync` — no `IsMemberAsync` guard; membership errors surface as opaque connection failures

**File:** `src/GameKit.Lobby/Hubs/LobbyHub.cs:164-169`

**Issue:** Every other hub method that operates on a lobby (`JoinLobbyAsync` at line 101,
`SendChatMessageAsync` at line 137) calls `IsMemberAsync` first and throws `HubException` with
a clear message when membership fails. `MarkReadyAsync` skips this check entirely and delegates
directly to `_lobby.MarkReadyAsync`. Two problems result:

1. **Error surface:** When the caller is not a member, `LobbyService.MarkReadyAsync` throws
   `NotAMemberException` (a non-`HubException`). SignalR's hub dispatcher converts
   non-`HubException` throws into the generic `"An unexpected error occurred"` message, hiding
   the actual error from the client and potentially terminating the connection depending on
   SignalR server configuration.

2. **Pattern inconsistency:** Any authenticated player can invoke `MarkReadyAsync(anyLobbyId)`,
   triggering a full SERIALIZABLE Postgres transaction before the membership rejection is
   discovered inside the transaction. The `IsMemberAsync` check on the other two methods is
   a fast DB read that short-circuits before the expensive path.

**Fix:**
```csharp
public async Task MarkReadyAsync(Guid lobbyId)
{
    var playerId = GetPlayerId();

    // Consistent with JoinLobbyAsync and SendChatMessageAsync — verify membership
    // server-side before the SERIALIZABLE transaction (T-11-03-02).
    if (!await _lobby.IsMemberAsync(lobbyId, playerId, Context.ConnectionAborted)
            .ConfigureAwait(false))
    {
        throw new HubException("Player is not a member of this lobby.");
    }

    await _lobby.MarkReadyAsync(lobbyId, playerId, Context.ConnectionAborted)
        .ConfigureAwait(false);
}
```

---

## Warnings

### WR-01: `LobbyHub.MarkReadyAsync` passes `CancellationToken.None` to the service instead of `Context.ConnectionAborted`

**File:** `src/GameKit.Lobby/Hubs/LobbyHub.cs:167`

**Issue:**
```csharp
var ct = Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;
```
During a WebSocket hub invocation `Context.GetHttpContext()` is `null` — this is explicitly
documented in the hub class remarks (line 31, RESEARCH Pitfall 1) and is the reason the class
uses `Context.User` rather than `IHttpContextAccessor` for identity. The null-coalesce therefore
always resolves to `CancellationToken.None`, which means the SERIALIZABLE transaction inside
`LobbyService.MarkReadyAsync` (and the subsequent `IPartyService` and `IMatchmakingService`
calls) will not be cancelled if the client disconnects mid-flight. Every other hub method
correctly uses `Context.ConnectionAborted`.

**Fix:**
```csharp
public async Task MarkReadyAsync(Guid lobbyId)
{
    var playerId = GetPlayerId();
    await _lobby.MarkReadyAsync(lobbyId, playerId, Context.ConnectionAborted)
        .ConfigureAwait(false);
}
```

---

### WR-02: REST endpoint handlers swallow domain exceptions as HTTP 500 — violates project convention

**File:** `src/GameKit.Lobby/Http/LobbyEndpoints.cs:104-116`

**Issue:** The established pattern in this codebase (see `GameKit.Matchmaking/Http/PartyEndpoints.cs`
and `MatchmakingEndpoints.cs`) is to `catch` domain exceptions at the endpoint handler level
and map them to appropriate HTTP status codes. `LobbyEndpoints` catches nothing. Concrete gaps:

- `RemoveMemberAsync` — `LobbyNotFoundException` → 500 (should be 404), `LobbyAuthorizationException`
  → 500 (should be 403), `NotAMemberException` → 500 (should be 404).
- `GetLobbyAsync` correctly handles `null` return (explicit `NotFound` at line 88), but if
  `GetLobbyAsync` itself threw (unlikely here), it would still be a 500.
- `CreateLobbyAsync` — no current domain exceptions, but the pattern inconsistency creates a
  maintenance hazard as `CreateLobbyAsync` grows.

**Fix:** Add catch blocks matching the Matchmaking convention:
```csharp
private static async Task<IResult> RemoveMemberAsync(
    Guid lobbyId, Guid targetPlayerId,
    HttpContext http, ILobbyService svc, CancellationToken ct)
{
    if (!TryGetPlayerId(http, out var actorId))
        return Results.Unauthorized();
    try
    {
        await svc.RemoveMemberAsync(lobbyId, actorId, targetPlayerId, ct).ConfigureAwait(false);
        return Results.NoContent();
    }
    catch (LobbyNotFoundException)         { return Results.NotFound(new { error = "lobby_not_found" }); }
    catch (LobbyAuthorizationException)    { return Results.Forbid(); }
    catch (NotAMemberException)            { return Results.NotFound(new { error = "member_not_found" }); }
}
```

---

### WR-03: `LobbyRedisBackplanePostConfigure` registered with `AddSingleton` instead of `TryAddEnumerable` — inconsistent and double-registers if `AddLobby()` is called twice

**File:** `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs:81`

**Issue:**
```csharp
builder.Services.AddSingleton<IPostConfigureOptions<RedisOptions>, LobbyRedisBackplanePostConfigure>();
```
All other `IPostConfigureOptions` registrations in this file use `TryAddEnumerable` (lines 60,
64, 85-86), which is idempotent on repeated calls. `AddSingleton` is not idempotent: if
`AddLobby()` is called twice (e.g., in a test fixture that re-registers services),
`LobbyRedisBackplanePostConfigure` is registered twice. Both instances run during options
post-configuration, setting `ConnectionFactory` twice. The second write is harmless (same
multiplexer) but the double-execution is wasteful and inconsistent with the established pattern.

**Fix:**
```csharp
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IPostConfigureOptions<RedisOptions>, LobbyRedisBackplanePostConfigure>());
```

---

## Info

### IN-01: `JoinLobbyRequest` and its validator are registered in DI but are dead code until CR-01 is fixed

**File:** `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs:95-96` and `src/GameKit.Lobby/Http/Contracts/JoinLobbyRequest.cs`

**Issue:** `JoinLobbyRequestValidator` is registered as a scoped service but is never resolved
— there is no REST endpoint that attaches `ValidationEndpointFilter<JoinLobbyRequest>`. Once
CR-01 (missing join endpoint) is fixed, this validator should be attached to the new route.
Until then it is a dead registration.

**Fix:** Wire the validator to the new join endpoint (see CR-01 fix), or document that
`JoinLobbyRequest` is intentionally retained as a placeholder until the endpoint is added.

---

### IN-02: `null` message bypasses `MaxChatMessageLength` enforcement in `SendChatMessageAsync`

**File:** `src/GameKit.Lobby/Hubs/LobbyHub.cs:130-132`

**Issue:**
```csharp
if (message?.Length > _options.MaxChatMessageLength)
```
When `message` is `null`, `message?.Length` evaluates to `null` (lifted nullable int), and
`null > 500` is `false`. A null message therefore passes the length guard, proceeds through
the membership check, and is relayed as `string.Empty` via `message ?? string.Empty`. This is
not a security issue (empty relay is harmless) but the guard is inconsistent with its intent.

**Fix:**
```csharp
// Treat null as empty — consistent with relay behaviour and intent of the guard.
message ??= string.Empty;
if (message.Length > _options.MaxChatMessageLength)
    throw new HubException(
        $"Message exceeds maximum length of {_options.MaxChatMessageLength} characters.");
```

---

### IN-03: `GetLobbyAsync` uses a spurious null-forgiving operator on a `Task<T?>`

**File:** `src/GameKit.Lobby/Services/LobbyService.cs:253-255`

**Issue:**
```csharp
public Task<LobbyEntity?> GetLobbyAsync(Guid lobbyId, CancellationToken ct = default)
    => _ctx.Set<LobbyEntity>()
        .Include(l => l.Members)
        .FirstOrDefaultAsync(l => l.Id == lobbyId, ct)!;
```
The `!` null-forgiving operator is applied to `Task<LobbyEntity?>`. A `Task<T>` reference is
never null, so the operator suppresses a non-existent nullability warning and confuses the
intent. The return type is correctly `Task<LobbyEntity?>` (the inner `LobbyEntity?` can still
be null when the row is absent).

**Fix:** Remove the `!`:
```csharp
public Task<LobbyEntity?> GetLobbyAsync(Guid lobbyId, CancellationToken ct = default)
    => _ctx.Set<LobbyEntity>()
        .Include(l => l.Members)
        .FirstOrDefaultAsync(l => l.Id == lobbyId, ct);
```

---

_Reviewed: 2026-06-06_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
