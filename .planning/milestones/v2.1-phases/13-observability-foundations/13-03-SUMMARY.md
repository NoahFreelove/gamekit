---
phase: 13-observability-foundations
plan: 03
subsystem: telemetry
tags: [observability, opentelemetry, rankings, matchmaking, refactor]
dependency_graph:
  requires: [13-01, 13-02]
  provides: [RankingsActivitySource.cs, normalized Matchmaking span tag keys]
  affects: [GameKit.Rankings, GameKit.Matchmaking]
tech_stack:
  added: []
  patterns:
    - "RankingsActivitySource mirrors MatchmakingActivitySource (static class, SourceName const, internal Source, typed StartDrainLadderActivity helper)"
    - "All span tag keys route through GameKitTelemetry constants for cross-cutting keys (AttrLadderId, AttrPoolName, AttrLadderName, AttrResult, AttrErrorType)"
    - "Per-package keys (candidates.evaluated, matches.formed, phase.*) use lowercase-dotted OTel convention as inline literals; Phase 15 promotes to consts"
key_files:
  created:
    - src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs
    - tests/GameKit.Rankings.Tests/Telemetry/RankingsActivitySourceTests.cs
    - tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingTagNamingTests.cs
  modified:
    - src/GameKit.Rankings/Services/RankingsTickerService.cs
    - src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs
    - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
decisions:
  - "Test assertions use SetTag(\"key\") pattern (not bare string presence) to avoid false positives from Redis hash field reads, XML doc attributes, and C# parameter names that share camelCase identifiers with the old tag keys"
  - "MatchmakingActivitySource.StartPoolActivity() parameters renamed from ladderId/poolName to ladderIdValue/poolNameValue to prevent quoted-string collisions in source-assert tests while maintaining public API shape"
metrics:
  duration: ~35 minutes
  completed: "2026-06-14T22:38:25Z"
  tasks_completed: 2
  files_changed: 6
---

# Phase 13 Plan 03: Telemetry Consistency — RankingsActivitySource + Matchmaking Tag Normalization Summary

**One-liner:** Extract Rankings inline ActivitySource to canonical `RankingsActivitySource.cs` and normalize all Matchmaking camelCase span tag keys to OTel-compliant lowercase-dotted, routing cross-cutting keys through `GameKitTelemetry` constants.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| RED 1 | RankingsActivitySource criterion #5 tests (RED) | cce712b | tests/GameKit.Rankings.Tests/Telemetry/RankingsActivitySourceTests.cs |
| GREEN 1 | Extract RankingsActivitySource + refactor RankingsTickerService | f36fe03 | src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs, src/GameKit.Rankings/Services/RankingsTickerService.cs |
| RED 2 | Matchmaking tag-naming criterion #4 tests (RED) | 4c7440f | tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingTagNamingTests.cs |
| GREEN 2 | Normalize Matchmaking camelCase span tags to lowercase-dotted | 6bb566b | src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs, src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs, tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingTagNamingTests.cs |

## What Was Built

### Task 1: RankingsActivitySource Extraction (criterion #5)

Created `src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs` as a `public static class` in `GameKit.Rankings.Telemetry` namespace, mirroring `MatchmakingActivitySource`:
- `SourceName = GameKitTelemetry.RankingsTickerSourceName` (single source of truth, criterion #5 link)
- `Source = new(SourceName, GameKitTelemetry.Version)` (version constant, not `"1.0.0"` literal)
- `StartDrainLadderActivity()` typed helper

Refactored `RankingsTickerService.cs`:
- Removed inline `_activitySource` field (was `new("GameKit.Rankings.Ticker", "1.0.0")`)
- `using GameKit.Rankings.Telemetry` and `using GameKit.Core.Telemetry` added
- `_activitySource.StartActivity("DrainLadder")` → `RankingsActivitySource.StartDrainLadderActivity()`
- `SetTag("ladder.id", ...)` → `SetTag(GameKitTelemetry.AttrLadderId, ...)`
- `SetTag("ladder.name", ...)` → `SetTag(GameKitTelemetry.AttrLadderName, ...)`
- `SetTag("result", ...)` → `SetTag(GameKitTelemetry.AttrResult, ...)`
- `SetTag("error", ...)` → `SetTag(GameKitTelemetry.AttrErrorType, ...)` (D-04 rename: error → error.type)

Tests (`RankingsActivitySourceTests.cs`, 3 tests):
- `SourceName_EqualsGameKitTelemetry_RankingsTickerSourceName` — reflection equality check
- `Source_IsNonNull_AndNameMatchesExpected` — ActivitySource.Name == "GameKit.Rankings.Ticker"
- `RankingsTickerService_DoesNotContain_InlineActivitySourceDeclaration` — source assert: no `new ActivitySource(`

### Task 2: Matchmaking Tag Normalization (criterion #4, D-03 rename map)

**`MatchmakingActivitySource.cs`:**
- Added `using GameKit.Core.Telemetry`
- `Source` version: `"1.0.0"` → `GameKitTelemetry.Version`
- `SetTag("ladderId", ...)` → `SetTag(GameKitTelemetry.AttrLadderId, ...)`
- `SetTag("poolName", ...)` → `SetTag(GameKitTelemetry.AttrPoolName, ...)`
- XML doc updated to reference `ladder.id` / `pool.name`
- Parameters renamed `ladderId → ladderIdValue`, `poolName → poolNameValue` (see Decisions)

**`MatchmakerTickerService.cs` (D-03 rename map applied exactly):**
- `candidatesEvaluated` → `candidates.evaluated`
- `phase.hashFanoutMs` → `phase.hash_fanout_ms`
- `budgetBail` → `budget.bail`
- `matchCapBail` → `match_cap.bail`
- `matchesFormed` → `matches.formed`
- `phase.matchLoopMs` → `phase.match_loop_ms`
- `phase.totalMs` → `phase.total_ms`
- `paused` / `reaped` unchanged (single-word, already compliant)

Redis hash field reads (`case "ladderId":` in `BuildQueuedPartyFromHash`) were correctly left unchanged — these are wire-format field names, not OTel span tag keys.

Tests (`MatchmakingTagNamingTests.cs`, 12 tests):
- 9× `MatchmakingSource_DoesNotContain_OldCamelCaseTagKey` — source assert via `SetTag("key"` pattern
- `MatchmakingActivitySource_References_GameKitTelemetry_AttrLadderId`
- `MatchmakingActivitySource_References_GameKitTelemetry_AttrPoolName`
- `MatchmakingActivitySource_SourceVersion_EqualsGameKitTelemetry_Version`

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet test tests/GameKit.Rankings.Tests/` (18 tests) | PASS |
| `dotnet test tests/GameKit.Matchmaking.Tests/ -p:NuGetAudit=false` (103 tests) | PASS |
| `dotnet build GameKit.sln -p:NuGetAudit=false` (solution-wide) | PASS — 0 warnings, 0 errors |
| GK0001 / GK0002 analyzer | No violations on any renamed key |
| `grep -c 'new ActivitySource(' src/GameKit.Rankings/Services/RankingsTickerService.cs` | 0 |
| `grep -c 'GameKitTelemetry.RankingsTickerSourceName' src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs` | 2 |
| `grep -cE 'SetTag\("(ladderId\|poolName\|...)' Matchmaking files` | 0 |

**Note on Matchmaking build verification:** `dotnet test` and `dotnet build` for `GameKit.Matchmaking` were run with `-p:NuGetAudit=false` due to the pre-existing `NU1903` advisory on `MessagePack 2.5.187` (GHSA-hv8m-jj95-wg3x). This blocker predates Phase 13 and is tracked separately. No audit-config changes were committed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Test Precision] Updated source-assert test pattern from bare string to SetTag(key) pattern**
- **Found during:** Task 2 GREEN phase
- **Issue:** The initial test used `Assert.DoesNotContain("ladderId", source)` which matched Redis hash field reads (`case "ladderId":` in `BuildQueuedPartyFromHash`) and C# parameter names (`<param name="ladderId">`), causing false failures even after the tag keys were correctly renamed
- **Fix:** Changed test assertions to check for `SetTag("ladderId"` pattern to scope the check to actual span tag callsites. Also renamed `StartPoolActivity` parameters from `ladderId/poolName` to `ladderIdValue/poolNameValue` to prevent XML doc `<param name="ladderId">` from triggering the assertion
- **Files modified:** `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingTagNamingTests.cs`, `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs`
- **Commit:** 6bb566b

### Worktree Base Reset

The worktree was initialized at commit `5d5102d` (Phase 13 kickoff, before plans 13-01/13-02 were merged). The `<worktree_branch_check>` detected that `merge-base HEAD 59a2cf10...` ≠ `59a2cf10...` and correctly ran `git reset --hard 59a2cf10cb80...` to include the prerequisite Plan 13-01 (PII analyzer) and Plan 13-02 (GameKitTelemetry constants) commits before implementation began.

## TDD Gate Compliance

Both tasks followed full RED/GREEN cycle:

| Task | RED commit | GREEN commit |
|------|-----------|-------------|
| Task 1 (RankingsActivitySource) | cce712b (3 tests fail: CS0234 type not found) | f36fe03 (3 tests pass) |
| Task 2 (Matchmaking tag normalization) | 4c7440f (11/12 tests fail) | 6bb566b (12/12 tests pass) |

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes introduced. Changes are confined to in-process ActivitySource span tag keys and a new static utility class. The GK0001 PII analyzer confirmed all renamed keys are PII-free (no denylist tokens in `ladder.id`, `pool.name`, `error.type`, `candidates.evaluated`, etc.).

## Self-Check: PASSED

| Item | Status |
|------|--------|
| `src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs` | FOUND |
| `tests/GameKit.Rankings.Tests/Telemetry/RankingsActivitySourceTests.cs` | FOUND |
| `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingTagNamingTests.cs` | FOUND |
| `.planning/phases/13-observability-foundations/13-03-SUMMARY.md` | FOUND |
| Commit cce712b (RED test Rankings) | FOUND |
| Commit f36fe03 (GREEN Rankings extraction) | FOUND |
| Commit 4c7440f (RED test Matchmaking) | FOUND |
| Commit 6bb566b (GREEN Matchmaking normalization) | FOUND |
