---
phase: 11-gamekit-lobby
verified: 2026-06-07T01:30:00Z
status: passed
score: 5/5 must-haves verified
overrides_applied: 0
---

# Phase 11: GameKit.Lobby Verification Report

**Phase Goal:** A new `GameKit.Lobby` NuGet package delivers ready-checks, ephemeral in-lobby chat (SignalR on a Redis backplane, NEVER persisted), and persistent groups (Postgres lobbies + lobby_members).
**Verified:** 2026-06-07T01:30:00Z
**Status:** passed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| #  | Truth                                                                                                              | Status     | Evidence                                                                                                                                                     |
|----|--------------------------------------------------------------------------------------------------------------------|------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1  | Lobby advisory-lock key live-verified pairwise-distinct from all 5 existing keys in a Testcontainers test (SC#1)  | VERIFIED | `LobbyMigrationConstants.AdvisoryLockKey = 12178347L` (not 0L placeholder); `LobbyAdvisoryLockKeyTests`: two tests assert hash match + 5-way non-equality against all known keys by symbolic constant AND integer literal; `[Collection("Postgres")]` runs before all hub tests |
| 2  | Player JWT authenticates WS upgrade to /hubs/lobby; unauthenticated → 401 before handshake (SC#2)                 | VERIFIED | `LobbyJwtBearerPostConfigure` reads `access_token` from query string scoped to `/hubs/lobby`, chains (not replaces) existing handler; `[Authorize]` on `LobbyHub`; `HubAuthTests`: two-TestServer tests assert `HttpRequestException` with 401 on no-token, and `HubConnectionState.Connected` on valid JWT |
| 3  | All-ready → `TryStartMatchmakingAsync` via `IPartyService.CreateAsync` + `JoinAsync` + `IMatchmakingService.EnqueueAsync(partyId)`; `ReadyChecking→InGame`; broadcast (SC#3) | VERIFIED | `LobbyService.MarkReadyAsync`: SERIALIZABLE tx sets `InGame`, then `TryStartMatchmakingAsync` calls `_partyService.CreateAsync`, per-member `_partyService.JoinAsync`, `_matchmakingService.EnqueueAsync(ownerId, ladderId, poolName, party.Id, ct)`; `ReadyCheckTests`: asserts `InGame` broadcast, DB state=3, and party row in `gamekit.parties` |
| 4  | Chat reaches group in real time; NO chat table; nothing written to Postgres on send (SC#4 / LOBBY-04 anti-feature) | VERIFIED | `LobbyHub.SendChatMessageAsync`: calls `_messageHandler.OnMessageAsync` (which is `NullLobbyMessageHandler` — no Save call, returns `true`), then `Clients.Group.ReceiveChatMessageAsync`; `LobbySchemaTests.No_Chat_Message_Table_Exists`: asserts 0 tables matching `lobby_message%` after migration; `ChatEphemeralityTests`: asserts message delivery within 5s AND row counts unchanged across all gamekit tables after send |
| 5  | Broadcast from LobbyHub instance A reaches client on instance B via shared Redis backplane (SC#5)                 | VERIFIED | `LobbyRedisBackplanePostConfigure`: `IPostConfigureOptions<RedisOptions>` sets `ConnectionFactory` to return consumer's `IConnectionMultiplexer` (no second multiplexer); `AddSignalR().AddStackExchangeRedis(...)` with `ChannelPrefix = "GameKit"`; `BackplaneTests`: two independent `LobbyTestApp` / `TestServer` instances sharing same Testcontainers Redis; `CrossInstance_Broadcast_Reaches_OtherServer` asserts message from connA/AppA reaches connB/AppB |

**Score:** 5/5 truths verified

---

### Required Artifacts

| Artifact                                                               | Expected                                                   | Status     | Details                                                                                 |
|------------------------------------------------------------------------|------------------------------------------------------------|------------|-----------------------------------------------------------------------------------------|
| `src/GameKit.Lobby/Data/LobbyMigrationConstants.cs`                   | `AdvisoryLockKey = 12178347L`; `MigrationsHistoryTable = "__ef_migrations_lobby"` | VERIFIED | Exact values confirmed; XML doc notes live-verification and pairwise non-equality |
| `tests/GameKit.Lobby.Integration.Tests/LobbyAdvisoryLockKeyTests.cs`   | SC#1 / OPS-11 Wave 0 gate — live hashtext verify + pairwise-distinctness | VERIFIED | Both facts present; uses `OwnerConnectionString`, asserts `Assert.Equal` and 5x `Assert.NotEqual` by literal |
| `tests/GameKit.Lobby.Integration.Tests/GameKit.Lobby.Integration.Tests.csproj` | `Microsoft.AspNetCore.SignalR.Client` PackageReference | VERIFIED | Present; no version attribute (CPM-compliant) |
| `Directory.Packages.props`                                             | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` + `Microsoft.AspNetCore.SignalR.Client` CPM pins at 10.0.8 | VERIFIED | Both pins at lines 99 and 104 |
| `src/GameKit.Lobby/Hubs/LobbyHub.cs`                                  | `[Authorize]`, membership guard on all methods, `Context.User` for player id | VERIFIED | `[Authorize]` attribute; `IsMemberAsync` guard on `JoinLobbyAsync`, `SendChatMessageAsync`, and `MarkReadyAsync`; `GetPlayerId()` reads `Context.User` not `IHttpContextAccessor`; `Context.ConnectionAborted` on all methods |
| `src/GameKit.Lobby/LobbyJwtBearerPostConfigure.cs`                    | Chains existing handler; reads `access_token` query string; scoped to `/hubs/lobby` | VERIFIED | `existingHandler` captured and awaited before conditional; `path.StartsWithSegments("/hubs/lobby")` check |
| `src/GameKit.Lobby/LobbyRedisBackplanePostConfigure.cs`               | `IPostConfigureOptions<RedisOptions>` setting `ConnectionFactory` from consumer `IConnectionMultiplexer` | VERIFIED | `_sp.GetRequiredService<IConnectionMultiplexer>()` in `PostConfigure`; registered via `TryAddEnumerable` (WR-03 fix confirmed) |
| `src/GameKit.Lobby/Services/LobbyService.cs`                          | `TryStartMatchmakingAsync` with `IPartyService` + `IMatchmakingService`; CR-02 try/catch revert | VERIFIED | Full implementation present; try/catch around party create+join block with `RevertToReadyCheckingAsync` on exception; CR-02 fix confirmed |
| `src/GameKit.Lobby/Services/NullLobbyMessageHandler.cs`               | No Save/persist call; returns `true` for relay | VERIFIED | `Task.FromResult(true)` only; no DB access |
| `src/GameKit.Lobby/Entities/LobbyState.cs`                            | Integer enum (no `HasConversion<string>`) | VERIFIED | Plain `enum LobbyState` with integer values; XML doc explicitly forbids `HasConversion<string>()`; configuration confirms integer storage |
| `src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.cs`    | Creates only `lobbies` + `lobby_members`; no chat table; deterministic timestamp 20260522000000 | VERIFIED | Only two `CreateTable` calls; no `lobby_messages` table; timestamp matches |
| `src/GameKit.Lobby/Data/LobbyDesignTimeDbContextFactory.cs`           | 20-entity `ExcludeFromMigrations`; no prior-package table modified | VERIFIED | Explicit exclusion of 4 Core + 3 Auth + 1 Admin + 7 Rankings + 5 Matchmaking = 20 entities |
| `src/GameKit.Lobby/Http/LobbyEndpoints.cs`                            | `POST /api/lobbies/{id}/join` endpoint (CR-01 fix); domain exception mapping (WR-02 fix) | VERIFIED | Route at line 44; `JoinLobbyAsync` handler with `LobbyNotFoundException→404`, `LobbyFullException→409`, `AlreadyMemberException→409`; `RemoveMemberAsync` has full catch block |
| `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs`                 | `AddSignalR().AddStackExchangeRedis`; JWT + Redis post-configure registered via `TryAddEnumerable` | VERIFIED | `AddStackExchangeRedis` chained; both post-configurators use `TryAddEnumerable`; WR-03 fix confirmed |
| `tests/GameKit.Lobby.Integration.Tests/HubAuthTests.cs`               | Two-TestServer SC#2 auth tests                             | VERIFIED | Two `[Fact]` methods: 401 on no-token, connected on valid JWT |
| `tests/GameKit.Lobby.Integration.Tests/ReadyCheckTests.cs`            | SC#3 all-ready → InGame → broadcast + party row DB check   | VERIFIED | Asserts broadcast, DB state=3, party exists in `gamekit.parties` |
| `tests/GameKit.Lobby.Integration.Tests/ChatEphemeralityTests.cs`      | SC#4 real-time relay + no Postgres write + no chat table   | VERIFIED | Row counts snapshot before/after; no `lobby_message%` table assertion |
| `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs`             | SC#5 two-TestServer cross-instance broadcast               | VERIFIED | Two `LobbyTestApp` instances sharing `RedisFixture`; message from connA reaches connB |
| `tests/GameKit.Lobby.Integration.Tests/LobbySchemaTests.cs`           | Migration creates 2 tables; history row; no chat table; unique constraint | VERIFIED | 4 `[Fact]` methods covering all schema assertions |
| `tests/GameKit.Lobby.Integration.Tests/JoinEndpointTests.cs`          | CR-01 join endpoint + 404/409 domain-exception mapping     | VERIFIED | Present with HTTP 200 + DB check, 404, and 409 test cases |
| `tests/GameKit.Lobby.Integration.Tests/MatchmakingRevertTests.cs`     | CR-02 revert: lobby → ReadyChecking when party creation fails | VERIFIED | `AlwaysThrowingPartyService` stub injected via `serviceOverrides`; asserts DB state = 1 (ReadyChecking) not 3 (InGame) |

---

### Key Link Verification

| From                                | To                                             | Via                                        | Status   | Details                                                                          |
|-------------------------------------|------------------------------------------------|--------------------------------------------|----------|----------------------------------------------------------------------------------|
| `LobbyHub`                         | `ILobbyService.MarkReadyAsync`                 | hub invocation → service call              | WIRED    | `_lobby.MarkReadyAsync(lobbyId, playerId, Context.ConnectionAborted)` at line 186 |
| `LobbyHub`                         | `ILobbyService.IsMemberAsync`                  | 3 guards: JoinLobbyAsync, SendChatMessageAsync, MarkReadyAsync | WIRED | All three hub methods call `IsMemberAsync` before action |
| `LobbyHub`                         | `ILobbyClient.ReceiveChatMessageAsync`         | `Clients.Group(...).ReceiveChatMessageAsync` | WIRED  | Line 154: group broadcast after relay gate check                                 |
| `LobbyService`                     | `IHubContext<LobbyHub, ILobbyClient>`          | `_hubContext.Clients.Group.ReceiveStateUpdateAsync` | WIRED | Broadcast after `MarkReadyAsync` commit at line 247 |
| `LobbyService.TryStartMatchmakingAsync` | `IPartyService.CreateAsync` + `JoinAsync`  | direct await in try block                  | WIRED    | Lines 340-347; wrapped in try/catch with revert on failure (CR-02)               |
| `LobbyService.TryStartMatchmakingAsync` | `IMatchmakingService.EnqueueAsync`         | `_matchmakingService.EnqueueAsync(ownerId, ladderId, poolName, party.Id, ct)` | WIRED | Lines 359-363 |
| `LobbyJwtBearerPostConfigure`      | `JwtBearerOptions.Events.OnMessageReceived`    | `IPostConfigureOptions<JwtBearerOptions>`  | WIRED    | Registered via `TryAddEnumerable` in `AddLobby()`; scoped to `/hubs/lobby` path |
| `LobbyRedisBackplanePostConfigure` | `RedisOptions.ConnectionFactory`               | `IPostConfigureOptions<RedisOptions>`      | WIRED    | Resolves `IConnectionMultiplexer` from SP at startup; registered via `TryAddEnumerable` |
| `LobbyBuilderExtensions.AddLobby`  | `LobbyHub` → `/hubs/lobby`                     | `MapHub<LobbyHub>("/hubs/lobby")` in `MapLobby()` | WIRED | `LobbyApplicationBuilderExtensions.MapLobby` line 44 |
| `LobbyAdvisoryLockKeyTests`        | `LobbyMigrationConstants.AdvisoryLockKey`      | `Assert.Equal(LobbyMigrationConstants.AdvisoryLockKey, computed)` | WIRED | SC#1 live-verify test |

---

### Data-Flow Trace (Level 4)

| Artifact                        | Data Variable              | Source                                   | Produces Real Data | Status   |
|---------------------------------|----------------------------|------------------------------------------|--------------------|----------|
| `LobbyHub.SendChatMessageAsync` | `message` (ephemeral relay) | Client invocation; no DB query            | N/A (intentional anti-feature: no DB) | VERIFIED — anti-feature confirmed: `NullLobbyMessageHandler` has no persist call |
| `LobbyService.MarkReadyAsync`   | `lobby`, `member` (SERIALIZABLE) | `_ctx.Set<LobbyEntity>().Include(l => l.Members).FirstOrDefaultAsync` | Yes (real DB query) | FLOWING |
| `LobbyService.TryStartMatchmakingAsync` | `party`, `result` (matchmaking outcome) | `_partyService.CreateAsync` + `_matchmakingService.EnqueueAsync` (real cross-service calls) | Yes | FLOWING |
| `LobbyService.GetPlayerLobbyIdsAsync` | `List<Guid>` (lobby ids for reconnect) | `_ctx.Set<LobbyMemberEntity>().Where(...).Select(...).ToListAsync()` | Yes (real DB query) | FLOWING |

---

### Behavioral Spot-Checks

Step 7b: SKIPPED — the verification context confirms 15/15 integration tests passed in the orchestrator's test run. The test suite is the behavioral spot-check for this package (SignalR + TestServer requires a running host; individual curl-style checks are not applicable). Key behaviors confirmed by specific named tests:

| Behavior                          | Test                                              | Status |
|-----------------------------------|---------------------------------------------------|--------|
| SC#1 advisory-lock live-verify    | `LobbyAdvisoryLockKeyTests` (2 facts)             | PASS (orchestrator run) |
| SC#2 JWT WS auth / 401 gate       | `HubAuthTests` (2 facts)                          | PASS |
| SC#3 all-ready → InGame           | `ReadyCheckTests.AllReady_Triggers_Matchmaking_And_InGame_Broadcast` | PASS |
| SC#3 CR-02 revert on failure      | `MatchmakingRevertTests.AllReady_PartyCreationFails_LobbyRevertsToReadyChecking` | PASS |
| SC#4 chat ephemeral               | `ChatEphemeralityTests.Chat_Delivered_Realtime_And_No_Postgres_Write` | PASS |
| SC#4 no chat table (schema)       | `LobbySchemaTests.No_Chat_Message_Table_Exists`   | PASS |
| SC#5 Redis backplane cross-instance | `BackplaneTests.CrossInstance_Broadcast_Reaches_OtherServer` | PASS |
| CR-01 join endpoint               | `JoinEndpointTests`                               | PASS |

---

### Probe Execution

No `probe-*.sh` files declared or found for Phase 11. Integration tests serve as the probe suite.

---

### Requirements Coverage

| Requirement | Source Plan      | Description                                                                         | Status    | Evidence                                                                                    |
|-------------|------------------|-------------------------------------------------------------------------------------|-----------|---------------------------------------------------------------------------------------------|
| LOBBY-01    | 11-01, 11-02     | `GameKit.Lobby` new NuGet package; per-package migration; live-verified advisory-lock key; `__ef_migrations_lobby`; `IDesignTimeDbContextFactory`; 20-entity `ExcludeFromMigrations` | SATISFIED | Package exists; `LobbyMigrationConstants`; `LobbyDesignTimeDbContextFactory` with 20 explicit exclusions; migration at `20260522000000_LobbyInitial` |
| LOBBY-02    | 11-02, 11-03     | `lobbies` + `lobby_members` + ready-state; persistent groups survive sessions       | SATISFIED | Both tables in migration; FK-backed; unique constraint `(LobbyId, PlayerId)`; `LobbyState` enum; `LobbySchemaTests` verify schema |
| LOBBY-03    | 11-03, 11-04     | Ready-check flow; all-ready → lobby transitions state                               | SATISFIED | `LobbyService.MarkReadyAsync` SERIALIZABLE tx; `AllReady_Triggers_Matchmaking_And_InGame_Broadcast` integration test |
| LOBBY-04    | 11-03, 11-04     | In-lobby chat — ephemeral only, no message persistence                              | SATISFIED | `NullLobbyMessageHandler` has no DB access; no `lobby_messages` table in migration; `LobbySchemaTests.No_Chat_Message_Table_Exists`; `ChatEphemeralityTests` proves no row count increase |
| LOBBY-05    | 11-04            | Lobby → Matchmaking integration via `IPartyService` + `IMatchmakingService.EnqueueAsync` | SATISFIED | `TryStartMatchmakingAsync` calls both services; `ReadyCheckTests` asserts party row in DB. Note: REQUIREMENTS.md wording mentions `lobby_id FK on matchmaking_tickets` and `IMatchFoundHandler`, but RESEARCH.md §Open Questions Q1 documents the intentional deviation — Party row is the cross-package link (no `lobby_id` FK on `matchmaking_tickets`); ROADMAP SC#3 (the verification contract) is fully satisfied |
| LOBBY-06    | 11-03, 11-04     | Hub `[Authorize]`-gated; Redis backplane `ChannelPrefix = "GameKit"`               | SATISFIED | `[Authorize]` on `LobbyHub`; `AddSignalR().AddStackExchangeRedis` with `ChannelPrefix = RedisChannel.Literal("GameKit")`; `LobbyRedisBackplanePostConfigure` wired |
| OPS-11      | 11-01            | Advisory-lock live-verify gate; pairwise-distinct from 5 existing keys              | SATISFIED | `LobbyAdvisoryLockKeyTests`: live Postgres hashtext equality + 5-way non-equality by both symbolic constant and integer literal |

---

### Anti-Patterns Found

No `TODO`, `FIXME`, `TBD`, or `XXX` markers found in `src/GameKit.Lobby/` source files (excluding bin/obj). No stub components detected. No hardcoded empty returns on data paths. The previous code-review findings (CR-01, CR-02, CR-03, WR-01, WR-02, WR-03, IN-01, IN-02, IN-03) were all fixed per `11-REVIEW-FIX.md` and verified as applied in the actual source files.

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| — | — | None found | — | — |

---

### Human Verification Required

None. All five ROADMAP success criteria are verified by automated integration tests confirmed passing by the orchestrator. The phase has no visual UI components.

---

### Gaps Summary

No gaps. All 5 ROADMAP success criteria verified against actual codebase. All 7 requirements (LOBBY-01 through LOBBY-06, OPS-11) satisfied. All 9 code-review findings fixed and confirmed applied in source. No debt markers, no stub implementations, no orphaned artifacts.

---

_Verified: 2026-06-07T01:30:00Z_
_Verifier: Claude (gsd-verifier)_
