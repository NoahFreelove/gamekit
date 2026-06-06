---
phase: 09
plan: "01"
subsystem: matchmaking
tags: [schema, enum, config, migration, test-scaffold, wave-0]
dependency_graph:
  requires: []
  provides:
    - MatchmakingTicketType integer enum (Normal=0, Backfill=1)
    - MatchmakingTicket.TicketType property + EF config
    - SessionParticipant.ParticipationFraction property + EF config
    - Migration 20260520000000 (TicketType + ParticipationFraction via raw ALTER TABLE)
    - MatchmakingLadderConfig.AllowedRegions + MinParticipationFractionForRating
    - ValidateLadderConfig fail-fast for AllowedRegions + MinParticipationFractionForRating
    - Wave 0 RED scaffolds: RegionalPoolTests, BackfillTests, BackfillParticipationTests
  affects:
    - GameKit.Matchmaking (entity, config, migration)
    - GameKit.Core (SessionParticipant entity + EF config)
    - tests/GameKit.Matchmaking.Integration.Tests (3 new test files)
tech_stack:
  added: []
  patterns:
    - Integer enum storage (no HasConversion<string>()) per Phase 5 mandatory
    - Raw migrationBuilder.Sql() ALTER TABLE for cross-package column additions
    - Builder-time fail-fast validation (ArgumentException with paramName: nameof(config))
    - Wave 0 RED test scaffolds (NotImplementedException with owning-plan markers)
key_files:
  created:
    - src/GameKit.Matchmaking/Entities/MatchmakingTicketType.cs
    - src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs
    - src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.Designer.cs
    - tests/GameKit.Matchmaking.Integration.Tests/RegionalPoolTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/BackfillTests.cs
    - tests/GameKit.Matchmaking.Integration.Tests/BackfillParticipationTests.cs
  modified:
    - src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs
    - src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs
    - src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs
    - src/GameKit.Matchmaking/Migrations/GameKitDbContextModelSnapshot.cs
    - src/GameKit.Core/Entities/SessionParticipant.cs
    - src/GameKit.Core/Data/Configurations/SessionParticipantConfiguration.cs
decisions:
  - AllowedRegions validation uses HashSet<string>(StringComparer.OrdinalIgnoreCase) for case-insensitive duplicate detection; "default" is reserved and rejected at AddLadder time to prevent malformed Redis pool keys (T-09-01-01 mitigation)
  - Migration uses raw migrationBuilder.Sql() (not AddColumn) because the design-time factory does not apply Core/Rankings configurations per the per-package migration boundary rule — same approach as RankingsDecayPlacement precedent
  - ParticipationFraction column added by Matchmaking migration to Core-owned session_participants table; T-09-01-03 accepted as additive-only nullable column under advisory lock 388956820
  - Wave 0 scaffolds compile green but individual [Fact]s are RED (throw NotImplementedException) pending Plans 09-02/09-03/09-04
metrics:
  duration: 7min
  completed: "2026-06-06"
  tasks: 3
  files: 13
---

# Phase 9 Plan 01: Foundation Schema + Config + Wave 0 Scaffolds Summary

**One-liner:** Integer enum MatchmakingTicketType + raw migration 20260520000000 (TicketType+ParticipationFraction) + builder-time AllowedRegions/MinParticipationFractionForRating validation + 3 RED Wave-0 test scaffolds for Plans 09-02/03/04.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | MatchmakingTicketType enum + entity + EF config | eddf0d8 | 5 |
| 2 | Migration 20260520000000_MatchmakingBackfillRegions | d7d4ffb | 3 |
| 3 | AllowedRegions config + builder validation + Wave 0 scaffolds | e888cfb | 5 |

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None. The three Wave 0 test scaffolds are intentional RED-by-design stubs tracked explicitly in the plan (plans 09-02/09-03/09-04 resolve them).

## Threat Surface Scan

No new network endpoints, auth paths, or trust-boundary changes introduced in this plan. Migration adds two columns additive-only under advisory lock 388956820. T-09-01-01 (AllowedRegions → Redis key component) mitigated by builder-time validation. T-09-01-02 (MinParticipationFractionForRating out-of-range) mitigated by [0.0, 1.0] range check at AddLadder time. T-09-01-03 (cross-package column) accepted.

## Self-Check: PASSED
