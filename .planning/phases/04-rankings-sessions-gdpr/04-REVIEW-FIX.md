---
phase: 04-rankings-sessions-gdpr
fixed_at: 2026-05-16T00:00:00Z
review_path: .planning/phases/04-rankings-sessions-gdpr/04-REVIEW.md
iteration: 1
findings_in_scope: 19
fixed: 18
skipped: 1
status: partial
---

# Phase 4: Code Review Fix Report

**Fixed at:** 2026-05-16T00:00:00Z
**Source review:** `.planning/phases/04-rankings-sessions-gdpr/04-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 19 (6 BLOCKER + 13 WARNING; INFO findings out of scope)
- Fixed: 18
- Skipped: 1 (WR-11 — deferred per reviewer's explicit follow-up suggestion)

**Build status:** `dotnet build GameKit.sln -c Debug --nologo` succeeds with 0 warnings, 0 errors after all commits.

**Unit-test status (non-Docker):**
- `tests/GameKit.Core.Tests` — 131 passed, 0 failed
- `tests/GameKit.Rankings.Tests` — 9 passed, 0 failed
- `tests/GameKit.Auth.Tests` — 35 passed, 0 failed
- `tests/GameKit.Admin.Tests` — 92 passed, 0 failed

**Integration tests (Testcontainers/Docker required):** not executed in this fix pass — defer to the verifier phase. The Glicko-2 convergence test, Rankings migration determinism test, idempotency cleanup tests, ladder upsert tests, lazy rank creation tests, ranking leader-election tests, season archive tests, service-token authentication tests, and session-complete idempotency tests all depend on Postgres + Redis containers and were not exercised here. The `Glicko2ConvergenceTests` 1000-match expectations may need to be regenerated downstream now that CR-01 is fixed — the reviewer flagged that the ±50 tolerance was probably loose enough to absorb the doubled deltas, but the new (correct) numerics should still fit; verifier should run that test first and re-baseline if needed.

## Fixed Issues

### CR-01: Glicko-2 double-counts every match in the ticker drain path

**Files modified:** `src/GameKit.Rankings/Services/RankingsTickerService.cs`
**Commit:** `9823308`
**Applied fix:** Changed `BuildMatchOutcomes` to emit ONE `MatchOutcome` per pairwise match using a deterministic canonical perspective (lowest `PlayerId.CompareTo` wins). The earlier dual-perspective emission caused `Glicko2Algorithm.Apply` to add the same `Result` to `RatingPeriodResults` twice — every player saw their A-vs-B match twice during `UpdateRatings`, inflating delta magnitude and over-tightening RD. Added a remarks block on the helper explaining the canonical-perspective rule and why dual emission is mathematically wrong.

**Note (human verification recommended):** the rating-dynamics change is a logic correctness fix — the existing `Glicko2ConvergenceTests` may have absorbed the bug within its ±50 tolerance. Run that test first under the verifier phase; if it passes, the new (correct) deltas are within tolerance and no re-baseline is needed. If it fails, the test's expected deltas need to be regenerated against the corrected output.

### CR-02: `RankingsTickerService` applies wrong per-session rating snapshots

**Files modified:** `src/GameKit.Rankings/Services/RankingsTickerService.cs`
**Commit:** `c2375e0`
**Applied fix:** For each player in a drain, group their pending rows by `SessionId` ordered by `EnqueuedAt`. Attribute the full period-aggregate `RatingDelta` to the player's LATEST session only; earlier sessions in the same drain receive `RatingAfter = pre-drain rating` and `RatingDelta = 0`. Documented as a v1 limitation in the code (Glicko-2 batches outcomes across the period — there is no clean per-session intermediate state to attribute). For the common single-session-per-drain case this is exact.

**Note (human verification recommended):** logic change — verify the per-session delta semantics meet product expectations before shipping.

### CR-03: SERIALIZABLE transactions throw 40001 to callers — no retry-on-conflict

**Files modified:** `src/GameKit.Rankings/Services/SerializationFailureRetry.cs` (new), `src/GameKit.Rankings/Services/RankAdjustService.cs`, `src/GameKit.Rankings/Services/EndSeasonService.cs`, `src/GameKit.Rankings/Services/StartupLadderUpserter.cs`
**Commit:** `1d6dd65`
**Applied fix:** Added a shared `SerializationFailureRetry.Build(logger, name)` static helper that builds a Polly v8 `ResiliencePipeline` configured for 3 retries, exponential backoff with jitter, predicate matching both `PostgresException { SqlState: "40001" }` and the EF wrapper `DbUpdateException { InnerException: PostgresException { SqlState: "40001" } }`. Wired all three services to wrap their SERIALIZABLE transaction bodies in `_serializationRetry.ExecuteAsync(...)`.

### CR-04: Partial-index filter case mismatch produces EF-Core model drift

**Files modified:** `src/GameKit.Rankings/Data/Configurations/PendingRatingUpdateConfiguration.cs`, `src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs`, `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.Designer.cs`
**Commit:** `feafd86`
**Applied fix:** Changed `HasFilter("applied_at IS NULL")` to `HasFilter("\"AppliedAt\" IS NULL")` (PascalCase quoted) in all three locations (configuration + two snapshots) to match the actual migration SQL. Documented that removing the `PendingModelChangesWarning` suppression from integration tests is a separate follow-up — the snapshot-vs-runtime model hash mismatch (called out in `SchemaTypeAssertions.cs:56`) is a different concern that requires a proper `dotnet ef migrations add` pass.

### CR-05: `pending_rating_updates` rows grow unbounded — no cleanup service

**Files modified:** `src/GameKit.Rankings/GameKitRankingsOptions.cs`, `src/GameKit.Rankings/Services/IdempotencyCleanupService.cs`
**Commit:** `f294a6e`
**Applied fix:** Added a new `GameKitRankingsCleanupOptions` class on the root options with `PendingRetentionTtl` defaulting to 30 days. Extended `IdempotencyCleanupService.RunCleanupOnceAsync` with a second `ExecuteDeleteAsync` pass that deletes `pending_rating_updates` rows where `AppliedAt != null && AppliedAt < cutoff`. Unapplied rows (the ticker's working set) are never touched.

### CR-06: `Guid.NewGuid()` for audit row id violates `IIdGenerator` UUIDv7 convention

**Files modified:** `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs`
**Commit:** `ec4d911`
**Applied fix:** Injected `IIdGenerator idGen` into the `AdminGdprExportAsync` handler and replaced `Guid.NewGuid()` with `idGen.NewId()` on the `AdminAuditLog.Id` assignment. Added a comment block citing CR-06 and the time-ordering invariant of `admin_audit_log`.

### WR-01: Session-complete validator does not reject duplicate `PlayerId`

**Files modified:** `src/GameKit.Rankings/Http/Validators/SessionCompleteRequestValidator.cs`
**Commit:** `fd6988f`
**Applied fix:** Added a `.Must(p => p is null || p.Select(x => x.PlayerId).Distinct().Count() == p.Count)` rule on `Participants` with message "Each participant PlayerId must appear at most once."

### WR-02: `RankAdjustService` uses `beforeRating == 0` as a sentinel for "no prior row"

**Files modified:** `src/GameKit.Rankings/Services/IRankAdjustService.cs`, `src/GameKit.Rankings/Services/RankAdjustService.cs`
**Commit:** `9479d4e`
**Applied fix:** Added `WasLazyCreated: bool` to `RankAdjustResult` record. Replaced the `beforeRating == 0 ? null : ...` branch in the snapshot builder with `wasLazyCreated ? null : ...`. The `Before` field on the result is preserved (still `0` for lazy-created) so existing tests are not broken; new callers can branch on the explicit bool.

### WR-03: `AddRankings` calls `AddRateLimiter` again; configuration overwrites are last-write-wins

**Files modified:** `src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs`, `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs`
**Commit:** `b1c3afa`
**Applied fix:** Removed the `opt.RejectionStatusCode = ...` and `opt.OnRejected = ...` assignments from `AddRankingsRateLimits` (Rankings is now purely additive: `AddPolicy` only). Documented the contract in the XML doc. Added a no-arg `AddRankingsRateLimits()` overload that resolves `IGameKitRateLimitPolicies` from DI via `services.AddOptions<RateLimiterOptions>().Configure<IGameKitRateLimitPolicies>(...)`. `AddSessionCompleteInfrastructure` now calls the no-arg overload instead of `new GameKitRateLimitPolicies()`.

### WR-04: `ServiceTokenAuthenticationHandler` never updates `LastUsedAt`

**Files modified:** `src/GameKit.Rankings/Services/IServiceTokenService.cs`, `src/GameKit.Rankings/Services/ServiceTokenService.cs`, `src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationHandler.cs`
**Commit:** `77ba36b`
**Applied fix:** Added `IServiceTokenService.TouchLastUsedAsync(Guid id, CancellationToken ct)` implemented as a single `ExecuteUpdateAsync` (no SELECT). Wired the auth handler to call it on every successful authentication, debounced via `IMemoryCache` so the DB sees at most one UPDATE per minute per token. The write is best-effort — failures log a warning but never break authentication. `IMemoryCache` is already registered by `GameKit.Core.AddMemoryCache()`.

### WR-05: `LeaderboardService.AroundAsync` leaks 500 on missing player rank

**Files modified:** `src/GameKit.Rankings/Services/LeaderboardService.cs`, `src/GameKit.Rankings/Services/ILeaderboardService.cs`
**Commit:** `d93ff84`
**Applied fix:** Both `AroundLiveAsync` and `AroundArchiveAsync` now return `Array.Empty<LeaderboardRowDto>()` when the target player has no rank row. Updated the `AroundAsync` XML doc to describe the empty-result behavior and removed the `KeyNotFoundException` contract.

### WR-06: `RankAdjustDialog` hardcodes MudNumericField Min/Max

**Files modified:** `src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor`
**Commit:** `eeda19b`
**Applied fix:** Injected `IOptions<GameKitRankingsOptions> RankingsOpts`, replaced `Min="100" Max="4000"` with `Min="@_minRating" Max="@_maxRating"` bound to `RankAdjust.MinRating` / `MaxRating`. Clamped the default `_newRating = 1500.0` into the configured range during `OnInitializedAsync`.

### WR-07: `GdprExportService` audit/byte-size path serialises the response twice

**Files modified:** `src/GameKit.Rankings/Services/IGdprExportService.cs`, `src/GameKit.Rankings/Services/GdprExportService.cs`, `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs`
**Commit:** `5ddffef`
**Applied fix:** Added `IGdprExportService.ExportWithSizeAsync` returning `(GdprExportResponse?, long ByteSize)`. The implementation surfaces the serialized byte length that was already computed for the 25MB cap check. `ExportAsync` is preserved as a thin delegating wrapper for backward compatibility. The admin endpoint now uses the new method and drops the `SerializeToUtf8Bytes(response).Length` re-serialization.

### WR-08: Dialogs pass `CancellationToken.None`

**Files modified:** `src/GameKit.Admin.UI/Components/Dialogs/EndSeasonDialog.razor`, `src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor`
**Commit:** `0fdd4ad`
**Applied fix:** Both dialogs now implement `IDisposable`, hold a private `CancellationTokenSource _cts`, pass `_cts.Token` to all DB / service calls, and cancel + dispose the token source in `Dispose()`. Added a guarded `catch (OperationCanceledException)` block at each call site so cancellation does not surface a misleading error toast.

### WR-09: `nav.rank-adjust` URL mismatch

**Files modified:** `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs`
**Commit:** `d1743b0`
**Applied fix:** Updated the `nav.rank-adjust` registry row to `Url: "/admin/rankings/adjust"` to match the `@page "/admin/rankings/adjust"` declared on `RankAdjust.razor`.

### WR-10: `SessionCompleteService` re-lookup on `InvalidState` path skips the request-hash check

**Files modified:** `src/GameKit.Core/Services/SessionCompleteService.cs`
**Commit:** `9aaa4d2`
**Applied fix:** Added a hash-mismatch check inside the post-UPDATE idempotency lookup. When `lookup.Found` and the stored `ExistingRequestHash` differs from the current `requestHash`, the service now returns `SessionCompleteResult.IdempotencyKeyReused()` instead of the cached response — same semantics as the pre-UPDATE check at line 121.

### WR-12: `Glicko2Algorithm.Apply` thread-safety contract

**Files modified:** `src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs`
**Commit:** `c79a5aa`
**Applied fix:** Added a `Thread-safety` clause to `Apply`'s `<remarks>` documenting that implementations must either be safe for concurrent invocations or document their concurrency model. Noted that `Glicko2Algorithm` satisfies the safe-by-construction discipline (fresh `RatingCalculator` per call, no mutable instance state).

### WR-13: Hardcoded development password in design-time factories

**Files modified:** `src/GameKit.Core/Data/CoreDesignTimeFactory.cs`, `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs`, `src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs`, `src/GameKit.Admin.UI/Data/AdminDesignTimeDbContextFactory.cs`, `tests/GameKit.Core.Tests/Data/CoreDesignTimeFactoryTests.cs`
**Commits:** `d582a40`, `ef7feb9`
**Applied fix:** Removed the `?? "...Password=gamekit_owner_dev"` fallback from all four design-time factories. Each factory now throws `InvalidOperationException` with a clear example when `GAMEKIT_MIGRATIONS_CONNECTION` is unset. Updated `CoreDesignTimeFactoryTests` to set the env var explicitly and added a regression test asserting the thrown error.

## Skipped Issues

### WR-11: Tests use string interpolation for SQL

**Files:** multiple test files across `tests/GameKit.Rankings.Integration.Tests/` (5 files cited)
**Reason:** Deferred per reviewer's explicit suggestion ("Apply project-wide in a follow-up plan"). The fix touches helpers across SessionCompleteIdempotencyTests, Glicko2ConvergenceTests, AdminRankAdjustTransactionTests, and others — a broad refactor that would consume a disproportionate share of the time budget for what the reviewer rated WARNING ("test correctness is rarely affected") and tagged for a follow-up plan. The production code path uses parameterised SQL throughout (verified in `GdprExportService.cs:115, 133`). Re-open in a dedicated cleanup plan.
**Original issue:** Integration test helpers (`SeedActivatedSessionAsync`, `InsertMatchAsync`, etc.) build SQL via `$"... '{p1Id}', '{now:O}' ..."` interpolation. Trains the codebase eye to accept interpolated SQL; breaks on test data containing a single quote; conflicts with production parameterised style.

---

_Fixed: 2026-05-16T00:00:00Z_
_Fixer: Claude (gsd-code-fixer), Opus 4.7 (1M context)_
_Iteration: 1_
