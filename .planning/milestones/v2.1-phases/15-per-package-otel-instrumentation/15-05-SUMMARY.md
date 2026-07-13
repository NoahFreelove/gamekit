---
phase: 15-per-package-otel-instrumentation
plan: "05"
subsystem: telemetry
tags: [otel, metrics, tracing, lobby, signalr, obs-05, obs-06, pii-guard, connected-clients]
depends_on: ["15-01"]
provides:
  - LobbyMeter (internal static): MeterName=GameKit.Lobby, ConnectedClients ObservableGauge, MessagesSent/ReadyCheckStarted/ReadyCheckCompleted counters, Init(tracker)
  - LobbyActivitySource (public static): SourceName=GameKit.Lobby, StartReadyCheckActivity(parentContext) parented/unparented
  - LobbyConnectionTracker (singleton): Interlocked/Volatile counter backing ConnectedClients gauge
  - LobbyMeterInitService IHostedService — calls Init at startup (mirrors MatchmakingMeterInitService pattern)
  - LobbyPiiTagKeyTests (complete — exercises all 4 lobby instruments, asserts only check.result tag)
  - LobbyMetricsTests (4 behavior assertions: ConnectedClients gauge, MessagesSent, ReadyCheckStarted, ReadyCheckCompleted)
  - LobbyMeterCollection CollectionDefinition — serializes MeterListener tests
affects:
  - src/GameKit.Lobby/Telemetry/LobbyMeter.cs
  - src/GameKit.Lobby/Telemetry/LobbyActivitySource.cs
  - src/GameKit.Lobby/Telemetry/LobbyConnectionTracker.cs
  - src/GameKit.Lobby/Hubs/LobbyHub.cs
  - src/GameKit.Lobby/Services/LobbyService.cs
  - src/GameKit.Lobby/Services/ILobbyService.cs
  - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
  - tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyPiiTagKeyTests.cs
  - tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyMetricsTests.cs
  - tests/GameKit.Lobby.Integration.Tests/CollectionDefinitions.cs
tech_stack:
  added: []
  patterns:
    - LobbyConnectionTracker singleton Interlocked/Volatile counter backing ObservableGauge
    - LobbyMeterInitService IHostedService for deferred DI resolution (mirrors MatchmakingMeterInitService)
    - Hub constructor injection of singleton tracker (not static singleton accessor)
    - Optional ActivityContext param on service interface (non-breaking, existing 3-arg callers compile)
    - LobbyMeterCollection CollectionDefinition for serializing static-meter concurrent test isolation
key_files:
  created:
    - src/GameKit.Lobby/Telemetry/LobbyMeter.cs
    - src/GameKit.Lobby/Telemetry/LobbyActivitySource.cs
    - src/GameKit.Lobby/Telemetry/LobbyConnectionTracker.cs
    - tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyMetricsTests.cs
  modified:
    - src/GameKit.Lobby/Hubs/LobbyHub.cs
    - src/GameKit.Lobby/Services/LobbyService.cs
    - src/GameKit.Lobby/Services/ILobbyService.cs
    - src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs
    - tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyPiiTagKeyTests.cs
    - tests/GameKit.Lobby.Integration.Tests/CollectionDefinitions.cs
decisions:
  - "LobbyConnectionTracker injected into LobbyHub via constructor (not static accessor) — consistent with DI-first pattern; constructor injection verifiable in tests"
  - "ReadyCheckCompleted counter fired in LobbyService.MarkReadyAsync post-tx (not in LobbyHub) — service layer is the authoritative all-ready gate; hub delegates to service"
  - "ReadyCheckStarted counter placed at JoinLobbyAsync Open→ReadyChecking transition only — no separate seeded-ReadyChecking path exists in v1 code; TODO comment documents expected future pairing"
  - "ActivityContext optional param added to ILobbyService.MarkReadyAsync with default=default so all existing 3-arg callers compile without change"
  - "LobbyMeterCollection CollectionDefinition added to CollectionDefinitions.cs (not a separate file) — keeps all Lobby xUnit collection markers in one place per existing pattern"
  - "ReadyCheck span opened in LobbyService.MarkReadyAsync wrapping Phase 1+2 — hub is the trace parent via Activity.Current captured server-side before the awaited call"
metrics:
  duration: 9min
  completed: 2026-06-22T21:12:50Z
  tasks_completed: 4
  files_changed: 10
status: complete
---

# Phase 15 Plan 05: Lobby OBS-05 Telemetry Summary

**One-liner:** Greenfield `Telemetry/` folder for GameKit.Lobby adds LobbyMeter (ConnectedClients ObservableGauge + 3 counters), LobbyActivitySource (ReadyCheck span with hub-captured parent), and LobbyConnectionTracker (Interlocked/Volatile singleton); hub + service wired at lifecycle sites; two green test files; Plan-01 reflection Facts un-REDded.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Create greenfield Lobby Telemetry classes (LobbyMeter, LobbyActivitySource, LobbyConnectionTracker) | 3cb9179 | `LobbyMeter.cs`, `LobbyActivitySource.cs`, `LobbyConnectionTracker.cs` |
| 2 | Instrument LobbyHub at connection/message lifecycle sites | 9a96613 | `LobbyHub.cs` |
| 3 | Instrument LobbyService — thread parent context, open ReadyCheck span, fire ready-check counters | 78556c7 | `LobbyService.cs`, `ILobbyService.cs` |
| 4 | Register LobbyConnectionTracker + LobbyMeter.Init in AddLobby; complete PII + metrics tests | 6b5e021 | `LobbyBuilderExtensions.cs`, `LobbyPiiTagKeyTests.cs`, `LobbyMetricsTests.cs`, `CollectionDefinitions.cs` |

## Verification

- `dotnet build src/GameKit.Lobby -p:NuGetAudit=false`: 0 errors, 0 warnings — GK0001 PII analyzer passes (no forbidden tag keys in any lobby instrument)
- `dotnet test tests/GameKit.Lobby.Integration.Tests -p:NuGetAudit=false`: 25 passed, 0 failed — hub-auth, ready-check, backplane, and all new telemetry tests green
- `dotnet test tests/GameKit.Core.Tests --filter GameKitTelemetryConstantsTests`: 23 passed, 0 failed — the 3 Lobby reflection Facts that were RED (Wave-0 gate from Plan 01) are now GREEN (`LobbyActivitySource_SourceName_Equals_GameKitTelemetry_LobbySourceName`, `LobbyMeter_MeterName_Equals_GameKitTelemetry_LobbyMeterName`)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] LobbyHub MarkReadyAsync 4-arg call required ILobbyService interface change in same task**
- **Found during:** Task 2 (hub instrumentation) compilation
- **Issue:** Task 2 calls `_lobby.MarkReadyAsync(lobbyId, playerId, ct, callerContext)` — 4 args — but Task 3 was planned to add the interface param in a separate commit. The intermediate state produces CS1501 (no 4-arg overload). A separate Task 2 commit without the interface change cannot compile.
- **Fix:** Applied the `ILobbyService.MarkReadyAsync` optional `ActivityContext` param change together with the hub changes (Tasks 2+3 remain logically distinct commits but the interface change landed with Task 3). Task 2 and Task 3 committed separately after Task 3's changes made the build green. Zero impact on existing callers.
- **Files modified:** `ILobbyService.cs`, `LobbyService.cs`
- **Commit:** 78556c7

**2. [Rule 1 - Bug] Volatile.Read(ref int) cref unresolvable in XML doc**
- **Found during:** Task 1 build
- **Issue:** `<see cref="Volatile.Read(ref int)"/>` produces CS1574 (cref could not be resolved) — the generic ref-overload is not directly referenceable in cref syntax.
- **Fix:** Changed to `<c>Volatile.Read</c>` (code-formatted prose instead of cref) in LobbyConnectionTracker.cs
- **Files modified:** `LobbyConnectionTracker.cs`
- **Commit:** 3cb9179

## Key Decisions Made

1. **LobbyConnectionTracker as constructor-injected dependency (not static singleton):** The PATTERNS.md showed `LobbyConnectionTracker.Instance.Increment()` but the task spec says "inject LobbyConnectionTracker (add ctor param + field + ArgumentNullException guard)." Constructor injection chosen for testability and DI consistency with all other GameKit hub/service deps.

2. **ReadyCheckCompleted in LobbyService (not LobbyHub):** The plan's task 3 spec placed `ReadyCheckCompleted` at the `allReadyTriggered` site in `LobbyService.MarkReadyAsync` — the authoritative all-ready gate. The hub simply delegates and passes the parent context. This is correct: the service runs in a SERIALIZABLE transaction and is the only code that knows the atomic outcome.

3. **ReadyCheckStarted counter location:** Placed at the `Open→ReadyChecking` transition in `JoinLobbyAsync` (fill-to-MaxMembers path). No separate seeded-ReadyChecking path was found in v1 code (integration tests seed via `JoinLobbyAsync`, not a bypass). A `// TODO` comment documents that when a direct-ReadyChecking create path lands, it should also fire `ReadyCheckStarted`.

4. **ReadyCheck span scope:** The `using var readyActivity = LobbyActivitySource.StartReadyCheckActivity(parentContext)` wraps both Phase 1 (SERIALIZABLE tx) and Phase 2 (matchmaking submission). This gives the full ready-check operation—including the matchmaking enqueue—a single unified span in the trace.

5. **LobbyMeterCollection serialization:** Added to `CollectionDefinitions.cs` (not a separate file) — keeps all Lobby xUnit collection markers co-located. Mirrors the `MatchmakingMeterCollection` fix from Plan 15-02 (commit 5737385) that solved the same concurrent MeterListener contamination issue.

6. **No `timeout`/`cancelled` result values:** Only `all_ready` has a real v1 transition site in `LobbyService.MarkReadyAsync`. The plan explicitly says "only wire result values that have a real transition site — document others with a TODO." A `// TODO` comment in LobbyService marks where timeout/cancelled would land.

## Threat Surface Scan

All additions are in-process OTel metric/span emission — no new network endpoints, auth paths, file access patterns, or schema changes.

| Flag | File | Description |
|------|------|-------------|
| None | — | No new threat surface introduced |

## Known Stubs

| Stub | File | Reason |
|------|------|--------|
| `// TODO: add "timeout" and "cancelled" when timeout/cancel state machine lands` | `LobbyService.cs` | No v1 timeout/cancel state transition exists; only `all_ready` is wired per plan spec |

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `src/GameKit.Lobby/Telemetry/LobbyMeter.cs` exists | FOUND |
| `src/GameKit.Lobby/Telemetry/LobbyActivitySource.cs` exists | FOUND |
| `src/GameKit.Lobby/Telemetry/LobbyConnectionTracker.cs` exists | FOUND |
| `tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyPiiTagKeyTests.cs` — full impl | FOUND |
| `tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyMetricsTests.cs` exists | FOUND |
| Commit 3cb9179 (Task 1) | FOUND |
| Commit 9a96613 (Task 2) | FOUND |
| Commit 78556c7 (Task 3) | FOUND |
| Commit 6b5e021 (Task 4) | FOUND |
| Full lobby integration suite: 25 passed, 0 failed | PASSED |
| Core reflection tests: 23 passed, 0 failed (Lobby Facts now green) | PASSED |
