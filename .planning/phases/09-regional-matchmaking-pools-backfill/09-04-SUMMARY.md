---
phase: 09
plan: "04"
subsystem: rankings
tags: [participation-guard, jsonb, rating-update, matchmaking, wave-2]
dependency_graph:
  requires: ["09-01"]
  provides:
    - LadderConfig.MinParticipationFractionForRating (double?) with XML doc
    - StartupLadderUpserter serializes MinParticipationFractionForRating into ladder JSONB Config
    - PendingRatingUpdatesAdapter.ReadMinParticipationFraction private static helper
    - PendingRatingUpdatesAdapter MATCH-19 SC#4 participation-fraction guard (before PendingRatingUpdate INSERT)
    - BackfillParticipationTests SC4 green (cross-package integration test)
  affects:
    - GameKit.Rankings (LadderConfig, StartupLadderUpserter, PendingRatingUpdatesAdapter)
    - tests/GameKit.Matchmaking.Integration.Tests (BackfillParticipationTests)
tech_stack:
  added: []
  patterns:
    - JSONB read via TryGetProperty/TryGetDouble with try/catch (mirrors RankingsTickerService.ReadRatingPeriod)
    - Guard-before-INSERT pattern in per-participant loop (adapter layer, not algorithm layer)
    - Cross-package integration test driving PendingRatingUpdatesAdapter via BuildMatchmakingContext
key_files:
  created: []
  modified:
    - src/GameKit.Rankings/Builder/LadderConfig.cs
    - src/GameKit.Rankings/Services/StartupLadderUpserter.cs
    - src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs
    - tests/GameKit.Matchmaking.Integration.Tests/BackfillParticipationTests.cs
decisions:
  - XML doc cref for StartupLadderUpserter in LadderConfig.cs uses plain text (not <see cref>) because LadderConfig is in the Builder namespace while StartupLadderUpserter is in the Services namespace — avoids CS1574 on warnaserror
  - InvokeAdapterAsync in BackfillParticipationTests uses IntegrationTestHelpers.BuildMatchmakingContext (MatchmakingTestModelCustomizer) rather than MatchmakingTestApp.Services to avoid adding a Services property to the test app — matches existing IntegrationTestHelpers DI-free construction pattern
  - SeedLadderWithMinFractionAsync injects JSONB directly via raw SQL (::jsonb cast) with the exact property name "MinParticipationFractionForRating" — validates the round-trip from seed to ReadMinParticipationFraction reader
metrics:
  duration: 12min
  completed: "2026-06-06"
  tasks: 3
  files: 4
---

# Phase 9 Plan 04: Participation-Fraction Rating Guard Summary

**One-liner:** Participation-fraction guard in PendingRatingUpdatesAdapter reads MinParticipationFractionForRating from ladder JSONB Config and skips the PendingRatingUpdate INSERT for under-participating players (MATCH-19 SC#4 closes).

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Rankings LadderConfig.MinParticipationFractionForRating + StartupLadderUpserter JSONB write | 4f5268c | 2 |
| 2 | Participation-fraction guard in PendingRatingUpdatesAdapter + JSONB read helper | f034b91 | 1 |
| 3 | BackfillParticipationTests SC4 cross-package integration test green | 94f956d | 1 |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CS1574 on warnaserror — XML doc cref unresolvable across namespace boundary**
- **Found during:** Task 1 — first build attempt
- **Issue:** `LadderConfig.cs` is in `GameKit.Rankings.Builder` namespace; `StartupLadderUpserter` is in `GameKit.Rankings.Services`. Using `<see cref="StartupLadderUpserter"/>` in LadderConfig's XML doc produced CS1574 (unresolvable cref) under `-warnaserror`.
- **Fix:** Changed to plain text `<c>StartupLadderUpserter</c>` in the XML doc comment.
- **Files modified:** `src/GameKit.Rankings/Builder/LadderConfig.cs`
- **Commit:** 4f5268c

## Known Stubs

None. All plan objectives delivered. BackfillParticipationTests is fully implemented and passing.

## Threat Surface Scan

No new network endpoints, auth paths, or schema changes introduced. The participation-fraction guard is entirely in the adapter layer (no API surface change). T-09-04-02 mitigation confirmed: ReadMinParticipationFraction wraps JSON read in try/catch, returning null on any parse error. T-09-04-01 (trust of game-server-supplied fraction) accepted as documented in plan threat model.

## Self-Check: PASSED
