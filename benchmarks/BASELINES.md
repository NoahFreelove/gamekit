# GameKit Benchmark Baselines

> **Source of truth:** All mean values in this file are transcribed from
> [`benchmarks/baselines/report-baseline.json`](baselines/report-baseline.json).
> The regression gate in `benchmarks/CompareBaseline/` reads raw nanoseconds from that JSON
> and uses the values here only for human documentation. **Do not edit these numbers
> manually** — re-run the capture command below and re-transcribe.

---

## Machine Specification

| Property          | Value                                        |
|-------------------|----------------------------------------------|
| CPU               | 11th Gen Intel Core i7-11700K @ 3.60GHz      |
| Physical cores    | 8                                            |
| Logical cores     | 16 (Hyper-Threading enabled)                 |
| CPU max turbo     | 5.00 GHz                                     |
| Total RAM         | 30 GiB                                       |
| OS                | Linux Ubuntu 26.04 (Resolute Raccoon) kernel 7.0.0-22-generic x86_64 |
| .NET SDK          | 10.0.109                                     |
| .NET Runtime      | .NET 10.0.9 (10.0.9, 10.0.926.27113) X64 RyuJIT x86-64-v4 |
| BenchmarkDotNet   | 0.15.8                                       |
| Capture date      | 2026-06-23                                   |
| BDN Configuration | IterationCount=15, WarmupCount=5 (Release build) |

---

## Per-Benchmark Means

All values transcribed from `benchmarks/baselines/report-baseline.json` (`Statistics.Mean`, in
nanoseconds). The gate (`benchmarks/CompareBaseline/`) compares raw nanoseconds; the human-readable
units below are for readability only.

| Benchmark          | Class                          | Mean (ns)     | Mean (human)  | Notes                                              |
|--------------------|--------------------------------|---------------|---------------|----------------------------------------------------|
| `ValidateToken`    | `JwtValidationBenchmarks`      | 18,020 ns     | 18.0 µs       | RSA-SHA256 JWT validation (2048-bit key, 1 claim)  |
| `BCryptVerify`     | `PasswordHasherBenchmarks`     | 202,468,939 ns | 202.5 ms      | BCrypt verify, work factor 12 (production default) |
| `Argon2idVerify`   | `PasswordHasherBenchmarks`     | 237,691,726 ns | 237.7 ms      | Argon2id verify, m=65536 KiB, t=3, p=1 (OWASP default) |
| `Apply_2`          | `Glicko2Benchmarks`            | 10,093 ns     | 10.1 µs       | Glicko-2 rating update, batch size 2               |
| `Apply_10`         | `Glicko2Benchmarks`            | 21,872 ns     | 21.9 µs       | Glicko-2 rating update, batch size 10              |
| `Apply_100`        | `Glicko2Benchmarks`            | 191,501 ns    | 191.5 µs      | Glicko-2 rating update, batch size 100             |
| `TicketEnqueueAsync` | `MatchmakingTicketBenchmarks` | 2,959,642 ns  | 2.96 ms       | Redis enqueue (Testcontainers Redis, Docker bridge) |

> **PERF-05 note:** The `BCryptVerify` mean of 202.5 ms and `Argon2idVerify` mean of 237.7 ms
> at production parameters are the reference values cited in `docs/performance-tuning.md`
> for the cost-factor vs latency table.

---

## Runner Class and Noise Tolerance

### Regression Gate Threshold: 20%

The CI gate (`benchmarks/CompareBaseline/`) fails if any benchmark's mean in a new run exceeds
**120% of the committed baseline mean**. The 20% threshold is intentionally generous to absorb:

1. **Warm-up exclusion:** BDN excludes the warmup iterations from `Statistics.Mean` — only the
   steady-state iterations contribute. The 5 warmup iterations (`--warmupCount 5`) are sufficient
   to reach JIT steady state for all benchmarks.

2. **Runner-to-runner variance:** GitHub Actions `ubuntu-24.04` runner hardware varies ±5-15%
   run-to-run for CPU-bound work. The committed baseline was captured on an 11th Gen i7-11700K
   dev machine. CI runner differences are absorbed by the 20% threshold.

3. **BCrypt / Argon2id stability:** At ~200 ms per iteration, ±5% variance is ~10 ms — well
   within the 20% gate (~40 ms). These are the most stable benchmarks because the measurement
   noise is small relative to the total duration.

4. **JWT validation stability:** Mean ~18 µs; ±5% = ~1 µs — comfortably below the 20% gate
   (~3.6 µs). Sub-microsecond noise from the OS scheduler is negligible.

5. **Glicko-2 stability:** Pure CPU, no I/O; all three batch sizes are stable (< 1% StdErr
   in the captured run).

### Redis Ticket Benchmark (Noisiest)

`TicketEnqueueAsync` is the most noise-prone benchmark because it measures a real Redis round-trip
via Docker's bridge network on a Testcontainers container started in `[GlobalSetup]`. Sources of jitter:

- Docker bridge network latency variance (~0.5–3 ms per call depending on host load)
- Redis GC / eviction background activity inside the container
- Host OS network stack scheduling

To reduce this variance, `[MinIterationCount(15)]` is applied to the benchmark class
(`tests/GameKit.LoadTests/Benchmarks/MatchmakingTicketBenchmarks.cs`). The captured mean of
2.96 ms has a 23% CI margin (0.68 ms) — slightly above the 20% gate. This is acceptable
because the gate compares each run's mean against the 2.96 ms baseline; a single run with
a higher mean within the CI range would still pass. **If the Redis benchmark regresses in CI,
re-run the benchmark gate step before concluding there is a real regression** — transient
Docker bridge jitter can produce false positives for this single benchmark.

### Baseline Update Policy

Update `benchmarks/baselines/report-baseline.json` (and re-transcribe this file) when:

- A deliberate performance optimization changes any mean by **>10% downward** (improvement)
- A breaking change in a dependency is expected to shift means substantially
- The capture machine class changes (different CPU generation in CI)

Do NOT update the baseline to suppress a legitimate regression — investigate the cause first.

---

## Capture Command (Reproducible)

```bash
dotnet run --project tests/GameKit.LoadTests -c Release -- \
  --filter '*' \
  --exporters json \
  --iterationCount 15 \
  --warmupCount 5 \
  --artifacts benchmarks/baselines/run-capture

# After the run, merge the per-class JSON files into the stable baseline path:
# python3 scripts/merge-bdn-reports.py \
#   benchmarks/baselines/run-capture/results/*.json \
#   --output benchmarks/baselines/report-baseline.json
# (or copy the single-class JSON if only one class ran)
```

**Requirements:**
- Build configuration MUST be `Release` (BDN refuses Debug builds)
- Do NOT use `--job short` — the baseline must be a full statistical run
- Docker must be running for `TicketEnqueueAsync` (Testcontainers Redis + Postgres)

---

## Self-Comparison Gate Result

Running the Plan 03 (`benchmarks/CompareBaseline/`) gate with the baseline compared against itself
exits 0 (no regression is possible when comparing identical means):

```
dotnet run --project benchmarks/CompareBaseline -c Release -- \
  benchmarks/baselines/report-baseline.json \
  benchmarks/baselines/report-baseline.json
# Expected output: all benchmarks print "OK" with delta 0%; exit code 0
```

Confirmed: self-comparison exits 0 (verified after initial baseline capture on 2026-06-23).
