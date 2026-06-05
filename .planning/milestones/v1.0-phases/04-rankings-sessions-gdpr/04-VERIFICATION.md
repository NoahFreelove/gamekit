---
phase: 04-rankings-sessions-gdpr
verified: 2026-05-16T12:00:00Z
status: human_needed
score: 6/6
overrides_applied: 0
human_verification:
  - test: "Run integration test suite (GameKit.Rankings.Integration.Tests) in a Docker-capable environment"
    expected: "All 11 integration test classes pass: Glicko2ConvergenceTests, SessionCompleteIdempotencyTests, SeasonArchiveLeaderboardTests, GdprExportContractTests, AdminRankAdjustTransactionTests, RankingsTickerLeaderElectionTests, LazyRankCreationTests, IdempotencyCleanupServiceTests, LadderUpsertOnStartupTests, RankingsMigrationDeterminismTests, RankingsAdvisoryLockKeyTests, SchemaTypeAssertions, ServiceTokenAuthenticationHandlerTests, LeaderboardServiceTests"
    why_human: "All Rankings integration tests require Testcontainers Docker (Postgres 17.9 + Redis) and cannot run in this environment"
  - test: "Verify Glicko2ConvergenceTests.Two_Populations_Converge_Within_Tolerance passes after CR-01 fix"
    expected: "After 1000 matches / 100 rating periods: mean strong-population rating within ±50 of 1700, mean weak-population rating within ±50 of 1300. The reviewer noted the ±50 tolerance was probably loose enough to absorb the delta change from the CR-01 double-count fix, but this must be confirmed empirically."
    why_human: "SC#1 is the gating convergence proof — correctness of the Glicko-2 ticker path. Requires Docker for Testcontainers."
  - test: "Verify SessionCompleteIdempotencyTests.Retry_Five_Times_Applies_Delta_Once passes (SC#2)"
    expected: "5× identical POST /api/sessions/{id}/complete yields exactly ONE row in pending_rating_updates per participant, ONE row in session_complete_idempotency, all 5 HTTP responses return 200"
    why_human: "SC#2 requires a live Postgres + Redis stack via Testcontainers"
  - test: "Verify SeasonArchiveLeaderboardTests.Archive_Preserves_Previous_Season_TopN passes (SC#4)"
    expected: "After EndSeasonService.EndAsync, the season_rank_archive contains all prior player_ranks rows and ILeaderboardService.TopAsync with the archived seasonId returns the same ordering as before the season end"
    why_human: "SC#4 requires Testcontainers Postgres"
  - test: "Verify GdprExportContractTests (SC#5) — confirm PlayerSubMismatch_Returns_403 and AdminPath_Requires_Superadmin_And_Writes_Audit are covered"
    expected: "The plan specified 6 test methods; the codebase only has 5 in GdprExportContractTests (Response_Has_All_Documented_Top_Level_Keys, NonExistentPlayer_Returns_Null, Excludes_GDPR_Cascade_Null_Rows, Over_Cap_Throws, Export_Returns_Only_Pre_Snapshot_Sessions). PlayerSubMismatch_Returns_403 and AdminPath_Requires_Superadmin_And_Writes_Audit appear to have been merged or are missing. A human should run the full test class and confirm sub-mismatch (403) and admin-path audit-write are exercised, either by existing tests or need to be added."
    why_human: "Requires Docker. Also needs human review of which planned tests were not implemented vs. covered via different method names."
  - test: "Verify AdminRankAdjustTransactionTests (SC#6) — confirm ShortReason_Returns_400, MissingAntiforgery_Returns_400, and PlayerJWT_Returns_403 are covered"
    expected: "The plan specified 7 test methods. The codebase has 8 (UpdateAndAudit_RollBack_Together, HappyPath, LazyCreate, OutOfBoundsRating_Below_Min, OutOfBoundsRating_Above_Max, EmptyReason, MissingLadder, Adjust_Does_Not_Modify_RD). ShortReason_Returns_400, MissingAntiforgery_Returns_400, and PlayerJWT_Returns_403 appear absent by exact name — human should confirm the gap is real or has equivalent coverage."
    why_human: "Requires Docker + human review of whether the planned tests are implemented under different method names or genuinely missing"
  - test: "Verify CR-02 per-session delta semantics meet product expectations"
    expected: "For a drain with multiple sessions for the same player in the same period, only the latest session receives RatingAfter/RatingDelta; earlier sessions in the same drain receive RatingAfter = pre-drain rating and RatingDelta = 0. Confirm this v1 limitation is acceptable."
    why_human: "This is a product/behavior decision that cannot be verified programmatically — requires human judgment on whether the documented limitation is acceptable"
  - test: "Confirm EndSeasonDialog type-the-name-to-confirm gate works in the browser"
    expected: "Opening the end-season palette verb from the admin UI presents a dialog requiring the operator to type the exact ladder name before the 'End Season' button becomes enabled"
    why_human: "Blazor Server UI behavior cannot be verified via grep or build — requires browser testing"
  - test: "Confirm RankAdjustDialog opens from palette and submits to IRankAdjustService"
    expected: "The rank-adjust palette verb opens the dialog, the ladder selector populates from live data, the numeric field enforces min/max from GameKitRankingsOptions, and submitting calls IRankAdjustService.AdjustAsync which writes an audit row"
    why_human: "Blazor Server UI behavior requires browser testing"
---

# Phase 4: Rankings + Sessions + GDPR Export — Verification Report

**Phase Goal:** Completed matches produce correct, idempotent rating updates via a windowed Glicko-2 default that a developer can swap out, seasonal boundaries archive ratings without data loss, and operators can satisfy GDPR export requests over the full PII surface.

**Verified:** 2026-05-16T12:00:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

All six roadmap success criteria are implemented in code. Unit tests (266 total: Core 131 + Rankings 9 + Auth 35 + Admin 92) pass. The entire solution builds with 0 warnings, 0 errors. Integration tests require Docker (Testcontainers) and cannot be executed in this environment — they are deferred to human verification. Two gaps in the planned SC#5/SC#6 test methods require human confirmation. One TODO found in service authentication files is appropriately tagged for v2 with no issue reference — classified as INFO per the v2 marker pattern (not a blocker).

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | 1000-match convergence test using batched `IRankingAlgorithm.Apply(state, batch)` (anti-Pitfall §1) | VERIFIED (unit); HUMAN NEEDED (integration) | `Glicko2ConvergenceTests.Two_Populations_Converge_Within_Tolerance` exists at `/tests/GameKit.Rankings.Integration.Tests/Glicko2ConvergenceTests.cs:82` with correct 1000-match / 100-period / Random(42) structure. `IRankingAlgorithm` has exactly ONE public `Apply` method confirmed by reflection in `Glicko2AlgorithmContractTests`. `DrainLadderAsync` calls `_algorithm.Apply` exactly once per drain (line 318 of `RankingsTickerService.cs`). Cannot run Docker-based test in this env. |
| 2 | `POST /api/sessions/{id}/complete` state-conditional + Idempotency-Key + 5× retry → 1 rating delta | VERIFIED (code); HUMAN NEEDED (integration) | Endpoint exists in `src/GameKit.Core/Http/SessionEndpoints.cs`. `IdempotencyKeyEndpointFilter` enforces header (400 on missing). `SessionCompleteService` performs state-conditional UPDATE WHERE state=Active. `RankingsIdempotencyStore` persists per (SessionId, IdempotencyKey). `SessionCompleteIdempotencyTests.Retry_Five_Times_Applies_Delta_Once` exists at line 80. Requires Docker to run. |
| 3 | rating/RD/volatility columns = `double precision`; `rating_before/after/delta` snapshotted on session_participants | VERIFIED | `PlayerRankConfiguration.cs`: `HasColumnType("double precision")` on Rating, RatingDeviation, Volatility. `SeasonRankArchiveConfiguration.cs`: same. `SessionParticipantConfiguration.cs`: no explicit `HasColumnType` but entity declares `double?` properties and `CoreInitial.cs` migration line 86-88 confirms `type: "double precision"` for all three session_participants rating columns. RankingsInitial migration lines 133-135 and 192-194 confirm `type: "double precision"` for player_ranks and season_rank_archive. |
| 4 | Seasonal reset archives prior season; leaderboard queries against archived season still return top-N + around-me | VERIFIED (code); HUMAN NEEDED (integration) | `EndSeasonService.EndAsync` implements SERIALIZABLE tx that closes current season, opens new, inserts `SeasonRankArchive` rows for all player_ranks, and applies reset policy (SoftRegress/HardReset/ArchiveOnly) at lines 136-177. `LeaderboardService.TopAsync` and `AroundAsync` accept optional `seasonId` to query `season_rank_archive`. `SeasonArchiveLeaderboardTests` has all 6 expected test methods including `Archive_Preserves_Previous_Season_TopN`. |
| 5 | `GET /api/players/{id}/export` returns full GDPR bundle (player + identities + credential metadata sans hash + sessions + rating history) | VERIFIED (code); HUMAN NEEDED (integration) | `GdprExportService.ExportWithSizeAsync` opens REPEATABLE READ + READ ONLY tx, queries all 6 tables filtering by `PlayerId`. `GdprExportResponse` uses explicit `[JsonPropertyName]` on every property. Service never materializes `PasswordHash`. `IdentitySection` includes `external_id_hash` only. Cap at 25 MB enforced. Test `Response_Has_All_Documented_Top_Level_Keys` exists. However, `PlayerSubMismatch_Returns_403` and `AdminPath_Requires_Superadmin_And_Writes_Audit` appear absent by exact name from the test class — requires human verification. |
| 6 | Manual rank adjustment writes before/after audit row + updates rating atomically (single tx) | VERIFIED (code); HUMAN NEEDED (integration) | `RankAdjustService.AdjustAsync` wraps UPDATE player_ranks + write `AdminAuditLog` in a SERIALIZABLE transaction with Polly retry (CR-03). `AdminRankAdjustTransactionTests.UpdateAndAudit_RollBack_Together_On_Failure` exists (SC#6). However, `ShortReason_Returns_400`, `MissingAntiforgery_Returns_400`, and `PlayerJWT_Returns_403` tests planned in 04-08 appear absent — requires human verification of coverage. |

**Score: 6/6 truths verified at code level; 9 items deferred to human verification for Docker-required integration tests and test coverage gaps.**

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs` | Strategy interface — batched-only Apply | VERIFIED | Exists; exactly one public `Apply(RankingState, RankingBatch)` method plus `Name` property; XML doc forbids per-match calls |
| `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` | Default IRankingAlgorithm with tau=0.5 | VERIFIED | Exists; ctor default `tau = 0.5`; constructs `RatingCalculator(initVolatility: _initVolatility, tau: _tau)` |
| `src/GameKit.Rankings/Glicko2/RatingCalculator.cs` | Vendored Glicko-2 with BSD-3-Clause dual header | VERIFIED | Exists; `SPDX-License-Identifier: BSD-3-Clause AND GPL-3.0-or-later`; `Copyright (c) 2015, Maarten Staa`; commit `59033eec` |
| `src/GameKit.Rankings/Glicko2/Rating.cs` | Vendored with dual header | VERIFIED | Same dual header pattern |
| `src/GameKit.Rankings/Glicko2/RatingPeriodResults.cs` | Vendored with dual header | VERIFIED | Same dual header pattern |
| `src/GameKit.Rankings/Glicko2/Result.cs` | Vendored with dual header | VERIFIED | Same dual header pattern |
| `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs` | 7 tables + FK from game_sessions | VERIFIED | Exists; `fk_game_sessions_ladders` FK via raw SQL at line 298; 7 tables created; double precision columns confirmed |
| `src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs` | EF Core design-time factory | VERIFIED | Exists |
| `src/GameKit.Rankings/Data/RankingsMigrationHostedService.cs` | Applies migrations on startup | VERIFIED | Exists |
| `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs` | History table + advisory lock key | VERIFIED | `MigrationsHistoryTable = "__ef_migrations_rankings"`, `AdvisoryLockKey = -156812172L` with XML doc citing Core/Auth/Admin distinct values |
| `src/GameKit.Rankings/Services/RankingsTickerService.cs` | BackgroundService with Redis lease | VERIFIED | Exists; `PeriodicTimer`; calls `_lease.TryAcquireLeaseAsync`; `_algorithm.Apply` called exactly once at line 318; `IRankingsTicker` implemented |
| `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` | Redis LockTake/Extend/Release + Polly | VERIFIED | Exists; uses `LockTakeAsync/LockExtendAsync/LockReleaseAsync`; `ResiliencePipelineBuilder().AddRetry(...)` with `RedisConnectionException/RedisTimeoutException` |
| `src/GameKit.Rankings/Services/IdempotencyCleanupService.cs` | Nightly cleanup of session_complete_idempotency | VERIFIED | Exists; also cleans `pending_rating_updates` older than 30-day retention TTL (CR-05 fix) |
| `src/GameKit.Rankings/Services/GdprExportService.cs` | REPEATABLE READ export | VERIFIED | Exists; `IsolationLevel.RepeatableRead`; `SET TRANSACTION READ ONLY`; PasswordHash never queried |
| `src/GameKit.Rankings/Services/RankAdjustService.cs` | SERIALIZABLE tx rank adjust | VERIFIED | Exists; `IsolationLevel.Serializable`; Polly retry (CR-03); audit row via `AdminAuditLog` entity (no Admin.UI compile dep per D-22) |
| `src/GameKit.Rankings/Services/EndSeasonService.cs` | SERIALIZABLE tx season archive | VERIFIED | Exists; all three reset policies implemented; audit row written |
| `src/GameKit.Rankings/Services/LeaderboardService.cs` | TopAsync + AroundAsync | VERIFIED | Exists; queries both `PlayerRank` and `SeasonRankArchive` when `seasonId` provided |
| `src/GameKit.Rankings/Http/Contracts/GdprExportResponse.cs` | Explicit JsonPropertyName on all properties | VERIFIED | All top-level and nested record properties have `[JsonPropertyName("snake_case_key")]`; no JsonNamingPolicy used |
| `src/GameKit.Core/Services/IPostSessionCompleteHandler.cs` | Port interface in Core | VERIFIED | Exists; `OnCompletedAsync(Guid sessionId, IReadOnlyList<SessionParticipantSnapshot> participants, CancellationToken ct)` |
| `src/GameKit.Core/Services/IIdempotencyStore.cs` | Port interface in Core | VERIFIED | Exists; `TryGetAsync` + `StoreAsync` |
| `src/GameKit.Core/Services/ICanonicalRequestHasher.cs` | Port interface in Core | VERIFIED | Exists |
| `src/GameKit.Core/Http/SessionEndpoints.cs` | POST /api/sessions/{id}/complete | VERIFIED | Exists; `RequireAuthorization("RequiresServiceToken")` as string literal; `IdempotencyKeyEndpointFilter` applied; rate limit applied |
| `src/GameKit.Core/Http/EndpointFilters/IdempotencyKeyEndpointFilter.cs` | Generic Core primitive | VERIFIED | Exists in Core (not Rankings) |
| `src/GameKit.Core/Http/EndpointFilters/ValidationEndpointFilter.cs` | Generic Core primitive | VERIFIED | Exists in Core |
| `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` | IPostSessionCompleteHandler impl | VERIFIED | Exists; skips null PlayerId (Pitfall §12) |
| `src/GameKit.Rankings/Services/RankingsIdempotencyStore.cs` | IIdempotencyStore impl | VERIFIED | Exists; writes to session_complete_idempotency |
| `src/GameKit.Rankings/Json/CanonicalJsonHasher.cs` | Body canonicalization for idempotency | VERIFIED | Exists; sorts by PlayerId; `JsonNamingPolicy.CamelCase`; `WriteIndented = false` |
| `src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationHandler.cs` | SHA-256 token lookup | VERIFIED | Exists; checks revoked/expired; updates LastUsedAt (WR-04 fix with IMemoryCache debounce) |
| `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` | AddRankings + AddLadder | VERIFIED | Exists |
| `src/GameKit.Rankings/Services/StartupLadderUpserter.cs` | IHostedService ladder upsert | VERIFIED | Exists |
| `src/GameKit.Admin.UI/Components/Dialogs/EndSeasonDialog.razor` | Type-the-name confirmation gate | VERIFIED | Exists; injects `IEndSeasonService`; type-the-name-to-confirm gate per `EndSeasonDialog.razor:4-13` |
| `src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor` | Rating adjust dialog | VERIFIED | Exists; injects `IRankAdjustService`; `MudNumericField` with config-driven min/max (WR-06 fix) |
| `THIRD-PARTY-NOTICES.md` | Verbatim BSD-3-Clause LICENSE for Glicko-2 | VERIFIED | Contains `BSD-3-Clause`, MaartenStaa attribution, commit SHA `59033eec`, verbatim license text |
| `REUSE.toml` | Glicko2 files annotated BSD-3-Clause AND GPL-3.0-or-later | VERIFIED | `src/GameKit.Rankings/Glicko2/*.cs` annotated with `SPDX-License-Identifier = "BSD-3-Clause AND GPL-3.0-or-later"` |
| `tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json` | Expected outputs 1464.05/151.52/0.05999 | VERIFIED | Contains `"rating": 1464.05`, `"ratingDeviation": 151.52`, `"volatility": 0.05999` |
| `tests/GameKit.TestFixtures/RankingsFixture.cs` | PostgresFixture + RedisFixture composite | VERIFIED | Exists; confirmed via earlier build checks |
| `samples/TicTacToeDuel/Program.cs` | Full Phase 1-4 stack | VERIFIED | `AddRankings(...)` and `AddLadder("main", ...)` confirmed at lines 59-73; `MapRankings()` at line 108 |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `Glicko2Algorithm.cs` | `RatingCalculator.cs` | `new RatingCalculator(initVolatility: _initVolatility, tau: _tau)` | VERIFIED | `tau = 0.5` confirmed as default constructor param and usage |
| `RankingsTickerService.cs` | `IRankingAlgorithm.Apply` | `_algorithm.Apply(state, batch)` exactly once per drain | VERIFIED | Line 318; single call confirmed; `BuildMatchOutcomes` canonical-perspective fix (CR-01) |
| `RankingsTickerService.cs` | `RankingsTickerLeaseHelper` | `TryAcquireLeaseAsync` / `LockExtendAsync` (return-value check) / `ReleaseLeaseAsync` | VERIFIED | `RenewLeaseAsync` return value consumed per Pitfall §6 |
| `SessionEndpoints.cs` | `ServiceTokenAuthenticationDefaults.PolicyName` | String literal `"RequiresServiceToken"` (zero compile-time dep on Rankings) | VERIFIED | Line 48-50 of SessionEndpoints.cs |
| `SessionCompleteService.cs` | `IPostSessionCompleteHandler` | Optional port call `OnCompletedAsync` | VERIFIED | Null-safe injection confirmed at lines 84-86 |
| `SessionCompleteService.cs` | `IIdempotencyStore` | `TryGetAsync` / `StoreAsync` | VERIFIED | Optional port; dedup runs inside caller's tx per contract |
| `RankingsIdempotencyStore.cs` | `SessionCompleteIdempotency` entity | `_ctx.Set<SessionCompleteIdempotency>()` | VERIFIED | Exists |
| `PendingRatingUpdatesAdapter.cs` | `PendingRatingUpdate` entity | `_ctx.Set<PendingRatingUpdate>().Add(...)` | VERIFIED | Skips null PlayerId (Pitfall §12) |
| `EndSeasonService.cs` | `AdminAuditLog` (Core entity) | Direct `_ctx.Set<AdminAuditLog>().Add(...)` (no Admin.UI dep) | VERIFIED | Lines 193-204; `LadderEndSeasonAction = "admin.ladder.end_season"` string constant |
| `RankAdjustService.cs` | `AdminAuditLog` | Direct `_ctx.Set<AdminAuditLog>().Add(...)` (no Admin.UI dep) | VERIFIED | String constant `"admin.player.rank_adjust"` at line 42 |
| `MainLayout.razor` | `EndSeasonDialog` | `"end-season" => typeof(EndSeasonDialog)` switch arm | VERIFIED | Line 147 confirmed |
| `MainLayout.razor` | `RankAdjustDialog` | `"rank-adjust" => typeof(RankAdjustDialog)` switch arm | VERIFIED | Line 148 confirmed |
| `AdminCommandRegistry.AllCommands` | `end-season` entry | `new("end-season", "End ladder season", "actions", RequiresSuperadmin: true, RequiresTarget: true)` | VERIFIED | Line 40 confirmed |
| `AuditSentenceTemplates.Registry` | `LadderEndSeason` mapping | `[AdminAuditActions.LadderEndSeason] = ctx => ...` | VERIFIED | Lines 67+ confirmed |
| `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` | `GameKit.Rankings` | ProjectReference (plan 04-07 controlled dep-direction) | VERIFIED | Line 28 of Admin.UI.csproj |
| `samples/TicTacToeDuel/Program.cs` | `AddRankings().AddLadder("main")` | Wired after AddAuth, before MapGameKit | VERIFIED | Lines 59-73; `MapRankings()` line 108 |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `RankingsTickerService.DrainLadderAsync` | `pending_rating_updates` rows | EF Core query on `PendingRatingUpdate` WHERE `AppliedAt IS NULL AND PlayerId IS NOT NULL` | Yes — real DB rows from `PendingRatingUpdatesAdapter.OnCompletedAsync` | FLOWING |
| `GdprExportService.ExportWithSizeAsync` | Player/identities/credentials/sessions/ranks | 6 REPEATABLE READ EF Core queries filtered by `PlayerId` | Yes — live Postgres queries | FLOWING |
| `LeaderboardService.TopAsync` | `player_ranks` rows sorted by Rating DESC | EF Core query using `idx_player_ranks_ladder_rating` index | Yes | FLOWING |
| `RankAdjustService.AdjustAsync` | `PlayerRank` row before/after | EF Core SELECT + UPDATE in SERIALIZABLE tx | Yes | FLOWING |
| `EndSeasonService.EndAsync` | `player_ranks` snapshot → `season_rank_archive` | EF Core SELECT all rows + batch INSERT archive rows in SERIALIZABLE tx | Yes | FLOWING |
| `SessionCompleteService.CompleteAsync` | State-conditional UPDATE result | `ExecuteUpdateAsync WHERE State == Active` | Yes — UPDATE affected count drives control flow | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Solution builds with 0 warnings/errors | `dotnet build GameKit.sln -c Debug --nologo -v quiet` | `Build succeeded. 0 Warning(s) 0 Error(s)` | PASS |
| GameKit.Rankings.Tests unit tests | `dotnet test tests/GameKit.Rankings.Tests/ -c Debug --nologo` | `Passed! 9 passed, 0 failed` | PASS |
| GameKit.Core.Tests unit tests | `dotnet test tests/GameKit.Core.Tests/ -c Debug --nologo` | `Passed! 131 passed, 0 failed` | PASS |
| GameKit.Auth.Tests unit tests | `dotnet test tests/GameKit.Auth.Tests/ -c Debug --nologo` | `Passed! 35 passed, 0 failed` | PASS |
| GameKit.Admin.Tests unit tests | `dotnet test tests/GameKit.Admin.Tests/ -c Debug --nologo` | `Passed! 92 passed, 0 failed` | PASS |
| Integration tests (Docker required) | `dotnet test tests/GameKit.Rankings.Integration.Tests/` | SKIP — Docker unavailable | SKIP |
| tau=0.5 in Glicko2Algorithm | `grep "tau.*0\.5" src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` | Found at line 48 | PASS |
| Core has zero Rankings reference | `grep -r "GameKit\.Rankings" src/GameKit.Core/ --include="*.cs"` | No matches | PASS |
| FK constraint in migration | `grep "fk_game_sessions_ladders" src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs` | Found at line 298 | PASS |
| Advisory lock key distinct | `RankingsMigrationConstants.AdvisoryLockKey = -156812172L` vs Core `1800940027L`, Auth `-298890956L`, Admin `-2101739634L` | All distinct | PASS |
| JsonPropertyName on GdprExportResponse | `grep "JsonPropertyName" src/GameKit.Rankings/Http/Contracts/GdprExportResponse.cs` | 30+ matches — all properties annotated | PASS |
| IRankingAlgorithm has exactly one Apply method | `Glicko2AlgorithmContractTests.IRankingAlgorithm_Has_Only_Apply_Batch_Method` (unit test passed) | PASS | PASS |

### Probe Execution

No conventional `scripts/*/tests/probe-*.sh` probes declared for this phase. Step 7c skipped.

### Requirements Coverage

| Requirement | Source Plans | Description | Status | Evidence |
|-------------|-------------|-------------|--------|---------|
| RANK-01 | 04-01, 04-02, 04-04 | Library ships as GameKit.Rankings NuGet package | VERIFIED | `src/GameKit.Rankings/GameKit.Rankings.csproj` exists; IsPackable not false (production package); builds successfully |
| RANK-02 | 04-02 | ladders entity | VERIFIED | `Ladder.cs` entity + `LadderConfiguration.cs`; migration creates `ladders` table |
| RANK-03 | 04-02 | player_ranks with double precision columns | VERIFIED | `HasColumnType("double precision")` on Rating/RatingDeviation/Volatility in `PlayerRankConfiguration.cs`; confirmed in migration SQL |
| RANK-04 | 04-03, 04-06 | IRankingAlgorithm.Apply batched-only | VERIFIED | One public method; reflection test confirms; ticker calls Apply once per drain |
| RANK-05 | 04-03 | Default Glicko2Algorithm vendored | VERIFIED | 4 vendored files under `Glicko2/`; BSD-3-Clause dual headers; `Glicko2WorkedExampleTests` verifies 1464.05/151.52/0.05999 |
| RANK-06 | 04-06 | 1000-match convergence integration test | HUMAN NEEDED | `Glicko2ConvergenceTests.Two_Populations_Converge_Within_Tolerance` exists with correct structure; requires Docker to run |
| RANK-07 | 04-06, 04-08 | Rank records created lazily on first match | VERIFIED (code) | `DrainLadderAsync` INSERTs `player_ranks` if missing; `RankAdjustService` also lazy-creates; `LazyRankCreationTests.Rank_Row_Created_On_First_Match_Drain` test exists |
| RANK-08 | 04-07 | Leaderboard top-N + around-me | VERIFIED (code) | `LeaderboardService.TopAsync` + `AroundAsync` with seasonal override; `idx_player_ranks_ladder_rating` index declared; 4 tests exist |
| RANK-09 | 04-04 | AddLadder("name") registration API | VERIFIED | `RankingsBuilderExtensions.AddLadder` exists; `StartupLadderUpserter` runs on startup; `LadderUpsertOnStartupTests.AddLadder_Inserts_Row_Idempotently` test exists |
| RANK-10 | 04-07 | Seasonal leaderboard reset + archival | VERIFIED (code) | `EndSeasonService.EndAsync` implements all three reset policies in SERIALIZABLE tx; 6 `SeasonArchiveLeaderboardTests` test methods exist |
| RANK-11 | 04-05 | POST /api/sessions/{id}/complete idempotent | VERIFIED (code) | Endpoint in Core; state-conditional UPDATE; Idempotency-Key filter; 5 `SessionCompleteIdempotencyTests` test methods confirmed (Cancelled test also present); SC#2 test exists |
| RANK-12 | 04-08 | Manual rank adjust writes audit atomically | VERIFIED (code); HUMAN NEEDED (integration) | `RankAdjustService` SERIALIZABLE + audit; `UpdateAndAudit_RollBack_Together_On_Failure` SC#6 test exists |
| RANK-13 | 04-08 | GDPR export GET /api/players/{id}/export | VERIFIED (code); HUMAN NEEDED (integration) | `GdprExportService` REPEATABLE READ; `RankingsPlayerEndpoints` registers the endpoint; `GdprExportContractTests` has 5 test methods (2 planned tests may be missing — see human verification item) |
| RANK-14 | 04-02 | Per-package migrations __ef_migrations_rankings | VERIFIED | `RankingsMigrationConstants.MigrationsHistoryTable = "__ef_migrations_rankings"`; migration file `20260515000000_RankingsInitial.cs`; advisory lock key live-verified at -156812172L |

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `ServiceTokenAuthenticationHandler.cs` | 33 | `TODO(v2): Add IMemoryCache TTL layer` | INFO | Intentional v2 deferral; no issue reference required — this is an enhancement not a gap; accepted per code review WR-04 which already added IMemoryCache debounce for `TouchLastUsedAsync` |
| `ServiceTokenAuthenticationOptions.cs` | 14 | `TODO(v2): Add CacheTtlSeconds property` | INFO | Same v2 deferral context; purely additive future config knob |
| Multiple integration test files | Various | SQL string interpolation in test helpers (WR-11) | WARNING (deferred) | Intentionally deferred per code reviewer's explicit suggestion; production code uses parameterised SQL; test-side risk only; follow-up plan planned |

No `TBD`, `FIXME`, or `XXX` markers found anywhere in `src/GameKit.Rankings/` or the key Core/Admin files modified by this phase. The two `TODO(v2)` markers are appropriately scoped enhancement notes, not unresolved gaps.

### Human Verification Required

### 1. Integration Test Suite (Docker Required)

**Test:** Run `dotnet test tests/GameKit.Rankings.Integration.Tests/ -c Debug` in a Docker-capable environment
**Expected:** All integration test classes pass — Glicko2ConvergenceTests, SessionCompleteIdempotencyTests, SeasonArchiveLeaderboardTests, GdprExportContractTests, AdminRankAdjustTransactionTests, RankingsTickerLeaderElectionTests, LazyRankCreationTests, IdempotencyCleanupServiceTests, LadderUpsertOnStartupTests, RankingsMigrationDeterminismTests, RankingsAdvisoryLockKeyTests, SchemaTypeAssertions, ServiceTokenAuthenticationHandlerTests, LeaderboardServiceTests
**Why human:** All integration tests require Testcontainers (Postgres 17.9 + Redis). The orchestrator environment does not have Docker.

### 2. SC#1 Convergence After CR-01 Fix

**Test:** Confirm `Glicko2ConvergenceTests.Two_Populations_Converge_Within_Tolerance` passes with the corrected single-perspective match emission
**Expected:** Mean strong-population rating within ±50 of 1700; mean weak-population rating within ±50 of 1300 after 1000 matches / 100 rating periods with `Random(42)` seed
**Why human:** CR-01 changed the ticker's double-count fix — the ±50 tolerance was documented as "probably loose enough to absorb" the correction, but this must be confirmed empirically with Docker.

### 3. SC#5 GDPR Test Coverage Gap

**Test:** Review `GdprExportContractTests` — the plan specified 6 test methods but only 5 are found by name. Confirm that:
- Sub-mismatch returns 403 (player JWT for player A cannot export player B's data)
- Admin path (GET /admin/api/players/{id}/export) requires Superadmin policy and writes an `admin.player.gdpr_export` audit row

**Expected:** Either these behaviors are covered by the existing 5 test methods under different names, or 2 additional tests need to be added
**Why human:** Requires Docker to run tests; requires human review of actual test coverage vs. planned coverage

### 4. SC#6 Rank-Adjust Test Coverage Gap

**Test:** Review `AdminRankAdjustTransactionTests` — the plan specified 7 test methods including `ShortReason_Returns_400`, `MissingAntiforgery_Returns_400`, and `PlayerJWT_Returns_403`. The implementation has 8 tests but with different names.
**Expected:** Either short reason / missing antiforgery / wrong auth scheme are covered by the existing tests (`EmptyReason_Throws_ArgumentException` may cover the short-reason case; antiforgery and JWT rejection may be untested), or additional tests need to be added
**Why human:** Requires Docker; requires human judgment on whether the gap is a real missing test or a naming variation

### 5. CR-02 Per-Session Delta Semantics

**Test:** Review the v1 limitation in `RankingsTickerService.DrainLadderAsync` where multi-session drains attribute delta only to the latest session
**Expected:** Confirm the product-level decision to attribute the period-aggregate delta to the latest session only (with earlier sessions receiving RatingAfter = pre-drain rating) is acceptable for v1
**Why human:** This is a product/behavior decision that requires human judgment; cannot be verified programmatically

### 6. Blazor UI — EndSeasonDialog Type-Confirm Gate

**Test:** In a browser with the sample app running, invoke the "end-season" palette verb and verify the dialog requires typing the exact ladder name before the button enables
**Expected:** Dialog renders; submit button disabled until `_confirmName == LadderName`; submitting calls `EndSeasonService.EndAsync`
**Why human:** Blazor Server interactive behavior cannot be verified via static analysis

### 7. Blazor UI — RankAdjustDialog Min/Max Binding

**Test:** In a browser, invoke the "rank-adjust" palette verb and verify the numeric field enforces `GameKitRankingsOptions.RankAdjust.MinRating/MaxRating` (100/4000 defaults) from injected `IOptions`
**Expected:** MudNumericField min/max come from options (WR-06 fix) rather than hardcoded
**Why human:** Blazor Server interactive behavior cannot be verified via static analysis

### Gaps Summary

No blockers were found. All 6 success criteria are implemented in code. The solution builds cleanly. 267 unit tests pass across all packages. Integration tests require Docker (Testcontainers) — these are classified as `human_needed` rather than `gaps_found` because the code is substantive and wired, not stub or hollow. Two test coverage items (SC#5 GDPR sub-mismatch/admin-path tests; SC#6 short-reason/antiforgery/JWT tests) require human confirmation of whether the missing planned test names are genuine gaps or covered under different method names.

**Known deferred item (WR-11, intentional):** Test-side SQL string interpolation in integration test helpers was explicitly deferred by the code reviewer — production code uses parameterised SQL throughout; follow-up cleanup plan expected.

---

_Verified: 2026-05-16T12:00:00Z_
_Verifier: Claude (gsd-verifier), Sonnet 4.6_
