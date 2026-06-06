---
phase: 08-rankings-depth-rating-aware-matchmaking
fixed_at: 2026-06-05T00:00:00Z
review_path: .planning/phases/08-rankings-depth-rating-aware-matchmaking/08-REVIEW.md
iteration: 1
findings_in_scope: 4
fixed: 4
skipped: 0
status: all_fixed
---

# Phase 8: Code Review Fix Report

**Fixed at:** 2026-06-05T00:00:00Z
**Source review:** `.planning/phases/08-rankings-depth-rating-aware-matchmaking/08-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 4
- Fixed: 4
- Skipped: 0

## Fixed Issues

### CR-01: MinPoolDepthBeforeBracketExpansion depth guard off-by-one

**Files modified:** `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs`, `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeGuardrailTests.cs`
**Commit:** 2bcd8e6
**Applied fix:**
Removed `- 1` from both pool-depth guard sites in `EloRangeMatchmakingStrategy.Match()`:
- Line ~94 (candidate bracket guard): `(pool.Count - 1) < cfg.MinPoolDepthBeforeBracketExpansion.Value` → `pool.Count < cfg.MinPoolDepthBeforeBracketExpansion.Value`
- Line ~123 (pool-entry bracket guard): same change

Updated `EloRangeGuardrailTests` to use candidate-exclusive pools (removing the candidate from the pool argument to `Match()`), matching production ticker semantics. The two affected tests (`Match_HoldsAtBracketStart_WhenPoolBelowMinDepth` and `Match_ExpandsBracket_WhenPoolMeetsMinDepth`) now genuinely exercise the correct threshold rather than masking the bug. Also updated inline comments at both guard sites to explain that `pool` is candidate-exclusive.

Build result: 0 warnings, 0 errors. Tests: 91/91 passed (GameKit.Matchmaking.Tests).

---

### WR-01: MaxBracketWidth < BracketStart not rejected at registration

**Files modified:** `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs`, `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs`, `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeGuardrailTests.cs`
**Commit:** 292c063
**Applied fix:**
Added guard in `ValidateLadderConfig`:
```csharp
if (config.MaxBracketWidth.HasValue && config.MaxBracketWidth.Value < config.BracketStart)
    throw new ArgumentException(
        $"{nameof(config.MaxBracketWidth)} ({config.MaxBracketWidth.Value}) must be >= {nameof(config.BracketStart)} ({config.BracketStart}) ...",
        nameof(config));
```

Updated `MaxBracketWidth` XML doc on `MatchmakingLadderConfig` to document the `>= BracketStart` constraint and the fail-fast rejection.

Updated the existing `AddLadder_Accepts_PositiveMaxBracketWidth` test (renamed to `AddLadder_Accepts_PositiveMaxBracketWidth_AtLeastBracketStart`) to set `MaxBracketWidth = 200` (>= BracketStart=100) since the old value of `1` would now correctly throw. Added two new unit tests:
- `AddLadder_Throws_WhenMaxBracketWidth_BelowBracketStart` — confirms the new guard fires for MaxBracketWidth=50 with BracketStart=100
- `AddLadder_Accepts_MaxBracketWidth_EqualToBracketStart` — confirms boundary case MaxBracketWidth==BracketStart is accepted

Build result: 0 warnings, 0 errors. Tests: 91/91 passed (GameKit.Matchmaking.Tests, all guardrail tests now 15/15).

---

### WR-02: Decay background service never renews lease between ladder batches

**Files modified:** `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs`
**Commit:** 6062eb9
**Applied fix:**
Added `_lease.RenewLeaseAsync(ct)` call before each ladder in the `foreach` loop in `RunOnceAsync`. If renewal returns `false`, logs a warning and `break`s out of the ladder loop (deferring remaining ladders to the next scheduled run). The `finally`-block `ReleaseLeaseAsync` is retained unchanged, ensuring the lock is always released regardless of whether the loop completed or broke early.

This mirrors the `RankingsTickerService` lease-renewal pattern and prevents the double-RD-inflation scenario where a pass exceeding `LockTtlSeconds` allows a standby replica to concurrently run decay and apply phi' = sqrt(phi^2 + sigma^2) twice (yielding phi'' = sqrt(phi^2 + 2*sigma^2)).

Build result: 0 warnings, 0 errors. RankDecay integration tests: 3/3 passed.

---

### IN-01: Dead Guid.Empty guard with misleading comment in PendingRatingUpdatesAdapter

**Files modified:** `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs`
**Commit:** 3b8b17c
**Applied fix:**
Removed the `if (participant.PlayerId == Guid.Empty) continue;` guard and its misleading "null check" comment. Replaced with an accurate comment explaining that `PlayerId` is a non-nullable `Guid` value type, the GDPR cascade sets `session_participants.PlayerId = NULL` in the DB post-completion (not at snapshot time), and no null-guard is needed here — distinguishing this from the ticker's `PendingRatingUpdate.PlayerId` which IS `Guid?`.

Also updated the class-level `<remarks>` GDPR-safety paragraph to accurately describe the non-nullable type and clarify the distinction from the ticker's nullable field.

Build result: 0 warnings, 0 errors.

---

## Build and Test Results

**Full solution build (`dotnet build GameKit.sln`):** 0 warnings, 0 errors

**Test results:**
| Suite | Passed | Failed | Notes |
|-------|--------|--------|-------|
| `GameKit.Matchmaking.Tests` (EloRangeGuardrailTests + all) | 91 | 0 | All green |
| `GameKit.Matchmaking.Integration.Tests` (RatingAwareEnqueue) | 68 | 0 | All green |
| `GameKit.Rankings.Integration.Tests` (RankDecay filter) | 3 | 0 | RankDecay tests all green |
| `GameKit.Rankings.Integration.Tests` (full suite) | 71 | 3 | 3 pre-existing failures in `LeaderboardServiceTests.TopAsync_Returns_Sorted_By_Rating_Desc` and `RankingsMigrationDeterminismTests.Apply_Then_ReApply_Produces_No_Diff` — confirmed present before any fix was applied; not caused by these changes |

---

_Fixed: 2026-06-05T00:00:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
