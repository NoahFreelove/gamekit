---
phase: 11-gamekit-lobby
plan: "04"
subsystem: lobby
tags: [lobby, matchmaking, signalr, backplane, integration-tests, success-criteria, party, ready-check]

requires:
  - phase: 11-03
    provides: LobbyHub, LobbyService (stub TryStartMatchmakingAsync), AddLobby/MapLobby, LobbyJwtBearerPostConfigure, LobbyRedisBackplanePostConfigure

provides:
  - TryStartMatchmakingAsync: real IPartyService.CreateAsync + JoinAsync + IMatchmakingService.EnqueueAsync(partyId)
  - LobbyMatchmakingException: guard for missing LadderId / missing OwnerId
  - LobbyTestApp: two-TestServer SignalR harness with shared Redis multiplexer, MintPlayerJwt, ConnectLobbyHubAsync, SeedLobbyAsync
  - LobbyTestModelCustomizer: applies Lobby + Matchmaking + Rankings model extensions for integration tests
  - HubAuthTests (SC#2): 401-before-handshake for unauthenticated upgrade; valid JWT connects
  - ReadyCheckTests (SC#3/LOBBY-05): all-ready -> party created -> EnqueueAsync -> InGame -> broadcast
  - ChatEphemeralityTests (SC#4/LOBBY-04): chat relayed real-time; no lobby_message% table; zero Postgres writes
  - BackplaneTests (SC#5/LOBBY-06): two-TestServer cross-instance broadcast via shared Redis backplane

affects: []

tech-stack:
  added: []
  patterns:
    - "TryStartMatchmakingAsync runs OUTSIDE the lobby SERIALIZABLE tx to avoid nested-transaction conflict with IPartyService.CreateAsync (which opens its own SERIALIZABLE tx on the shared GameKitDbContext)"
    - "Optimistic InGame state set inside lobby tx as double-submission gate; reverted to ReadyChecking if EnqueueAsync is rejected"
    - "LobbyTestApp two-TestServer pattern: both instances point to the same RedisFixture connection string, sharing the Redis backplane (SC#5)"
    - "UseWebSockets() before UseRouting() in TestServer pipeline (RESEARCH Pitfall 7)"
    - "InternalsVisibleTo('GameKit.Lobby.Integration.Tests') added to Matchmaking + Rankings AssemblyInfo for LobbyTestModelCustomizer access"

key-files:
  created:
    - tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs
    - tests/GameKit.Lobby.Integration.Tests/LobbyTestModelCustomizer.cs
    - tests/GameKit.Lobby.Integration.Tests/HubAuthTests.cs
    - tests/GameKit.Lobby.Integration.Tests/ReadyCheckTests.cs
    - tests/GameKit.Lobby.Integration.Tests/ChatEphemeralityTests.cs
    - tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs
  modified:
    - src/GameKit.Lobby/Services/LobbyService.cs (TryStartMatchmakingAsync real impl + MarkReadyAsync restructure)
    - src/GameKit.Lobby/Services/Exceptions/LobbyExceptions.cs (added LobbyMatchmakingException)
    - src/GameKit.Matchmaking/AssemblyInfo.cs (added InternalsVisibleTo("GameKit.Lobby.Integration.Tests"))
    - src/GameKit.Rankings/AssemblyInfo.cs (added InternalsVisibleTo("GameKit.Lobby.Integration.Tests"))

decisions:
  - "TryStartMatchmakingAsync runs after the lobby SERIALIZABLE tx commits (not inside it) because IPartyService.CreateAsync opens its own SERIALIZABLE tx on the same scoped GameKitDbContext — EF Core does not support nested transactions"
  - "No lobby_id FK on matchmaking_tickets — the Party row created via IPartyService.CreateAsync is the cross-package link (migration boundary, LOBBY-05 literal wording deviation, documented in RESEARCH §Q1)"
  - "Optimistic InGame state committed inside lobby tx as a double-submission guard; reverted by RevertToReadyCheckingAsync if matchmaking rejects"
  - "LobbyTestApp.SeedLobbyAsync inserts lobby state=1 (ReadyChecking) directly via Npgsql for SC#3 test setup"

metrics:
  duration: 20min
  completed: 2026-06-07
  tasks: 3
  files: 10
---

# Phase 11 Plan 04: Real TryStartMatchmakingAsync + SC#2/3/4/5 Integration Tests Summary

**Real IPartyService.CreateAsync + JoinAsync + IMatchmakingService.EnqueueAsync(partyId) wiring in LobbyService, two-TestServer SignalR harness (LobbyTestApp + LobbyTestModelCustomizer), and all four success-criteria integration tests (SC#2/3/4/5) — all 11 Lobby integration tests green.**

## Performance

- **Duration:** ~20 min
- **Completed:** 2026-06-07
- **Tasks:** 3
- **Files modified:** 10

## SC→Test Mapping

| SC | [Fact] | What it proves |
|----|--------|---------------|
| SC#2 | `HubAuthTests.Unauthenticated_Upgrade_Returns_401_Before_Handshake` | HTTP 401 before WS handshake when no access_token |
| SC#2 | `HubAuthTests.Valid_PlayerJwt_Connects_Successfully` | Valid JWT in access_token connects to /hubs/lobby |
| SC#3 / LOBBY-05 | `ReadyCheckTests.AllReady_Triggers_Matchmaking_And_InGame_Broadcast` | All-ready → party created → InGame state → broadcast observed |
| SC#4 / LOBBY-04 | `ChatEphemeralityTests.Chat_Delivered_Realtime_And_No_Postgres_Write` | Chat relayed real-time; no lobby_message% table; zero Postgres rows written |
| SC#5 / LOBBY-06 | `BackplaneTests.CrossInstance_Broadcast_Reaches_OtherServer` | Cross-instance broadcast via shared Redis backplane (two TestServer instances) |

## LOBBY-05 Deviation: No lobby_id FK on matchmaking_tickets

The LOBBY-05 requirement wording says "a ready lobby submits a party ticket (lobby_id FK on matchmaking_tickets)". This wording violates the migration boundary: GameKit.Lobby must not modify Matchmaking's tables.

**Implemented approach:** `TryStartMatchmakingAsync` calls `IPartyService.CreateAsync(ownerId)` to create a `Party` row, then `IPartyService.JoinAsync(party.PartyCode, memberId)` for each non-owner member, then `IMatchmakingService.EnqueueAsync(ownerId, ladderId, poolName, party.Id)`. The `Party` row is the cross-package link — it lives in Matchmaking's schema and carries the party composition. No `lobby_id` column was added to `matchmaking_tickets`. This is documented in `11-RESEARCH.md §Open Questions Q1`.

## Task Commits

1. **Task 1: Real TryStartMatchmakingAsync** — `482c3df` (feat)
2. **Task 2: LobbyTestApp + LobbyTestModelCustomizer** — `d3cf505` (feat)
3. **Task 3: SC#2/3/4/5 integration tests + MarkReadyAsync fix** — `f4dcf03` (feat)

## Files Created/Modified

- `src/GameKit.Lobby/Services/LobbyService.cs` — Real TryStartMatchmakingAsync; MarkReadyAsync restructured to call matchmaking outside lobby SERIALIZABLE tx; RevertToReadyCheckingAsync
- `src/GameKit.Lobby/Services/Exceptions/LobbyExceptions.cs` — Added LobbyMatchmakingException
- `src/GameKit.Matchmaking/AssemblyInfo.cs` — Added InternalsVisibleTo("GameKit.Lobby.Integration.Tests")
- `src/GameKit.Rankings/AssemblyInfo.cs` — Added InternalsVisibleTo("GameKit.Lobby.Integration.Tests")
- `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` — Two-TestServer SignalR harness; shared Redis multiplexer; MintPlayerJwt; ConnectLobbyHubAsync; SeedLobbyAsync
- `tests/GameKit.Lobby.Integration.Tests/LobbyTestModelCustomizer.cs` — Applies Lobby + Matchmaking + Rankings model extensions
- `tests/GameKit.Lobby.Integration.Tests/HubAuthTests.cs` — SC#2: 401-before-handshake + valid JWT connect
- `tests/GameKit.Lobby.Integration.Tests/ReadyCheckTests.cs` — SC#3: all-ready → InGame → broadcast
- `tests/GameKit.Lobby.Integration.Tests/ChatEphemeralityTests.cs` — SC#4: chat real-time + no Postgres write
- `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` — SC#5: cross-instance broadcast

## Decisions Made

- TryStartMatchmakingAsync runs outside the lobby SERIALIZABLE tx to avoid nested-transaction conflict (EF Core limitation)
- Optimistic InGame state inside lobby tx as double-submission guard; RevertToReadyCheckingAsync reverts if matchmaking rejected
- No lobby_id FK on matchmaking_tickets — Party row is the cross-package link (LOBBY-05 deviation, migration boundary compliant)
- InternalsVisibleTo grants added to Matchmaking + Rankings AssemblyInfo for LobbyTestModelCustomizer

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Nested SERIALIZABLE transaction conflict in TryStartMatchmakingAsync**
- **Found during:** Task 3 (test run: SC#3 ReadyCheckTests failed with HubException)
- **Issue:** `IPartyService.CreateAsync` opens its own SERIALIZABLE transaction on the same scoped `GameKitDbContext`. When called inside LobbyService's own SERIALIZABLE transaction, EF Core throws because nested transactions are not supported. The SC#3 test caught this empirically.
- **Fix:** Restructured `MarkReadyAsync` to: (1) SERIALIZABLE tx — mark member ready, set `State=InGame` optimistically as a double-submission gate, commit; (2) AFTER commit — call `TryStartMatchmakingAsync` which runs `IPartyService.CreateAsync/JoinAsync` + `IMatchmakingService.EnqueueAsync` with their own transactions; (3) if matchmaking rejected, call `RevertToReadyCheckingAsync` to set state back to ReadyChecking.
- **Files modified:** `src/GameKit.Lobby/Services/LobbyService.cs`
- **Committed in:** f4dcf03 (Task 3 commit)

**2. [Rule 3 - Blocking] Added InternalsVisibleTo("GameKit.Lobby.Integration.Tests") to Matchmaking + Rankings**
- **Found during:** Task 2 (build after creating LobbyTestModelCustomizer)
- **Issue:** `MatchmakingModelBuilderExtension` and `RankingsModelBuilderExtension` are `internal` and `GameKit.Lobby.Integration.Tests` was not in their `InternalsVisibleTo` grants, blocking `LobbyTestModelCustomizer` from using them.
- **Fix:** Added `[assembly: InternalsVisibleTo("GameKit.Lobby.Integration.Tests")]` to both `src/GameKit.Matchmaking/AssemblyInfo.cs` and `src/GameKit.Rankings/AssemblyInfo.cs`.
- **Files modified:** Both AssemblyInfo.cs files
- **Committed in:** d3cf505 (Task 2 commit)

**3. [Rule 1 - Bug] party.Code → party.PartyCode property name**
- **Found during:** Task 1 (build error CS1061)
- **Issue:** `Party.PartyCode` is the actual property name, not `Code` as assumed from PATTERNS.md §interfaces.
- **Fix:** Changed `party.Code` to `party.PartyCode` in TryStartMatchmakingAsync.
- **Files modified:** `src/GameKit.Lobby/Services/LobbyService.cs`
- **Committed in:** 482c3df (Task 1 commit)

**4. [Rule 1 - Bug] Lobby.OwnerId is Guid? (nullable)**
- **Found during:** Task 1 (build error CS1503)
- **Issue:** `Lobby.OwnerId` is `Guid?` (ON DELETE SET NULL FK), not `Guid`. All usages needed null-guards and `.Value` dereferences.
- **Fix:** Added `if (ownerIdNullable is null)` guard and used `.Value` where needed.
- **Files modified:** `src/GameKit.Lobby/Services/LobbyService.cs`
- **Committed in:** 482c3df (Task 1 commit)

---

**Total deviations:** 4 auto-fixed (3 bug, 1 blocking)
**Impact on plan:** All fixes required for correctness and compilation. No scope creep.

## Known Stubs

None — all TODO(11-04) stubs removed.

## Threat Flags

No new network surfaces or trust boundaries introduced beyond what was already registered in the plan's `<threat_model>` (T-11-04-01 through T-11-04-SC all addressed as designed).

## Self-Check: PASSED

- [x] `src/GameKit.Lobby/Services/LobbyService.cs` — FOUND; EnqueueAsync called 4 times
- [x] `src/GameKit.Lobby/Services/Exceptions/LobbyExceptions.cs` — FOUND; LobbyMatchmakingException present
- [x] `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` — FOUND; UseWebSockets count=3
- [x] `tests/GameKit.Lobby.Integration.Tests/LobbyTestModelCustomizer.cs` — FOUND
- [x] `tests/GameKit.Lobby.Integration.Tests/HubAuthTests.cs` — FOUND; contains "401"
- [x] `tests/GameKit.Lobby.Integration.Tests/ReadyCheckTests.cs` — FOUND; contains "InGame"
- [x] `tests/GameKit.Lobby.Integration.Tests/ChatEphemeralityTests.cs` — FOUND; contains "lobby_message"
- [x] `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` — FOUND; contains "CrossInstance"
- [x] Commits 482c3df, d3cf505, f4dcf03 — all present
- [x] `dotnet build GameKit.sln -warnaserror` — 0 warnings, 0 errors
- [x] All 11 integration tests pass: `Passed! - Failed: 0, Passed: 11, Skipped: 0, Total: 11`

---
*Phase: 11-gamekit-lobby*
*Completed: 2026-06-07*
