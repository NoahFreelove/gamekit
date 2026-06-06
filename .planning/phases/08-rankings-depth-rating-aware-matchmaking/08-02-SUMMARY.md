---
phase: 08-rankings-depth-rating-aware-matchmaking
plan: 02
subsystem: rankings
tags: [glicko2, redis, leader-election, background-service, rank-decay, testcontainers, polly]

requires:
  - phase: 08-01
    provides: GameKitRankingsDecayOptions (LockKey, LockTtlSeconds, Interval, DecayThresholdRating, InactivityDays, BatchSize), PlayerRank.LastDecayAt + IsInPlacement + PlacementMatchesRemaining columns, decay_candidates index

provides:
  - RankDecayLeaseHelper: Redis leader-election lease for the decay runner using dedicated key gamekit:rankings:decay:lease
  - RankDecayBackgroundService: PeriodicTimer + leader-elected batch decay applying scale-correct Glicko-2 inactivity step
  - AddDecayInfrastructure: DI registration wired into AddRankings
  - RankDecayTests: Testcontainers integration proof (3 tests, all green)

affects:
  - 08-03 (placement + rating source plans may resolve RankDecayBackgroundService from DI)
  - any plan adding multi-replica hosting must verify decay:lease non-collision

tech-stack:
  added: []
  patterns:
    - "Dedicated Redis lease key per background service (decay key distinct from ticker key — never reuse across services)"
    - "PeriodicTimer BackgroundService with RunOnceAsync internal method for deterministic test invocation"
    - "Scale-correct Glicko-2 inactivity step: RD ÷ 173.7178 → phi' = sqrt(phi^2 + sigma^2) → × 173.7178 (Rating/Volatility never written)"
    - "Leader-election integration test: pre-take lock → assert no-op; release → assert decay proceeds"
    - "Two-key non-collision test: ticker key held does NOT block decay key acquisition"

key-files:
  created:
    - src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs
    - src/GameKit.Rankings/Services/RankDecayBackgroundService.cs
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Decay.cs
    - tests/GameKit.Rankings.Integration.Tests/RankDecayTests.cs
  modified:
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs

key-decisions:
  - "Dedicated lock key gamekit:rankings:decay:lease — never reuse the ticker's gamekit:rankings:ticker:lease to prevent mutual starvation (Pitfall 4)"
  - "RunOnceAsync is internal (not private) so integration tests can drive a single decay pass without waiting for the PeriodicTimer"
  - "Candidates loaded as tracked entities (not AsNoTracking) so EF Core saves RD/LastDecayAt mutations via SaveChangesAsync"

patterns-established:
  - "Decay service shape: mirrors RankingsTickerService — PeriodicTimer + IServiceScopeFactory scope-per-tick + leader-election + try/finally release"
  - "Scale-correct Glicko-2 inactivity step as the canonical reference for any future decay-adjacent code"

requirements-completed: [RANK-15]

duration: 20min
completed: 2026-06-05
---

# Phase 8 Plan 02: Leader-Elected RankDecayBackgroundService Summary

**Scale-correct Glicko-2 RD inflation for inactive above-threshold players via a dedicated Redis lease key, with Testcontainers proof of leader election and non-collision with the ticker service**

## Performance

- **Duration:** ~20 min
- **Started:** 2026-06-05T00:00:00Z
- **Completed:** 2026-06-05
- **Tasks:** 3
- **Files modified:** 5 (4 created, 1 modified)

## Accomplishments

- `RankDecayLeaseHelper` implementing Lua-verified `LockTakeAsync / LockReleaseAsync` with Polly v8 retry on the dedicated `gamekit:rankings:decay:lease` key — zero reference to the ticker lease key
- `RankDecayBackgroundService` inflating RD via `phi' = sqrt((RD/173.7178)^2 + vol^2) * 173.7178` with Rating and Volatility NEVER modified, LastDecayAt stamped, excluding placement/below-threshold/never-played players
- `AddDecayInfrastructure` partial class wired into `AddRankings` (step 8), mirroring the ticker pattern exactly
- Three integration tests (Testcontainers PG + Redis): RD inflation + rating constant + LastDecayAt; exclusion filter; dedicated key non-collision with ticker key — all 3 pass

## Task Commits

1. **Task 1: RankDecayLeaseHelper** - `a5ae268` (feat)
2. **Task 2: RankDecayBackgroundService + AddDecayInfrastructure** - `42f138d` (feat)
3. **Task 3: Testcontainers decay integration tests** - `b8d3d57` (test)

## Files Created/Modified

- `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` - Redis leader-election lease helper bound to `_opts.Decay.LockKey`
- `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` - PeriodicTimer background service applying Glicko-2 inactivity step
- `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Decay.cs` - Partial class registering decay singleton + hosted service
- `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` - Added `AddDecayInfrastructure(builder.Services)` call at step 8
- `tests/GameKit.Rankings.Integration.Tests/RankDecayTests.cs` - 3 integration tests covering RANK-15

## Decisions Made

- Dedicated lock key (`gamekit:rankings:decay:lease`) distinct from ticker — prevents mutual starvation across long ticker drains
- `RunOnceAsync` as `internal` (not `private`) enables deterministic single-run test invocation
- Candidates loaded as tracked entities (not `AsNoTracking`) so EF Core's change tracker persists `RatingDeviation` and `LastDecayAt` mutations via `SaveChangesAsync`

## Deviations from Plan

None — plan executed exactly as written. All three tasks completed on the first attempt with 0 build warnings.

## Issues Encountered

None.

## Known Stubs

None — all fields written are real (RD inflation is real Glicko-2 math, not placeholders).

## Threat Flags

None — no new network endpoints, auth paths, or schema changes beyond what the plan's threat model covers.

## Self-Check: PASSED

Files verified:
- `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` — FOUND
- `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` — FOUND
- `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Decay.cs` — FOUND
- `tests/GameKit.Rankings.Integration.Tests/RankDecayTests.cs` — FOUND

Commits verified:
- `a5ae268` — FOUND (feat(08-02): RankDecayLeaseHelper)
- `42f138d` — FOUND (feat(08-02): RankDecayBackgroundService + AddDecayInfrastructure)
- `b8d3d57` — FOUND (test(08-02): Testcontainers decay integration tests)

Build: `dotnet build src/GameKit.Rankings/GameKit.Rankings.csproj --nologo` — 0 warnings, 0 errors
Tests: `dotnet test ... --filter "FullyQualifiedName~RankDecay"` — 3/3 PASSED

## Next Phase Readiness

- RANK-15 complete; decay infrastructure wired and integration-proven
- 08-03 (placement match decrement + RankingsRatingSource) can proceed immediately
- No blockers

---
*Phase: 08-rankings-depth-rating-aware-matchmaking*
*Completed: 2026-06-05*
