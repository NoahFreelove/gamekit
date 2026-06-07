---
phase: 11-gamekit-lobby
fixed_at: 2026-06-07T00:45:00Z
review_path: .planning/phases/11-gamekit-lobby/11-REVIEW.md
iteration: 1
findings_in_scope: 9
fixed: 9
skipped: 0
status: all_fixed
---

# Phase 11: Code Review Fix Report

**Fixed at:** 2026-06-07T00:45:00Z
**Source review:** .planning/phases/11-gamekit-lobby/11-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 9
- Fixed: 9
- Skipped: 0

## Fixed Issues

### IN-03: Remove spurious null-forgiving operator on Task<LobbyEntity?>

**Files modified:** `src/GameKit.Lobby/Services/LobbyService.cs`
**Commit:** `8713cb5`
**Applied fix:** Removed the `!` null-forgiving operator from the `FirstOrDefaultAsync` call
in `GetLobbyAsync`. A `Task<T>` reference is never null, so the operator suppressed a
non-existent warning and confused the intent.

---

### IN-02 + CR-03 + WR-01: Hub fixes (null message bypass, IsMemberAsync guard, CancellationToken)

**Files modified:** `src/GameKit.Lobby/Hubs/LobbyHub.cs`
**Commit:** `cc85cd9`
**Applied fix (IN-02):** Coerced `message ??= string.Empty` before the length guard in
`SendChatMessageAsync` so a null message is treated as empty rather than bypassing the
`MaxChatMessageLength` enforcement via `null?.Length > N` evaluating false.

**Applied fix (CR-03):** Added `IsMemberAsync` pre-check to `MarkReadyAsync` consistent
with `JoinLobbyAsync` and `SendChatMessageAsync`. Non-member calls now throw `HubException`
with a descriptive message before triggering the SERIALIZABLE transaction.

**Applied fix (WR-01):** Replaced `Context.GetHttpContext()?.RequestAborted ?? CancellationToken.None`
(always `CancellationToken.None` in hub invocations) with `Context.ConnectionAborted` throughout
`MarkReadyAsync`, matching every other hub method.

---

### WR-03: TryAddEnumerable for LobbyRedisBackplanePostConfigure

**Files modified:** `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs`
**Commit:** `676b279`
**Applied fix:** Changed `AddSingleton<IPostConfigureOptions<RedisOptions>, ...>()` to
`TryAddEnumerable(ServiceDescriptor.Singleton<...>())` to match all other
`IPostConfigureOptions` registrations in the file and be idempotent under double `AddLobby()`.

---

### CR-02: TryStartMatchmakingAsync partial-state strands lobby in InGame

**Files modified:** `src/GameKit.Lobby/Services/LobbyService.cs`
**Commit:** `355d691`
**Applied fix:** Wrapped the party-create + join-loop block in a try/catch. On any exception
(e.g. `PartyConflictException`, transient error), the catch calls `RevertToReadyCheckingAsync`
and returns `LobbyState.ReadyChecking`, ensuring the lobby is never permanently stranded in
`InGame` after the lobby SERIALIZABLE transaction committed. Also added a `MatchmakingParty`
using alias to allow the `party` variable declaration to be hoisted outside the try block.

**Note:** Logic correctness of the revert path — requires human verification that the
`EnqueueAsync` path (which still runs outside the try/catch) and the new exception-catch
path together cover all failure modes.

---

### CR-01 + WR-02 + IN-01: Add join endpoint + domain exception mapping

**Files modified:** `src/GameKit.Lobby/Http/LobbyEndpoints.cs`
**Commit:** `83eb1d3`
**Applied fix (CR-01/IN-01):** Added `POST /api/lobbies/{lobbyId:guid}/join` route with
`RequireAuthorization()` and `ValidationEndpointFilter<JoinLobbyRequest>`. The handler
resolves the caller's player id and calls `ILobbyService.JoinLobbyAsync`. The previously
dead `JoinLobbyRequestValidator` registration is now active.

**Applied fix (WR-02):** Added domain exception mapping to `JoinLobbyAsync` handler
(LobbyNotFoundException → 404, LobbyFullException → 409, AlreadyMemberException → 409)
and `RemoveMemberAsync` handler (LobbyNotFoundException → 404,
LobbyAuthorizationException → 403, NotAMemberException → 404), consistent with
the `PartyEndpoints.cs` convention.

---

### Integration tests for CR-01 and CR-02

**Files modified:**
- `tests/GameKit.Lobby.Integration.Tests/JoinEndpointTests.cs` (new)
- `tests/GameKit.Lobby.Integration.Tests/MatchmakingRevertTests.cs` (new)
- `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` (added `serviceOverrides` parameter)
**Commit:** `3e98a48` (tests) + `bded536` (assertion fix)

**JoinEndpointTests:** Verifies a second player calling `POST /api/lobbies/{id}/join`
results in a `lobby_members` row in Postgres (HTTP 200 + DB check). Also verifies
404 on non-existent lobby and 409 on already-member.

**MatchmakingRevertTests:** Injects `AlwaysThrowingPartyService` stub (via the new
`serviceOverrides` callback on `LobbyTestApp.StartAsync`) to force a party creation failure.
Asserts that after all members mark ready and the failure triggers, the lobby row is
`ReadyChecking` (state 1), NOT `InGame` (state 3).

**LobbyTestApp:** Added `serviceOverrides: Action<IServiceCollection>?` optional parameter to
`StartAsync` (forwarded through new `StartCoreAsync` private method) so failure-path tests can
inject stub services without forking the test app class.

---

_Fixed: 2026-06-07T00:45:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
