---
phase: 08-rankings-depth-rating-aware-matchmaking
plan: 01
subsystem: database
tags: [glicko2, efcore, migrations, rankings, placement, decay, postgres, testcontainers]

requires:
  - phase: 04-rankings-core
    provides: PlayerRank entity, player_ranks table, EF migration infrastructure, RankingsTickerService lazy-create site
  - phase: 07-core-rating-seam-stateless-auth-packages
    provides: IPlayerRatingProvider seam in Core

provides:
  - player_ranks schema frozen with last_decay_at, placement_matches_remaining, is_in_placement columns (SC#5)
  - GameKitRankingsDecayOptions nested class with all phase-8 options surface (Interval, LockKey, PlacementMatchCount, etc.)
  - Visible-rank hiding in LeaderboardRowDto (Rating/RatingDeviation nullable while IsInPlacement)
  - Glicko-2 inactivity step unit test (RANK-15 formula gate)
  - SchemaFreezeTests integration test (columns + index + history table verified against Testcontainers Postgres)

affects:
  - 08-02: decay background service reads GameKitRankingsDecayOptions
  - 08-03: placement decrement reads GameKitRankingsDecayOptions.PlacementMatchCount
  - 08-04: rating-aware matchmaking reads updated leaderboard DTOs
  - phase-10: account merge reads frozen player_ranks schema

tech-stack:
  added: []
  patterns:
    - "Raw SQL migration Up() for ADD COLUMN + data-fixup + partial index (same as RankingsInitial partial-index pattern)"
    - "GameKitRankingsDecayOptions nested class added to GameKitRankingsOptions root, following GameKitRankingsTickerOptions pattern"
    - "Nullable double? Rating/RatingDeviation in leaderboard DTO with IsInPlacement ? null projection"

key-files:
  created:
    - src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs
    - src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.Designer.cs
    - tests/GameKit.Rankings.Tests/Glicko2/Glicko2InactivityTests.cs
    - tests/GameKit.Rankings.Integration.Tests/SchemaFreezeTests.cs
  modified:
    - src/GameKit.Rankings/Entities/PlayerRank.cs
    - src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs
    - src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs
    - src/GameKit.Rankings/GameKitRankingsOptions.cs
    - src/GameKit.Rankings/Services/RankingsTickerService.cs
    - src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs
    - src/GameKit.Rankings/Services/LeaderboardService.cs
    - tests/GameKit.Rankings.Integration.Tests/SeasonArchiveLeaderboardTests.cs

key-decisions:
  - "Migration uses raw SQL (migrationBuilder.Sql) per PATTERNS.md — not EF Core AddColumn — for ADD COLUMN + data-fixup + partial index"
  - "Existing players with Wins>0 OR Losses>0 OR Draws>0 set to IsInPlacement=false/PlacementMatchesRemaining=0 in migration data-fixup (Pitfall 2)"
  - "Glicko-2 inactivity test range corrected to 290.1..290.3; PATTERNS.md stated 290.5..291.0 which is arithmetically wrong (phi/M=290/173.7178=1.6694; result=1.6705*173.7178=290.19)"
  - "Season archive LeaderboardRowDto projections pass IsInPlacement=false/PlacementMatchesRemaining=0 (archive rows are always past placement)"
  - "EF CLI generated timestamp 20260605xxx renamed to 20260517000000 (deterministic cross-package ordering convention)"

patterns-established:
  - "Frozen schema columns: add to entity → EF config → migration raw SQL → Designer attribute fix → snapshot update"
  - "Test range validation: always verify expected numeric ranges with actual computation before committing"

requirements-completed: [RANK-15, RANK-16]

duration: 7min
completed: 2026-06-05
---

# Phase 8 Plan 01: player_ranks Schema Freeze + Decay Options + Placement DTO Summary

**player_ranks schema frozen with decay/placement columns via raw-SQL migration, GameKitRankingsDecayOptions added as full phase-8 options surface, visible-rank hiding wired in LeaderboardRowDto, and Glickman inactivity formula proven by scale-correct unit test**

## Performance

- **Duration:** 7 min
- **Started:** 2026-06-05T23:52:41Z
- **Completed:** 2026-06-05T23:59:41Z
- **Tasks:** 3
- **Files modified:** 12

## Accomplishments

- Migration `20260517000000_RankingsDecayPlacement` adds `LastDecayAt` (timestamptz null), `PlacementMatchesRemaining` (int default 10), `IsInPlacement` (bool default true) to `player_ranks`, with a data-fixup that sets existing game-history players to `IsInPlacement=false` and a partial index `idx_player_ranks_decay_candidates ON (LadderId, LastMatchAt) WHERE IsInPlacement=false`
- `GameKitRankingsDecayOptions` nested class adds the complete phase-8 options surface (Interval=24h, LockKey=`gamekit:rankings:decay:lease`, DecayThresholdRating=1500, InactivityDays=30, BatchSize=500, PlacementMatchCount=10); wired as `GameKitRankingsOptions.Decay`
- Lazy rank creation in `RankingsTickerService` now initializes `IsInPlacement=true` and `PlacementMatchesRemaining=opts.Decay.PlacementMatchCount`
- `LeaderboardRowDto` gains `bool IsInPlacement` + `int PlacementMatchesRemaining`; `Rating` and `RatingDeviation` are `double?` (null while `IsInPlacement=true` — T-08-01-01 mitigation)
- `SchemaFreezeTests` proves all three columns, the partial index, and the migration history table entry against a real Testcontainers Postgres instance

## Task Commits

1. **Task 1: entity + config + options + lazy-create defaults** - `d642e56` (feat)
2. **Task 2: migration + Glicko inactivity unit test** - `102d915` (feat)
3. **Task 3: placement-aware DTO + LeaderboardService + SchemaFreezeTests** - `84b0d41` (feat)

## Files Created/Modified

- `src/GameKit.Rankings/Entities/PlayerRank.cs` — three new properties: LastDecayAt, PlacementMatchesRemaining, IsInPlacement
- `src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs` — EF config for the three new properties
- `src/GameKit.Rankings/GameKitRankingsOptions.cs` — GameKitRankingsDecayOptions class + Decay property on root
- `src/GameKit.Rankings/Services/RankingsTickerService.cs` — lazy-create sets IsInPlacement=true + PlacementMatchesRemaining
- `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs` — raw SQL Up()/Down() migration
- `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.Designer.cs` — migration designer with correct attribute
- `src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs` — updated with three new PlayerRank properties
- `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` — nullable Rating/RatingDeviation + IsInPlacement + PlacementMatchesRemaining
- `src/GameKit.Rankings/Services/LeaderboardService.cs` — null-while-placement projection at all live-rank sites
- `tests/GameKit.Rankings.Tests/Glicko2/Glicko2InactivityTests.cs` — three unit tests proving scale-correct RD inflation
- `tests/GameKit.Rankings.Integration.Tests/SchemaFreezeTests.cs` — four integration tests (columns + index + history)
- `tests/GameKit.Rankings.Integration.Tests/SeasonArchiveLeaderboardTests.cs` — fixed Rating comparison for double?

## Decisions Made

- Raw SQL `migrationBuilder.Sql()` used for all three migration operations (ADD COLUMN, data-fixup, index) — consistent with RankingsInitial partial index pattern and deterministic vs EF Core code generation
- `IsInPlacement=false` for existing players is determined by `Wins>0 OR Losses>0 OR Draws>0` — any game played means placement is done (matches RESEARCH §Assumption A1)
- Season archive rows in LeaderboardService pass `IsInPlacement=false/PlacementMatchesRemaining=0` since archived ranks are always post-placement
- `GameKitRankingsDecayOptions` placed in `GameKitRankingsOptions.cs` (the single file both 08-02 decay and 08-03 placement read) to prevent Wave 2 option-file conflicts

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Corrected Glicko inactivity test expected range**
- **Found during:** Task 2 (Glicko2InactivityTests RED phase)
- **Issue:** PATTERNS.md stated expected RD' range 290.5..291.0 for phi=290, sigma=0.06. Actual computed value is ~290.19 (phi/173.7178=1.6694; sqrt(1.6694²+0.06²)=1.6705; ×173.7178=290.19). The PATTERNS.md range was arithmetically wrong.
- **Fix:** Test range corrected to 290.1..290.3 to match the actual Glicko-2 Step 6 formula result
- **Files modified:** tests/GameKit.Rankings.Tests/Glicko2/Glicko2InactivityTests.cs
- **Verification:** All 3 inactivity tests pass with corrected range
- **Committed in:** 102d915

**2. [Rule 1 - Bug] Fixed SeasonArchiveLeaderboardTests double? comparison breaks**
- **Found during:** Task 3 (LeaderboardRowDto signature change to double?)
- **Issue:** SeasonArchiveLeaderboardTests.cs compared `LeaderboardRowDto.Rating` as `double` with `Assert.Equal(double, double, precision:5)`. The DTO change to `double?` broke 6 comparison sites.
- **Fix:** Changed comparisons to `archived[i].Rating ?? 0.0` (archive rows always have non-null ratings)
- **Files modified:** tests/GameKit.Rankings.Integration.Tests/SeasonArchiveLeaderboardTests.cs
- **Verification:** Integration tests build compiles with 0 warnings/errors
- **Committed in:** 84b0d41

**3. [Rule 1 - Bug] EF CLI generated non-deterministic timestamp**
- **Found during:** Task 2 (running `dotnet ef migrations add`)
- **Issue:** EF CLI generated timestamp `20260605235446` instead of the required `20260517000000`
- **Fix:** Renamed both migration files and updated the `[Migration("...")]` attribute in Designer.cs
- **Files modified:** Migration .cs and Designer.cs files
- **Verification:** Build compiles; SchemaFreezeTests verifies `20260517000000_RankingsDecayPlacement` in history table
- **Committed in:** 102d915

---

**Total deviations:** 3 auto-fixed (all Rule 1 - Bug)
**Impact on plan:** All auto-fixes corrected factual/numeric errors in test specs and EF CLI output. No scope creep. Plan intent fully preserved.

## Issues Encountered

- The `dotnet ef migrations add` design-time factory required `GAMEKIT_MIGRATIONS_CONNECTION` env var — set and migration generated successfully.
- The EF CLI also added spurious `IX_season_rank_archive_PlayerId` and `IX_season_rank_archive_SeasonId` indexes in the generated migration body (foreign key indexes EF infers). These were removed when the Up()/Down() body was replaced with the plan-specified raw SQL approach.

## Threat Surface Scan

No new network endpoints or auth paths introduced. The migration adds columns to `player_ranks` (existing table). The `LeaderboardRowDto.Rating` null projection is the T-08-01-01 mitigation already in the plan's threat register — confirmed implemented.

No new threat flags.

## Known Stubs

None — all phase-8 options surface wired, visible-rank hiding implemented, migration data-fixup correct.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Schema frozen: 08-02 (decay service) and 08-03 (placement decrement) can both read `player_ranks` new columns and `GameKitRankingsOptions.Decay` safely in parallel
- `Glicko2InactivityTests` passes — this is the RANK-15 formula gate required by 08-02 decay service implementation
- `SchemaFreezeTests` passes — proves the migration applied cleanly against real Postgres under the existing advisory lock
- No blockers for Wave 2

---
*Phase: 08-rankings-depth-rating-aware-matchmaking*
*Completed: 2026-06-05*
