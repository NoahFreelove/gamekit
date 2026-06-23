---
phase: 19-load-performance-testing
fixed_at: 2026-06-23T00:00:00Z
review_path: .planning/phases/19-load-performance-testing/19-REVIEW.md
iteration: 1
findings_in_scope: 6
fixed: 6
skipped: 0
status: all_fixed
---

# Phase 19: Code Review Fix Report

**Fixed at:** 2026-06-23
**Source review:** `.planning/phases/19-load-performance-testing/19-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 6 (2 Critical, 4 Warning)
- Fixed: 6
- Skipped: 0

## Fixed Issues

### CR-01 + CR-02: Regression gate fail-closed — merge all BDN per-class reports; fail on missing/empty

**Files modified:** `.github/workflows/ci.yml`, `benchmarks/CompareBaseline/Program.cs`, `benchmarks/CompareBaseline.Tests/CompareBaselineTests.cs`
**Commit:** `eca3528`
**Applied fix:**

CI workflow now collects ALL `*-report-full.json` files emitted by BDN (one per benchmark class),
merges their `Benchmarks` arrays into a single combined JSON via an inline `python3` script, and
passes the combined file to `CompareBaseline`. This replaces the `ls | head -1` approach that only
ever checked the alphabetically-first class file (`Glicko2Benchmarks`), leaving BCrypt, Argon2, JWT
validation, and Redis ticket throughput regression-unchecked.

`CompareBaseline` is now fail-closed:
- **Baseline method absent from new report** → `ERROR`, `HasRegression=true`, exit 1 (was: WARNING, exit 0)
- **Empty `Benchmarks` array** → `ERROR`, `HasRegression=true`, exit 1 (was: WARNING loop, exit 0)
- **New method absent from baseline** → WARNING only (unchanged — not a failure)

Tests: 12/12 passing. The original `CompareReports_BaselineMethodMissingFromNew_WarnsDoesNotFail`
test was inverted to assert `HasRegression=true` and exit 1. New tests added:
`CompareReports_EmptyNewReport_HasRegressionTrue`, `Main_EmptyNewReport_Returns1`,
`Main_BaselineMethodMissingFromNew_Returns1`, `CompareReports_MergedMultiFileReport_AllPresent_HasRegressionFalse`,
`Main_MergedMultiFileReport_AllPresent_Returns0`.

**Gate self-test results:**
- Only-Glicko2 report (simulates old `head -1` bug) → exit 1 (FAIL-CLOSED)
- Empty `{"Benchmarks":[]}` report → exit 1 (FAIL-CLOSED)
- Complete merged report, all methods within threshold → exit 0 (PASS)

---

### WR-01: Add `auth_login` p99 threshold to enforce BCrypt latency gate

**Files modified:** `tests/k6/matchmaking-burst.js`
**Commit:** `a92191d`
**Applied fix:** Added `'http_req_duration{name:auth_login}': ['p(99)<1500']` to `options.thresholds`.
The `auth_throughput` scenario was measuring BCrypt latency but the threshold was documented only in
comments, not enforced. A BCrypt cost-factor regression now FAILS the k6 run.

---

### WR-04: Guard `match_formation_time_ms` vacuous Trend pass

**Files modified:** `tests/k6/matchmaking-burst.js`
**Commit:** `a92191d`
**Applied fix:** Added `matchPollEnqueueMiss` Counter metric (imported `Counter` from `k6/metrics`)
and threshold `'match_poll_enqueue_miss': ['count<5']`. Increments counter on every early-exit path
in `matchPoll()` where no `ticketId` is obtained (JWT missing, non-200/409 status, JSON parse fail,
409 without ticketId). When all 20 VUs miss, `match_formation_time_ms` has zero samples and k6
would pass its threshold vacuously; the counter ensures the run fails if more than 5 VUs miss.

---

### WR-02: Move SignalR negotiate JWT to Authorization header

**Files modified:** `tests/k6/helpers/signalr.js`
**Commit:** `9ea0065`
**Applied fix:** `negotiateSignalR()` now sends the bearer token in `Authorization: Bearer <jwt>`
header instead of as `?access_token=<jwt>` in the negotiate POST URL. The negotiate URL is now
`…/negotiate?negotiateVersion=1` only. The WebSocket URL retains `?access_token=<jwt>` — this is
required by the SignalR protocol because the WebSocket upgrade request cannot carry custom headers;
a comment documents this distinction explicitly.

---

### WR-03: Dispose IServiceScope in MatchmakingBenchmarkHost

**Files modified:** `tests/GameKit.LoadTests/Infrastructure/MatchmakingBenchmarkHost.cs`
**Commit:** `de329a3`
**Applied fix:** Added `private IServiceScope? _benchmarkScope` field. `InitializeAsync` now assigns
to it (`_benchmarkScope = _host.Services.CreateScope()`) rather than discarding the scope reference.
`DisposeAsync` disposes the scope before stopping the host so scoped services are released in the
correct order: scope → host → containers.

Build: `dotnet build tests/GameKit.LoadTests --configuration Release` — succeeded, 0 warnings, 0 errors.

---

_Fixed: 2026-06-23_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
