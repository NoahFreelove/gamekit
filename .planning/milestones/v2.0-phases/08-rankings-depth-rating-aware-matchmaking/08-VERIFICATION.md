---
phase: 08-rankings-depth-rating-aware-matchmaking
verified: 2026-06-06T00:37:26Z
status: passed
score: 5/5
overrides_applied: 0
---

# Phase 8: Rankings Depth + Rating-Aware Matchmaking — Verification Report

**Phase Goal:** The `player_ranks` schema reaches its final v2.0 shape (decay + placement columns added), real ratings flow into the matchmaking bracket, and guardrails ship alongside the rating wire — no feedback-loop risk.
**Verified:** 2026-06-06T00:37:26Z
**Status:** passed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| #  | Truth                                                                                                     | Status     | Evidence                                                                                                                                              |
|----|-----------------------------------------------------------------------------------------------------------|------------|-------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1  | Inactive player's RD inflates (not rating) after decay BackgroundService runs; scale-correct ÷173.7178   | ✓ VERIFIED | `RankDecayBackgroundService` lines 210–213: `phiG2 = RD/173.7178 → phiPrimeG2 = sqrt(phi²+σ²) → RD = phiPrimeG2*173.7178`; Rating never written. `Glicko2InactivityTests`: 3/3 pass; `RankDecayTests.Decay_InflatesRD_LeavesRatingConstant_StampsLastDecayAt`: 1/1 pass (integration). |
| 2  | New player in placement has `Rating`/`RatingDeviation` null in API responses; decrement is atomic        | ✓ VERIFIED | `LeaderboardService` lines 68–69: `Rating: row.rank.IsInPlacement ? (double?)null : row.rank.Rating`; `LeaderboardRowDto.Rating` is `double?`. `PendingRatingUpdatesAdapter` lines 107–118: `ExecuteUpdateAsync` with compound `WHERE IsInPlacement AND PlacementMatchesRemaining > 0` race guard. `PlacementMatchTests`: 5/5 pass. |
| 3  | `.WithRatingsFrom<RankingsRatingSource>()` injects real Glicko-2 ratings at enqueue; omitting gives zero-rating fallback | ✓ VERIFIED | `RankingsBuilderExtensions.RatingSource.cs`: `RemoveAll<IPlayerRatingProvider>() + AddScoped<IPlayerRatingProvider, T>()`. `MatchmakingService` lines 211–214: resolves `_ratingProvider?.GetRatingsAsync(...)` with `rv?.Rating ?? 0` fallback. `RatingAwareEnqueueTests.Enqueue_WritesRealRating_IntoTicketHash` + `Enqueue_ZeroRating_Fallback_WhenWithoutRankings`: 2/3 cross-package tests pass (3rd below). |
| 4  | `MaxBracketWidth` cap + `MinPoolDepthBeforeBracketExpansion` enforced simultaneously with real-rating injection; bracket expansion stops at cap regardless of pool depth | ✓ VERIFIED | `EloRangeMatchmakingStrategy.Bracket()` line 197: `Math.Min(capped, MaxBracketWidth.Value)`. `Match()` lines 93–96: depth guard resets elapsed to 0 when `pool.Count-1 < MinPoolDepthBeforeBracketExpansion`. `GameKitMatchmakingBuilder` lines 84–91: fail-fast validation. `EloRangeGuardrailTests`: 13/13 pass; `RatingAwareEnqueueTests.BracketExpansion_StopsAt_MaxBracketWidth_RegardlessOfPoolDepth`: 1/1 pass. |
| 5  | `player_ranks` schema finalized: `last_decay_at`, `placement_matches_remaining`, `is_in_placement` columns added; no Core/Auth tables touched | ✓ VERIFIED | Migration `20260517000000_RankingsDecayPlacement.cs` Up(): only `ALTER TABLE gamekit.player_ranks` + `CREATE INDEX idx_player_ranks_decay_candidates`. No Core/Auth table modifications. `SchemaFreezeTests`: 5/5 pass (columns + index + history table entry verified against Testcontainers Postgres). |

**Score:** 5/5 truths verified

---

### Required Artifacts

| Artifact                                                                       | Expected                                       | Status     | Details                                                  |
|--------------------------------------------------------------------------------|------------------------------------------------|------------|----------------------------------------------------------|
| `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs`                  | Leader-elected decay service (RANK-15)         | ✓ VERIFIED | 222 lines; PeriodicTimer + RankDecayLeaseHelper; scale-correct Glicko-2 inactivity step; internal RunOnceAsync for tests |
| `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs`                        | Redis distributed lock helper                  | ✓ VERIFIED | 173 lines; LockTakeAsync/LockReleaseAsync; Polly v8 retry; dedicated `gamekit:rankings:decay:lease` key |
| `src/GameKit.Rankings/Services/RankingsRatingSource.cs`                        | IPlayerRatingProvider backed by player_ranks   | ✓ VERIFIED | 82 lines; single batched AsNoTracking SELECT; omits players with no rank row |
| `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs`                 | Atomic placement decrement (RANK-16)           | ✓ VERIFIED | ExecuteUpdateAsync with compound WHERE guard; SetProperty IsInPlacement flip; no BeginTransaction |
| `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.RatingSource.cs`       | WithRatingsFrom<T>() partial class             | ✓ VERIFIED | RemoveAll + AddScoped pattern; XML doc correct |
| `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Decay.cs`              | AddDecayInfrastructure DI wiring               | ✓ VERIFIED | AddSingleton<RankDecayLeaseHelper> + AddHostedService<RankDecayBackgroundService> |
| `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs`     | Schema freeze migration                        | ✓ VERIFIED | Raw SQL Up(): ADD COLUMN (3) + data-fixup + partial index; Down() rolls back; deterministic timestamp |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs`                       | Rating-aware EnqueueAsync (MATCH-16)           | ✓ VERIFIED | IPlayerRatingProvider? optional ctor param; GetRatingsAsync called at Step 4; rv?.Rating ?? 0 fallback |
| `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs`              | MaxBracketWidth cap + depth guard (MATCH-17)   | ✓ VERIFIED | Bracket(): Math.Min chain; Match(): pool.Count-1 guard applied symmetrically |
| `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs`                   | MaxBracketWidth + MinPoolDepthBeforeBracketExpansion fields | ✓ VERIFIED | Both `int?` nullable fields with full XML docs |
| `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs`                     | Nullable Rating/RD + placement fields          | ✓ VERIFIED | `double? Rating`, `double? RatingDeviation`, `bool IsInPlacement`, `int PlacementMatchesRemaining` |
| `tests/GameKit.Rankings.Tests/Glicko2/Glicko2InactivityTests.cs`              | Glickman inactivity formula unit tests         | ✓ VERIFIED | 3 tests; scale-correct range 290.1..290.3 |
| `tests/GameKit.Rankings.Integration.Tests/SchemaFreezeTests.cs`               | Schema freeze integration proof                | ✓ VERIFIED | 5 tests; columns + index + history table against Testcontainers Postgres |
| `tests/GameKit.Rankings.Integration.Tests/RankDecayTests.cs`                  | RD inflation + exclusion + lock-key tests      | ✓ VERIFIED | 3 tests; dedicated lock key non-collision proven |
| `tests/GameKit.Rankings.Integration.Tests/PlacementMatchTests.cs`             | Atomic placement decrement tests               | ✓ VERIFIED | 5 tests |
| `tests/GameKit.Rankings.Integration.Tests/RankingsRatingSourceTests.cs`       | Rating projection integration proof            | ✓ VERIFIED | 3 tests; exact Rating/RD/Volatility; absent-player omission |
| `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeGuardrailTests.cs`          | MATCH-17 guardrail unit tests                  | ✓ VERIFIED | 13 tests; bracket cap + depth guard + builder validation |
| `tests/GameKit.Matchmaking.Integration.Tests/RatingAwareEnqueueTests.cs`      | Cross-package SC#3/SC#4 proof                  | ✓ VERIFIED | 3 tests; real rating in Redis hash; zero-rating fallback; bracket cap on real ratings |

---

### Key Link Verification

| From                              | To                          | Via                                   | Status     | Details                                                                          |
|-----------------------------------|-----------------------------|---------------------------------------|------------|----------------------------------------------------------------------------------|
| `MatchmakingService.EnqueueAsync` | `IPlayerRatingProvider`     | `_ratingProvider?.GetRatingsAsync()`  | ✓ WIRED    | Step 4 in EnqueueAsync (lines 210–214); Core seam only, no Rankings using import |
| `RankingsRatingSource`            | `player_ranks` table        | `GameKitDbContext.Set<PlayerRank>()`  | ✓ WIRED    | Single batched AsNoTracking query; returns `PlayerRatingValue` dict              |
| `.WithRatingsFrom<T>()`           | `IPlayerRatingProvider` DI  | `RemoveAll + AddScoped`               | ✓ WIRED    | Overrides Core TryAddSingleton null-object correctly                             |
| `RankDecayBackgroundService`      | `player_ranks` DB           | `IServiceScopeFactory` + tracked entities | ✓ WIRED | RunOnceAsync opens scope, loads tracked PlayerRank, SaveChangesAsync             |
| `RankDecayLeaseHelper`            | Redis lease                 | `LockTakeAsync/LockReleaseAsync`      | ✓ WIRED    | Dedicated key `gamekit:rankings:decay:lease` distinct from ticker key            |
| `PendingRatingUpdatesAdapter`     | `player_ranks` placement    | `ExecuteUpdateAsync` WHERE guard      | ✓ WIRED    | Atomic SQL; rides caller's ambient ReadCommitted transaction                     |
| `LeaderboardService`              | `LeaderboardRowDto.Rating`  | `IsInPlacement ? null : rank.Rating`  | ✓ WIRED    | Applied at all 4 projection sites in LeaderboardService                          |
| `AddDecayInfrastructure`          | `AddRankings` call chain    | Called at step 8 in RankingsBuilderExtensions.cs | ✓ WIRED | Singleton + HostedService registered automatically with Rankings                |

**Dependency boundary check:** `MatchmakingService.cs` has zero `using GameKit.Rankings` imports — the rating seam flows exclusively through `GameKit.Core.Services.IPlayerRatingProvider`. The `ProjectReference` to Rankings in `GameKit.Matchmaking.csproj` is pre-existing (present since Phase 5, commit `5bdc0c5`) and annotated as a design-time boundary reference, not a new runtime coupling introduced by Phase 8.

---

### Data-Flow Trace (Level 4)

| Artifact                              | Data Variable        | Source                                  | Produces Real Data | Status      |
|---------------------------------------|----------------------|-----------------------------------------|--------------------|-------------|
| `MatchmakingService.EnqueueAsync`     | `queuedMembers`      | `IPlayerRatingProvider.GetRatingsAsync` | Yes — queries `player_ranks` via `RankingsRatingSource` | ✓ FLOWING |
| `RankDecayBackgroundService`          | `candidates`         | `ctx.Set<PlayerRank>().Where(...)`      | Yes — tracked EF query with filter + batch                | ✓ FLOWING |
| `LeaderboardService` (live rank)      | `Rating`             | `row.rank.IsInPlacement ? null : row.rank.Rating` | Yes — real player_ranks rows; null only during placement  | ✓ FLOWING |
| `PendingRatingUpdatesAdapter`         | `PlacementMatchesRemaining` | `ExecuteUpdateAsync` SQL SET          | Yes — DB-side decrement with race guard                   | ✓ FLOWING |

---

### Behavioral Spot-Checks

| Behavior                                                                 | Command                                                                                                  | Result          | Status  |
|--------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------|-----------------|---------|
| Glicko-2 inactivity formula: RD inflates, rating constant               | `dotnet test GameKit.Rankings.Tests --filter "FullyQualifiedName~Glicko2Inactivity"`                    | 3 passed        | ✓ PASS  |
| EloRange guardrails: MaxBracketWidth cap + depth guard + builder validation | `dotnet test GameKit.Matchmaking.Tests --filter "FullyQualifiedName~EloRangeGuardrail"`              | 13 passed       | ✓ PASS  |
| Schema freeze: 3 columns + index + history entry in real Postgres        | `dotnet test GameKit.Rankings.Integration.Tests --filter "FullyQualifiedName~SchemaFreeze"`             | 5 passed        | ✓ PASS  |
| Decay: RD inflates + exclusions correct + lock key non-collision         | `dotnet test GameKit.Rankings.Integration.Tests --filter "FullyQualifiedName~RankDecay"`                | 3 passed        | ✓ PASS  |
| Placement atomic decrement                                               | `dotnet test GameKit.Rankings.Integration.Tests --filter "FullyQualifiedName~PlacementMatch"`           | 5 passed        | ✓ PASS  |
| RankingsRatingSource real-DB projection                                  | `dotnet test GameKit.Rankings.Integration.Tests --filter "FullyQualifiedName~RankingsRatingSource"`     | 3 passed        | ✓ PASS  |
| Real ratings in Redis ticket hash; zero-rating fallback; bracket cap     | `dotnet test GameKit.Matchmaking.Integration.Tests --filter "FullyQualifiedName~RatingAwareEnqueue"`    | 3 passed        | ✓ PASS  |
| Full solution build                                                      | `dotnet build GameKit.sln --nologo`                                                                      | 0 warnings / 0 errors | ✓ PASS |

---

### Requirements Coverage

| Requirement | Source Plan | Description                                                           | Status      | Evidence                                                     |
|-------------|-------------|-----------------------------------------------------------------------|-------------|--------------------------------------------------------------|
| RANK-15     | 08-01, 08-02 | Configurable rank decay as Glicko-2 RD inflation, leader-elected BackgroundService | ✓ SATISFIED | RankDecayBackgroundService + RankDecayLeaseHelper + 3 integration tests |
| RANK-16     | 08-01, 08-03 | Placement matches — visible rank hidden until N placements complete   | ✓ SATISFIED | LeaderboardRowDto nullable Rating/RD + PendingRatingUpdatesAdapter atomic decrement + 5 tests |
| RANK-17     | 08-03       | RankingsRatingSource : IPlayerRatingProvider + .WithRatingsFrom<>()   | ✓ SATISFIED | RankingsRatingSource + RankingsBuilderExtensions.RatingSource.cs + 6 tests |
| MATCH-16    | 08-04       | Rating-aware EloRange reads real ratings via IPlayerRatingProvider    | ✓ SATISFIED | MatchmakingService Step 4 GetRatingsAsync + cross-package integration test |
| MATCH-17    | 08-04       | MaxBracketWidth + MinPoolDepthBeforeBracketExpansion ship in same plan as MATCH-16 | ✓ SATISFIED | EloRangeMatchmakingStrategy + MatchmakingLadderConfig + 13 unit + 1 integration tests |

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None found | — | — | — | Zero TBD/FIXME/XXX markers. Zero unimplemented stubs. Zero placeholder returns. All data paths flow to real Postgres/Redis. |

---

### Human Verification Required

None. The VALIDATION.md phase document explicitly states: *"All Phase 8 behaviors have automated verification (Testcontainers Postgres + Redis). None require external credentials."* All 5 success criteria have automated test coverage confirmed passing in this verification session.

---

## Gaps Summary

None. All 5 success criteria are fully verified by evidence in the codebase and confirmed by live test execution:

- **SC#1** (RD inflation): Glicko-2 scale-correct formula in `RankDecayBackgroundService`; 3 unit + 3 integration tests green.
- **SC#2** (placement hiding + atomic decrement): `LeaderboardService` null projection + `PendingRatingUpdatesAdapter` ExecuteUpdateAsync race guard; 5 integration tests green.
- **SC#3** (WithRatingsFrom wiring + fallback): `RankingsRatingSource` + `RankingsBuilderExtensions.RatingSource.cs` RemoveAll+AddScoped; consumed in `MatchmakingService` via Core seam; cross-package integration tests green.
- **SC#4** (bracket cap + depth guard simultaneous with SC#3): `EloRangeMatchmakingStrategy` Math.Min chain + pool.Count guard; fail-fast builder validation; 13 unit + 1 integration tests green.
- **SC#5** (schema frozen): `20260517000000_RankingsDecayPlacement` adds only 3 columns to `player_ranks`; no Core/Auth table modifications; 5 Testcontainers integration tests confirm column existence, index, and history table entry.

---

_Verified: 2026-06-06T00:37:26Z_
_Verifier: Claude (gsd-verifier)_
