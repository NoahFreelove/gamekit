---
phase: 08-rankings-depth-rating-aware-matchmaking
plan: "03"
subsystem: Rankings
tags: [placement, rating-source, matchmaking-seam, rank16, rank17]
dependency_graph:
  requires: ["08-01"]
  provides: ["RANK-16-placement-decrement", "RANK-17-rating-source"]
  affects: ["08-04-matchmaking-wire"]
tech_stack:
  added: []
  patterns:
    - "ExecuteUpdateAsync with compound WHERE race guard for concurrent session-complete safety"
    - "RemoveAll<T>() + AddScoped<T, TImpl>() to override TryAddSingleton null-object"
    - "AsNoTracking batched SELECT with playerIds.Contains() for bulk rating projection"
key_files:
  created:
    - src/GameKit.Rankings/Services/RankingsRatingSource.cs
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.RatingSource.cs
    - tests/GameKit.Rankings.Integration.Tests/PlacementMatchTests.cs
    - tests/GameKit.Rankings.Integration.Tests/RankingsRatingSourceTests.cs
    - tests/GameKit.Rankings.Tests/RankingsRatingSourceRegistrationTests.cs
  modified:
    - src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs
decisions:
  - "Placement decrement uses ExecuteUpdateAsync (not entity mutation) because playerRank is loaded AsNoTracking; mutation + SaveChanges would be a silent no-op (Pitfall 6)"
  - "SetProperty IsInPlacement = PlacementMatchesRemaining - 1 == 0 ? false : r.IsInPlacement evaluated in SQL — atomicity maintained inside ReadCommitted tx"
  - "RankingsRatingSource lifetime is Scoped (not Singleton as CONTEXT.md stated) per RESEARCH A2 — shares GameKitDbContext with MatchmakingService.EnqueueAsync scope"
  - "WithRatingsFrom<T>() uses RemoveAll + AddScoped (not TryAdd); two XML-doc occurrences of TryAdd are documentation only, zero code occurrences"
metrics:
  duration_minutes: 8
  completed_date: "2026-06-06"
  tasks_completed: 3
  files_changed: 6
---

# Phase 08 Plan 03: Placement Decrement + RankingsRatingSource Summary

Atomic placement decrement (RANK-16) at the session-complete hook and `RankingsRatingSource : IPlayerRatingProvider` with `.WithRatingsFrom<>()` RemoveAll+AddScoped override (RANK-17).

## What Was Built

### Task 1 — Atomic placement decrement (RANK-16)

`PendingRatingUpdatesAdapter.OnCompletedAsync` now atomically decrements `placement_matches_remaining` using `ExecuteUpdateAsync` with two guards:

1. **Application guard:** `if (playerRank.IsInPlacement && playerRank.PlacementMatchesRemaining > 0)` — skips the SQL entirely for non-placement players (zero-row update avoided).
2. **Database race guard:** `WHERE IsInPlacement AND PlacementMatchesRemaining > 0` — prevents underflow when two concurrent session-complete calls race on the same player.

`IsInPlacement` flips to `false` via `SetProperty(r => r.IsInPlacement, r => r.PlacementMatchesRemaining - 1 == 0 ? false : r.IsInPlacement)` — evaluated atomically in SQL. Rides the caller's ambient ReadCommitted transaction; no `BeginTransaction` call added.

### Task 2 — RankingsRatingSource + .WithRatingsFrom<>() (RANK-17)

`RankingsRatingSource : IPlayerRatingProvider` executes a single batched `AsNoTracking` query against `player_ranks`, projecting `Rating`, `RatingDeviation`, `Volatility` into `PlayerRatingValue`. Players with no rank row are naturally omitted from the result dictionary.

`RankingsBuilderExtensions.RatingSource.cs` (partial class) adds `.WithRatingsFrom<T>()` which calls `RemoveAll<IPlayerRatingProvider>()` + `AddScoped<IPlayerRatingProvider, T>()`. This overcomes Core's `TryAddSingleton` null-object registration — a second `TryAdd` would be a silent no-op.

### Task 3 — Testcontainers integration proof (RANK-17)

Three integration tests against real Postgres via Testcontainers verify: exact Rating/RD/Volatility projection, absent-player omission (not zero entry), and ladder-scoped queries.

## Test Results

| Suite | Filter | Passed | Failed |
|-------|--------|--------|--------|
| Integration | `PlacementMatch` | 5 | 0 |
| Integration | `RankingsRatingSource` | 3 | 0 |
| Unit | `RankingsRatingSourceRegistration` | 3 | 0 |
| **Combined** | All plan 08-03 | **11** | **0** |

## Deviations from Plan

### Auto-applied (Rule 1 - deviation)

**Test service resolution: PendingRatingUpdatesAdapter registered as concrete type for direct test access**
- **Found during:** Task 1 RED phase
- **Issue:** `PendingRatingUpdatesAdapter` is registered only via `AddScoped<IPostSessionCompleteHandler, PendingRatingUpdatesAdapter>()`. The test needed to resolve it as the concrete type to call `OnCompletedAsync` directly.
- **Fix:** Added `services.AddScoped<PendingRatingUpdatesAdapter>()` in the test's `BuildAdapterServiceProvider` helper. This is a test-only addition, does not affect production DI.
- **Files modified:** `tests/GameKit.Rankings.Integration.Tests/PlacementMatchTests.cs`

### Lifetime deviation (documented in RESEARCH, carried forward)

CONTEXT.md said `AddSingleton` for `RankingsRatingSource`; RESEARCH §RANK-17 explicitly overrides this to `Scoped` (Assumption A2). `RankingsRatingSource` reads the scoped `GameKitDbContext` — registering as Singleton would require `IServiceScopeFactory` and create a second connection per call, breaking ambient-transaction semantics. Plan 08-03 frontmatter already noted "Scoped (deviates from CONTEXT 'Singleton' wording per RESEARCH §RANK-17)".

## Acceptance Criteria Verification

| Criterion | Result |
|-----------|--------|
| `grep -q "PlacementMatchesRemaining > 0" PendingRatingUpdatesAdapter.cs` | PASS |
| `grep -c "ExecuteUpdateAsync" PendingRatingUpdatesAdapter.cs` → 3 (existing + new) | PASS |
| `grep -c "BeginTransaction" PendingRatingUpdatesAdapter.cs` → 0 | PASS |
| `grep -q "class RankingsRatingSource : IPlayerRatingProvider"` | PASS |
| `grep -q "RemoveAll<IPlayerRatingProvider>"` | PASS |
| `grep -q "AddScoped<IPlayerRatingProvider"` | PASS |
| `grep -c "TryAdd" RankingsBuilderExtensions.RatingSource.cs` → 2 (XML doc only, 0 code) | PASS |
| `grep -q "WithRatingsFrom<RankingsRatingSource>" RankingsRatingSourceTests.cs` | PASS |
| `dotnet build src/GameKit.Rankings` → 0 warnings | PASS |

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 05685bc | test | PlacementMatchTests failing (RED) |
| 42c25db | feat | Placement decrement implementation (GREEN) |
| 0c3f6b6 | feat | RankingsRatingSource + WithRatingsFrom + registration unit tests (GREEN) |
| 848df36 | test | RankingsRatingSourceTests Testcontainers integration proof |

## Known Stubs

None — all data paths are wired to real Postgres. `RankingsRatingSource.GetRatingsAsync` queries live `player_ranks` rows; placement decrement issues real SQL against live `player_ranks` rows.

## Threat Flags

No new network endpoints, auth paths, file access patterns, or schema changes introduced. The placement decrement operates entirely inside existing `PendingRatingUpdatesAdapter.OnCompletedAsync` (already a server-side operation). The `RankingsRatingSource` is an in-process provider called by `MatchmakingService.EnqueueAsync` — it does not expose a new HTTP surface.

## Self-Check: PASSED

Files created:
- src/GameKit.Rankings/Services/RankingsRatingSource.cs — FOUND
- src/GameKit.Rankings/Builder/RankingsBuilderExtensions.RatingSource.cs — FOUND
- tests/GameKit.Rankings.Integration.Tests/PlacementMatchTests.cs — FOUND
- tests/GameKit.Rankings.Integration.Tests/RankingsRatingSourceTests.cs — FOUND
- tests/GameKit.Rankings.Tests/RankingsRatingSourceRegistrationTests.cs — FOUND

Commits verified: 05685bc, 42c25db, 0c3f6b6, 848df36 — all present in git log.
