---
phase: 08-rankings-depth-rating-aware-matchmaking
reviewed: 2026-06-05T00:00:00Z
depth: deep
files_reviewed: 14
files_reviewed_list:
  - src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs
  - src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.Designer.cs
  - src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs
  - src/GameKit.Rankings/Services/RankDecayBackgroundService.cs
  - src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs
  - src/GameKit.Rankings/Services/RankingsRatingSource.cs
  - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.RatingSource.cs
  - src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs
  - src/GameKit.Rankings/GameKitRankingsOptions.cs
  - src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs
  - src/GameKit.Rankings/Entities/PlayerRank.cs
  - src/GameKit.Matchmaking/Services/MatchmakingService.cs
  - src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs
  - src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs
findings:
  critical: 1
  warning: 2
  info: 1
  total: 4
status: fixed
---

# Phase 8: Code Review Report

**Reviewed:** 2026-06-05T00:00:00Z
**Depth:** deep
**Files Reviewed:** 14
**Status:** issues_found

## Summary

Phase 8 adds rank-decay (RANK-15), placement matches (RANK-16), rating-aware matchmaking (MATCH-16/17), and the `RankingsRatingSource` / `WithRatingsFrom<T>` seam. The Glicko-2 decay math, the distributed lock design, migration data-fixup, placement decrement atomicity, and the rating round-trip through Redis are all correct. One correctness bug was found in the pool-depth guard for bracket expansion (`MinPoolDepthBeforeBracketExpansion`): the implementation uses `pool.Count - 1` but the ticker passes a candidate-exclusive pool, causing the guard to be off by one. Two warnings cover a missing validation invariant and a missing mid-run lease renewal in the decay service. One info item covers a dead code guard with a misleading comment.

---

## Critical Issues

### CR-01: `MinPoolDepthBeforeBracketExpansion` depth guard off-by-one — expansion requires one extra party

**File:** `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs:94` and `:123`

**Issue:**
`MatchmakerTickerService` builds `poolScratch` by excluding `candidates[i]` (the current candidate) before passing it to `strategy.Match(candidate, poolScratch, now)` (ticker lines 438–445). So `pool.Count` received by `Match()` is the count of OTHER parties only — the candidate is not in the list.

The depth guard reads:

```csharp
// line 94 (candidate bracket) and line 123 (pool-entry bracket)
if (cfg.MinPoolDepthBeforeBracketExpansion.HasValue
    && (pool.Count - 1) < cfg.MinPoolDepthBeforeBracketExpansion.Value)
{
    candidateElapsed = 0; // force bracket to BracketStart
}
```

The `- 1` subtracts one party from an already-candidate-exclusive count. The net effect: bracket expansion is suppressed until `pool.Count > MinPoolDepthBeforeBracketExpansion`, i.e., until there are `MinDepth + 1` OTHER parties (plus the candidate = `MinDepth + 2` total). The documented invariant and option description say "fewer than this many candidates" implies `MinDepth` others triggers expansion.

The unit tests in `EloRangeGuardrailTests.Match_ExpandsBracket_WhenPoolMeetsMinDepth` pass `pool = [candidate, other1, other2]` (candidate-inclusive, `pool.Count = 3`), so `3 - 1 = 2 >= 2 = MinDepth` — the test passes. But in production with a candidate-exclusive pool `[other1, other2]`, `pool.Count = 2`, `2 - 1 = 1 < 2` — the guard fires and expansion is suppressed even though the intended threshold is met. The test setup masks the production bug.

**Fix:**
Remove the `- 1` from both guard sites. The correct expression is:

```csharp
// line 94 — candidate bracket
if (cfg.MinPoolDepthBeforeBracketExpansion.HasValue
    && pool.Count < cfg.MinPoolDepthBeforeBracketExpansion.Value)
{
    candidateElapsed = 0;
}

// line 122–126 — pool-entry bracket (same change)
if (pCfg.MinPoolDepthBeforeBracketExpansion.HasValue
    && pool.Count < pCfg.MinPoolDepthBeforeBracketExpansion.Value)
{
    poolElapsed = 0;
}
```

Also update the unit tests to pass candidate-exclusive pools (matching production behaviour) so the guard at `pool.Count < MinDepth` is exercised correctly.

---

## Warnings

### WR-01: `MaxBracketWidth < BracketStart` not rejected at registration — silently undercuts the initial bracket

**File:** `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs:84–87`

**Issue:**
`ValidateLadderConfig` checks `MaxBracketWidth > 0` but does not check `MaxBracketWidth >= BracketStart`. If an operator sets `MaxBracketWidth = 50` with `BracketStart = 100`, `EloRangeMatchmakingStrategy.Bracket()` returns `min(BracketStart, MaxBracketWidth) = 50` at `t = 0`, making the initial bracket narrower than `BracketStart`. No match requiring a spread of 51–100 rating points will ever succeed, regardless of wait time — the cap is never documented as "may undercut BracketStart," and the `BracketStart` setting becomes misleading.

**Fix:**
Add a guard to `ValidateLadderConfig`:

```csharp
if (config.MaxBracketWidth.HasValue && config.MaxBracketWidth.Value < config.BracketStart)
    throw new ArgumentException(
        $"{nameof(config.MaxBracketWidth)} ({config.MaxBracketWidth.Value}) must be >= {nameof(config.BracketStart)} ({config.BracketStart}) when set, or omit MaxBracketWidth to leave BracketStart effective.",
        nameof(config));
```

Also update the `MaxBracketWidth` XML doc on `MatchmakingLadderConfig` to document this constraint.

---

### WR-02: Decay background service never renews the Redis lease between ladder batches — diverges from ticker's safety pattern

**File:** `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs:155–165`

**Issue:**
`RankingsTickerService.RunOnceAsync` renews the Redis lease before processing each ladder (`_lease.RenewLeaseAsync()` called inside the ladder loop, line 180 of `RankingsTickerService.cs`). `RankDecayBackgroundService.RunOnceAsync` acquires the lease once (line 124) and never renews it during the subsequent per-ladder loop (lines 155–159). If the combined decay pass across all active ladders takes longer than `Decay.LockTtlSeconds` (default 120 s), the lock expires; a standby replica can acquire it and begin a parallel decay run. Both replicas would then apply `φ' = √(φ² + σ²)` to the same candidate set, double-inflating RD: the result is `φ'' = √(φ² + 2σ²)` instead of `√(φ² + σ²)`.

With the default `BatchSize = 500` and the 120 s TTL, the practical risk is low (500 rows update in well under 1 s on local Postgres), but the safety gap grows in proportion to `BatchSize` and the number of active ladders and is not bounded by configuration.

**Fix:**
Mirror the ticker pattern: add a `_lease.RenewLeaseAsync(ct)` call before each ladder in the decay loop, and abort the remaining ladders if renewal fails:

```csharp
foreach (var ladder in allActiveLadders)
{
    ct.ThrowIfCancellationRequested();

    // Renew lease before each ladder batch — mirrors ticker pattern.
    var renewed = await _lease.RenewLeaseAsync(ct).ConfigureAwait(false);
    if (!renewed)
    {
        _logger.LogWarning(
            "RankDecayBackgroundService: lease lost mid-run before ladder {LadderId}. Deferring remaining ladders.",
            ladder.Id);
        break;
    }

    await DecayLadderAsync(ctx, ladder.Id, now, ct).ConfigureAwait(false);
}
```

---

## Info

### IN-01: GDPR guard in `PendingRatingUpdatesAdapter` is dead code with a misleading comment

**File:** `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs:79–82`

**Issue:**
`SessionParticipantSnapshot.PlayerId` is `Guid` (a non-nullable value type). The guard `if (participant.PlayerId == Guid.Empty) continue;` can never fire in normal usage — `UUIDv7` generated by `IIdGenerator` is never the zero UUID, and the session-complete path constructs the snapshot from the API request's `PlayerId` which is already validated non-empty. The XML comment above the guard says "null PlayerId" which is factually wrong (the type cannot be null).

The guard is a copy of a pattern from the ticker's `PendingRatingUpdate.PlayerId` check (which IS `Guid?`, nullable), applied here to a non-nullable `Guid` by mistake.

**Fix:**
Remove the dead guard and replace the comment to accurately reflect the actual GDPR concern (the DB column `session_participants.PlayerId` can be set to `NULL` by the cascade, but that happens post-enqueue and does not affect this adapter):

```csharp
// PlayerId is Guid (non-nullable); GDPR cascade sets session_participants.PlayerId = NULL
// in the DB post-completion, but this snapshot is built before that happens (D-22 ordering).
// No null-guard needed here.
```

---

_Reviewed: 2026-06-05T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
