---
phase: 08-rankings-depth-rating-aware-matchmaking
plan: "04"
subsystem: matchmaking
tags: [matchmaking, ratings, guardrails, elo-range, cross-package]
dependency_graph:
  requires: ["08-01", "08-03"]
  provides: ["MATCH-16", "MATCH-17", "SC#3", "SC#4"]
  affects: ["GameKit.Matchmaking", "tests/GameKit.Matchmaking.Tests", "tests/GameKit.Matchmaking.Integration.Tests"]
tech_stack:
  added: []
  patterns:
    - "Optional ctor param injection (IPlayerRatingProvider?) — null-object covers no-Rankings install"
    - "Math.Min cap chain in Bracket() — first BracketEnd, then MaxBracketWidth"
    - "pool.Count - 1 depth guard in Match() — symmetrically applied to candidate and pool entries"
    - "MatchmakingTestApp.withRankingsRatingSource ctor param — opt-in for WithRatingsFrom<> wiring"
key_files:
  created:
    - tests/GameKit.Matchmaking.Tests/Strategy/EloRangeGuardrailTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/RatingAwareEnqueueTests.cs
  modified:
    - src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs
    - src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs
    - src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs
    - src/GameKit.Matchmaking/Services/MatchmakingService.cs
    - tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs
decisions:
  - "MaxBracketWidth and MinPoolDepthBeforeBracketExpansion validated at AddLadder time (fail-fast T-08-04-03)"
  - "Pool-depth guard applied symmetrically to both candidate bracket and pool-entry bracket — prevents asymmetric expansion"
  - "IPlayerRatingProvider? optional ctor param (not required) — null-object from Core satisfies production DI; test convenience preserved"
  - "RatingAwareEnqueueTests drives EloRangeMatchmakingStrategy.Match() directly for SC#4 cap proof — no ticker needed"
  - "MatchmakingTestApp extended with withRankingsRatingSource ctor param rather than a parallel test app class"
metrics:
  duration_minutes: 8
  tasks_completed: 3
  files_modified: 7
  completed_date: "2026-06-06"
---

# Phase 8 Plan 04: MATCH-16 Rating-Aware Enqueue + MATCH-17 Guardrails Summary

**One-liner:** Rating-aware EnqueueAsync resolves IPlayerRatingProvider (Core seam) and caches real Glicko-2 ratings into the Redis ticket hash; MaxBracketWidth + MinPoolDepthBeforeBracketExpansion guardrails ship in the same plan to close the anti-feedback-loop gap.

## What Was Built

### MATCH-17 Guardrails (Task 1)

**`MatchmakingLadderConfig`** — two new optional-int fields appended after `MaxPartyRatingSpread`:
- `MaxBracketWidth`: hard cap on bracket half-width; bracket widening never exceeds this value regardless of wait time. Prevents high-RD new players from being matched against top-rated players on sparse pools.
- `MinPoolDepthBeforeBracketExpansion`: when the pool has fewer than this many candidates, bracket stays at `BracketStart` regardless of wait time. Both default `null` (v1 behaviour unchanged).

**`EloRangeMatchmakingStrategy`**:
- `Bracket()`: after `Math.Min(raw, BracketEnd)`, applies `Math.Min(capped, MaxBracketWidth.Value)` when `HasValue`.
- `Match()`: before computing `candidateBracket` and `poolBracket`, resets elapsed to `0` (→ `BracketStart`) when `pool.Count - 1 < MinPoolDepthBeforeBracketExpansion.Value`. Guard applied symmetrically to both brackets.

**`GameKitMatchmakingBuilder.ValidateLadderConfig`**: two new checks rejecting `MaxBracketWidth <= 0` and `MinPoolDepthBeforeBracketExpansion <= 0` at `AddLadder` time with messages naming the field and instructing `use null to disable`.

**Unit tests** (`EloRangeGuardrailTests`): 13 tests covering bracket cap wins over BracketEnd, null cap falls back to v1, pool-depth guard holds at BracketStart, depth-meets threshold allows expansion, and builder validation for 0/negative/null values.

### MATCH-16 Rating-Aware Enqueue (Task 2)

**`MatchmakingService`**:
- Added `IPlayerRatingProvider? _ratingProvider` field.
- 10th optional ctor param `IPlayerRatingProvider? ratingProvider = null` (after logger).
- Replaced hardcoded `Rating: 0, RatingDeviation: 0, Volatility: 0` with: resolve `ratingMap` via `_ratingProvider.GetRatingsAsync(memberPlayerIds, ladderId, ct)`, then select each member using `ratingMap.TryGetValue(pid, out var rv)` with `rv?.Rating ?? 0` fallback.
- Redis hash write (lines 263–276) unchanged — `members` JSON + `aggregateRating` already consume `queuedMembers`, so real ratings flow through automatically.
- No new runtime `ProjectReference` to `GameKit.Rankings` (Core seam only). `GameKit.Matchmaking.csproj` unchanged: 1 pre-existing `ProjectReference` to `GameKit.Rankings` for design-time boundary (unchanged from 05-02).

### Cross-Package Testcontainers Tests (Task 3)

**`RatingAwareEnqueueTests`** (3 tests, Postgres + Redis via Testcontainers):
- `Enqueue_WritesRealRating_IntoTicketHash` (SC#3): seeds `player_ranks` with known Rating=1750/RD=95.5/Volatility=0.052; enqueues via HTTP; reads back Redis ticket hash `members` JSON; asserts exact values present (not 0).
- `Enqueue_ZeroRating_Fallback_WhenWithoutRankings` (SC#3 fallback): app without `WithRatingsFrom`; asserts all members have Rating=0/RD=0/Volatility=0 with no exception.
- `BracketExpansion_StopsAt_MaxBracketWidth_RegardlessOfPoolDepth` (SC#4): drives `EloRangeMatchmakingStrategy.Match` with `MaxBracketWidth=200`, `BracketEnd=500`; gap=300 → no match; gap=50 → match; also enqueues a player with real rating and asserts `aggregateRating` on the ticket hash is non-zero (proves MATCH-17 guardrails operate on real ratings).

**`MatchmakingTestApp`**: extended with `withRankingsRatingSource = false` ctor param; when true, chains `.WithRatingsFrom<RankingsRatingSource>()` onto the `AddRankings()` builder.

## Deviations from Plan

None — plan executed exactly as written.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes introduced. All code paths are internal to existing layers. Threat mitigations from the plan's STRIDE register:

| Threat ID | Status |
|-----------|--------|
| T-08-04-01 (rating source = server-side IPlayerRatingProvider) | Mitigated — client request carries no rating field; ratings are fetched server-side from `player_ranks` |
| T-08-04-02 (rating feedback loop / matchmaking starvation) | Mitigated — MaxBracketWidth + MinPoolDepthBeforeBracketExpansion ship in this plan; SC#4 asserts |
| T-08-04-03 (MaxBracketWidth=0 misconfiguration) | Mitigated — fail-fast validation at AddLadder |
| T-08-04-04 (no hard Rankings dep) | Mitigated — grep confirms 0 new runtime ProjectReferences to Rankings |

## Self-Check: PASSED

- `/home/noah/Desktop/projects/gamekit/src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` — `grep -q MaxBracketWidth` PASS
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` — `grep -q cfg.MaxBracketWidth.HasValue` PASS
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs` — `grep -q MaxBracketWidth.Value <= 0` PASS
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Matchmaking/Services/MatchmakingService.cs` — zero hardcoded `Rating: 0` zero-fills PASS
- `/home/noah/Desktop/projects/gamekit/tests/GameKit.Matchmaking.Tests/Strategy/EloRangeGuardrailTests.cs` — 13 tests pass PASS
- `/home/noah/Desktop/projects/gamekit/tests/GameKit.Matchmaking.Integration.Tests/RatingAwareEnqueueTests.cs` — 3 tests pass PASS
- Commits: 40cf993 (RED test), d01b96c (MATCH-17 impl), 0175fb8 (MATCH-16 impl), d1e9c20 (integration tests) — all verified in git log PASS
