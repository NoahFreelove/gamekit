---
phase: 19-load-performance-testing
plan: "03"
subsystem: benchmarks/regression-gate
status: complete
tags: [perf, benchmarks, ci, regression-gate, tdd]
dependency_graph:
  requires: ["19-01"]
  provides: [PERF-06-gate, CompareBaseline-tool, CompareBaseline-self-test, benchmarks-ci-job]
  affects: [.github/workflows/ci.yml, GameKit.sln]
tech_stack:
  added:
    - "benchmarks/CompareBaseline — net10.0 console tool, zero NuGet deps (System.Text.Json in-box)"
    - "benchmarks/CompareBaseline.Tests — xUnit 2.9.2 self-test project"
  patterns:
    - "Static testable comparison method (Comparator.CompareReports) separated from entry point (Program.Main)"
    - "Fixture-based xUnit tests: injected JSON strings for missing-method cases; file-based for exit-code tests"
    - "CI job-level if: condition to gate benchmark job on push-to-main only"
key_files:
  created:
    - benchmarks/CompareBaseline/CompareBaseline.csproj
    - benchmarks/CompareBaseline/Program.cs
    - benchmarks/CompareBaseline.Tests/CompareBaseline.Tests.csproj
    - benchmarks/CompareBaseline.Tests/CompareBaselineTests.cs
    - benchmarks/CompareBaseline.Tests/fixtures/baseline.json
    - benchmarks/CompareBaseline.Tests/fixtures/within-threshold-report.json
    - benchmarks/CompareBaseline.Tests/fixtures/regressed-report.json
  modified:
    - GameKit.sln
    - .github/workflows/ci.yml
decisions:
  - "Comparator.CompareReports() is public static — tests call it directly without spawning a process; Program.Main() is the entry point tested for exit codes"
  - "Threshold constant Threshold.Regression = 0.20 defined in a public static class for discoverability and test assertion"
  - "Missing/added methods produce MethodResult with IsWarning=true, never set HasRegression=true (T-19-03-01 mitigation)"
  - "CI benchmarks job uses if: github.event_name == 'push' && github.ref == 'refs/heads/main' rather than a separate on: trigger so both build-and-test and benchmarks share the same workflow file"
  - "Graceful baseline-missing guard: step echoes INFO message and exits 0 (not 1) when baseline JSON absent — prevents the job blocking CI before Plan 19-04 commits the baseline"
metrics:
  duration_minutes: 15
  completed: "2026-06-23"
  tasks_total: 2
  tasks_completed: 2
  files_created: 7
  files_modified: 2
---

# Phase 19 Plan 03: CompareBaseline Regression Gate + CI Job Summary

**One-liner:** PERF-06 regression gate implemented as a zero-dep net10.0 console tool with a 7-test xUnit self-proof (exit 1 on +30% injected regression, exit 0 within +10%) and a push-to-main-only CI benchmarks job.

---

## What Was Built

### Task 1: CompareBaseline Tool + Proving Self-Test (commit a8dd05f)

**`benchmarks/CompareBaseline/Program.cs`**
- `Comparator.CompareReports(newReportJson, baselineJson)` — public static method that parses two BDN `-report-full.json` strings, computes `delta = (newMean - baseMean) / baseMean` per matched method, and returns `CompareResult { HasRegression, Results }`.
- `Threshold.Regression = 0.20` — the 20% gate constant, visible in source and asserted in tests.
- `Program.Main(string[])` — reads two file-path arguments, calls `CompareReports`, prints OK/REGRESSION/WARNING lines, returns 0 or 1.
- Missing baseline method → `WARNING` entry, not a regression (T-19-03-01 / Pitfall §6).
- New report method absent from baseline → `WARNING` entry, not a regression.

**`benchmarks/CompareBaseline.Tests/`**
Three fixture files covering the proving scenarios:
- `baseline.json`: BCryptVerify 100 ms, ValidateToken 5 µs, Glicko2Apply 250 µs
- `within-threshold-report.json`: all methods within +10% of baseline
- `regressed-report.json`: BCryptVerify at 130 ms (+30%, above the 20% gate)

Seven xUnit tests in `CompareBaselineTests.cs`:
1. `CompareReports_RegressedFixture_HasRegressionTrue` — HasRegression=true, exactly one method flagged, delta ~0.30
2. `Main_RegressedFixture_Returns1` — **the gate proof**: exit code 1 on +30% regression
3. `CompareReports_WithinThreshold_HasRegressionFalse` — no regressions at +10%
4. `Main_WithinThreshold_Returns0` — exit code 0 within threshold
5. `CompareReports_BaselineMethodMissingFromNew_WarnsDoesNotFail` — WARNING, not failure
6. `CompareReports_NewMethodAbsentFromBaseline_WarnsDoesNotFail` — WARNING, not failure
7. `Threshold_RegressionConstant_Is0Point20` — threshold constant is exactly 0.20

**Self-test result:** `Passed! — Failed: 0, Passed: 7, Skipped: 0, Total: 7`

**Manual proof:**
```
dotnet run --project benchmarks/CompareBaseline -c Release -- \
  benchmarks/CompareBaseline.Tests/fixtures/regressed-report.json \
  benchmarks/CompareBaseline.Tests/fixtures/baseline.json
# Output:
# REGRESSION: BCryptVerify: 130.000 ms vs baseline 100.000 ms (+30.0%, threshold 20%)
#   OK: ValidateToken: 0.005 ms vs baseline 0.005 ms (+0.0%)
#   OK: Glicko2Apply: 0.250 ms vs baseline 0.250 ms (+0.0%)
# Benchmark regression gate FAILED — one or more benchmarks exceeded the 20% threshold.
# Exit code: 1
```

### Task 2: Solution Registration + CI Job (commit e338901)

**`GameKit.sln`** — both CompareBaseline projects added via `dotnet sln add`.

**`.github/workflows/ci.yml`** — new `benchmarks` job:
- `if: github.event_name == 'push' && github.ref == 'refs/heads/main'` — skipped on all PRs
- Runs on `ubuntu-24.04` (consistent with existing job; baseline runner class)
- Steps: checkout (fetch-depth 0), setup .NET 10, restore, build Release (no NuGetAudit=false), run LoadTests with `--exporters json`, then CompareBaseline gate
- Graceful guard: if `benchmarks/baselines/report-baseline.json` absent (before Plan 19-04), prints INFO and exits 0
- YAML comment block: benchmarks never run against an external/production target
- Artifact upload of BenchmarkRun/ for inspection

---

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Top-level statement conflict with Program.Main entry point**
- **Found during:** Task 1, first build attempt
- **Issue:** Initial `Program.cs` had both a top-level `return CompareBaseline.Program.Main(args);` statement AND a `Program.Main(string[])` method. The compiler emitted CS7022 ("global code; ignoring 'Program.Main(string[])' entry point").
- **Fix:** Removed the top-level statement; `Program.Main(string[])` is the sole entry point (no partial class / synthesized `Main` conflict).
- **Files modified:** `benchmarks/CompareBaseline/Program.cs`
- **Commit:** a8dd05f (fixed before commit)

---

## Threat Mitigations Applied

| Threat ID | Status |
|-----------|--------|
| T-19-03-01 (Tampering: silent method skip) | MITIGATED — tool warns on missing/added methods, never silently skips |
| T-19-03-02 (Tampering: external target) | MITIGATED — CI job has no BASE_URL; YAML comment records invariant |
| T-19-03-03 (Repudiation: unproven gate) | MITIGATED — 7-test self-proof with proven exit-1-on-regression |
| T-19-03-SC (New NuGet refs) | MITIGATED — only Microsoft.NET.Test.Sdk + xunit + runner (CPM); audit gate on |

---

## Known Stubs

None. The gate is fully functional. The one intentional "incomplete" state is that the baseline JSON (`benchmarks/baselines/report-baseline.json`) does not yet exist — this is by design (Plan 19-04 commits it). The gate step gracefully skips when absent.

---

## Self-Check: PASSED

- [x] `benchmarks/CompareBaseline/Program.cs` — exists, contains `0.20`, zero NuGet deps
- [x] `benchmarks/CompareBaseline.Tests/CompareBaselineTests.cs` — exists, 7 tests, >30 lines
- [x] `benchmarks/CompareBaseline.Tests/fixtures/baseline.json` — exists
- [x] `benchmarks/CompareBaseline.Tests/fixtures/within-threshold-report.json` — exists
- [x] `benchmarks/CompareBaseline.Tests/fixtures/regressed-report.json` — exists
- [x] `.github/workflows/ci.yml` — contains "CompareBaseline", "benchmarks:", push-to-main gate
- [x] Commits a8dd05f and e338901 confirmed in git log
- [x] `dotnet test benchmarks/CompareBaseline.Tests` — 7/7 passed
- [x] `dotnet run ... -- regressed.json baseline.json` — exit code 1 confirmed
