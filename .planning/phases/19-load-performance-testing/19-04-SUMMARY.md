---
phase: 19-load-performance-testing
plan: "04"
subsystem: benchmarks
tags: [performance, benchmarking, baselines, benchmarkdotnet, perf-02]
dependency_graph:
  requires: [19-01]
  provides: [benchmarks/baselines/report-baseline.json, benchmarks/BASELINES.md]
  affects: [PERF-02, PERF-06-gate]
tech_stack:
  added: []
  patterns:
    - BenchmarkDotNet Release-mode baseline capture (IterationCount=15, WarmupCount=5)
    - GameKitModelCacheKeyFactory for EF model-cache isolation in multi-SP benchmark hosts
key_files:
  created:
    - benchmarks/baselines/report-baseline.json
    - benchmarks/BASELINES.md
  modified:
    - tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs
decisions:
  - "Merged per-class BDN JSON files into a single report-baseline.json (Plan 03 gate reads a single file)"
  - "Registered GameKitModelCacheKeyFactory in both migration SP and runtime host to prevent EF model-cache key collision"
metrics:
  duration: "11m 11s"
  completed: "2026-06-23"
  tasks_completed: 2
  tasks_total: 2
  files_created: 2
  files_modified: 1
status: complete
---

# Phase 19 Plan 04: Capture + Commit Benchmark Baselines Summary

Committed the BenchmarkDotNet Release-mode baseline JSON (`benchmarks/baselines/report-baseline.json`) for all 7 hot-path benchmarks, with `benchmarks/BASELINES.md` recording machine spec, .NET version, runner configuration, per-benchmark means, and the 20% regression gate rationale.

## Tasks Completed

| Task | Description | Commit | Status |
|------|-------------|--------|--------|
| 1 | Run full Release benchmark suite + commit baseline JSON | `5fa818f` | Done |
| 2 | Write BASELINES.md with machine spec + per-benchmark means | `3685849` | Done |

## Per-Benchmark Means (from report-baseline.json)

All values are `Statistics.Mean` in nanoseconds from the committed JSON. Human-readable units shown for convenience; the gate reads raw nanoseconds.

| Benchmark            | Mean (ns)       | Mean (human) |
|----------------------|-----------------|--------------|
| `ValidateToken`      | 18,020          | 18.0 µs      |
| `BCryptVerify`       | 202,468,939     | 202.5 ms     |
| `Argon2idVerify`     | 237,691,726     | 237.7 ms     |
| `Apply_2`            | 10,093          | 10.1 µs      |
| `Apply_10`           | 21,872          | 21.9 µs      |
| `Apply_100`          | 191,501         | 191.5 µs     |
| `TicketEnqueueAsync` | 2,959,642       | 2.96 ms      |

## Self-Comparison Gate Result

```
dotnet run --project benchmarks/CompareBaseline -c Release -- \
  benchmarks/baselines/report-baseline.json \
  benchmarks/baselines/report-baseline.json
```

Output:
```
  OK: ValidateToken: 0.018 ms vs baseline 0.018 ms (+0.0%)
  OK: BCryptVerify: 202.469 ms vs baseline 202.469 ms (+0.0%)
  OK: Argon2idVerify: 237.692 ms vs baseline 237.692 ms (+0.0%)
  OK: Apply_2: 0.010 ms vs baseline 0.010 ms (+0.0%)
  OK: Apply_10: 0.022 ms vs baseline 0.022 ms (+0.0%)
  OK: Apply_100: 0.192 ms vs baseline 0.192 ms (+0.0%)
  OK: TicketEnqueueAsync: 2.960 ms vs baseline 2.960 ms (+0.0%)

Benchmark regression gate PASSED — all benchmarks within threshold.
Exit code: 0
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed EF model-cache collision causing TicketEnqueueAsync to fail**

- **Found during:** Task 1 (first full benchmark run)
- **Issue:** The first run captured 6 of 7 benchmarks but `TicketEnqueueAsync` failed immediately after jitting. The `MatchmakingBenchmarkHost` builds a Core-only migration `ServiceProvider` (via `BuildServiceProviderForMigrations`) then starts a full-runtime host. Both call `AddGameKit()` → `AddDbContext<GameKitDbContext>`. EF Core's model cache key is `(GameKitDbContext, GameKitModelCustomizer, false)` — shared between both SPs. The migration SP builds and caches a Core-only model first. When the runtime host's context tries to create a `DbSet<SessionCompleteIdempotency>` (Rankings entity), the cached model doesn't include it, causing `BackgroundServiceExceptionBehavior=StopHost` to kill the host with `Cannot create a DbSet for 'SessionCompleteIdempotency'` and `Cannot create a DbSet for 'DeclineHistory'`.
- **Fix:** Registered `GameKitModelCacheKeyFactory` (existing test-fixture utility documented for exactly this use case) in both the migration SP's `AddDbContext` override and the runtime host's `AddDbContext` override. The factory appends the registered `IModelBuilderExtension` type list to the cache key, producing distinct keys for Core-only and full-runtime models.
- **Files modified:** `tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs`
- **Commit:** `5fa818f`

## Machine Specification (Capture Machine)

| Property | Value |
|----------|-------|
| CPU | 11th Gen Intel Core i7-11700K @ 3.60GHz (8 physical / 16 logical cores) |
| RAM | 30 GiB |
| OS | Linux Ubuntu 26.04 kernel 7.0.0-22-generic x86_64 |
| .NET SDK | 10.0.109 |
| .NET Runtime | 10.0.9 X64 RyuJIT x86-64-v4 |
| BenchmarkDotNet | 0.15.8 |
| BDN Config | IterationCount=15, WarmupCount=5, Release build |

## Known Stubs

None. All benchmark means are captured from real hardware execution.

## Threat Flags

None. The baseline JSON contains hardware metadata (CPU model, OS version) that is non-sensitive and required by PERF-02 for reproducibility.

## Self-Check: PASSED

- `benchmarks/baselines/report-baseline.json` — EXISTS, valid JSON, 7 benchmarks all with `Statistics.Mean`
- `benchmarks/BASELINES.md` — EXISTS, 141 lines (> 25 minimum), all acceptance criteria met
- Commit `5fa818f` — EXISTS (fix + baseline JSON)
- Commit `3685849` — EXISTS (BASELINES.md)
- Self-comparison gate — EXIT CODE 0 (all 7 benchmarks OK at +0.0% delta)
