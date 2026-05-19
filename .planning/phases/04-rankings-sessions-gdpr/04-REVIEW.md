---
phase: 04-rankings-sessions-gdpr
reviewed: 2026-05-16T00:00:00Z
depth: standard
files_reviewed: 113
files_reviewed_list:
  - samples/TicTacToeDuel/Program.cs
  - samples/TicTacToeDuel/TicTacToeDuel.csproj
  - src/GameKit.Admin.UI/Components/Dialogs/EndSeasonDialog.razor
  - src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor
  - src/GameKit.Admin.UI/Components/Layout/MainLayout.razor
  - src/GameKit.Admin.UI/GameKit.Admin.UI.csproj
  - src/GameKit.Admin.UI/Services/AdminAuditActions.cs
  - src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs
  - src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs
  - src/GameKit.Cli/Commands/RankingsCliModelCustomizer.cs
  - src/GameKit.Cli/Commands/ServiceTokenIssueCommand.cs
  - src/GameKit.Cli/Commands/ServiceTokenListCommand.cs
  - src/GameKit.Cli/Commands/ServiceTokenRevokeCommand.cs
  - src/GameKit.Cli/GameKit.Cli.csproj
  - src/GameKit.Cli/Program.cs
  - src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs
  - src/GameKit.Core/Http/EndpointFilters/IdempotencyKeyEndpointFilter.cs
  - src/GameKit.Core/Http/EndpointFilters/ValidationEndpointFilter.cs
  - src/GameKit.Core/Http/SessionEndpoints.cs
  - src/GameKit.Core/Services/ICanonicalRequestHasher.cs
  - src/GameKit.Core/Services/IIdempotencyStore.cs
  - src/GameKit.Core/Services/IPostSessionCompleteHandler.cs
  - src/GameKit.Core/Services/SessionCompleteService.cs
  - src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs
  - src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs
  - src/GameKit.Rankings/Algorithms/RankingBatch.cs
  - src/GameKit.Rankings/Algorithms/RankingState.cs
  - src/GameKit.Rankings/AssemblyInfo.cs
  - src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationDefaults.cs
  - src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationHandler.cs
  - src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationOptions.cs
  - src/GameKit.Rankings/Authentication/ServiceTokenAuthorizationPolicy.cs
  - src/GameKit.Rankings/Builder/GameKitRankingsBuilder.cs
  - src/GameKit.Rankings/Builder/IGameKitRankingsBuilder.cs
  - src/GameKit.Rankings/Builder/LadderConfig.cs
  - src/GameKit.Rankings/Builder/RankingsApplicationBuilderExtensions.cs
  - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Export.cs
  - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Season.cs
  - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs
  - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.Ticker.cs
  - src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs
  - src/GameKit.Rankings/Data/Configurations/LadderConfiguration.cs
  - src/GameKit.Rankings/Data/Configurations/LadderSeasonConfiguration.cs
  - src/GameKit.Rankings/Data/Configurations/PendingRatingUpdateConfiguration.cs
  - src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs
  - src/GameKit.Rankings/Data/Configurations/SeasonRankArchiveConfiguration.cs
  - src/GameKit.Rankings/Data/Configurations/ServiceTokenConfiguration.cs
  - src/GameKit.Rankings/Data/Configurations/SessionCompleteIdempotencyConfiguration.cs
  - src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs
  - src/GameKit.Rankings/Data/RankingsMigrationConstants.cs
  - src/GameKit.Rankings/Data/RankingsMigrationHostedService.cs
  - src/GameKit.Rankings/Data/RankingsModelBuilderExtension.cs
  - src/GameKit.Rankings/Entities/Ladder.cs
  - src/GameKit.Rankings/Entities/LadderSeason.cs
  - src/GameKit.Rankings/Entities/PendingRatingUpdate.cs
  - src/GameKit.Rankings/Entities/PlayerRank.cs
  - src/GameKit.Rankings/Entities/SeasonRankArchive.cs
  - src/GameKit.Rankings/Entities/SeasonResetPolicy.cs
  - src/GameKit.Rankings/Entities/ServiceToken.cs
  - src/GameKit.Rankings/Entities/SessionCompleteIdempotency.cs
  - src/GameKit.Rankings/GameKit.Rankings.csproj
  - src/GameKit.Rankings/GameKitRankingsOptions.cs
  - src/GameKit.Rankings/Glicko2/Rating.cs
  - src/GameKit.Rankings/Glicko2/RatingCalculator.cs
  - src/GameKit.Rankings/Glicko2/RatingPeriodResults.cs
  - src/GameKit.Rankings/Glicko2/Result.cs
  - src/GameKit.Rankings/Http/Contracts/EndSeasonRequest.cs
  - src/GameKit.Rankings/Http/Contracts/GdprExportResponse.cs
  - src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs
  - src/GameKit.Rankings/Http/Contracts/RankAdjustRequest.cs
  - src/GameKit.Rankings/Http/EndpointFilters/AntiforgeryValidationFilter.cs
  - src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs
  - src/GameKit.Rankings/Http/RankingsPlayerEndpoints.cs
  - src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs
  - src/GameKit.Rankings/Http/Validators/EndSeasonRequestValidator.cs
  - src/GameKit.Rankings/Http/Validators/RankAdjustRequestValidator.cs
  - src/GameKit.Rankings/Http/Validators/SessionCompleteRequestValidator.cs
  - src/GameKit.Rankings/Json/CanonicalJsonHasher.cs
  - src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs
  - src/GameKit.Rankings/Services/EndSeasonService.cs
  - src/GameKit.Rankings/Services/GdprExportPayloadTooLargeException.cs
  - src/GameKit.Rankings/Services/GdprExportService.cs
  - src/GameKit.Rankings/Services/IEndSeasonService.cs
  - src/GameKit.Rankings/Services/IGdprExportService.cs
  - src/GameKit.Rankings/Services/ILeaderboardService.cs
  - src/GameKit.Rankings/Services/IRankAdjustService.cs
  - src/GameKit.Rankings/Services/IRankingsTicker.cs
  - src/GameKit.Rankings/Services/IServiceTokenService.cs
  - src/GameKit.Rankings/Services/IdempotencyCleanupService.cs
  - src/GameKit.Rankings/Services/LeaderboardService.cs
  - src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs
  - src/GameKit.Rankings/Services/RankAdjustService.cs
  - src/GameKit.Rankings/Services/RankingsIdempotencyStore.cs
  - src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs
  - src/GameKit.Rankings/Services/RankingsTickerService.cs
  - src/GameKit.Rankings/Services/ServiceTokenService.cs
  - src/GameKit.Rankings/Services/StartupLadderUpserter.cs
  - tests/GameKit.Cli.Tests/ServiceTokenCommandsTests.cs
  - tests/GameKit.Core.Tests/LicenseHeaderTests.cs
  - tests/GameKit.Rankings.Integration.Tests/AdminRankAdjustTransactionTests.cs
  - tests/GameKit.Rankings.Integration.Tests/CollectionDefinitions.cs
  - tests/GameKit.Rankings.Integration.Tests/GdprExportContractTests.cs
  - tests/GameKit.Rankings.Integration.Tests/Glicko2ConvergenceTests.cs
  - tests/GameKit.Rankings.Integration.Tests/IdempotencyCleanupServiceTests.cs
  - tests/GameKit.Rankings.Integration.Tests/LadderUpsertOnStartupTests.cs
  - tests/GameKit.Rankings.Integration.Tests/LazyRankCreationTests.cs
  - tests/GameKit.Rankings.Integration.Tests/LeaderboardServiceTests.cs
  - tests/GameKit.Rankings.Integration.Tests/RankingsAdvisoryLockKeyTests.cs
  - tests/GameKit.Rankings.Integration.Tests/RankingsMigrationDeterminismTests.cs
  - tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs
  - tests/GameKit.Rankings.Integration.Tests/SchemaTypeAssertions.cs
  - tests/GameKit.Rankings.Integration.Tests/SeasonArchiveLeaderboardTests.cs
  - tests/GameKit.Rankings.Integration.Tests/ServiceTokenAuthenticationHandlerTests.cs
  - tests/GameKit.Rankings.Integration.Tests/SessionCompleteIdempotencyTests.cs
  - tests/GameKit.Rankings.Tests/Glicko2/Glicko2AlgorithmContractTests.cs
  - tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs
  - tests/GameKit.Rankings.Tests/Json/CanonicalJsonHasherTests.cs
  - tests/GameKit.TestFixtures/RankingsFixture.cs
findings:
  critical: 6
  warning: 13
  info: 9
  total: 28
status: issues_found
---

# Phase 4: Code Review Report — Rankings + Sessions Wiring + GDPR Export

**Reviewed:** 2026-05-16
**Depth:** standard
**Files Reviewed:** 113 (source + tests in scope; excluded planning artifacts)
**Status:** issues_found

## Summary

Adversarial review of the Phase 4 implementation surfaces **six BLOCKER-class defects**, **thirteen WARNINGS**, and **nine INFO findings**. Most severe is **CR-01**: the live ticker drain path (`RankingsTickerService.BuildMatchOutcomes`) emits two `MatchOutcome` records per match (one per perspective), and `Glicko2Algorithm.Apply` then converts each into a `Result` via `RatingPeriodResults.AddResult(winner, loser)`. The two resulting `Result` objects represent the SAME ordered (winner, loser) pair, so `RatingPeriodResults.GetResults(player)` returns each match **twice**. Glicko-2 then processes a single A-vs-B game as if A played B in two distinct matches in the same rating period — corrupting `v`, `Δ`, and the volatility update. The worked-example and algorithm-contract unit tests pass because they hand-build batches with one outcome per match, so this defect ships into production undetected by the existing test suite.

Other critical issues include a SERIALIZABLE transaction without retry-on-conflict in `RankAdjustService` (40001 errors leak as 500), an EF Core model snapshot drift (`HasFilter("applied_at IS NULL")` while the migration created the index with `WHERE "AppliedAt" IS NULL`), unbounded growth of `pending_rating_updates` rows (only `SessionCompleteIdempotency` is cleaned up despite docs claiming a 30-day retention), `Guid.NewGuid()` used for the GDPR-export audit row in violation of the UUIDv7/`IIdGenerator` convention, and an incorrect ticker per-session delta computation that conflates multi-session rating drift into a single delta value applied across every session a player participated in.

Project conventions verified: PascalCase column names (no snake_case mapping), `HasConversion<string>()` on `Result` enum, per-package migrations under `__ef_migrations_rankings`, vendored Glicko-2 with explicit BSD-3-Clause + GPL dual headers, τ=0.5 default. Most spec-driven discipline holds — the bugs cluster around concurrency, edge cases, and contract glue between subsystems rather than first-principles violations.

## Structural Findings (fallow)

No `<structural_findings>` block was supplied. All findings below are derived from per-file narrative review.

## Narrative Findings (AI reviewer)

## Critical Issues

### CR-01: Glicko-2 double-counts every match in the ticker drain path

**Severity:** BLOCKER
**File:** `src/GameKit.Rankings/Services/RankingsTickerService.cs:444-465` and `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs:84-108`

**Issue:** `BuildMatchOutcomes` emits two `MatchOutcome` records per pairwise match — one from A's perspective `(A, B, resultA)` and one from B's perspective `(B, A, resultB)`. Both records flow into `Glicko2Algorithm.Apply`, which converts each into a `RatingPeriodResults.AddResult(winner, loser)` call. For a single A-wins-B match this produces:

- From `(A, B, Win)`: `AddResult(winner: ratingA, loser: ratingB)` → `Result{winner=A, loser=B}`
- From `(B, A, Loss)`: `AddResult(winner: ratingA, loser: ratingB)` → `Result{winner=A, loser=B}` (because the Loss case is `AddResult(winner: opponent, loser: player)` with `player=B, opponent=A`)

The internal `RatingPeriodResults._results` list now contains TWO identical (or near-identical) `Result` entries for the same physical match. When `RatingCalculator.UpdateRatings` iterates participants and calls `results.GetResults(player)`, both `Result` objects' `Participated(player)` returns true for A AND for B, so each player sees their A-vs-B match **twice**. Glicko-2's `v()`, `Δ()`, and outcome-based rating sums accumulate both — the algorithm effectively believes A played B in two distinct games within one rating period, producing inflated/deflated deltas and an over-confident RD reduction.

The bug is invisible to the existing `Glicko2WorkedExampleTests` and `Glicko2AlgorithmContractTests` because both hand-build batches with one outcome per match (e.g. `new MatchOutcome(playerId, opp1, Win)` is the sole entry for that match). Only the live ticker path triggers double-counting. The `Glicko2ConvergenceTests` 1000-match simulation may still pass because the ±50 tolerance is loose enough to absorb doubled deltas in a stochastic two-population test, but the rating dynamics are **wrong** for real workloads — every actual rating drift is roughly 2× too aggressive, and RD collapses too fast.

**Fix:** Either emit one `MatchOutcome` per match (recommended — pick a deterministic "canonical participant" and only add that perspective), or detect mirror outcomes inside `Glicko2Algorithm.Apply` and skip the redundant `AddResult`. Concretely:

```csharp
// Option A (preferred): in RankingsTickerService.BuildMatchOutcomes,
// emit ONE outcome per match using the lowest PlayerId as the canonical perspective.
for (var i = 0; i < participants.Count; i++)
{
    for (var j = i + 1; j < participants.Count; j++)
    {
        var a = participants[i];
        var b = participants[j];
        if (!a.PlayerId.HasValue || !b.PlayerId.HasValue) continue;

        // Single perspective is sufficient — Result.GetScore handles both sides.
        outcomes.Add(new MatchOutcome(a.PlayerId.Value, b.PlayerId.Value, ParseResult(a.Result)));
    }
}
```

Add a regression test that runs a full A-vs-B match through `Glicko2Algorithm.Apply` (via the ticker pipeline or by constructing both-perspective outcomes manually) and asserts the resulting rating delta matches a one-perspective baseline within ε.

---

### CR-02: `RankingsTickerService` applies wrong per-session rating snapshots

**Severity:** BLOCKER
**File:** `src/GameKit.Rankings/Services/RankingsTickerService.cs:364-383`

**Issue:** When a single ticker drain processes multiple sessions for the same player, the inner loop:

```csharp
foreach (var pid in playerIds)
{
    if (!updatedState.Ratings.TryGetValue(pid, out var newRatingSnapshot)) continue;
    var newRating = newRatingSnapshot.Rating;
    var oldRating = stateDictionary.TryGetValue(pid, out var old) ? old.Rating : defaults.DefaultRating;
    var delta = newRating - oldRating;

    foreach (var sessionId in sessionIds)
    {
        await ctx.SessionParticipants
            .Where(sp => sp.SessionId == sessionId && sp.PlayerId == pid)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(sp => sp.RatingAfter, newRating)
                .SetProperty(sp => sp.RatingDelta, delta), ct);
    }
}
```

writes the **same** `RatingAfter` and `RatingDelta` to EVERY session-participant row for that player, regardless of which session the player actually appeared in. Player B participated in session S1 (alone) but session S2 (without B) is also in `sessionIds`; the WHERE filter does prevent S2 from being touched for B because of the `sp.PlayerId == pid` clause, so the broken update never lands — but it still issues N × M UPDATE statements per tick (`O(playerIds × sessionIds)`), and **for a player who DID participate in multiple sessions in the same drain, every one of their rows receives the SAME `RatingDelta` value** (the period-aggregate delta, not the per-session delta). This is wrong: each `session_participants.RatingDelta` should reflect the rating change attributable to that specific session, not the total change across the period. The session-complete API response (`SessionCompleteResponse`) reads from these columns, so callers will see incorrect per-session deltas.

**Fix:** Distribute the period delta across sessions proportionally (or, more correctly, compute a per-session contribution using the algorithm's intermediate state — non-trivial for Glicko-2 because it batches all outcomes together). For v1 a pragmatic fix is to record the *first* session's snapshot accurately and leave subsequent sessions with `RatingAfter = pre-drain rating` and `RatingDelta = 0`, OR record the same period-aggregate on the player's *last* session only, OR document this as a known limitation. The current behavior — same delta on every session — is misleading.

Also fix the query count by joining `playerIds × sessionIds` into a single `Where(sp => playerIds.Contains(sp.PlayerId) && sessionIds.Contains(sp.SessionId))` and grouping properly.

---

### CR-03: SERIALIZABLE transactions throw `40001` to callers — no retry-on-conflict

**Severity:** BLOCKER
**File:** `src/GameKit.Rankings/Services/RankAdjustService.cs:87-89, 166`; `src/GameKit.Rankings/Services/EndSeasonService.cs:70-72, 204`; `src/GameKit.Rankings/Services/StartupLadderUpserter.cs:70-127`

**Issue:** All three services open `IsolationLevel.Serializable` transactions, but none catches the Postgres `40001 serialization_failure` error and retries. The Phase 4 research document explicitly calls out "SERIALIZABLE retry-on-conflict for `RankAdjustService`" as a known concern. Today:

- `RankAdjustService.AdjustAsync` — two concurrent superadmins adjusting the same `(playerId, ladderId)` row → one gets HTTP 500 (the exception bubbles past `RankingsAdminEndpoints.AdminRankAdjustAsync` which only catches `KeyNotFoundException` and `ArgumentOutOfRangeException`).
- `EndSeasonService.EndAsync` — two concurrent admins triggering `end-season` for the same ladder → same 500 leak.
- `StartupLadderUpserter.StartAsync` — two app replicas booting simultaneously → one `IHostedService` throws on serialization conflict, crashing the host. The docstring at lines 32-33 explicitly *promises* "Postgres serialization failure will cause one to retry," but no retry exists.

`PostgresException.SqlState == "40001"` (Npgsql) is the canonical signal. Without a retry loop, every SERIALIZABLE service is a 500-on-conflict booby trap.

**Fix:**

```csharp
// Wrap each SERIALIZABLE service call in a Polly retry pipeline with 3 attempts,
// exponential backoff, and a predicate matching Npgsql 40001:
private static readonly ResiliencePipeline _serializationRetry = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<DbUpdateException>(ex =>
                ex.InnerException is PostgresException { SqlState: "40001" })
            .Handle<PostgresException>(ex => ex.SqlState == "40001"),
    })
    .Build();

// Usage in AdjustAsync, EndAsync, and StartupLadderUpserter.StartAsync:
return await _serializationRetry.ExecuteAsync(async ct => /* the existing tx body */, ct);
```

Add an integration test that fires N concurrent `AdjustAsync` calls against the same `(player, ladder)` and asserts none returns 500.

---

### CR-04: Partial-index filter case mismatch produces EF-Core model drift

**Severity:** BLOCKER
**File:** `src/GameKit.Rankings/Data/Configurations/PendingRatingUpdateConfiguration.cs:28`; `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs:290-293`; `src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs:291`

**Issue:** The EF Core model configures the partial index with `HasFilter("applied_at IS NULL")` (snake_case, lower), but the migration creates the index with raw SQL `WHERE "AppliedAt" IS NULL` (PascalCase, quoted). Because the project uses PascalCase column names project-wide, the actual database column is `"AppliedAt"`. Two failure modes:

1. The snapshot's `HasFilter("applied_at IS NULL")` (line 291 of `GameKitDbContextModelSnapshot.cs`) does not match what was created on disk. Any subsequent `dotnet ef migrations add` will generate a "drop and recreate the index" migration to reconcile. This silently destabilises the per-package migration story (RANK-02 / Pitfalls §3).
2. The current tests pass because `PendingModelChangesWarning` is *suppressed* in every test fixture (`.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))`). The warning is the very thing that would surface this defect, and it is muzzled in `SessionCompleteIdempotencyTests`, `Glicko2ConvergenceTests`, `RankingsMigrationDeterminismTests`, `AdminRankAdjustTransactionTests`, and `GdprExportContractTests`. The model-drift gate exists but is disabled.

**Fix:** Change the configuration to match what the migration actually creates:

```csharp
b.HasIndex(p => new { p.LadderId, p.EnqueuedAt })
    .HasDatabaseName("idx_pending_rating_updates_ladder_pending")
    .HasFilter("\"AppliedAt\" IS NULL");  // Match PascalCase quoted column name
```

Then regenerate the snapshot (`dotnet ef migrations script` or re-run `dotnet ef migrations add`), confirm the snapshot's `.HasFilter(...)` line updates to the quoted PascalCase form, and **remove the `PendingModelChangesWarning` ignore from all integration tests** so model drift cannot be reintroduced silently.

---

### CR-05: `pending_rating_updates` rows grow unbounded — no cleanup service

**Severity:** BLOCKER
**File:** `src/GameKit.Rankings/Entities/PendingRatingUpdate.cs:18-19`; `src/GameKit.Rankings/Services/IdempotencyCleanupService.cs:1-133`

**Issue:** `PendingRatingUpdate.cs` docstring (lines 18-19) claims rows "are retained after a successful drain (audit trail) and cleaned up by `IdempotencyCleanupService` after the configured retention period (default 30 days)." This is false. `IdempotencyCleanupService.RunCleanupOnceAsync` only deletes rows from `session_complete_idempotency`:

```csharp
var deleted = await ctx.Set<SessionCompleteIdempotency>()
    .Where(r => r.CreatedAt < cutoff)
    .ExecuteDeleteAsync(ct);
```

There is no cleanup of `pending_rating_updates`. After the ticker marks a row with `AppliedAt = now`, the row stays forever. On a busy game backend producing 1k matches/hour with 2 participants each, that's ~17M rows/year, all on the ticker's hot read path (`WHERE LadderId = ? AND AppliedAt IS NULL`). Even with the partial index, EVERY drain scans more table to maintain the index. This is a slow-burn data integrity / operational time-bomb.

**Fix:** Either (a) add a `pending_rating_updates` cleanup pass to `IdempotencyCleanupService.RunCleanupOnceAsync` (delete `WHERE AppliedAt < cutoff` with a configurable TTL, default 30d as the docstring promises), or (b) change `RankingsTickerService.DrainLadderAsync` to DELETE applied rows after a successful commit instead of nulling them, and remove the misleading docstring. The first option preserves the audit-trail intent; the second is simpler. Either way, fix the misleading docstring.

```csharp
// Option (a): extend IdempotencyCleanupService.RunCleanupOnceAsync
var pendingCutoff = clock.UtcNow - _opts.SessionComplete.PendingRetentionTtl; // new option
var pendingDeleted = await ctx.Set<PendingRatingUpdate>()
    .Where(r => r.AppliedAt != null && r.AppliedAt < pendingCutoff)
    .ExecuteDeleteAsync(ct);
```

---

### CR-06: `Guid.NewGuid()` for audit row id violates `IIdGenerator` UUIDv7 convention

**Severity:** BLOCKER
**File:** `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs:193`

**Issue:** `AdminGdprExportAsync` writes an `admin_audit_log` row with `Id = Guid.NewGuid()` (UUIDv4). Every other audit-row insertion in this codebase — `EndSeasonService` line 180, `RankAdjustService` line 153 — uses `_idGen.NewId()` (UUIDv7 from `IIdGenerator`). UUIDv7 ids are time-ordered; the audit log is sorted by id for the admin UI's "recent activity" panel. A mixed UUIDv4/UUIDv7 audit table breaks the implicit timestamp ordering invariant and means `admin.player.gdpr_export` entries appear scattered through history rather than at the head.

The fix is one line: inject `IIdGenerator` into the handler and replace `Guid.NewGuid()` with `idGen.NewId()`.

**Fix:**

```csharp
private static async Task<IResult> AdminGdprExportAsync(
    Guid id,
    HttpContext http,
    IGdprExportService svc,
    GameKitDbContext ctx,
    IClock clock,
    IIdGenerator idGen,                         // add
    CancellationToken ct)
{
    // ...
    var auditRow = new AdminAuditLog
    {
        Id = idGen.NewId(),                     // was Guid.NewGuid()
        // ...
    };
}
```

Add a unit test that captures `AdminAuditLog.Id` for two consecutive GDPR exports and asserts the second is greater than the first (UUIDv7 monotonicity).

---

## Warnings

### WR-01: Session-complete validator does not reject duplicate `PlayerId`

**Severity:** WARNING
**File:** `src/GameKit.Rankings/Http/Validators/SessionCompleteRequestValidator.cs:19-44`

**Issue:** The validator enforces `Count >= 1`, `Count <= 32`, non-empty `PlayerId`, non-negative `Team`, enum-valid `Result`, and non-negative `Score`. It does NOT check for duplicate `PlayerId` values in the participants list. A malicious or buggy game server can submit:

```json
{ "participants": [
  { "playerId": "A", "team": 0, "result": "Win", "score": 10 },
  { "playerId": "A", "team": 1, "result": "Loss", "score": 0 }
] }
```

`SessionCompleteService.RunCompletionAsync` then runs `ExecuteUpdateAsync` twice with the same `(SessionId, PlayerId)` WHERE filter — the second overwrites the first, producing nondeterministic results depending on iteration order. The `PendingRatingUpdatesAdapter` will then enqueue two `pending_rating_updates` rows for the same (Session, Player, Ladder) tuple, and the ticker will count one player as having played the session twice in a single batch.

**Fix:**

```csharp
RuleFor(x => x.Participants)
    .Must(p => p.Select(x => x.PlayerId).Distinct().Count() == p.Count)
    .WithMessage("Each participant PlayerId must appear at most once.");
```

---

### WR-02: `RankAdjustService` uses `beforeRating == 0` as a sentinel for "no prior row"

**Severity:** WARNING
**File:** `src/GameKit.Rankings/Services/RankAdjustService.cs:104-132, 168`

**Issue:** When a player has no existing `player_ranks` row, the code sets `beforeRating = 0` (line 109) to signal "lazy-created — no prior rating." Later (line 131) `beforeRating == 0 ? null : ...` determines whether to write a `before` snapshot to the audit log. The returned `RankAdjustResult.Delta` is `newRating - 0 = newRating`. This conflates two scenarios:

1. Player had no row → real "delta" is undefined; reporting `newRating` as the delta is misleading.
2. Player had an existing row with `Rating = 0` (unlikely with default `MinRating = 100`, but `MinRating` is configurable down to 0 or negative — there is no lower-bound assertion in `GameKitRankingsRankAdjustOptions`).

A configured `MinRating = 0` (e.g. operators using a 0-100 skill scale) would render the sentinel ambiguous and audit rows for first-rating-set-to-100 would be indistinguishable from adjust-from-zero events.

**Fix:** Pass a `bool wasLazyCreated` flag through the function instead of overloading `0.0` as a sentinel. Return `RankAdjustResult(Before: rank.Rating /* pre-update */, After: newRating, Delta: After - Before, WasLazyCreated: rank-was-null)` and let the audit-row builder check the bool explicitly.

---

### WR-03: `AddRankings` calls `AddRateLimiter` again; configuration overwrites are last-write-wins

**Severity:** WARNING
**File:** `src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs:44-83`; `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs:47`

**Issue:** `AddRankingsRateLimits` calls `services.AddRateLimiter(opt => ...)` which registers a fresh `IConfigureOptions<RateLimiterOptions>`. If the host (or `GameKit.Auth`) also calls `AddRateLimiter` to register their own policies (e.g. `gamekit:auth:login`), the framework's option configuration delegates *accumulate*, so policies aggregate correctly — but `opt.RejectionStatusCode` and `opt.OnRejected` are scalar fields set by the last-registered configuration delegate. The Rankings call wins for both, silently overriding any custom `OnRejected` behavior the host registered for auth-tier 429s. Operators get Rankings-flavoured problem+json on auth 429s.

Also: `AddSessionCompleteInfrastructure` calls `services.AddRankingsRateLimits(new Core.RateLimiting.GameKitRateLimitPolicies())` — constructing a fresh `GameKitRateLimitPolicies()` rather than resolving the singleton via DI. Today the class only exposes constants so functionality is unaffected, but the precedent invites future bugs.

**Fix:** Make `AddRankingsRateLimits` purely additive — register the policy via `opt.AddPolicy` only, and document that callers must wire their own `OnRejected`/`RejectionStatusCode` once at the application root. Either move `OnRejected` to `GameKit.Core` (so it's set once for the whole library) or document the contract explicitly. Resolve `IGameKitRateLimitPolicies` from DI rather than `new`.

---

### WR-04: `ServiceTokenAuthenticationHandler` never updates `LastUsedAt`

**Severity:** WARNING
**File:** `src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationHandler.cs:74-94`; `src/GameKit.Rankings/Entities/ServiceToken.cs:51-52`

**Issue:** `ServiceToken.LastUsedAt`'s docstring claims it is "Updated by `ServiceTokenAuthenticationHandler`" but the handler only reads, never writes. The column is included in `ServiceTokenSummaryDto` so `gamekit service-token list` will always show `LastUsedAt = null` for every token regardless of usage. Operators trying to identify dormant tokens for rotation cannot. The Phase 4 plan explicitly accepts the DB hot-read in `HandleAuthenticateAsync` (Pitfall §10), so adding a fire-and-forget `UPDATE service_tokens SET LastUsedAt = NOW() WHERE Id = @id` is consistent with the existing v1 scope.

**Fix:** Either implement the write (one extra round-trip, but already in the hot-path budget; debounce via `IMemoryCache` to write at most once per minute per token), or remove the misleading docstring promise and stop returning the column in `ServiceTokenSummaryDto` until v2 adds it for real.

---

### WR-05: `LeaderboardService.AroundAsync` leaks 500 on missing player rank

**Severity:** WARNING
**File:** `src/GameKit.Rankings/Services/LeaderboardService.cs:151-155, 232-234`

**Issue:** Both `AroundLiveAsync` and `AroundArchiveAsync` throw `KeyNotFoundException` when the target player has no rank row. There is currently no HTTP endpoint mapped to `AroundAsync` (the admin leaderboard handler only calls `TopAsync`), but `ILeaderboardService` is part of the public Rankings surface — game developers calling `AroundAsync` from their own player-facing endpoint will see a 500 if a freshly-registered player hasn't completed a ranked match yet. The interface docstring does not document this behaviour.

**Fix:** Return an empty `IReadOnlyList<LeaderboardRowDto>` when the target rank is missing (or return null and let callers map to 404). Update the interface XML doc.

```csharp
var target = await _ctx.Set<PlayerRank>()
    .AsNoTracking()
    .FirstOrDefaultAsync(r => r.LadderId == ladderId && r.PlayerId == playerId, ct);
if (target is null) return Array.Empty<LeaderboardRowDto>();
```

---

### WR-06: `RankAdjustDialog` hardcodes MudNumericField Min/Max — drifts from `GameKitRankingsRankAdjustOptions`

**Severity:** WARNING
**File:** `src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor:59-60`

**Issue:** `<MudNumericField Min="100" Max="4000" />` mirrors the default `MinRating`/`MaxRating` literally — but the validator (`RankAdjustRequestValidator`) reads bounds from `IOptions<GameKitRankingsOptions>`. An operator who reduces the bounds via `AddRankings(opts => { opts.RankAdjust.MaxRating = 2500; })` will be able to type values in the 2501-4000 range; the server validator then rejects with a 400, which the dialog renders as an inline error after submit. The UI lies about the input contract.

**Fix:** Inject `IOptions<GameKitRankingsOptions>` and bind `Min`/`Max` to the options. Optionally expose them as `Parameter`s if the dialog wants to override per-invocation.

---

### WR-07: `GdprExportService` audit/byte-size path serialises the response twice

**Severity:** WARNING
**File:** `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs:187-189`; `src/GameKit.Rankings/Services/GdprExportService.cs:262-264`

**Issue:** `GdprExportService.ExportAsync` already serialises the response to bytes (line 262) for the 25MB cap check. The admin endpoint handler then calls `JsonSerializer.SerializeToUtf8Bytes(response)` AGAIN (line 188) just to compute `byte_size` for the audit row. For a 20MB legitimate export this is 40MB of allocations + 2× CPU work. Cleaner: have `ExportAsync` return the byte size alongside the response (or thread the serialised bytes through).

**Fix:** Return `(GdprExportResponse Response, long ByteSize)` from `ExportAsync` or expose a `LastSerialisedByteSize` property. Skip the re-serialisation in the admin endpoint.

---

### WR-08: `EndSeasonDialog` and `RankAdjustDialog` pass `CancellationToken.None`

**Severity:** WARNING
**File:** `src/GameKit.Admin.UI/Components/Dialogs/EndSeasonDialog.razor:94`; `src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor:175, 128`

**Issue:** Three call sites pass `CancellationToken.None` to long-running database operations: `EndSeasonSvc.EndAsync`, `RankAdjustSvc.AdjustAsync`, and the dialog's initial `_ladders = await ToListAsync(..., CancellationToken.None)`. If the admin closes the browser tab mid-operation, the Blazor Server circuit disposes but the EF Core transaction continues to completion. For end-season this can leave a partially-acknowledged commit; for rank-adjust the operation completes but the dialog can no longer surface success or failure to the admin.

**Fix:** Hold a `CancellationTokenSource` tied to the component's `IDisposable`/`IAsyncDisposable` lifetime and pass its token. Cancel on `Dispose`.

---

### WR-09: `nav.rank-adjust` Url is `/admin/rank-adjust` but the page is at `/admin/rankings/adjust`

**Severity:** WARNING
**File:** `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs:58`

**Issue:** `nav.rank-adjust` row declares `Url: "/admin/rank-adjust"` but `Components/Pages/RankAdjust.razor` declares `@page "/admin/rankings/adjust"`. Selecting the row in the command palette navigates the user to a 404.

**Fix:** Update the registry to `"/admin/rankings/adjust"` (or move the page route — but the registry is the safer change). Add a test in `AdminCommandRegistryTests` that asserts each nav row's `Url` resolves to a registered `@page` route on a known Razor page.

---

### WR-10: `SessionCompleteService` re-lookup on `InvalidState` path skips the request-hash check

**Severity:** WARNING
**File:** `src/GameKit.Core/Services/SessionCompleteService.cs:188-200`

**Issue:** When the state-conditional UPDATE produces zero rows because the session was already completed by a concurrent call, the fallback path (lines 188-200) looks up `(sessionId, idempotencyKey)` in the idempotency store and returns the cached response if found. **It does NOT compare `lookup.ExistingRequestHash` against the current `requestHash`.** If two concurrent calls A and B race the state UPDATE:

- A computes `hashA`, finds nothing in store, wins the UPDATE, writes `(SessionId, key, hashA, responseA)`.
- B computes `hashB ≠ hashA`, finds nothing in store at line 117, loses the UPDATE (0 rows), then falls through to lines 188-200, finds A's row, returns A's cached response **without checking hashes**.

B receives a `200 OK` carrying the wrong body. The earlier 409-on-mismatch branch (line 121) only fires when B's TryGetAsync at line 117 finds A's row — which requires A to have committed before B's lookup. The race window is real (between A's UPDATE commit and A's idempotency store write — though in practice they're in the same transaction, so it requires the store flush to happen before the transaction commit, which it does via `RankingsIdempotencyStore.StoreAsync` calling `SaveChangesAsync` before the outer Commit).

**Fix:** In the post-UPDATE lookup, compare hashes and return `IdempotencyKeyReused` on mismatch:

```csharp
if (lookup.Found && lookup.ExistingRequestHash != requestHash)
{
    await tx.CommitAsync(ct);
    return new SessionCompleteResult.IdempotencyKeyReused();
}
if (lookup.Found && lookup.CachedResponseBody is { Length: > 0 })
{
    // ... existing return cached path
}
```

---

### WR-11: Tests use string interpolation for SQL — anti-pattern even in test code

**Severity:** WARNING
**File:** `tests/GameKit.Rankings.Integration.Tests/SessionCompleteIdempotencyTests.cs:108-119, 274-287, 361-409, 422-449`; `tests/GameKit.Rankings.Integration.Tests/Glicko2ConvergenceTests.cs:225-302, 311-330`; `tests/GameKit.Rankings.Integration.Tests/AdminRankAdjustTransactionTests.cs:429-470`

**Issue:** Most integration-test helpers (`SeedActivatedSessionAsync`, `SeedPlayerAsync`, `SeedLadderAsync`, `InsertMatchAsync`, `QueryScalarAsync`, `ExecuteAsync`) build SQL via string interpolation: `cmd.CommandText = $"... '{p1Id}', '{now:O}' ...";`. Even though every value is internally controlled, this is a project-wide anti-pattern that:

1. Trains the codebase eye to accept interpolated SQL — a future test author adding user-supplied input will copy-paste the pattern.
2. Breaks if any test data ever contains a single quote (`'O'Brien'` display name → SQL syntax error).
3. Conflicts with the production code's strict use of `cmd.Parameters.AddWithValue(...)` (verified in `GdprExportService.cs:115, 133` and `AdminRankAdjustTransactionTests.QueryRatingAsync` at line 400). The mixed style is confusing.

**Fix:** Replace all interpolated-SQL helpers with parameterised commands. The benefit is small (test correctness is rarely affected) but the consistency win is large. Apply project-wide in a follow-up plan.

---

### WR-12: `Glicko2Algorithm.Apply` builds new `RatingCalculator` per call without thread-safety contract

**Severity:** WARNING
**File:** `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs:60-61`

**Issue:** `Glicko2Algorithm` is registered as a **singleton** in `RankingsBuilderExtensions.Ticker.cs:31`. Singletons may be called concurrently. `Apply` constructs a fresh `RatingCalculator` per call, but the inputs `state` and `batch` are shared `IReadOnlyDictionary` and `IReadOnlyList` — those are safe. However: nothing in `IRankingAlgorithm.Apply`'s contract explicitly says implementations must be re-entrant for concurrent calls. Today only the ticker invokes it (single-threaded inside the lock), but consumers writing their own `IRankingAlgorithm` for testing or alternate paths could share mutable state across calls and corrupt it. The XML doc warns about determinism but not concurrency.

**Fix:** Add a "thread-safety" clause to `IRankingAlgorithm`'s XML doc: "Implementations must be safe for concurrent invocations or document their concurrency model." Optionally tighten the singleton registration to `Scoped` if concurrent invocation is not actually needed.

---

### WR-13: Hardcoded development password in `RankingsDesignTimeDbContextFactory`

**Severity:** WARNING
**File:** `src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs:46`

**Issue:** The fallback connection string is `"...Password=gamekit_owner_dev"`. This string is checked into the GPL-licensed source repository. Operators reading the code may assume `gamekit_owner_dev` is a sentinel for a project-default password and configure their production databases with the same value. The string also appears in many test fixtures (verified via grep) so the pattern is widely propagated. The factory is for design-time `dotnet ef` use only, but the hardcoded literal is unnecessary — `GAMEKIT_MIGRATIONS_CONNECTION` is the documented input path.

**Fix:** Remove the fallback connection string and throw if the env var is unset, OR move the dev default to a `.env.example` file with prominent comments. Mirror the same change in `GameKit.Auth.Data.AuthDesignTimeDbContextFactory` and `GameKit.Admin.UI.Data.AdminDesignTimeDbContextFactory`.

---

## Info

### IN-01: `ServiceTokenIssueCommand` bypasses `IServiceTokenService` and uses `DateTimeOffset.UtcNow` instead of `IClock`

**Severity:** INFO
**File:** `src/GameKit.Cli/Commands/ServiceTokenIssueCommand.cs:79-101, 138`

**Issue:** The CLI verb mints tokens by directly building a `ServiceToken` entity, hashing the raw token inline, and calling `SaveChangesAsync`. The production code path (`ServiceTokenService.IssueAsync`) does identical work behind an interface that future audit/observability additions will hook. The CLI duplicates the helper (`GenerateRaw`, `Sha256Hex`) verbatim and uses `DateTimeOffset.UtcNow.Add(...)` directly (line 138) instead of the project-standard `IClock`.

**Fix:** Resolve `IServiceTokenService` via a minimal DI container (mirror `AdminCreateCommand`'s pattern with `GameKitDbContext` + `IClock` + `IIdGenerator` registered in a `ServiceCollection` inside `ExecuteAsync`). Eliminates duplication and keeps the CLI in step with the lib.

---

### IN-02: `ValidationEndpointFilter<T>` silently passes through when validator is unregistered

**Severity:** INFO
**File:** `src/GameKit.Core/Http/EndpointFilters/ValidationEndpointFilter.cs:35-36`

**Issue:** If `IValidator<TRequest>` is not registered in DI, the filter returns `next(ctx)` without warning. This is "fail open" — a typo in the validator registration silently disables all input validation for that endpoint. A LogWarning would surface the misconfiguration during smoke tests.

**Fix:** `_logger?.LogWarning("No IValidator<{T}> registered — skipping validation.", typeof(TRequest).Name)` once at startup or per-invocation.

---

### IN-03: `LadderSeason` lacks "only one open season per ladder" DB constraint

**Severity:** INFO
**File:** `src/GameKit.Rankings/Data/Configurations/LadderSeasonConfiguration.cs:1-31`; `src/GameKit.Rankings/Entities/LadderSeason.cs:30-43`

**Issue:** `EndSeasonService` finds the current season via `WHERE LadderId = ? AND EndedAt IS NULL` and assumes exactly one such row exists. SERIALIZABLE prevents concurrent insertion of two open seasons, but if a bug elsewhere leaves two rows with `EndedAt IS NULL`, the SERIALIZABLE check fires `FirstOrDefaultAsync` and silently picks one. A partial unique index `(LadderId) WHERE EndedAt IS NULL` defends in depth.

**Fix:** Add a partial unique index to `LadderSeasonConfiguration`:

```csharp
b.HasIndex(s => s.LadderId)
    .HasDatabaseName("uq_ladder_seasons_one_open_per_ladder")
    .IsUnique()
    .HasFilter("\"EndedAt\" IS NULL");
```

---

### IN-04: `RankingsAdminEndpoints.AdminGdprExportAsync` audit write is not idempotent across export retries

**Severity:** INFO
**File:** `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs:157-207`

**Issue:** Every successful GDPR export writes a new `admin_audit_log` row. If the operator retries (browser refresh, network blip), each retry writes another audit row. There is no idempotency key on this endpoint. A single GDPR fulfilment by an operator may produce 3-5 audit rows for the same dispatch event.

**Fix:** Either accept the duplication (it's audit, more is OK), or require an `Idempotency-Key` header for admin GDPR exports too. Document the chosen behaviour.

---

### IN-05: `EndSeasonRequest` contract has no `[Required]` or non-null hint on `ConfirmLadderName`

**Severity:** INFO
**File:** `src/GameKit.Rankings/Http/Contracts/EndSeasonRequest.cs` (unread but referenced)

**Issue:** The validator handles `NotEmpty` but the record likely lacks a `string ConfirmLadderName { get; init; } = string.Empty;` initialiser. Missing default produces a `null!` after deserialisation when the client omits the field, which trips `string.Equals(null, ladder.Name)` → `false`. Validator catches it, but defence in depth would set a default.

**Fix:** Verify the record initialises `ConfirmLadderName` to `string.Empty`. If not, add it.

---

### IN-06: Spectre.Console markup not escaped in CLI error paths

**Severity:** INFO
**File:** `src/GameKit.Cli/Commands/ServiceTokenIssueCommand.cs:99`; `src/GameKit.Cli/Commands/ServiceTokenRevokeCommand.cs:67, 75, 79`

**Issue:** `AnsiConsole.MarkupLine($"[red]ERROR:[/] A service token named '[bold]{name}[/]' already exists.");` — if `name` contains `[` or `]` characters, Spectre interprets them as markup and may garble or throw. Only `ServiceTokenListCommand` calls `Markup.Escape(t.Name)` (line 79).

**Fix:** Apply `Markup.Escape(name)` consistently across all CLI commands that render user-supplied strings.

---

### IN-07: Misleading docstring — `PendingRatingUpdate.Result` lists "forfeit" but only `SessionResult` enum values are written

**Severity:** INFO
**File:** `src/GameKit.Rankings/Entities/PendingRatingUpdate.cs:48`

**Issue:** Docstring says "Values: `"win"`, `"loss"`, `"draw"`, `"forfeit"`" but `PendingRatingUpdatesAdapter.OnCompletedAsync` writes `participant.Result.ToString()` from `SessionResult` enum, whose values are `Win`, `Loss`, `Draw`, `Abandoned`. The ticker's `ParseResult` then case-folds and maps both `"forfeit"` and `"abandoned"` to `MatchResult.Forfeit`. The actual stored values are PascalCase. The doc is misleading.

**Fix:** Update the docstring to enumerate the actual stored values (`"Win"`, `"Loss"`, `"Draw"`, `"Abandoned"`) and document the lower-case mapping accepted by `ParseResult` for inter-op with potential future writers.

---

### IN-08: `GdprExportContractTests.Excludes_GDPR_Cascade_Null_Rows` inserts lowercase `'draw'` into `Result` column

**Severity:** INFO
**File:** `tests/GameKit.Rankings.Integration.Tests/GdprExportContractTests.cs:178`

**Issue:** Test inserts `INSERT INTO ... session_participants ("Result") VALUES (..., 'draw')`. The column is `HasConversion<string>()` against the `SessionResult` enum, which serialises `Draw` (PascalCase). EF Core read-back of `'draw'` may silently coerce to the zero default (`Win`) or fail. The test passes because it asserts only `Assert.Equal(1, sessions.GetArrayLength())` — the result-field value of the returned session is never inspected.

**Fix:** Use `'Draw'` (matching the convention from other tests, e.g. `SessionCompleteIdempotencyTests:395` which correctly uses `nameof(GameSessionState.Active)`).

---

### IN-09: `GameKitRankingsRateLimitOptions` exposed but never wired

**Severity:** INFO
**File:** `src/GameKit.Rankings/GameKitRankingsOptions.cs:106-113`; `src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs:24-27`

**Issue:** `GameKitRankingsSessionCompleteOptions.RateLimit` exposes `PermitLimit` and `Window` for operators to configure, but `RankingsRateLimitRegistrations` hardcodes `SessionsCompletePermitLimit = 300` and `SessionsCompleteWindow = TimeSpan.FromMinutes(1)`. Changing the option has no effect — silent configuration drift. Operators believing they raised the limit to 1000/min will still see 429s at 300.

**Fix:** Resolve `IOptions<GameKitRankingsOptions>` inside the `AddPolicy` factory and read `opts.SessionComplete.RateLimit.PermitLimit / Window`. Remove the hardcoded constants.

---

_Reviewed: 2026-05-16T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer), Opus 4.7 (1M context)_
_Depth: standard_
