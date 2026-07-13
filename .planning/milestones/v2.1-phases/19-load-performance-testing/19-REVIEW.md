---
phase: 19-load-performance-testing
reviewed: 2026-06-23T00:00:00Z
depth: deep
files_reviewed: 16
files_reviewed_list:
  - .github/workflows/ci.yml
  - benchmarks/CompareBaseline/Program.cs
  - benchmarks/CompareBaseline.Tests/CompareBaselineTests.cs
  - benchmarks/CompareBaseline.Tests/fixtures/baseline.json
  - benchmarks/CompareBaseline.Tests/fixtures/regressed-report.json
  - benchmarks/CompareBaseline.Tests/fixtures/within-threshold-report.json
  - benchmarks/baselines/report-baseline.json
  - tests/GameKit.LoadTests/Program.cs
  - tests/GameKit.LoadTests/GameKit.LoadTests.csproj
  - tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs
  - tests/GameKit.LoadTests/Benchmarks/MatchmakingTicketBenchmarks.cs
  - tests/GameKit.LoadTests/Benchmarks/PasswordHasherBenchmarks.cs
  - tests/GameKit.LoadTests/Benchmarks/JwtValidationBenchmarks.cs
  - tests/k6/helpers/signalr.js
  - tests/k6/matchmaking-burst.js
  - tests/k6/spike-signalr.js
  - tests/k6/lobby-signalr-fanout.js
  - tests/k6/SpikeHost/Program.cs
findings:
  critical: 2
  warning: 4
  info: 0
  total: 6
status: issues_found
---

# Phase 19: Code Review Report

**Reviewed:** 2026-06-23
**Depth:** deep
**Files Reviewed:** 16
**Status:** issues_found

## Summary

Phase 19 adds BenchmarkDotNet micro-benchmarks, a CompareBaseline regression gate, and k6 load
test scripts. The production source (`src/`) is untouched. The gate self-tests are well-written
and do prove the core regression logic. However there are two CRITICAL bugs: one in the CI
workflow and one in the gate's handling of an all-absent-benchmark edge case. Four warnings
cover missing k6 threshold enforcement, a JWT-in-URL exposure, an undisposed DI scope, and a
vacuous-pass risk in the match-formation polling scenario.

---

## Critical Issues

### CR-01: CI regression gate only checks one of four benchmark classes — three classes silently skipped

**File:** `.github/workflows/ci.yml:200`

**Issue:** BenchmarkDotNet emits one `*-report-full.json` file **per benchmark class** when run
via `BenchmarkRunner.Run(assembly, args)`. With four classes
(`Glicko2Benchmarks`, `JwtValidationBenchmarks`, `MatchmakingTicketBenchmarks`,
`PasswordHasherBenchmarks`) the runner writes four separate files to `BenchmarkRun/results/`.
The CI step picks exactly one:

```bash
REPORT=$(ls BenchmarkRun/results/*-report-full.json 2>/dev/null | head -1)
```

`ls | head -1` returns the alphabetically first result, which is the `Glicko2Benchmarks` file.
The other three files — containing `BCryptVerify`, `Argon2idVerify`, `ValidateToken`, and
`TicketEnqueueAsync` — are never passed to `CompareBaseline`. When those four methods are absent
from the compared file, `CompareBaseline` emits `WARNING: baseline method '...' is missing from
the new report` and exits 0. A 100% regression in BCrypt, Argon2, JWT validation, or Redis
ticket throughput is silently ignored on every push to main.

**The committed `benchmarks/baselines/report-baseline.json` is a manually combined single-file
baseline containing all 7 methods from all 4 classes.** BDN never emits such a combined file;
nothing in CI produces one from the four class-level files.

**Fix — Option A (recommended): loop over all report files:**

```bash
# In the benchmark regression gate step:
REPORT_DIR="BenchmarkRun/results"
REPORTS=$(ls "${REPORT_DIR}"/*-report-full.json 2>/dev/null)
if [ -z "$REPORTS" ]; then
  echo "WARNING: No benchmark report emitted — benchmark run may have failed."
  exit 1
fi

BASELINE="benchmarks/baselines/report-baseline.json"
if [ ! -f "$BASELINE" ]; then
  echo "INFO: Baseline not yet committed — skipping regression gate."
  exit 0
fi

GATE_EXIT=0
for REPORT in $REPORTS; do
  dotnet run --project benchmarks/CompareBaseline -c Release -- "$REPORT" "$BASELINE" || GATE_EXIT=$?
done
exit $GATE_EXIT
```

**Fix — Option B (simpler): update `CompareBaseline` to accept a directory and merge files
before comparison, or run BDN with a custom combiner that merges per-class JSON into one file.**

This is a confirmed BLOCKER: three of four benchmark classes are never regression-checked on any
push to main.

---

### CR-02: Empty `Benchmarks` array in new report produces exit 0 (silent gate pass)

**File:** `benchmarks/CompareBaseline/Program.cs:82-133`

**Issue:** If `newReportJson` contains `{"Benchmarks": []}` (e.g., BDN wrote an empty results
file because a benchmark class panicked or was removed), the comparator builds an empty `newMap`.
It then iterates the `baselineMap` (7 entries) and emits a `WARNING` for each method absent from
the new report. `hasRegression` stays `false`. `CompareReports` returns `HasRegression: false`.
`Main` prints the warnings and exits **0** — the gate passes.

A BDN crash that produces an empty results file, or a deliberate deletion of all benchmarks,
silently clears the regression gate. This directly defeats the gate's purpose.

The self-test `CompareReports_BaselineMethodMissingFromNew_WarnsDoesNotFail` explicitly asserts
this behaviour by design (line 133–135), but that design decision makes the gate trivially
bypassable.

**Fix:** Treat an empty `Benchmarks` array in the new report as a gate error, not a warning:

```csharp
// After building newMap, before iterating baselineMap:
if (newMap.Count == 0 && baselineMap.Count > 0)
{
    // New report has no benchmarks but baseline has some — this indicates a
    // crashed or empty benchmark run. Fail the gate rather than warning.
    return new CompareResult(
        HasRegression: true,
        Results: [new MethodResult(
            "<all-benchmarks>",
            double.NaN, double.NaN, double.NaN,
            IsRegression: true,
            IsWarning: false,
            "ERROR: new report contains 0 benchmarks but baseline has " +
            $"{baselineMap.Count}. Benchmark run likely crashed or all methods were removed.")]);
}
```

Update the self-test `CompareReports_BaselineMethodMissingFromNew_WarnsDoesNotFail` to only
apply when a **subset** of methods is missing (not all of them), and add a new test:
`CompareReports_EmptyNewReport_HasRegressionTrue`.

---

## Warnings

### WR-01: `auth_throughput` scenario has no k6 threshold — BCrypt latency regression is never caught

**File:** `tests/k6/matchmaking-burst.js:86-87, 108-119`

**Issue:** The `auth_throughput` scenario (100 VUs, 30 s) documents a `p99 < 1500ms` threshold
in two comments (lines 86 and 163) but no corresponding entry exists in `options.thresholds`.
The defined thresholds are:

```js
thresholds: {
  'http_req_duration{name:enqueue}': ['p(99)<2000'],
  'http_req_failed': ['rate<0.01'],
  [`match_formation_time_ms`]: [`p(99)<${MATCH_P99_MS}`],
},
```

There is no `'http_req_duration{name:auth_login}': ['p(99)<1500']`. k6 exits 0 regardless of
BCrypt latency under load — a BCrypt cost-factor regression is invisible.

**Fix:**

```js
thresholds: {
  'http_req_duration{name:enqueue}': ['p(99)<2000'],
  'http_req_duration{name:auth_login}': ['p(99)<1500'],  // BCrypt wf=12 under 100 VUs
  'http_req_failed': ['rate<0.01'],
  [`match_formation_time_ms`]: [`p(99)<${MATCH_P99_MS}`],
},
```

---

### WR-02: JWT passed as a query parameter in the SignalR negotiate URL — exposed in server logs

**File:** `tests/k6/helpers/signalr.js:45`, `tests/k6/spike-signalr.js:125`,
`tests/k6/lobby-signalr-fanout.js:198`

**Issue:** The JWT is embedded in the HTTP POST URL as a query parameter:

```js
const url = `${baseUrl}${hubPath}/negotiate?negotiateVersion=1&access_token=${jwt}`;
```

And again in the WebSocket URL:

```js
const fullUrl = `${wsUrl}${hubPath}?id=${connectionToken}&access_token=${jwt}`;
```

Query parameters are captured verbatim in server access logs, reverse-proxy logs, and k6 summary
output. For this tool, the JWT is issued against a local spike stack and is ephemeral (1-hour
expiry), so the practical exposure is low. However, passing credentials in URLs is a poor pattern
that persists in any log artifact, including the CI `benchmark-results` upload-artifact (k6
generates a summary HTML/JSON that may contain the full URLs).

ASP.NET Core's SignalR server supports the `access_token` query parameter for WebSocket
connections specifically because the WebSocket upgrade HTTP request cannot carry custom headers —
this use is documented and required for browser clients. The negotiate POST, however, could
instead use the `Authorization: Bearer <jwt>` header.

**Fix:** Use the `Authorization` header for the negotiate POST:

```js
const res = http.post(url, null, {
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${jwt}`,
  },
});
```

Keep `access_token` in the WebSocket URL (required by the SignalR protocol). Remove it from the
negotiate POST URL.

---

### WR-03: `IServiceScope` created at line 220 of `MatchmakingBenchmarkHost` is never disposed

**File:** `tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs:218-223`

**Issue:** The scope created to resolve `IMatchmakingService` is immediately discarded:

```csharp
MatchmakingService = _host.Services
    .CreateScope()           // IServiceScope created here
    .ServiceProvider         // scope reference dropped after this chain
    .GetRequiredService<IMatchmakingService>();
```

The `IServiceScope` object is never stored and therefore never disposed. DI scoped resources
tied to that scope — including any `IDisposable` or `IAsyncDisposable` services within the
Matchmaking pipeline — are held alive until GC finalisation, not until
`MatchmakingBenchmarkHost.DisposeAsync()` returns. In practice the containers are stopped by
`DisposeAsync` before GC runs, so this causes no crash, but it is a resource-tracking bug and
will suppress any finaliser-based warnings from DI.

**Fix:** Store and dispose the scope:

```csharp
// Add field:
private IServiceScope? _benchmarkScope;

// In InitializeAsync, replace the scope creation:
_benchmarkScope = _host.Services.CreateScope();
MatchmakingService = _benchmarkScope.ServiceProvider
    .GetRequiredService<IMatchmakingService>();

// In DisposeAsync, before stopping the host:
if (_benchmarkScope is not null)
{
    _benchmarkScope.Dispose();
    _benchmarkScope = null;
}
```

---

### WR-04: `match_formation_time_ms` threshold passes vacuously if all enqueue calls fail

**File:** `tests/k6/matchmaking-burst.js:231-301`

**Issue:** In the `match_poll` scenario, `matchFormationTime.add()` is called on two paths:

1. Matched successfully (line 287)
2. Not matched within timeout (line 301: `matchFormationTime.add(maxWaitMs)`)

But both paths are only reached **after** a successful enqueue returns `ticketId` (lines
238-249). If the enqueue returns a non-200/409 status (e.g., `401 Unauthorized` because `JWT`
is not set, or `500` from the spike host), the function returns early at lines 232-235 or
248-251 without adding to the Trend. If all 20 `match_poll` VUs take the early exit, the
`match_formation_time_ms` Trend has zero data points.

k6 evaluates thresholds on metrics with no data points as passing (no observations = no
violation). The `match_formation_time_ms` `p(99)<${MATCH_P99_MS}` threshold therefore passes
silently even when matchmaking has completely stopped working and no tickets are ever issued.

The `http_req_failed` threshold (`rate<0.01`) provides a secondary guard, but it excludes `401`
and `500` responses for the `enqueue_poll` tag (these are not counted as k6 HTTP failures). Only
network-level failures count.

**Fix:** Add a counter for VUs that hit the early-exit paths and gate on it:

```js
import { Counter } from 'k6/metrics';
const enqueueMisses = new Counter('match_poll_enqueue_miss');

// In matchPoll(), on each early-exit path:
enqueueMisses.add(1);

// In thresholds:
'match_poll_enqueue_miss': ['count<5'],  // at most 5 out of 20 VUs may miss
```

Alternatively, log a warning and call `matchFormationTime.add(maxWaitMs)` even on enqueue
failure so the Trend always has data, making the threshold meaningful regardless.

---

## Structural Findings (fallow)

No structural pre-pass was provided.

---

_Reviewed: 2026-06-23_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
