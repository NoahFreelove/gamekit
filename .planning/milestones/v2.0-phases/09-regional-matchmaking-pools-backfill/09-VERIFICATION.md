---
phase: 09-regional-matchmaking-pools-backfill
verified: 2026-06-06T00:00:00Z
status: passed
score: 10/10
overrides_applied: 0
---

# Phase 9: Regional Matchmaking Pools + Backfill Verification Report

**Phase Goal:** Regional matchmaking pools are a first-class concept (no schema migration needed for MATCH-18 routing), and backfill into in-progress sessions ships with the participation-fraction guard in the same unit.
**Verified:** 2026-06-06T00:00:00Z
**Status:** passed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| #   | Truth | Status | Evidence |
|-----|-------|--------|----------|
| 1   | EnqueueRequest with RegionName not in AllowedRegions → HTTP 400 region_not_allowed | ✓ VERIFIED | `MatchmakingService.EnqueueAsync` lines 209-214: guard fires before Redis writes; endpoint maps to `Results.BadRequest(new { error = "region_not_allowed" })`. `RegionalPoolTests.SC1_Enqueue_MismatchedRegionName_Returns400` covers this. |
| 2   | EnqueueRequest with RegionName=null routes to "default" pool (backwards-compatible) | ✓ VERIFIED | `MatchmakingEndpoints.cs:90`: `var resolvedPool = req.RegionName ?? req.PoolName;`; service normalizes null→"default" at line 132. `SC1_NullRegion_RoutesToDefaultPool` asserts ticket is in default pool and not in us-east pool. |
| 3   | Regional enqueue lands in `mm:queue:{ladderId}:{regionName}`, distinct from `mm:queue:{ladderId}:default` | ✓ VERIFIED | `MatchmakingRedisKeys.Queue` returns `$"mm:queue:{ladderId}:{pool}"` (line 72); service uses `MatchmakingRedisKeys.Queue(ladderId, pool)` for ZADD. `SC2_RegionalKey_IsDistinctFromDefaultKey` asserts regional score is non-null, default score is null. |
| 4   | Ticker scans default pool + every AllowedRegions pool per ladder per tick | ✓ VERIFIED | `GetPoolNamesForLadder` (line 509) yields "default" then all `AllowedRegions` entries; inner `foreach (var poolName in GetPoolNamesForLadder(ladderCfg))` at line 221 with per-pool lease renewal. `SC2_TickerGlob_PicksUpBothRegionalAndDefaultKeys` drains both pools in one tick. |
| 5   | POST /api/matchmaking/backfill creates a TicketType=1 (Backfill) ticket in Postgres | ✓ VERIFIED | `BackfillService.cs:179`: `TicketType = MatchmakingTicketType.Backfill`. `BackfillTests.SC3_Backfill_CreatesBackfillTypedTicket` asserts `ticket.TicketType == Backfill` and integer value == 1. |
| 6   | Backfill ticket inserted at Redis score 0 (sorts before all Normal tickets) | ✓ VERIFIED | `BackfillService.cs:207`: `SortedSetAddAsync(queueKey, ticketId.ToString(), score: 0)`. No `ToUnixTimeMilliseconds` call present. `SC3_Priority_BackfillTicket_ProcessedBeforeNormalTicket` asserts `members[0].Score == 0` with backfill ticket at index 0. |
| 7   | Backfill validates session exists and is Active, validates RegionName against AllowedRegions | ✓ VERIFIED | `BackfillService.cs:140-149`: session loaded, null→SessionNotFound, non-Active→SessionNotActive. Region guard at lines 132-135. All outcomes mapped in endpoint handler. |
| 8   | A backfill player with ParticipationFraction below MinParticipationFractionForRating receives NO rating change | ✓ VERIFIED | `PendingRatingUpdatesAdapter.cs:140-148`: reads `ParticipationFraction` AsNoTracking, calls `ReadMinParticipationFraction`, issues `continue` when below threshold. `SC4_ParticipationFractionBelowMinimum_SkipsRatingChange` asserts 0 rows for fraction=0.3 with min=0.5. |
| 9   | Null ParticipationFraction (pre-Phase-9 rows) falls through to normal rating flow | ✓ VERIFIED | Guard at line 140: `sp?.ParticipationFraction.HasValue == true` — only enters guard block when value is non-null. Positive control in SC4 test (fraction=0.8, min=0.5 → exactly 1 row). |
| 10  | IRankingAlgorithm.Apply signature unchanged | ✓ VERIFIED | `IRankingAlgorithm.cs:75`: `RankingState Apply(RankingState state, RankingBatch batch)` — guard is at adapter layer (`continue` before `Add`), never touches the algorithm interface. |

**Score:** 10/10 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/GameKit.Matchmaking/Entities/MatchmakingTicketType.cs` | Integer enum Normal=0, Backfill=1 | ✓ VERIFIED | Exists, `Normal = 0` and `Backfill = 1` present; no `HasConversion`. |
| `src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs` | TicketType column via raw ALTER TABLE | ✓ VERIFIED | 1 `migrationBuilder.Sql` call adding `"TicketType" integer NOT NULL DEFAULT 0` to `gamekit.matchmaking_tickets`. 0 `AddColumn` calls. |
| `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs` | ParticipationFraction column via Core migration | ✓ VERIFIED | Core migration correctly owns the column per per-package boundary rule. Uses `migrationBuilder.AddColumn<double>` (not raw SQL). Migration timestamp 20260519 sorts before Matchmaking migration 20260520. |
| `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` | AllowedRegions + MinParticipationFractionForRating | ✓ VERIFIED | Both properties present with full XML docs. |
| `src/GameKit.Core/Entities/SessionParticipant.cs` | `double? ParticipationFraction` | ✓ VERIFIED | Property present; comment correctly attributes `GameKit.Core migration 20260519000000_AddSessionParticipationFraction`. |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs` | AllowedRegions guard; ladderId-first cfg resolution | ✓ VERIFIED | DB lookup `Where(l => l.Id == ladderId).Select(l => l.Name)` before cfg resolution; region guard uses `StringComparer.OrdinalIgnoreCase`. |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | GetPoolNamesForLadder; per-pool lease renewal | ✓ VERIFIED | Helper at line 509; inner loop with `RenewLeaseAsync` before each pool at lines 221-242. |
| `src/GameKit.Matchmaking/Services/BackfillService.cs` | Backfill at score 0; ladderId-first cfg resolution; session-active gate; dedup | ✓ VERIFIED | All four behaviors confirmed in code. |
| `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` | POST /api/matchmaking/backfill route | ✓ VERIFIED | Route at line 67 with `.RequireAuthorization()`, `.RequireRateLimiting(names.MmEnqueue)`, `.AddEndpointFilter<ValidationEndpointFilter<BackfillRequest>>()`. |
| `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs` | ValidateLadderConfig: AllowedRegions char-class + dedup + reserved + length + MinParticipationFraction range | ✓ VERIFIED | All five validations present including `Regex.IsMatch(region, @"^[a-zA-Z0-9\-]+$")` (WR-01 fix). |
| `src/GameKit.Rankings/Builder/LadderConfig.cs` | `double? MinParticipationFractionForRating` | ✓ VERIFIED | Property at line 95 with full XML doc referencing MATCH-19 SC#4. |
| `src/GameKit.Rankings/Services/StartupLadderUpserter.cs` | Writes MinParticipationFractionForRating into ladder JSONB | ✓ VERIFIED | Line 107: `config.MinParticipationFractionForRating` in the `JsonSerializer.SerializeToDocument` anonymous object. |
| `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` | Participation guard + ReadMinParticipationFraction JSONB helper | ✓ VERIFIED | Guard at lines 133-148; helper at lines 182-196 using `TryGetProperty("MinParticipationFractionForRating")` + `TryGetDouble` with try/catch (T-09-04-02 mitigation). |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `MatchmakingEndpoints.cs` | `MatchmakingService.EnqueueAsync` | `req.RegionName ?? req.PoolName` as `resolvedPool` | ✓ WIRED | Line 90-91; `InvalidRegion` outcome mapped at line 111. |
| `MatchmakerTickerService` | `mm:queue:*:{poolName}` | Inner loop over `GetPoolNamesForLadder(cfg)` | ✓ WIRED | Lines 221-242; glob format unchanged. |
| `MatchmakingEndpoints.cs` | `IBackfillService.BackfillAsync` | Authorized + rate-limited + validated route at `/api/matchmaking/backfill` | ✓ WIRED | Lines 67-70 (route registration); lines 231-252 (handler). |
| `BackfillService.cs` | `mm:queue:{ladderId}:{pool}` | `SortedSetAddAsync` with score 0 | ✓ WIRED | Line 207: `score: 0` confirmed, no `ToUnixTimeMilliseconds`. |
| `PendingRatingUpdatesAdapter.cs` | `session_participants.ParticipationFraction` | `AsNoTracking` re-read selecting `ParticipationFraction` | ✓ WIRED | Lines 133-138. |
| `PendingRatingUpdatesAdapter.cs` | `Ladder.Config` JSONB `MinParticipationFractionForRating` | `ReadMinParticipationFraction` JSONB helper | ✓ WIRED | Lines 142-146; JSONB property key matches writer in `StartupLadderUpserter`. |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|--------------|--------|--------------------|--------|
| `MatchmakingService.EnqueueAsync` | `cfg.AllowedRegions` | `_ladders` (DI-injected list from builder) | Yes — populated at host startup from `GameKitMatchmakingBuilder` | ✓ FLOWING |
| `BackfillService.BackfillAsync` | `ladderName` (cfg resolution) | `_db.Set<Ladder>().AsNoTracking().Where(l => l.Id == ladderId).Select(l => l.Name)` | Yes — real DB query | ✓ FLOWING |
| `PendingRatingUpdatesAdapter.OnCompletedAsync` | `sp.ParticipationFraction` | `_ctx.SessionParticipants.AsNoTracking().Where(...)` real DB read | Yes — real DB query | ✓ FLOWING |
| `PendingRatingUpdatesAdapter.OnCompletedAsync` | `minFraction` | `ReadMinParticipationFraction` reads `Ladder.Config` JSONB | Yes — populated by `StartupLadderUpserter` at startup | ✓ FLOWING |

### Behavioral Spot-Checks

Not run — per verification context instructions, the full suites were confirmed green by the orchestrator (Matchmaking integration 76/76, Rankings integration 74/74, Matchmaking unit 91/91, Core unit 133/133). The critical behaviors are tested by the specific integration test classes verified above.

### Probe Execution

No conventional probes (`scripts/*/tests/probe-*.sh`) declared or applicable for this phase.

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|-------------|-------------|--------|----------|
| MATCH-18 | 09-01, 09-02 | Regional matchmaking pools as first-class concept — AllowedRegions config + region-validated enqueue partitioning | ✓ SATISFIED | SC#1 and SC#2 verified across `MatchmakingLadderConfig`, `MatchmakingService`, `MatchmakerTickerService`, `EnqueueRequest`, `RegionalPoolTests`. |
| MATCH-19 | 09-01, 09-03, 09-04 | Backfill — fill vacated slots; participation-fraction guard ships in the same unit | ✓ SATISFIED | SC#3 and SC#4 verified across `BackfillService`, `IBackfillService`, `MatchmakingEndpoints`, `PendingRatingUpdatesAdapter`, `BackfillTests`, `BackfillParticipationTests`. |

Both MATCH-18 and MATCH-19 are marked Complete in REQUIREMENTS.md Traceability table (lines 94-95). Both requirement IDs declared in PLAN frontmatter are covered.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (none in Phase 9 modified files) | — | — | — | — |

The `PLACEHOLDER` comment in `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Strategy.cs` is a pre-Phase-9 artifact from the Phase 5 commit (`5bdc0c5`) — not a Phase 9 debt marker.

No `TBD`, `FIXME`, `XXX`, `NotImplementedException`, or unreferenced stub markers found in any file modified by this phase.

### Convention Compliance

| Check | Status | Evidence |
|-------|--------|----------|
| MatchmakingTicketType stored as integer, no `HasConversion<string>()` | ✓ PASS | `MatchmakingTicketConfiguration.cs` comment: "Integer enum storage — DO NOT add HasConversion<string>()"; enum file has no conversion attribute. |
| Per-package migration boundary: Core owns `ParticipationFraction`, Matchmaking owns `TicketType` | ✓ PASS | `20260519000000_AddSessionParticipationFraction.cs` (Core) adds the column; `20260520000000_MatchmakingBackfillRegions.cs` (Matchmaking) only adds `TicketType`. Comment in Matchmaking migration explicitly calls out the boundary. |
| WR-03/IN-02 fix: session_participants comment cites correct Core migration | ✓ PASS | `SessionParticipantConfiguration.cs:37` comment: "column added by GameKit.Core migration 20260519000000_AddSessionParticipationFraction"; `SessionParticipant.cs:52-53` XML doc: correct package and timestamp. |
| CR-01 fix: PoolName character-class restriction in EnqueueRequestValidator | ✓ PASS | `EnqueueRequestValidator.cs:26`: `Matches(@"^[a-zA-Z0-9\-]+$").When(x => !string.IsNullOrEmpty(x.PoolName))`. |
| CR-02 fix: ladderId-first cfg resolution in both services | ✓ PASS | Both `MatchmakingService.cs` (lines 183-200) and `BackfillService.cs` (lines 109-127) resolve `Ladder.Name` from DB by `ladderId` before resolving `MatchmakingLadderConfig`. |
| WR-01 fix: AllowedRegions char-class in builder | ✓ PASS | `GameKitMatchmakingBuilder.cs:121`: `Regex.IsMatch(region, @"^[a-zA-Z0-9\-]+$")`. |
| WR-02 fix: BackfillService dedup guard | ✓ PASS | `BackfillService.cs:156-166`: existing active Backfill-typed ticket check before Postgres INSERT. `BackfillOutcome.AlreadyEnqueued = 5` present in `IBackfillService.cs`. |

### Human Verification Required

None — all must-haves are verifiable programmatically and confirmed via code evidence + confirmed-green test suite counts.

---

_Verified: 2026-06-06T00:00:00Z_
_Verifier: Claude (gsd-verifier)_
