---
phase: 15-per-package-otel-instrumentation
plan: "04"
subsystem: telemetry
tags: [otel, metrics, tracing, rankings, decay, pii-guard, obs-04, obs-06]
depends_on: ["15-01"]
provides:
  - RankingsMeter (GameKit.Rankings) with rankings.decay.duration histogram + rankings.decay.rows_updated counter
  - RankDecayBackgroundService instrumented with Stopwatch (post-lease, Pitfall-5 compliant) + RankDecay span
  - RankingsPiiTagKeyTests fully exercising both instruments (criterion #1 satisfied for Rankings)
  - RankingsMetricsTests asserting DecayDuration + DecayRowsUpdated instrument contract
  - Plan-01 reflection Fact RankingsMeter_MeterName_Equals_GameKitTelemetry_RankingsMeterName now GREEN
affects:
  - src/GameKit.Rankings/Telemetry/RankingsMeter.cs
  - src/GameKit.Rankings/Services/RankDecayBackgroundService.cs
  - tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs
  - tests/GameKit.Rankings.Tests/Telemetry/RankingsMetricsTests.cs
tech_stack:
  added: []
  patterns:
    - Internal static Meter pattern (mirrors MatchmakingMeter)
    - Stopwatch post-lease placement (Pitfall-5 compliant decay duration)
    - Fresh-root ActivitySource span (background job, no inbound traceparent — T-15-04-TRACE)
    - MeterListener PII tag-key test pattern (TicketEventChannelDropTests analog)
    - [CollectionDefinition(DisableParallelization=true)] for static-instrument test isolation
key_files:
  created:
    - src/GameKit.Rankings/Telemetry/RankingsMeter.cs
    - tests/GameKit.Rankings.Tests/Telemetry/RankingsMetricsTests.cs
  modified:
    - src/GameKit.Rankings/Services/RankDecayBackgroundService.cs
    - tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs
decisions:
  - "RankingsMetrics xUnit Collection serializes both PII + Metrics test classes — static MeterListener cross-contamination (same pattern as 15-02 MatchmakingMeterTests fix, see deviation below)"
  - "Sentinel values (999001.0 / 999002L) used in metrics assertions rather than exact-value assertions to handle serialized-but-sequential ordering within the collection"
  - "DecayRowsUpdated.Add(candidates.Count) called directly inside DecayLadderAsync after SaveChangesAsync (not returned as int from the method) — avoids changing DecayLadderAsync return type, keeps the counter co-located with the save site"
  - "RankDecay span uses RankingsActivitySource.Source.StartActivity (no parent context — fresh root per T-15-04-TRACE / RESEARCH §Rank-decay)"
metrics:
  duration: 6min
  completed: 2026-06-22T21:00:39Z
  tasks_completed: 2
  files_changed: 4
status: complete
---

# Phase 15 Plan 04: Rankings Decay Metrics + Trace Summary

**One-liner:** RankingsMeter (GameKit.Rankings) with decay.duration histogram and decay.rows_updated counter, RankDecayBackgroundService instrumented with post-lease Stopwatch + fresh-root RankDecay span, Rankings PII test fully wired (criterion #1), and two new RankingsMetricsTests — un-REDs the Plan-01 RankingsMeter reflection Fact.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Create RankingsMeter | c058505 | `src/GameKit.Rankings/Telemetry/RankingsMeter.cs` |
| 2 | Instrument RankDecayBackgroundService + complete Rankings PII + metrics tests | b452ddb | `RankDecayBackgroundService.cs`, `RankingsPiiTagKeyTests.cs`, `RankingsMetricsTests.cs` |
| 2a | Fix: serialize RankingsMetrics test collection (Rule 1 deviation) | 5f2de99 | `RankingsPiiTagKeyTests.cs`, `RankingsMetricsTests.cs` |

## Verification

- `dotnet build src/GameKit.Rankings -p:NuGetAudit=false`: 0 errors, 0 warnings
- `dotnet test tests/GameKit.Rankings.Tests -p:NuGetAudit=false`: 24/24 pass (stable across 3 sequential runs)
- `dotnet test tests/GameKit.Core.Tests --filter "RankingsMeter_MeterName"`: PASS (reflection Fact un-REDed)
- `dotnet test tests/GameKit.Core.Tests --filter "GameKitTelemetryConstantsTests"`: 21/23 pass, 2 remaining RED are Lobby Facts (Plan 05 gate — expected)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] MeterListener cross-contamination between parallel test classes**
- **Found during:** Task 2 verification (full suite run after commit)
- **Issue:** xUnit runs test classes in parallel by default. `RankingsPiiTagKeyTests` and `RankingsMetricsTests` both exercise the same static `RankingsMeter` instruments via `MeterListener`. Callbacks fire on the calling thread of `Record()`/`Add()`, so a `Record(1.0)` call in the PII test landed in the Metrics test's listener before the Metrics test called `Record(sentinel)`. Result: `capturedValues == [1.0]`, sentinel missing — intermittent `Assert.Contains` failure.
- **Fix:** Added `[CollectionDefinition("RankingsMetrics", DisableParallelization = true)]` and `[Collection("RankingsMetrics")]` to both test classes. Matches the identical fix applied in Plan 02 to `MatchmakingMeterTests` (same root cause, same pattern — see 15-02-SUMMARY deviation #1).
- **Files modified:** `RankingsPiiTagKeyTests.cs`, `RankingsMetricsTests.cs`
- **Commit:** 5f2de99

## Key Decisions Made

1. **Stopwatch placement (Pitfall 5):** `Stopwatch.StartNew()` called immediately after `TryAcquireLeaseAsync` returns `true`, before any DB work. `RankingsMeter.DecayDuration.Record()` called in `finally` before `ReleaseLeaseAsync`. This records decay work time, not Redis lock-wait contention (T-15-04-TIME mitigation).

2. **Fresh-root RankDecay span (T-15-04-TRACE):** `RankingsActivitySource.Source.StartActivity("RankDecay")` with no parent context argument — background job has no inbound traceparent, so a fresh root span is correct. Prevents trace-context injection via the decay path.

3. **DecayRowsUpdated placement:** `RankingsMeter.DecayRowsUpdated.Add(candidates.Count)` inside `DecayLadderAsync` immediately after `SaveChangesAsync`. This co-locates the counter with the save site, avoids changing the return type of `DecayLadderAsync`, and ensures the count is always the actual persisted row count.

4. **Sentinel values in metrics tests:** `999001.0` and `999002L` used as test sentinel values. Avoids false-positive failures from other tests' `Record(1.0)` calls captured during the serialized window.

## Threat Surface Scan

No new network endpoints, auth paths, or schema changes. All additions are in-process telemetry (ActivitySource span + MeterListener instruments). Tag keys checked against GK0001 PII forbidden set — no player identifiers emitted by any rankings instrument.

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `src/GameKit.Rankings/Telemetry/RankingsMeter.cs` exists | FOUND |
| `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` instrumented | FOUND |
| `tests/GameKit.Rankings.Tests/Telemetry/RankingsMetricsTests.cs` exists | FOUND |
| `tests/GameKit.Rankings.Tests/Telemetry/RankingsPiiTagKeyTests.cs` updated | FOUND |
| Commit c058505 (Task 1) | FOUND |
| Commit b452ddb (Task 2) | FOUND |
| Commit 5f2de99 (deviation fix) | FOUND |
| `dotnet test tests/GameKit.Rankings.Tests` 24/24 | PASS |
| Reflection Fact RankingsMeter_MeterName green | PASS |
