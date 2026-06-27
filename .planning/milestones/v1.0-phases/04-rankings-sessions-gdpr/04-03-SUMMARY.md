<!-- REUSE-IgnoreStart -->
---
phase: 04-rankings-sessions-gdpr
plan: "03"
subsystem: glicko2-algorithm-core
tags:
  - glicko2
  - ranking-algorithm
  - vendor-attribution
  - tdd
  - rank-04
  - rank-05
dependency_graph:
  requires:
    - 04-01 (BSD-3-Clause attribution + test project scaffolding)
  provides:
    - src/GameKit.Rankings/Glicko2/Rating.cs
    - src/GameKit.Rankings/Glicko2/RatingCalculator.cs
    - src/GameKit.Rankings/Glicko2/RatingPeriodResults.cs
    - src/GameKit.Rankings/Glicko2/Result.cs
    - src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs
    - src/GameKit.Rankings/Algorithms/RankingState.cs
    - src/GameKit.Rankings/Algorithms/RankingBatch.cs
    - src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs
    - tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs
    - tests/GameKit.Rankings.Tests/Glicko2/Glicko2AlgorithmContractTests.cs
  affects:
    - tests/GameKit.Core.Tests/LicenseHeaderTests.cs (dual-license allow-list for Glicko2 dir)
    - tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj (fixture copy wiring)
tech_stack:
  added:
    - Four vendored Glicko-2 source files under GameKit.Rankings.Glicko2 namespace (internal sealed)
    - IRankingAlgorithm strategy interface — batched-only Apply(RankingState, RankingBatch)
    - Glicko2Algorithm public sealed adapter with tau=0.5 default (Glickman's value)
    - RankingState / RankingBatch / PlayerRatingSnapshot / MatchOutcome / MatchResult domain types
  patterns:
    - Vendored source dual-license header: BSD-3-Clause AND GPL-3.0-or-later
    - Strategy adapter pattern (mirrors IOAuthProvider shape from Phase 2)
    - TDD RED/GREEN cycle for Task 2 (tests committed before implementation)
    - Immutable record types for algorithm I/O (RankingState, RankingBatch)
key_files:
  created:
    - src/GameKit.Rankings/Glicko2/Rating.cs
    - src/GameKit.Rankings/Glicko2/RatingCalculator.cs
    - src/GameKit.Rankings/Glicko2/RatingPeriodResults.cs
    - src/GameKit.Rankings/Glicko2/Result.cs
    - src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs
    - src/GameKit.Rankings/Algorithms/RankingState.cs
    - src/GameKit.Rankings/Algorithms/RankingBatch.cs
    - src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs
    - tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs
    - tests/GameKit.Rankings.Tests/Glicko2/Glicko2AlgorithmContractTests.cs
  modified:
    - tests/GameKit.Core.Tests/LicenseHeaderTests.cs
    - tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj
decisions:
  - "BSD-3-Clause AND GPL-3.0-or-later dual SPDX header on all four vendored Glicko2 files (upstream commit 59033eec)"
  - "IRankingAlgorithm declares exactly one public method Apply — no per-match overload (RANK-04 / Pitfall §1)"
  - "Glicko2Algorithm default tau=0.5 not 0.75 upstream default — produces volatility=0.05999 not 0.06000 (Pitfall §2)"
  - "MatchResult.Forfeit treated as loss for Glicko-2 purposes (documented in XML doc)"
  - "LicenseHeaderTests updated with dual-license allow-list for src/GameKit.Rankings/Glicko2/*.cs"
  - "Fixture copy wired in csproj via CopyToOutputDirectory: PreserveNewest for JSON test fixtures"
metrics:
  duration: "~40min"
  completed: "2026-05-16T05:00:00Z"
  tasks: 3
  files: 12
requirements:
  - RANK-04
  - RANK-05
---

# Phase 04 Plan 03: Glicko-2 Vendor + IRankingAlgorithm + Algorithm Adapter Summary

**One-liner:** Four Glicko-2 source files vendored under BSD-3-Clause AND GPL-3.0-or-later dual header; IRankingAlgorithm batched-only interface (RANK-04); Glicko2Algorithm adapter with tau=0.5 proven correct via Glickman 2012 §3.1 worked example (RANK-05).

## What Was Built

### Task 1 — Vendor four Glicko-2 source files (0a64d3b)

Four files vendored from MaartenStaa/glicko2-csharp commit `59033eeca27a49a444897430dc0a63a33bc99870`
into `src/GameKit.Rankings/Glicko2/`:

- `Rating.cs` — per-player rating wrapper with GetRating/GetRatingDeviation/GetVolatility accessors
- `RatingCalculator.cs` — core algorithm engine; performs UpdateRatings against RatingPeriodResults
- `RatingPeriodResults.cs` — batch-results collector; AddResult/AddDraw/Clear
- `Result.cs` — single-match-result data with score computation

Changes from upstream:
- Namespace: `Glicko2` → `GameKit.Rankings.Glicko2`
- Visibility: `public class` → `internal sealed class` (encapsulation per RANK-04)
- Target framework: net10.0 (upstream targeted .NET 4.5 / PCL)
- Upstream tau=0.75 default preserved verbatim; Glicko2Algorithm passes tau=0.5 explicitly

License variant (locked by plan 04-01 human checkpoint):
- **BSD-3-Clause** (NOT MIT as incorrectly stated in CLAUDE.md and 04-CONTEXT.md)
- Dual SPDX identifier: `BSD-3-Clause AND GPL-3.0-or-later` on every vendored file
- Upstream commit SHA: `59033eeca27a49a444897430dc0a63a33bc99870`

`LicenseHeaderTests.cs` updated with a `_dualLicensePaths` allow-list that recognizes
`src/GameKit.Rankings/Glicko2/*.cs` files as dual-licensed — checking for both `GPL-3.0-or-later`
AND `BSD-3-Clause` on the first line.

### Task 2 — IRankingAlgorithm + domain types + Glicko2Algorithm (01bffe1 RED, 09b9112 GREEN)

**Domain types (all public, all immutable records):**

- `IRankingAlgorithm` — batched-only strategy interface:
  - `string Name { get; }` — stable discriminator (e.g. "glicko2")
  - `RankingState Apply(RankingState state, RankingBatch batch)` — the ONLY public method
  - XML doc explicitly forbids per-match overloads (Pitfall §1 / RANK-04)
  - XML doc warns implementers about numerical stability and determinism requirements

- `RankingState(IReadOnlyDictionary<Guid, PlayerRatingSnapshot> Ratings)` — immutable snapshot
- `PlayerRatingSnapshot(Guid PlayerId, double Rating, double RatingDeviation, double Volatility)`
- `RankingBatch(IReadOnlyList<MatchOutcome> Outcomes)` — immutable rating-period batch
- `MatchOutcome(Guid PlayerId, Guid OpponentId, MatchResult Result)`
- `MatchResult { Win, Loss, Draw, Forfeit }` — Forfeit treated as loss by Glicko2Algorithm

**Glicko2Algorithm:**
- `public sealed class Glicko2Algorithm : IRankingAlgorithm`
- Constructor: `(double tau = 0.5, double initVolatility = 0.06)` — tau override for plan 04-04 wiring
- `Name => "glicko2"`
- `Apply` builds `RatingCalculator(initVolatility: _initVolatility, tau: _tau)` per call (stateful)
- Maps Guid→Rating, accumulates ALL outcomes into `RatingPeriodResults` (batched), calls `UpdateRatings`
- Returns new immutable `RankingState` from updated `Rating` wrappers

### Task 3 — Regression + contract tests (committed in RED phase 01bffe1)

Four tests passing, all in `tests/GameKit.Rankings.Tests/`:

| Test | What it proves |
|------|---------------|
| `Glicko2AlgorithmContractTests.IRankingAlgorithm_Has_Only_Apply_Batch_Method` | Reflection: exactly one public non-accessor method named Apply with signature (RankingState, RankingBatch)→RankingState |
| `Glicko2AlgorithmContractTests.Glicko2Algorithm_Reports_Name_Glicko2` | `new Glicko2Algorithm().Name == "glicko2"` |
| `Glicko2AlgorithmContractTests.Tau_Is_05_By_Default_Not_075` | Runs Glickman §3.1 example; asserts volatility < 0.06000 (tau=0.5 path); tau=0.75 would produce 0.06000 |
| `Glicko2WorkedExampleTests.Glickman_Worked_Example_Matches_Within_Tolerance` | Loads JSON fixture; asserts rating≈1464.05±0.5, rd≈151.52±0.5, volatility≈0.05999±0.0001 |

**Worked example numerics:**
- Expected rating: **1464.05** (tolerance ±0.5) — PASS
- Expected RD: **151.52** (tolerance ±0.5) — PASS
- Expected volatility: **0.05999** (tolerance ±0.0001) — PASS

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing functionality] LicenseHeaderTests allow-list for dual-licensed files**

- **Found during:** Task 1 verify step
- **Issue:** The existing `LicenseHeaderTests.Every_CSharp_Source_File_Has_SPDX_GPL_Header` checked for the literal substring `"SPDX-License-Identifier: GPL-3.0-or-later"` on line 0. Our vendored files carry `"SPDX-License-Identifier: BSD-3-Clause AND GPL-3.0-or-later"` which contains "GPL-3.0-or-later" but not the exact prefix. The test failed on all four vendored files.
- **Fix:** Added a `_dualLicensePaths` static allow-list array to `LicenseHeaderTests`. For files within allowed paths, the check verifies both `GPL-3.0-or-later` AND the specified upstream identifier (`BSD-3-Clause`) appear on line 0. Standard files retain the original exact-substring check.
- **Files modified:** `tests/GameKit.Core.Tests/LicenseHeaderTests.cs`
- **Commit:** 0a64d3b

**2. [Rule 2 - Missing functionality] Fixture copy not wired in test csproj**

- **Found during:** Task 3 verify
- **Issue:** `Glickman_Worked_Example.json` is not copied to the test output directory by default; `AppContext.BaseDirectory` points to `bin/Debug/net10.0/` but the JSON file wasn't there.
- **Fix:** Added `<Content Include="Glicko2\Fixtures\**\*.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>` to `GameKit.Rankings.Tests.csproj`.
- **Files modified:** `tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj`
- **Commit:** 09b9112

## TDD Gate Compliance

Task 2 followed the RED/GREEN cycle:
- **RED** commit `01bffe1`: both test files added; build failed with CS0234 (Algorithms namespace absent)
- **GREEN** commit `09b9112`: implementation files created; all 4 tests pass

Task 3 was executed as part of the Task 2 RED phase (test files reference Task 2 types so they had to be committed together in the RED commit).

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build src/GameKit.Rankings` | PASS — 0 warnings, 0 errors |
| GPL SPDX in all 4 vendored files | PASS — `BSD-3-Clause AND GPL-3.0-or-later` contains GPL-3.0-or-later |
| BSD-3-Clause in all 4 vendored files | PASS |
| `LicenseHeaderTests` | PASS |
| `IRankingAlgorithm_Has_Only_Apply_Batch_Method` | PASS |
| `Glicko2Algorithm_Reports_Name_Glicko2` | PASS |
| `Tau_Is_05_By_Default_Not_075` | PASS |
| `Glickman_Worked_Example_Matches_Within_Tolerance` | PASS |
| `grep "tau: 0.5" Glicko2Algorithm.cs` | PASS |
| Single Apply method on IRankingAlgorithm | PASS (no per-match overload) |

## Commit Log

| Task | Phase | Commit | Message |
|------|-------|--------|---------|
| 1 | — | 0a64d3b | feat(04-03): vendor four Glicko-2 source files with BSD-3-Clause AND GPL-3.0-or-later dual header |
| 2 | RED | 01bffe1 | test(04-03): add failing tests for IRankingAlgorithm contract + Glickman worked example |
| 2 | GREEN | 09b9112 | feat(04-03): IRankingAlgorithm + RankingState/RankingBatch domain + Glicko2Algorithm adapter |

## Known Stubs

None — all types are fully implemented, algorithm produces Glickman-paper-correct numerics.

## Threat Flags

| Flag | File | Description |
|------|------|-------------|
| No new flags | — | All threats from the plan's threat model are mitigated: T-04-03-LC (attribution headers verified), T-04-03-PM (contract test guards single Apply), T-04-03-TC (tau=0.5 discriminator test), T-04-03-LH (LicenseHeaderTests updated) |

## Self-Check: PASSED

- src/GameKit.Rankings/Glicko2/Rating.cs: EXISTS
- src/GameKit.Rankings/Glicko2/RatingCalculator.cs: EXISTS
- src/GameKit.Rankings/Glicko2/RatingPeriodResults.cs: EXISTS
- src/GameKit.Rankings/Glicko2/Result.cs: EXISTS
- src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs: EXISTS
- src/GameKit.Rankings/Algorithms/RankingState.cs: EXISTS
- src/GameKit.Rankings/Algorithms/RankingBatch.cs: EXISTS
- src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs: EXISTS
- tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs: EXISTS
- tests/GameKit.Rankings.Tests/Glicko2/Glicko2AlgorithmContractTests.cs: EXISTS
- Commits 0a64d3b, 01bffe1, 09b9112: VERIFIED in git log
- All 4 tests: PASS
- LicenseHeaderTests: PASS
- dotnet build GameKit.Rankings: 0 warnings, 0 errors
<!-- REUSE-IgnoreEnd -->
