---
phase: 21-final-demo-3d-multiplayer-platformer
plan: "04"
subsystem: host-composition-game-server
tags: [aspnetcore, websocket, matchmaking, service-token, idempotency, admin-ui, embedded-hosted-service]
status: complete

dependency_graph:
  requires:
    - "21-01 (Platformer3D project scaffold + stub Program.cs)"
    - "21-02 (BestTimeMatchmakingStrategy + TimeMarginRankingAlgorithm)"
    - "21-03 (browser client + WS run-summary frame format)"
  provides:
    - "samples/Platformer3D/Program.cs — complete host composition with custom strategy/algorithm, admin, WebSocket"
    - "samples/Platformer3D/appsettings.json + appsettings.Development.json — GameKit + Redis + JWT + service-token config"
    - "samples/Platformer3D.GameServer/RunSummary.cs — run-summary record + WS message DTOs"
    - "samples/Platformer3D.GameServer/RunSummaryValidator.cs — pure D-03 sanity validator"
    - "samples/Platformer3D.GameServer/WebSocketGameSession.cs — per-connection WS state machine"
    - "samples/Platformer3D.GameServer/PlatformerGameServerService.cs — embedded IHostedService"
    - "tests/GameKit.Platformer3D.Tests/GameServer/RunSummaryValidatorTests.cs — 10 D-03 unit tests"
    - "tests/GameKit.Platformer3D.Tests/GameServer/IdempotentCompletionUnitTests.cs — 8 Docker-free R7 tests"
  affects:
    - "21-05 (Dockerfile — GameServer DLL is now produced and must be in COPY layer)"
    - "21-06 (integration smoke tests drive the exact WS wire format and service-token endpoints)"

tech_stack:
  added:
    - "PlatformerGameServerService (IHostedService) — embedded game server using IServiceScopeFactory + IHttpClientFactory"
    - "RunSummaryValidator — pure static D-03 sanity checker (no I/O)"
    - "WebSocketGameSession — per-connection state machine (System.Net.WebSockets raw API)"
  patterns:
    - "services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>()) after AddMatchmaking() (A3 LOCKED)"
    - "revoke-then-issue service token at IHostedService.StartAsync via scoped IServiceScopeFactory (A5)"
    - "Deterministic Idempotency-Key: 'platformer-session-{sessionId}' (R7/D-05)"
    - "BuildCompleteRequest: faster-ms→Win/Loss, exact-ms-tie→both Draw (D-10)"
    - "PeriodicTimer app-level ping/pong at 15s intervals (D-04)"

key_files:
  created:
    - "samples/Platformer3D/appsettings.json"
    - "samples/Platformer3D/appsettings.Development.json"
    - "samples/Platformer3D.GameServer/RunSummary.cs"
    - "samples/Platformer3D.GameServer/RunSummaryValidator.cs"
    - "samples/Platformer3D.GameServer/WebSocketGameSession.cs"
    - "samples/Platformer3D.GameServer/PlatformerGameServerService.cs"
    - "tests/GameKit.Platformer3D.Tests/GameServer/RunSummaryValidatorTests.cs"
    - "tests/GameKit.Platformer3D.Tests/GameServer/IdempotentCompletionUnitTests.cs"
  modified:
    - "samples/Platformer3D/Program.cs (replaced stub with full composition)"

decisions:
  - "A3 LOCKED: services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>()) called AFTER AddMatchmaking() — MatchmakerTickerService injects a SINGLE IMatchmakingStrategy; Scrutor registers EloRange inside AddMatchmaking(); Replace() removes it; the gate is the 21-06 resolution test"
  - "ladder.Algorithm = 'time-margin' so TimeMarginRankingAlgorithm drives the platformer leaderboard (R6/D-12)"
  - "WebSocket endpoint /ws/game/{matchId:guid} placed after UseGameKitAuth middleware so ctx.User is authenticated (D-01)"
  - "PlatformerGameServerService registered as both singleton and IHostedService so /ws/game handler and StartAsync share the same instance holding the in-process token (D-13)"
  - "Idempotency-Key = 'platformer-session-{sessionId}' — deterministic, per-session, matches across retries (R7/D-05)"
  - "RatingPeriod = TimeSpan.FromMinutes(1) on rankings ladder for live demo (Pitfall 8 — visible leaderboard changes)"

metrics:
  duration: "18min"
  completed: "2026-06-23"
  tasks_completed: 4
  files_created: 8
  files_modified: 1
---

# Phase 21 Plan 04: Host Composition + Embedded GameServer Summary

**One-liner:** Platformer3D host wired with custom strategy (services.Replace after AddMatchmaking, A3), custom algorithm (ladder.Algorithm="time-margin"), admin console, and an embedded IHostedService game server that issues an in-process service token, validates run-summaries, and posts idempotent session completions.

## Tasks Completed

| Task | Name | Commit | Key Files |
|------|------|--------|-----------|
| 1 | Host composition (Program.cs) — custom seams, admin, WebSocket, GameServer | e96dc5b | Program.cs, appsettings.json, appsettings.Development.json |
| 2 | Run-summary DTOs + D-03 sanity validator + unit tests | 47ca75d | RunSummary.cs, RunSummaryValidator.cs, RunSummaryValidatorTests.cs |
| 3 | Embedded GameServer IHostedService — in-process token, WS session, idempotent completion | 2387eb7 | PlatformerGameServerService.cs, WebSocketGameSession.cs |
| 4 | Docker-free R7 idempotency unit test (mocked ISessionCompleteService + IIdempotencyStore) | 81c9f13 | IdempotentCompletionUnitTests.cs |

## A3 Wiring (CONFIRMED LOCKED)

The custom strategy is registered with the **LOCKED** form:

```csharp
builder.Services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>());
```

This call appears **after** `gameKitBuilder.AddMatchmaking(...).AddLadder("platformer", ...)`.

**Rationale:** `MatchmakerTickerService` injects a single `IMatchmakingStrategy` (not `IEnumerable`, not keyed). `AddMatchmaking()` internally calls `AddStrategyServices()` which Scrutor-scans `FromAssemblyOf<EloRangeMatchmakingStrategy>()` and registers `EloRangeMatchmakingStrategy`. MS.DI returns the last-registered descriptor, so `Replace()` removes the Scrutor entry and installs exactly one strategy. The 21-06 R5 resolution test (`GetRequiredService<IMatchmakingStrategy>() is BestTimeMatchmakingStrategy`) is the gate.

## WebSocket Wire Format (canonical for 21-06 smoke test)

**Endpoint:** `GET /ws/game/{matchId:guid}` (HTTP upgraded to WebSocket)
**Auth:** Player Bearer JWT validated by UseGameKitAuth before the WS upgrade (D-01)

**Client → server (JSON text frames):**
```json
{ "type": "run_start", "matchId": "<guid>", "startMs": 1750000000000 }
{ "type": "checkpoint", "index": 0, "timestampMs": 1750000005000 }
{ "type": "checkpoint", "index": 1, "timestampMs": 1750000010000 }
{ "type": "run_finish", "finishMs": 1750000045000 }
{ "type": "pong" }
```

**Server → client (JSON text frames):**
```json
{ "type": "validated", "completionMs": 45000, "sessionId": "<guid>" }
{ "type": "rejected", "reason": "non_monotonic_checkpoints" }
{ "type": "rejected", "reason": "implausible_duration" }
{ "type": "rejected", "reason": "duplicate_finish" }
{ "type": "ping" }
```

D-03 thresholds: `[5_000ms, 300_000ms]` duration, strictly ascending checkpoints, StartMs < all checkpoints < FinishMs.

## Service Token (D-13)

- **Token name:** `platformer-gameserver-embedded` (from `Platformer:ServiceTokenName` config key)
- **Issuance:** `StartAsync` resolves scoped `IServiceTokenService` via `IServiceScopeFactory`, calls `RevokeAsync(name)` (returns false-on-missing, never throws), then `IssueAsync(name, null, ct)`
- **Storage:** raw token held in `private string? _serviceTokenRaw` — never written to a log, response, config, or database
- **Loopback POST URL:** `{Platformer:WebApiBaseUrl}/api/sessions/{sessionId}/complete` (default `http://localhost:8080`)

## Idempotency-Key (R7/D-05)

- **Format:** `platformer-session-{sessionId}` — deterministic, per-session
- **Proven by:** `IdempotentCompletionUnitTests.IdempotencyKeyFor_SameSessionId_ReturnsByteEqualKeys`
- **Duplicate-post behavior:** first call → `Completed`; second call with same key → `AlreadyCompletedCached`; exactly one outcome row

## Win/Loss/Draw Mapping (D-10)

`BuildCompleteRequest(p1Id, p1Ms, p2Id, p2Ms)`:
- `p1Ms < p2Ms` → p1 Win, p2 Loss
- `p1Ms > p2Ms` → p1 Loss, p2 Win
- `p1Ms == p2Ms` → both Draw (exact integer-ms tie, D-10)
- Score = integer-ms completion time

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] C# records are sealed — WsInMessage hierarchy invalid**
- **Found during:** Task 2 (first build of RunSummary.cs)
- **Issue:** The plan sketch used `WsRunStart : WsInMessage`, `WsCheckpoint : WsInMessage`, etc. C# record types are implicitly sealed; they cannot be subclassed.
- **Fix:** Made each inbound DTO an independent `sealed record` without a base class. The type discriminator is parsed manually by the `WebSocketGameSession` frame dispatcher via `JsonDocument.RootElement.TryGetProperty("type", ...)`.
- **Files modified:** RunSummary.cs

**2. [Rule 2 - Missing Critical Functionality] GameServer stub must be replaced before Program.cs builds**
- **Found during:** Task 1 (Program.cs references PlatformerGameServerService which did not exist)
- **Issue:** Program.cs (Task 1) references `PlatformerGameServerService` (Task 3 artifact). The build fails if Task 3 files are not present. Commit ordering required creating GameServer files before committing Task 1.
- **Fix:** Created all GameServer files (Tasks 2/3) before committing Task 1. The commit sequence is: Task 1 (Program.cs) → Task 2 (DTOs/validator) → Task 3 (GameServer service) → Task 4 (tests). All four commits are on the correct task boundary.
- **Note:** GameServerPlaceholder.cs retained (internal class, no conflict with new public classes).

## Known Stubs

None. All wiring is real:
- `Program.cs` does actual DI composition (not a stub chain)
- `PlatformerGameServerService.StartAsync` issues a real token via `IServiceTokenService`
- `RunSummaryValidator.Validate` performs real D-03 checks
- `WebSocketGameSession` drives a real receive loop
- Integration behavior (R5 resolution, R7 double-post, R8/R9 end-to-end) is deferred to 21-06 Testcontainers tests as planned

## Threat Flags

None beyond what the plan's threat model covers:
- T-21-11: `RequiresServiceToken` on session-complete endpoint is not weakened by the host (verified by reading endpoint registration)
- T-21-12: `RunSummaryValidator.Validate` enforces monotonic checkpoints + plausible bounds + one-finish (D-03)
- T-21-13: Deterministic `Idempotency-Key` per session; `AlreadyCompletedCached` path proven by Docker-free unit test
- T-21-14: Raw token in `private string? _serviceTokenRaw`; never logged/returned/persisted
- T-21-15: `RevokeAsync` before `IssueAsync` at `StartAsync` handles container restarts (A5)

## Self-Check: PASSED

All 9 created/modified files verified present on disk.
All 4 task commits verified in git log (e96dc5b, 47ca75d, 2387eb7, 81c9f13).
Build: both `Platformer3D.csproj` and `Platformer3D.GameServer.csproj` exit 0.
Tests: 10 RunSummary validator tests PASS; 8 idempotency/BuildCompleteRequest tests PASS.
Acceptance criteria greps: `AlreadyCompletedCached` FOUND; `Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>` FOUND; `AddGameKitAdmin` FOUND; `Algorithm.*time-margin` FOUND; `UseWebSockets` FOUND.
