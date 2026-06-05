---
phase: 04-rankings-sessions-gdpr
plan: "07"
subsystem: rankings
tags: [glicko2, leaderboard, season-reset, admin-ui, blazor, serializable-tx, audit]

# Dependency graph
requires:
  - phase: 04-02
    provides: Rankings entity model (Ladder, PlayerRank, LadderSeason, SeasonRankArchive, SeasonResetPolicy) + InitialCreate migration
  - phase: 04-04
    provides: AddRankings builder, RankingsBuilderExtensions pattern, ServiceTokenAuth
  - phase: 04-06
    provides: RankingsTickerService, IdempotencyCleanupService, LazyRankCreationTests patterns

provides:
  - ILeaderboardService + LeaderboardService (TopAsync / AroundAsync, live + archived season queries)
  - IEndSeasonService + EndSeasonService (SERIALIZABLE tx: archive + reset policy + audit row)
  - RankingsAdminEndpoints (POST /admin/api/ladders/{id}/end-season, GET /admin/api/leaderboard)
  - AntiforgeryValidationFilter (DRY clone in Rankings package, preserves package boundary)
  - EndSeasonDialog.razor (type-the-name-to-confirm gate, Admin.UI → Rankings controlled dep)
  - LeaderboardServiceTests (4 tests: TopAsync sorted, AroundAsync window, archive, 404 throw)
  - SeasonArchiveLeaderboardTests (6 tests: SC#4 archive, SoftRegress, HardReset, ArchiveOnly, audit row, AroundAsync on archive)

affects:
  - 04-08 (RankAdjustDialog follows same Admin.UI <- Rankings pattern)
  - Consumer integration (MapRankingsAdmin must be called to expose the new endpoints)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "SeasonResetPolicy formulas: SoftRegress = defaultRating + (rating - defaultRating) * RegressionFactor; RD = min(RdCeiling, currentRd + RdBump); Volatility = defaultVolatility"
    - "HardReset = rating/RD/Volatility all reset to ladder defaults (1500/350/0.06)"
    - "ArchiveOnly = archive row written, live player_ranks unchanged"
    - "EndSeasonService writes audit rows directly to _ctx.Set<AdminAuditLog>() (Core entity) instead of IAdminAuditWriter — avoids circular dep (Admin.UI <- Rankings + Rankings -> Admin.UI would be cyclic)"
    - "Admin.UI -> Rankings ProjectReference: controlled dep-direction to enable IEndSeasonService injection in EndSeasonDialog; Rankings still does NOT reference Admin.UI"
    - "AntiforgeryValidationFilter DRY cloned into Rankings Http/EndpointFilters/ (Open Q4 pattern — package boundary preserved)"

key-files:
  created:
    - src/GameKit.Rankings/Services/ILeaderboardService.cs
    - src/GameKit.Rankings/Services/LeaderboardService.cs
    - src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs
    - src/GameKit.Rankings/Services/IEndSeasonService.cs
    - src/GameKit.Rankings/Services/EndSeasonService.cs
    - src/GameKit.Rankings/Http/Contracts/EndSeasonRequest.cs
    - src/GameKit.Rankings/Http/Validators/EndSeasonRequestValidator.cs
    - src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs
    - src/GameKit.Rankings/Http/EndpointFilters/AntiforgeryValidationFilter.cs
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Season.cs
    - src/GameKit.Admin.UI/Components/Dialogs/EndSeasonDialog.razor
    - tests/GameKit.Rankings.Integration.Tests/LeaderboardServiceTests.cs
    - tests/GameKit.Rankings.Integration.Tests/SeasonArchiveLeaderboardTests.cs
  modified:
    - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs
    - src/GameKit.Admin.UI/GameKit.Admin.UI.csproj
    - src/GameKit.Admin.UI/Components/Layout/MainLayout.razor
    - src/GameKit.Admin.UI/Services/AdminAuditActions.cs
    - src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs
    - src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs

key-decisions:
  - "IAdminAuditWriter package boundary: EndSeasonService writes admin_audit_log directly via _ctx.Set<AdminAuditLog>() (Core entity) rather than IAdminAuditWriter (Admin.UI). Avoids circular dep between Admin.UI and Rankings."
  - "AdminCommandRegistry entry: new('end-season', 'End ladder season', 'actions', RequiresSuperadmin: true, RequiresTarget: true)"
  - "AntiforgeryValidationFilter is DRY-cloned into Rankings/Http/EndpointFilters/ (not imported from Admin.UI) — preserves D-22 package boundary"
  - "Admin.UI -> Rankings ProjectReference is a controlled dep-direction reversal documented in plan 04-07. Rankings still does NOT reference Admin.UI."
  - "LeaderboardService assigns Rank in-memory (not via ROW_NUMBER() OVER) — EF Core/Npgsql translation limitation; 500 row cap ensures bounded allocation"
  - "SoftRegress defaults: RegressionFactor=0.5, RdCeiling=200, RdBump=50 (from LadderConfig defaults)"

patterns-established:
  - "EndSeasonDialog: type-the-name-to-confirm gate mirrors GdprDeleteDialog (D-11)"
  - "OpenDialog switch arm conditionally builds DialogParameters based on commandId for end-season (LadderId/LadderName keys vs PlayerId/DisplayName)"
  - "ILeaderboardService: seasonId=null queries live player_ranks; seasonId!=null queries season_rank_archive (SC#4 archive path)"

requirements-completed:
  - RANK-08
  - RANK-10

# Metrics
duration: 45min
completed: 2026-05-16
---

# Phase 04 Plan 07: Seasonal Reset + Leaderboard + Admin Palette Wire-Up Summary

**SERIALIZABLE seasonal-reset service with SoftRegress/HardReset/ArchiveOnly policies, TopAsync/AroundAsync leaderboard service over live and archived seasons, and EndSeasonDialog wired into the Phase-3 admin palette verb**

## Performance

- **Duration:** ~45 min
- **Started:** 2026-05-16T00:00:00Z
- **Completed:** 2026-05-16T00:45:00Z
- **Tasks:** 3
- **Files modified:** 19

## Accomplishments

- ILeaderboardService + LeaderboardService: TopAsync (top-N, keyset via idx_player_ranks_ladder_rating) + AroundAsync (±window around target player); both support optional seasonId to query season_rank_archive (SC#4)
- EndSeasonService: SERIALIZABLE transaction closes current ladder_seasons row, opens next season, archives all player_ranks to season_rank_archive, applies SeasonResetPolicy (SoftRegress/HardReset/ArchiveOnly), writes admin_audit_log row — all in one atomic commit
- RankingsAdminEndpoints: POST /admin/api/ladders/{id}/end-season (Superadmin + antiforgery + validator) + GET /admin/api/leaderboard
- EndSeasonDialog.razor: type-the-name-to-confirm gate wired into MainLayout.OpenDialog "end-season" switch arm (Phase-3 no-op slot now live)
- 10 integration tests pass: 4 LeaderboardServiceTests + 6 SeasonArchiveLeaderboardTests (SC#4 anchor)
- AdminAuditActions, AdminCommandRegistry, AuditSentenceTemplates all extended for LadderEndSeason

## Task Commits

1. **Task 1: ILeaderboardService + LeaderboardService + LeaderboardServiceTests** - `7b5762b` (feat)
2. **Task 2: IEndSeasonService + EndSeasonService + admin endpoints + audit wiring + SeasonArchiveLeaderboardTests** - `bf76870` (feat)
3. **Task 3: EndSeasonDialog.razor + MainLayout.OpenDialog switch arm wiring** - `15b8501` (feat)

## Files Created/Modified

- `src/GameKit.Rankings/Services/ILeaderboardService.cs` — TopAsync + AroundAsync interface with seasonId optional (RANK-08/D-23)
- `src/GameKit.Rankings/Services/LeaderboardService.cs` — EF Core queries against player_ranks + season_rank_archive; in-memory rank assignment
- `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` — Wire record (Rank, PlayerId, DisplayName, Rating, RatingDeviation, Wins, Losses, Draws)
- `src/GameKit.Rankings/Services/IEndSeasonService.cs` — EndAsync interface + EndSeasonResult record
- `src/GameKit.Rankings/Services/EndSeasonService.cs` — SERIALIZABLE tx: 5 mutations + audit row via _ctx.Set<AdminAuditLog>() (Core entity)
- `src/GameKit.Rankings/Http/Contracts/EndSeasonRequest.cs` — ConfirmLadderName record
- `src/GameKit.Rankings/Http/Validators/EndSeasonRequestValidator.cs` — FluentValidation (NotEmpty, MaxLength 256)
- `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs` — POST end-season + GET leaderboard
- `src/GameKit.Rankings/Http/EndpointFilters/AntiforgeryValidationFilter.cs` — DRY clone (Open Q4 pattern)
- `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Season.cs` — registers ILeaderboardService + IEndSeasonService + validator
- `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` — wires AddSeasonInfrastructure() call
- `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` — adds ProjectReference to GameKit.Rankings
- `src/GameKit.Admin.UI/Components/Dialogs/EndSeasonDialog.razor` — confirm-gate dialog injecting IEndSeasonService
- `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor` — end-season switch arm + conditional LadderId/LadderName parameters
- `src/GameKit.Admin.UI/Services/AdminAuditActions.cs` — adds LadderEndSeason = "admin.ladder.end_season"
- `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs` — adds end-season row (RequiresSuperadmin, RequiresTarget)
- `src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs` — adds LadderEndSeason template
- `tests/GameKit.Rankings.Integration.Tests/LeaderboardServiceTests.cs` — 4 tests (TopAsync sorted, AroundAsync window, archive, 404)
- `tests/GameKit.Rankings.Integration.Tests/SeasonArchiveLeaderboardTests.cs` — 6 tests (SC#4 archive, SoftRegress, HardReset, ArchiveOnly, audit, AroundAsync on archive)

## Decisions Made

1. **Audit write without IAdminAuditWriter**: EndSeasonService writes audit rows directly via `_ctx.Set<AdminAuditLog>()` (Core entity) instead of going through `IAdminAuditWriter` (Admin.UI). This avoids a circular dependency: Admin.UI references Rankings for the dialog, so Rankings cannot reference Admin.UI. The audit action literal `"admin.ladder.end_season"` is a private const that must stay in sync with `AdminAuditActions.LadderEndSeason`.

2. **Admin.UI → Rankings controlled dep-direction**: `GameKit.Admin.UI.csproj` adds a `ProjectReference` to `GameKit.Rankings` so `EndSeasonDialog.razor` can inject `IEndSeasonService`. Rankings still does NOT reference Admin.UI (D-22 invariant intact).

3. **AddAuthenticationSchemes removed from per-endpoint**: `.AddAuthenticationSchemes("GameKitAdmin")` is not available on `RouteHandlerBuilder` in ASP.NET Core 10 minimal API style. The admin cookie scheme is baked into the policy registration at DI time (via `AddGameKitAdmin`), so `RequireAuthorization("gamekit.admin.superadmin")` suffices.

4. **AuditSentenceTemplates Registry visibility**: `Registry` is `private static readonly` — the LadderEndSeason template is registered within the same initializer block as all other templates.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] IAdminAuditWriter circular dependency resolution**
- **Found during:** Task 2 (EndSeasonService)
- **Issue:** Plan said EndSeasonService "consumes IAdminAuditWriter verbatim" but Admin.UI → Rankings ProjectReference (Task 3) makes Rankings → Admin.UI a cycle
- **Fix:** EndSeasonService writes directly to `_ctx.Set<AdminAuditLog>()` (Core entity, no Admin.UI dep needed). Action literal duplicated as private const with sync comment.
- **Files modified:** src/GameKit.Rankings/Services/EndSeasonService.cs
- **Verification:** SeasonArchiveLeaderboardTests.EndSeason_Writes_Audit_Row passes (audit row present in admin_audit_log)

**2. [Rule 3 - Blocking] AddAuthenticationSchemes not available on RouteHandlerBuilder**
- **Found during:** Task 2 (RankingsAdminEndpoints)
- **Issue:** `.AddAuthenticationSchemes("GameKitAdmin")` is an `AuthorizationPolicyBuilder` method, not a `RouteHandlerBuilder` method. Compile error.
- **Fix:** Removed per-endpoint scheme override; the scheme is already baked into the admin policies registered by `AddGameKitAdmin`. `RequireAuthorization(policyName)` alone is sufficient.
- **Files modified:** src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs
- **Verification:** Solution builds clean; admin endpoints are covered by the Superadmin/Admin policy (which already includes the GameKitAdmin scheme).

---

**Total deviations:** 2 auto-fixed (1 bug/architectural resolution, 1 blocking compile error)
**Impact on plan:** Both auto-fixes necessary for correctness. No scope creep. SC#4 anchor green.

## Issues Encountered

None beyond the two auto-fixed deviations above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Plan 04-08 follows the same Admin.UI ← Rankings pattern for RankAdjustDialog
- RankingsAdminEndpoints.MapRankingsAdmin() must be called in the consumer's pipeline (extension method is defined and ready)
- ILeaderboardService + IEndSeasonService are registered in DI by AddRankings() (via AddSeasonInfrastructure)
- All 10 new tests pass; SC#4 anchor (SeasonArchiveLeaderboardTests) is green

## Known Stubs

None — all delivered functionality is wired end-to-end.

## Threat Flags

None — no new network endpoints, auth paths, file access patterns, or schema changes beyond what the plan's threat model covers.

## Self-Check: PASSED

All 11 created files verified present. All 3 task commits (7b5762b, bf76870, 15b8501) confirmed in git log.

---
*Phase: 04-rankings-sessions-gdpr*
*Completed: 2026-05-16*
