---
phase: 21-final-demo-3d-multiplayer-platformer
plan: "02"
subsystem: samples/Platformer3D
status: complete
tags: [custom-strategy, custom-algorithm, matchmaking, ranking, tdd, unit-tests]

dependency_graph:
  requires: ["21-01"]
  provides: ["BestTimeMatchmakingStrategy", "TimeMarginRankingAlgorithm"]
  affects: ["21-04 (host wiring registers these types)", "21-06 (integration smoke test drives them)"]

tech_stack:
  added: []
  patterns:
    - "Fixed-delta Elo (batched-only) for custom IRankingAlgorithm — O(n), no convergence loop"
    - "Linear bracket ramp with cold-start neutral override for custom IMatchmakingStrategy"
    - "TDD Red→Green on each file pair before committing"

key_files:
  created:
    - samples/Platformer3D/Algorithms/TimeMarginRankingAlgorithm.cs
    - samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs
  modified:
    - tests/GameKit.Platformer3D.Tests/Rankings/TimeMarginRankingAlgorithmTests.cs
    - tests/GameKit.Platformer3D.Tests/Strategy/BestTimeMatchmakingStrategyTests.cs

decisions:
  - "D-09 AMENDMENT: Fixed-delta Elo replaces margin-scaled — MatchOutcome has no Score/margin field; adding one would violate D-15 API boundary"
  - "Cold-start conjunctive exception: when candidate is cold-start, only candidate bracket must fit (implements 'matches anyone' intent of D-08)"
  - "Name discriminators: time-margin (algorithm), best-time (strategy) — confirmed for 21-04 wiring"

metrics:
  duration: "6 minutes"
  completed: "2026-06-23T02:18:29Z"
  tasks_completed: 2
  tasks_total: 2
  files_created: 2
  files_modified: 2
  tests_added: 19
  tests_passing: 19
---

# Phase 21 Plan 02: Custom Strategy + Algorithm Summary

**One-liner:** Fixed-delta `TimeMarginRankingAlgorithm` ("time-margin") + linear-bracket `BestTimeMatchmakingStrategy` ("best-time") with cold-start neutral bracket, both fully unit-tested (19 tests green).

---

## D-09 AMENDMENT — Fixed-Delta, Not Margin-Scaled (READ THIS)

D-09 originally specified rating updates "scaled by the time margin (bigger gap → bigger swing)". This sub-clause is **dropped and replaced with a fixed-delta rule**. The reason is forced and confirmed:

`RankingBatch.cs` defines `MatchOutcome(Guid PlayerId, Guid OpponentId, MatchResult Result)` — **there is no Score or margin field**. Carrying the completion-time margin into the rating batch would require adding a field to a `GameKit.*` package public API, which the SPEC prohibits ("Changes to any GameKit.* package public API") and D-15 reinforces. SPEC (WHAT) outranks CONTEXT D-09 (HOW).

**Resolution:** `TimeMarginRankingAlgorithm` implements **fixed-delta Elo** — Win = +K (30.0), Loss/Forfeit = −K, Draw = 0.0, symmetric. The head-to-head outcome (faster integer-ms time = Win) is decided by the GameServer when it posts `SessionResult.Win/Loss/Draw` in plan 21-04 — the time comparison lives there, not in the rating algorithm.

This still satisfies:
- **R6**: verifiable leaderboard change via a custom rule, `Name != "glicko2"`
- **D-10**: exact integer-ms tie → `MatchResult.Draw` → zero rating change, symmetric
- **D-11**: batched-only (accumulate full batch, apply once; O(n), no convergence loop)
- **D-12**: drives the admin leaderboard

The class name retains "TimeMargin" for continuity with PATTERNS and plan artifacts.

---

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Custom IRankingAlgorithm (fixed-delta) + unit tests | `7710a50` | `TimeMarginRankingAlgorithm.cs`, `TimeMarginRankingAlgorithmTests.cs` |
| 2 | Custom IMatchmakingStrategy (best-time proximity) + unit tests | `4ce346f` | `BestTimeMatchmakingStrategy.cs`, `BestTimeMatchmakingStrategyTests.cs` |

---

## Implementation Details

### TimeMarginRankingAlgorithm

- **Name:** `"time-margin"` (not `"glicko2"`)
- **KWin constant:** `30.0`
- **Apply behavior:**
  - Builds fresh working dictionary per call (no mutable instance fields)
  - Seeds absent players at `DefaultRating=1500.0 / DefaultRd=350.0 / DefaultVolatility=0.06`
  - Accumulates per-player delta map across ALL batch outcomes (batched-only D-11)
  - `Win` → +KWin, `Loss/Forfeit` → −KWin, `Draw` → 0.0 (D-10)
  - Applies accumulated deltas once; rating floored at 0.0
  - Returns new `RankingState`; input not mutated

### BestTimeMatchmakingStrategy

- **Name:** `"best-time"` (not `"elo-range"`)
- **Cold-start constants (D-08):**
  - `ColdStartRdThreshold = 300.0` — RD at or above this → cold-start party
  - `NeutralBracketMs = 60_000.0` — cold-start flat bracket
- **Ramp constants (D-06):**
  - `InitialBracketMs = 5_000.0` — starting bracket at t=0
  - `MaxBracketMs = 30_000.0` — cap after full ramp
  - `RampSeconds = 60.0` — ramp duration
- **Match behavior:**
  - No mutable instance fields; all per-call state inside `Match()`
  - Resolves ladder config via `FindLadderConfig` (single-ladder shortcut or name match)
  - Computes each party's bracket: cold-start → `NeutralBracketMs`; else linear ramp capped at `MaxBracketMs`
  - **Cold-start exception:** when the candidate is cold-start, only the candidate's bracket must contain the diff (implements "matches anyone" intent of D-08). The pool entry's bracket is not checked.
  - Standard symmetric conjunctive overlap for non-cold-start candidates
  - Skips self (same TicketId)
  - `BuildMatchResult` copied verbatim from EloRange (CSPRNG team assignment)

---

## Key Discriminator Names (for 21-04 wiring)

| Class | Name | Field wired in ladder config |
|-------|------|------------------------------|
| `TimeMarginRankingAlgorithm` | `"time-margin"` | `RankingsLadderConfig.Algorithm = "time-margin"` |
| `BestTimeMatchmakingStrategy` | `"best-time"` | Registered via `services.Replace(...)` after `AddMatchmaking()` (A3) |

---

## Test Coverage

**TimeMarginRankingAlgorithmTests** (8 tests):
- `Name_IsTimeMargin_NotGlicko2` — discriminator assertion
- `WinLossDelta_WinnerGains_LoserLoses` — symmetric +KWin/-KWin
- `DrawEdge_ExactTie_ZeroRatingChange` — D-10 exact-tie = zero delta
- `Forfeit_TreatedAsLoss` — forfeit = −KWin
- `BatchedAccumulation_MultipleOutcomesSamePlayer` — D-11 two wins → +2*KWin in one Apply
- `Apply_DoesNotMutate_InputState` — immutability
- `UnknownPlayer_SeededAtDefault_ThenDeltaApplied` — default seeding
- `Rating_FlooredAtZero` — 0.0 floor

**BestTimeMatchmakingStrategyTests** (11 tests):
- `BestTimeMatchmakingStrategyResolutionTests` — Name="best-time", != "elo-range", correct type
- `BestTimeMatchmakingStrategyMatchTests` — in-window match returns non-null
- `OutOfWindow_ReturnsNull` — large diff returns null
- `QueueTimeWidening_EventuallyMatches` — bracket grows with wait time
- `ColdStart_NeutralBracket_MatchesAnyone` — cold candidate matches far opponent
- `ColdStart_BothCold_StillMatch` — two cold-start parties match
- `SelfMatch_IsNotAllowed` — TicketId skip
- `SymmetricConjunctive_BothBracketsMustFit` — conjunctive rule for non-cold-start
- `EmptyPool_ReturnsNull` — empty pool guard
- `Stateless_RepeatedCalls_EquivalentResults` — no state leakage
- `MatchResult_ContainsBothTickets` — result has both parties

**All 19 tests: PASSED**

---

## Deviations from Plan

### Auto-discovered: Cold-start conjunctive resolution

**Found during:** Task 2 test execution (ColdStart_NeutralBracket_MatchesAnyone)

**Issue:** The plan spec says both "matches anyone" (D-08 cold-start) AND "symmetric conjunctive overlap" (copy EloRange rule). These are contradictory when the pool entry has a small bracket and a very different AggregateRating.

**Fix:** Cold-start exception: when the CANDIDATE is cold-start (all members RD >= 300), the conjunctive check uses only the candidate's bracket (NeutralBracketMs = 60,000). The pool entry's bracket is not checked in this case. For non-cold-start candidates, the standard symmetric conjunctive rule applies.

**Rationale:** D-08's "matches anyone" is semantically stronger than the general symmetric rule — it is a deliberate override for onboarding. The RESEARCH states: "A fresh player with no PlayerRank row gets DefaultRd (e.g. 350) at enqueue; the strategy detects RD >= threshold and applies a neutral bracket."

**Files modified:** `samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs`

---

## Threat Mitigations

| Threat ID | Disposition | Evidence |
|-----------|-------------|---------|
| T-21-04 (DoS — convergence loop) | Mitigated | Fixed-delta is O(n), no loop; all tests verify single-pass accumulation |
| T-21-05 (Race — mutable singleton) | Mitigated | Both classes: only `readonly _ladders`; all per-call state local to method |
| T-21-06 (Info — Score field API change) | Accepted→Mitigated | Apply reads only Result; no Score field; D-09 amendment documented |

---

## Self-Check: PASSED

Files exist:
- `samples/Platformer3D/Algorithms/TimeMarginRankingAlgorithm.cs` — FOUND
- `samples/Platformer3D/Strategy/BestTimeMatchmakingStrategy.cs` — FOUND
- `tests/GameKit.Platformer3D.Tests/Rankings/TimeMarginRankingAlgorithmTests.cs` — FOUND
- `tests/GameKit.Platformer3D.Tests/Strategy/BestTimeMatchmakingStrategyTests.cs` — FOUND

Commits exist:
- `7710a50` — TimeMarginRankingAlgorithm — FOUND
- `4ce346f` — BestTimeMatchmakingStrategy — FOUND

Tests: 19 passed, 0 failed, 0 skipped
