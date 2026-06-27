---
phase: 21-final-demo-3d-multiplayer-platformer
plan: 06
subsystem: integration-tests
status: complete
tags: [integration-tests, R5, R7, R8, R9, R10, matchmaking, lobby, smoke, admin-seeder]
dependencies:
  requires: [21-01, 21-02, 21-03, 21-04, 21-05]
  provides: [R5-gate, R7-gate, R8-gate, R9-gate, R10-gate, compose-port-gate, admin-console-gate]
  affects: []
tech-stack:
  added: []
  patterns:
    - PlatformerTestApp in-process ASP.NET Core test host (mirrors LobbyTestApp)
    - SeedLobbyAsync for ReadyChecking lobby state injection (bypasses REST for solo lobbies)
    - PollBothUntilMatchedAsync concurrent proposal auto-accept pattern
    - IRankingsTicker.RunOnceAsync for synchronous rating flush in tests
    - TestServer.CreateWebSocketClient() with ConfigureRequest for WS+JWT auth
    - DemoAdminSeederHostedService config-gated + non-Production-guarded startup seeder
    - Virtual seam pattern (AnyAdminExistsAsync/PersistAdminAsync) for unit testability of EF-backed hosted services
key-files:
  created:
    - tests/GameKit.Platformer3D.Integration.Tests/Strategy/BestTimeStrategyResolutionTests.cs
    - tests/GameKit.Platformer3D.Integration.Tests/Auth/GuestOnboardingTests.cs
    - tests/GameKit.Platformer3D.Integration.Tests/Auth/PlayerJwtRejectedTests.cs
    - tests/GameKit.Platformer3D.Integration.Tests/Packaging/ComposePortMappingTests.cs
    - tests/GameKit.Platformer3D.Integration.Tests/Lobby/LobbyToMatchTests.cs
    - tests/GameKit.Platformer3D.Integration.Tests/Smoke/EndToEndSmokeTests.cs
    - samples/Platformer3D/DemoAdminSeederHostedService.cs
    - tests/GameKit.Platformer3D.Tests/Admin/DemoAdminSeederHostedServiceTests.cs
  modified:
    - tests/GameKit.Platformer3D.Integration.Tests/PlatformerTestApp.cs
    - samples/Platformer3D.GameServer/PlatformerGameServerService.cs
    - samples/Platformer3D/Program.cs
    - samples/Platformer3D/docker-compose.yml
    - samples/Platformer3D/README.md
    - tests/GameKit.Platformer3D.Tests/GameKit.Platformer3D.Tests.csproj
decisions:
  - "SeedLobbyAsync (direct SQL insert State=1) used for solo MaxMembers=1 lobbies to bypass the Open→ReadyChecking gap in the lobby REST flow"
  - "IRankingsTicker.RunOnceAsync() invoked directly in test to flush PendingRatingUpdate rows immediately (60s ticker gap)"
  - "Session start (Pending→Active) added to PlatformerGameServerService.HandleConnectionAsync on first WS connection — missing from Wave 4"
  - "PollBothUntilMatchedAsync runs both player poll loops concurrently via Task.WhenAll to avoid proposal-accept deadlock"
  - "ForwardingHandler routes 'platformer.web-api' HttpClient through in-process TestServer handler"
  - "DemoAdminSeeder uses virtual seams (AnyAdminExistsAsync/PersistAdminAsync) instead of InMemory EF because GameKitDbContext has JsonDocument columns (AdminAuditLog.After) incompatible with InMemory provider"
  - "DemoAdminSeeder class changed from sealed to open (not sealed) to allow TestableSeeder subclass override in unit tests"
metrics:
  duration: "~75 min T1+T2 + ~35 min admin seeder"
  completed: 2026-06-23T21:42:00Z
  tasks_completed: 3
  tasks_total: 3
  files_created: 8
  files_modified: 6
---

# Phase 21 Plan 06: Integration Tests + Smoke Suite Summary

**One-liner:** 21 Testcontainers integration tests covering R5/R7/R8/R9/R10 with concurrent-proposal poll, solo-lobby SeedAsync pattern, and full WS game-session smoke loop; stopped at Task 3 (human-verify checkpoint).

## Tasks Executed

| Task | Name | Commit | Status |
|------|------|--------|--------|
| 1 | Strategy R5 + Guest R8 + JWT-Rejected R7 + Compose-Port R3 | `9f1b464` | Complete — 15 tests pass |
| 2 | Lobby→1v1 R9 + Full-loop smoke R10 | `f4843a8` | Complete — 6 tests pass (21 total) |
| 3 | Demo admin seeder — one-command stack with usable admin console | `98cddb2` | Complete — 45 unit tests pass, live verified |

## What Was Built

### Task 1 (9f1b464)

Five test files covering the quick-win automated requirements:

- **BestTimeStrategyResolutionTests** (R5/A3): resolves `IMatchmakingStrategy` from the Platformer3D DI container, asserts it is `BestTimeMatchmakingStrategy` (not `EloRangeMatchmakingStrategy`), and drives a real two-party match through it with Testcontainers Postgres + Redis.

- **GuestOnboardingTests** (R8): 5 tests — guest login returns tokens, guest player has no PII (no `player_identities` or `player_credentials` rows), auto-generated display name starts with "Guest-", guest JWT allows enqueue into the platformer ladder, two guest logins produce distinct player rows.

- **PlayerJwtRejectedTests** (R7 must-NOT): 3 tests — player JWT returns 401/403, unauthenticated request returns 401, guest JWT returns 401/403. All verify the `RequiresServiceToken` policy on `POST /api/sessions/{id}/complete`.

- **ComposePortMappingTests** (R3 must-NOT): 5 YAML-parse tests (no Docker needed) — compose file exists, has exactly 3 services (app/postgres/redis), `app` publishes exactly one host port (8080), `postgres` has no ports mapping, `redis` has no ports mapping.

- **AssemblyInfo.cs patches**: `[assembly: InternalsVisibleTo("GameKit.Platformer3D.Integration.Tests")]` added to Auth, Rankings, Matchmaking, and Lobby packages.

### Task 2 (f4843a8)

Two more test files covering the complex lobby and smoke paths:

- **LobbyToMatchTests** (R9): happy path seeds two solo-player lobbies in ReadyChecking state via `SeedLobbyAsync`, connects both to the SignalR hub, both call `MarkReadyAsync`, both get `InGame` broadcast, then polls both tickets until matched in the same session. Abort path (R9/D-04): owner creates 2-person lobby, joiner joins, owner removes joiner before ready-check — zero tickets enqueued, lobby still exists.

- **EndToEndSmokeTests** (R10): 4 tests:
  - `FullLoop_GuestToLeaderboard` — guest login, enqueue, match, WS `run_start`/`checkpoint`/`run_finish` for both players, game server posts session-complete, `IRankingsTicker.RunOnceAsync()` flushes ratings, both player ratings changed from default 1000.
  - `DoublePost_SessionComplete_IsIdempotent` — posts same idempotency key twice, asserts exactly one `game_sessions` row in Completed state.
  - `Rerun_FullLoopPassesTwice` — runs the full loop twice sequentially on the same host to verify no residual state.
  - `ConcurrentParties_EachFormExactlyOneMatch` — four players enqueue simultaneously, asserts exactly 2 distinct sessions formed.

**Infrastructure additions to PlatformerTestApp:**
- `SeedLobbyAsync` — direct SQL insert of lobby in State=1 (ReadyChecking) with members
- `IsSessionCompletedAsync` — polls `game_sessions.State = 'Completed'`
- `CountGameSessionOutcomesAsync` updated to filter by State = Completed
- `ForwardingHandler` — routes `"platformer.web-api"` HttpClient to in-process TestServer
- `NullAdminAuditWriter` — no-op `IAdminAuditWriter` for Matchmaking DI satisfaction
- WS `/ws/game` endpoint with correct path segment extraction (strips prefix, reads `segments[0]`)

**Bug fix in PlatformerGameServerService (samples/Platformer3D.GameServer):**
Session Pending→Active start call was missing. `HandleConnectionAsync` now calls `StartSessionAsync` (which posts `POST /api/sessions/{id}/start`) on the first player connection per match, transitioning the session from Pending to Active before the completion POST. Without this fix, `PostCompleteAsync` always returned 404 (session not in Active state).

## Test Results

All 21 tests pass with live Testcontainers (Postgres 16 + Redis 8, Docker 29.5.3):

```
Total tests: 21
     Passed: 21
 Total time: 15.3 seconds
```

| Filter | Tests | Status |
|--------|-------|--------|
| ComposePort | 5 | Pass |
| R5 (BestTimeStrategy) | 2 | Pass |
| R8 (GuestOnboarding) | 5 | Pass |
| R7 (PlayerJwtRejected) | 3 | Pass |
| R9 (LobbyToMatch) | 2 | Pass |
| R10 (EndToEndSmoke) | 4 | Pass |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Missing session Pending→Active start call in PlatformerGameServerService**
- **Found during:** Task 2 — FullLoop_GuestToLeaderboard failed with `Assert.NotNull` on ratingA (null because session complete returned 404)
- **Root cause:** `PostCompleteAsync` requires the session in `Active` state (WHERE state=Active), but matchmaking creates sessions in `Pending`. `HandleConnectionAsync` had no call to `POST /api/sessions/{id}/start`.
- **Fix:** Added `StartSessionAsync` method + `_activatedSessions` dictionary to `PlatformerGameServerService`. On first player WS connection per matchId, posts `POST /api/sessions/{matchId}/start` with service token Bearer, transitioning Pending→Active.
- **Files modified:** `samples/Platformer3D.GameServer/PlatformerGameServerService.cs`
- **Commit:** `f4843a8`

**2. [Rule 1 - Bug] /ws/game path segment extraction off by 2**
- **Found during:** Task 2 — `FullLoop_GuestToLeaderboard` and `Rerun_FullLoopPassesTwice` returned HTTP 400 on WS connect (`Incomplete handshake, status code: 400`)
- **Root cause:** `app.Map("/ws/game", ...)` strips the `/ws/game` prefix. Inside the branch, `ctx.Request.Path` is `"/{matchId}"` — segments at index 0. Code used `segments[2]`, finding an empty string, causing `Guid.TryParse` to fail → 400.
- **Fix:** Changed `segments[2]` to `segments[0]` in `PlatformerTestApp.cs`
- **Files modified:** `tests/GameKit.Platformer3D.Integration.Tests/PlatformerTestApp.cs`
- **Commit:** `f4843a8`

**3. [Rule 2 - Missing functionality] SeedLobbyAsync needed for solo MaxMembers=1 lobbies**
- **Found during:** Task 2 — `MarkReadyAsync` requires `lobby.State == LobbyState.ReadyChecking` to trigger matchmaking submission. MaxMembers=1 lobbies created via REST stay in `Open` state (no second joiner to trigger the Open→ReadyChecking transition).
- **Fix:** Added `SeedLobbyAsync` to `PlatformerTestApp` that directly inserts a lobby in State=1 (ReadyChecking) + members. `LobbyToMatchTests` uses this for solo lobbies.
- **Files modified:** `tests/GameKit.Platformer3D.Integration.Tests/PlatformerTestApp.cs`
- **Commit:** `f4843a8`

**4. [Rule 2 - Missing functionality] IRankingsTicker.RunOnceAsync needed in rating assertion**
- **Found during:** Task 2 — `GetPlayerRatingAsync` returned null after WS session complete. Rankings uses deferred `PendingRatingUpdate` rows processed by `RankingsTickerService` on a 60-second schedule.
- **Fix:** After `WaitForSessionCompleteAsync`, test resolves `IRankingsTicker` from DI and calls `RunOnceAsync(CancellationToken.None)` to flush pending updates immediately.
- **Files modified:** `tests/GameKit.Platformer3D.Integration.Tests/Smoke/EndToEndSmokeTests.cs`
- **Commit:** `f4843a8`

**5. [Rule 1 - Bug] CountGameSessionOutcomesAsync counted Pending sessions**
- **Found during:** Task 2 — `WaitForSessionCompleteAsync` returned immediately (count=1) because the matchmaking creates the session row in Pending state. The wait completed before the game server POSTed complete.
- **Fix:** Added `IsSessionCompletedAsync` (polls `State='Completed'`) and updated `CountGameSessionOutcomesAsync` to filter by `State='Completed'`.
- **Files modified:** `tests/GameKit.Platformer3D.Integration.Tests/PlatformerTestApp.cs`
- **Commit:** `f4843a8`

## Task 3: Demo Admin Seeder (98cddb2)

**DemoAdminSeederHostedService** added to `samples/Platformer3D/`:

- `Platformer:DemoAdmin:Enabled` config gate (bool, default false)
- `Platformer:DemoAdmin:Username` (default `root`)
- `Platformer:DemoAdmin:Password` (required when enabled)
- Production environment guard: absolute no-op in `ASPNETCORE_ENVIRONMENT=Production`
- Idempotent: only seeds when `admin_users` is completely empty
- Auto-promotes first admin to `superadmin` (mirrors CLI `AdminCreateCommand` behavior)
- Logs prominent `WARNING: seeded DEMO admin 'root' -- DEMO ONLY` on seed

**Registered** in `Program.cs` AFTER `AddGameKitAdmin` so `AdminMigrationHostedService` creates the table before the seeder runs.

**docker-compose.yml** updated with DEMO-ONLY env vars:
- `Platformer__DemoAdmin__Enabled: "true"`
- `Platformer__DemoAdmin__Username: "root"`
- `Platformer__DemoAdmin__Password: "platformer-demo-admin"`

**README.md** updated with admin creds, override/disable instructions, and production guidance.

**Live verification results (stack running at http://localhost:8080):**
- `/health/ready`: HTTP 200
- `admin_users` row: `root|superadmin`
- Admin login POST to `/admin/login/submit`: HTTP 302 redirect to `/admin` (success, session cookie issued)
- Authenticated GET `/admin`: HTTP 200
- DEMO-ONLY warning in app logs: confirmed

## Known Stubs

None. The demo pipeline is fully wired: guest JWT → enqueue → match → WS session → service-token complete → rating update → leaderboard.

## Threat Flags

None surfaced beyond the plan's declared STRIDE register (T-21-20 through T-21-SC).

## Self-Check: PASSED

- Task 1 commit `9f1b464`: FOUND
- Task 2 commit `f4843a8`: FOUND
- Task 3 commit `98cddb2`: FOUND
- All 45 unit tests pass (dotnet test tests/GameKit.Platformer3D.Tests/): CONFIRMED
- `samples/Platformer3D/DemoAdminSeederHostedService.cs`: FOUND
- `tests/GameKit.Platformer3D.Tests/Admin/DemoAdminSeederHostedServiceTests.cs`: FOUND
- Docker live stack: /health/ready=200, admin_users has root|superadmin, admin login returns 302, GET /admin returns 200
