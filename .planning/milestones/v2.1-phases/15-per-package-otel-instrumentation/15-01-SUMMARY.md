---
phase: 15-per-package-otel-instrumentation
plan: "01"
subsystem: telemetry
tags: [otel, constants, test-scaffolding, wave-0, pii-guard]
depends_on: []
provides:
  - GameKitTelemetry.LobbySourceName
  - GameKitTelemetry.RankingsMeterName
  - GameKitTelemetry.LobbyMeterName
  - GameKitTelemetry.AttrCheckResult
  - Wave-0 PII tag-key test stubs (Matchmaking, Rankings, Lobby)
  - Wave-0 W3C propagation test stubs (Matchmaking)
affects:
  - tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs
  - tests/GameKit.Matchmaking.Tests/Telemetry/
  - tests/GameKit.Rankings.Tests/Telemetry/
  - tests/GameKit.Lobby.Integration.Tests/Telemetry/
tech_stack:
  added: []
  patterns:
    - MeterListener PII tag-key test pattern (TicketEventChannelDropTests analog)
    - Assembly.LoadFrom probe-up-5-parents reflection enforcement pattern
    - Fact(Skip=...) Wave-0 stub pattern for future-plan contracts
key_files:
  created:
    - tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs
    - tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs
    - tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs
    - tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyPiiTagKeyTests.cs
  modified:
    - src/GameKit.Core/Telemetry/GameKitTelemetry.cs
    - tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs
decisions:
  - "Lobby PII test placed in GameKit.Lobby.Integration.Tests (not a new GameKit.Lobby.Tests project) — InternalsVisibleTo grant already present"
  - "Assert.DoesNotContain (xUnit2029) used instead of Assert.Empty(Where(...)) — xUnit analyzer requires this form"
  - "Three reflection Facts are intentionally RED — Wave-0 gate for Plans 04 and 05"
metrics:
  duration: 8min
  completed: 2026-06-22T20:33:39Z
  tasks_completed: 2
  files_changed: 6
status: complete
---

# Phase 15 Plan 01: Telemetry Foundation + Wave-0 Test Scaffolding Summary

**One-liner:** Four Phase-15 GameKitTelemetry constants (LobbySourceName, RankingsMeterName, LobbyMeterName, AttrCheckResult) added as single source of truth; reflection enforcement test extended with LoadRankingsAssembly/LoadLobbyAssembly helpers and three intentionally-RED gate Facts; four Wave-0 test stubs created across Matchmaking/Rankings/Lobby.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Add Phase-15 telemetry constants to GameKitTelemetry + extend reflection test | 7110b67 | `GameKitTelemetry.cs`, `GameKitTelemetryConstantsTests.cs` |
| 2 | Scaffold Wave-0 PII tag-key + W3C propagation test files | f5378b9 | 4 new test files |

## Verification

- `dotnet test tests/GameKit.Core.Tests --filter GameKitTelemetryConstantsTests`: 20 pass, 3 fail (RED-pending-04/05 — expected Wave-0 state)
- `dotnet build` GameKit.Matchmaking.Tests, GameKit.Rankings.Tests, GameKit.Lobby.Integration.Tests: all succeed (0 errors)
- MatchmakingPiiTagKeyTests: 1 fact green (exercises DroppedEvents counter)
- W3CTracePropagationTests: 3 facts Skip-marked (Plan 03 gate)
- RankingsPiiTagKeyTests: 1 fact trivially green (no RankingsMeter yet, empty-set passes)
- LobbyPiiTagKeyTests: 1 fact trivially green (no LobbyMeter yet, empty-set passes)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] xUnit2029: Assert.Empty on filtered collection**
- **Found during:** Task 2 build
- **Issue:** `Assert.Empty(emittedTagKeys.Where(k => ForbiddenKeys.Contains(k)))` triggers xUnit analyzer error xUnit2029 — projects have WarningsAsErrors and treat it as a hard error
- **Fix:** Replaced with `Assert.DoesNotContain(emittedTagKeys, k => ForbiddenKeys.Contains(k))` across all three PII test files
- **Files modified:** `MatchmakingPiiTagKeyTests.cs`, `RankingsPiiTagKeyTests.cs`, `LobbyPiiTagKeyTests.cs`
- **Commit:** f5378b9

## Key Decisions Made

1. **Lobby PII test placement:** In `GameKit.Lobby.Integration.Tests/Telemetry/` (plan specified) — the InternalsVisibleTo grant for `GameKit.Lobby.Integration.Tests` already exists in `GameKit.Lobby/AssemblyInfo.cs` line 9, satisfying the internal-type access requirement.

2. **Wave-0 RED acceptance:** The three reflection Facts (`RankingsMeter_MeterName_Equals_GameKitTelemetry_RankingsMeterName`, `LobbyActivitySource_SourceName_Equals_GameKitTelemetry_LobbySourceName`, `LobbyMeter_MeterName_Equals_GameKitTelemetry_LobbyMeterName`) fail today because the per-package classes do not exist. This is the intended deterministic gate: they turn green automatically when Plans 04 and 05 land without requiring changes to this file.

3. **No new NuGet packages:** All four test files use only in-box `System.Diagnostics.Metrics.MeterListener` and BCL types. Zero new dependencies.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes. All additions are in-process test infrastructure and compile-time constants. No threat flags to report.

## Known Stubs

| Stub | File | Reason |
|------|------|--------|
| `// TODO(15-02): add new instrument Add/Record calls` | MatchmakingPiiTagKeyTests.cs | Plan 02 ships new MatchmakingMeter instruments; stub exercises only DroppedEvents today |
| `// TODO(15-04): reference RankingsMeter once it ships` | RankingsPiiTagKeyTests.cs | RankingsMeter ships in Plan 04; stub passes trivially with empty-set assertion |
| `// TODO(15-05): reference LobbyMeter once it ships` | LobbyPiiTagKeyTests.cs | LobbyMeter ships in Plan 05; stub passes trivially with empty-set assertion |
| `[Fact(Skip = "15-03: implement once ...")]` (×3) | W3CTracePropagationTests.cs | OBS-06 W3C propagation contract documented for Plan 03 to implement |

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` exists | FOUND |
| `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` exists | FOUND |
| `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs` exists | FOUND |
| `tests/GameKit.Matchmaking.Tests/Telemetry/W3CTracePropagationTests.cs` exists | FOUND |
| `tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs` exists | FOUND |
| `tests/GameKit.Lobby.Integration.Tests/Telemetry/LobbyPiiTagKeyTests.cs` exists | FOUND |
| Commit 7110b67 (Task 1) | FOUND |
| Commit f5378b9 (Task 2) | FOUND |
