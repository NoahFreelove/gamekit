---
phase: 15-per-package-otel-instrumentation
plan: "02"
subsystem: telemetry
tags: [otel, metrics, matchmaking, obs-04, pii-guard, queue-depth]
depends_on: ["15-01"]
provides:
  - MatchmakingMeter.TickerLag histogram (matchmaking.ticker.lag, ms)
  - MatchmakingMeter.PoolSweepDuration histogram (matchmaking.pool_sweep.duration, ms, tag ladder.id)
  - MatchmakingMeter.QueueDepth ObservableGauge (matchmaking.queue.depth, no unit, tags pool.name + ladder.id)
  - MatchmakingMeter.LockAcquisitionFailures counter (matchmaking.leader_lock.acquisition_failures)
  - MatchmakingMeter.MatchesFormed counter (matchmaking.matches.formed, tag ladder.id)
  - MatchmakingMeter.BudgetBail counter (matchmaking.budget_bail, tag ladder.id)
  - MatchmakingMeter.LeaseAcquired counter (matchmaking.lease.acquired)
  - MatchmakingMeter.LeaseLost counter (matchmaking.lease.lost)
  - MatchmakingMeter.Init(IConnectionMultiplexer) — wires QueueDepth Redis reference
  - MatchmakingMeterInitService IHostedService — calls Init at startup
  - MatchmakingPiiTagKeyTests (complete — exercises all 8 new instruments)
  - MatchmakingMetricsTests (7 behavior assertions)
  - MatchmakingMeterCollection [CollectionDefinition] — serializes meter-based tests
affects:
  - src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs
  - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
  - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs
  - tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs
  - tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingMetricsTests.cs
  - tests/GameKit.Matchmaking.Tests/MatchmakingMeterCollection.cs
  - tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs
tech_stack:
  added: []
  patterns:
    - ObservableGauge with static Init + Redis SCAN (synchronous SortedSetLength)
    - MatchmakingMeterInitService IHostedService pattern for deferred DI resolution
    - xUnit CollectionDefinition for serializing static-meter concurrent test isolation
key_files:
  created:
    - tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingMetricsTests.cs
    - tests/GameKit.Matchmaking.Tests/MatchmakingMeterCollection.cs
  modified:
    - src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs
    - src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs
    - tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs
    - tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs
decisions:
  - "QueueDepth ObservableGauge uses Redis SCAN (mm:queue:*) rather than MatchmakingLadderConfig list — MatchmakingLadderConfig has Name but not Guid; the Guid comes from Postgres and is embedded in queue key format mm:queue:{guid}:{pool}"
  - "BudgetBail instrument name is matchmaking.budget_bail (no ticker. segment) to match existing dashboard query gamekit_matchmaking_budget_bail_total — code comment documents the naming choice"
  - "QueueDepth has no unit argument so Prometheus name lacks _tickets suffix and matches gamekit_matchmaking_queue_depth dashboard query"
  - "MatchmakingMeterInitService IHostedService resolves IConnectionMultiplexer lazily (avoids eager Redis connection during ConfigureServices)"
  - "MatchmakingMeterCollection [CollectionDefinition] added to serialize MatchmakingPiiTagKeyTests + MatchmakingMetricsTests + TicketEventChannelDropTests — static meter singletons cause MeterListener cross-contamination under xUnit parallel execution"
metrics:
  duration: 13min
  completed: 2026-06-22T20:51:30Z
  tasks_completed: 2
  files_changed: 7
status: complete
---

# Phase 15 Plan 02: Matchmaking OBS-04 Metrics Summary

**One-liner:** Eight OBS-04 matchmaking instruments added to MatchmakingMeter (ticker-lag/pool-sweep histograms, QueueDepth ObservableGauge via Redis SCAN, leader-lock/lease/match/budget-bail counters); all wired at their semantic sites in MatchmakerTickerService; two test files + a collection serializer added for full PII + behavior coverage.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Add OBS-04 instruments + ObservableGauge Init to MatchmakingMeter | a290dde | `MatchmakingMeter.cs`, `MatchmakingBuilderExtensions.cs` |
| 2 | Emit ticker/lease/pool/match metrics in MatchmakerTickerService + complete PII + metrics tests | a0f54f7 | `MatchmakerTickerService.cs`, `MatchmakingPiiTagKeyTests.cs`, `MatchmakingMetricsTests.cs` |
| fix | Serialize MeterListener tests via MatchmakingMeterCollection | 5737385 | `MatchmakingMeterCollection.cs`, `TicketEventChannelDropTests.cs`, `MatchmakingMetricsTests.cs`, `MatchmakingPiiTagKeyTests.cs` |

## Verification

- `dotnet build src/GameKit.Matchmaking -p:NuGetAudit=false`: 0 errors, 0 warnings — GK0001 analyzer passes (no forbidden tag keys)
- `dotnet test tests/GameKit.Matchmaking.Tests --filter "PiiTagKey|MatchmakingMetrics"`: 9/9 pass
- `dotnet test tests/GameKit.Matchmaking.Tests -p:NuGetAudit=false`: 112 passed, 3 skipped (Plan-03 W3C stubs), 0 failed — stable across 3 consecutive runs

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] QueueDepth ObservableGauge uses Redis SCAN instead of MatchmakingLadderConfig iteration**
- **Found during:** Task 1 implementation
- **Issue:** Plan specified `Init(IDatabase db, IReadOnlyList<MatchmakingLadderConfig> ladders)` and `MatchmakingRedisKeys.Queue(ladder.LadderId, pool)` — but `MatchmakingLadderConfig` has only `Name` (string), not `LadderId` (Guid). Ladder Guids come from Postgres at runtime, not from build-time config.
- **Fix:** ObservableGauge callback uses synchronous `IServer.Keys(pattern: "mm:queue:*")` SCAN (same pattern as `RedisMatchmakingObservability`) + `TryParseQueueKey` to extract the Guid. `Init` signature changed to `Init(IConnectionMultiplexer)` — no ladder list needed.
- **Files modified:** `MatchmakingMeter.cs`, `MatchmakingBuilderExtensions.cs`
- **Commit:** a290dde

**2. [Rule 1 - Bug] Pre-existing xUnit test isolation failure exposed by new MeterListener tests**
- **Found during:** Task 2 full-suite verification
- **Issue:** `TicketEventChannelDropTests.Counter_EmitsWith_PollyExhaustedReason` failed consistently when run alongside the new `MatchmakingPiiTagKeyTests` and `MatchmakingMetricsTests`. Root cause: `MatchmakingMeter.DroppedEvents` is a static singleton; concurrent `MeterListener` callbacks from parallel test classes write to non-thread-safe `List<T>` instances, causing `Assert.Contains` to miss the expected `(42, "polly_exhausted")` measurement (the measurement was written to another test's list or the list was corrupted).
- **Fix:** Added `MatchmakingMeterCollection` with `[CollectionDefinition("MatchmakingMeterTests", DisableParallelization = true)]`; applied `[Collection("MatchmakingMeterTests")]` to all three affected test classes. Tests now run sequentially within the collection — no concurrent listener interference.
- **Files modified:** `MatchmakingMeterCollection.cs` (new), `TicketEventChannelDropTests.cs`, `MatchmakingPiiTagKeyTests.cs`, `MatchmakingMetricsTests.cs`
- **Commit:** 5737385

## Key Decisions Made

1. **QueueDepth ObservableGauge design:** Uses Redis SCAN (`mm:queue:*`) at scrape time rather than iterating ladder configs. Matches `RedisMatchmakingObservability.GetQueueStatsAsync` pattern — the only source of truth for ladder Guids at scrape time is the Redis key itself. Synchronous `IDatabase.SortedSetLength` (not async) avoids thread-pool starvation on the OTel scrape path; bounded by operator pool count (~3–9 keys per scrape, <1ms total on loopback).

2. **BudgetBail name:** `matchmaking.budget_bail` (no `ticker.` segment) — matches the existing Grafana dashboard query `increase(gamekit_matchmaking_budget_bail_total[5m])`. Plan 06 confirms this naming choice; a code comment documents it directly.

3. **QueueDepth unit omitted:** `Meter.CreateObservableGauge<long>(name: "matchmaking.queue.depth", ...)` with no `unit:` argument — Prometheus metric name is then `gamekit_matchmaking_queue_depth` without a unit suffix. The PATTERNS.md showed a `unit: "tickets"` hint but the plan spec said "NO unit argument" for dashboard compatibility; plan wins.

4. **MatchmakingMeterInitService pattern:** Tiny `IHostedService` registered by `AddMatchmaking` that resolves `IConnectionMultiplexer` lazily from DI and calls `MatchmakingMeter.Init(multiplexer)` once at `StartAsync`. This avoids eager Redis connection construction during `ConfigureServices`, consistent with the deferred-resolution approach used by other matchmaking services.

## Threat Surface Scan

No new network endpoints, auth paths, schema changes, or trust boundary crossings. All additions are:
- In-process OTel metric emission (no outbound network)
- Synchronous Redis ZCARD reads at scrape time (bounded, error-safe)
- Test infrastructure (not shipped)

No threat flags to report.

## Known Stubs

None — all instruments are fully implemented and tested. The `QueueDepth` gauge emits measurements when called with a live Redis connection (`_multiplexer` set via `Init`); it correctly yields no measurements when `_multiplexer` is null (test-time default).

## Self-Check: PASSED

| Check | Result |
|-------|--------|
| `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` — 8 new instruments present | FOUND |
| `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` — MatchmakingMeterInitService | FOUND |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` — 7 emission sites | FOUND |
| `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingPiiTagKeyTests.cs` — all instruments exercised | FOUND |
| `tests/GameKit.Matchmaking.Tests/Telemetry/MatchmakingMetricsTests.cs` | FOUND |
| `tests/GameKit.Matchmaking.Tests/MatchmakingMeterCollection.cs` | FOUND |
| Commit a290dde (Task 1) | FOUND |
| Commit a0f54f7 (Task 2) | FOUND |
| Commit 5737385 (Rule-1 fix) | FOUND |
| Full suite: 112 passed, 0 failed, 3 skipped — 3 consecutive runs | PASSED |
