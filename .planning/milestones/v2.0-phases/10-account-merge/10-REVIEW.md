---
phase: 10-account-merge
reviewed: 2026-06-06T00:00:00Z
depth: standard
files_reviewed: 20
files_reviewed_list:
  - src/GameKit.Auth/Services/AccountMergeService.cs
  - src/GameKit.Auth/Services/IAccountMergeService.cs
  - src/GameKit.Auth/Services/MergeConflictException.cs
  - src/GameKit.Auth/Services/MergeResult.cs
  - src/GameKit.Auth/Entities/AccountMerge.cs
  - src/GameKit.Auth/Data/Configurations/AccountMergeConfiguration.cs
  - src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs
  - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
  - src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.cs
  - src/GameKit.Admin.UI/Http/AdminEndpoints.cs
  - src/GameKit.Admin.UI/Http/Contracts/MergePlayersRequest.cs
  - src/GameKit.Admin.UI/Http/Validators/MergePlayersRequestValidator.cs
  - src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs
  - src/GameKit.Admin.UI/Services/AdminAuditActions.cs
  - src/GameKit.Core/Entities/Player.cs
  - src/GameKit.Core/Data/Configurations/PlayerConfiguration.cs
  - src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs
  - src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.cs
  - src/GameKit.Auth.Argon2/Configuration/GameKitArgon2Options.cs
  - src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs
findings:
  critical: 3
  warning: 2
  info: 1
  total: 6
status: issues_found
---

# Phase 10: Code Review Report

**Reviewed:** 2026-06-06T00:00:00Z
**Depth:** standard
**Files Reviewed:** 20
**Status:** issues_found

## Summary

This phase delivers the irreversible superadmin account merge feature (AUTH-23/24/25/26), including the `AccountMergeService` crash-resume state machine, six FK-surgery steps across cross-package tables, the `POST /admin/api/players/merge` superadmin endpoint, the Auth migration, and the Core player tombstone migration. The Argon2 options package is also reviewed.

The overall architecture of the merge service is sound: the three-phase state machine (Pending → Committed → RedisCleaned) is correctly modelled, all raw SQL uses FormattableString interpolation so Guid values are parameterized (no SQL injection), the SERIALIZABLE isolation level + UNIQUE(SourcePlayerId) index correctly prevent double-merge, and the response body correctly never exposes the source player ID. The Argon2 guard is also correctly structured.

Three blockers are present: a missing DI registration for `MergePlayersRequestValidator` that silently disables validation on the merge endpoint; a wrong Redis key string in the cleanup step that deletes a non-existent key while leaving the actual presence key alive; and a data integrity gap where `season_rank_archive` rows are blindly re-pointed without de-duplicating, creating phantom duplicate leaderboard entries for the target player.

## Narrative Findings (AI reviewer)

## Critical Issues

### CR-01: `MergePlayersRequestValidator` never registered — validation silently bypassed

**File:** `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs:205-208`
**Issue:** `AdminBuilderExtensions` registers validators for `LoginRequest`, `BanPlayerRequest`, `CreateAdminRequest`, and `PlayerSearchRequest`, but does NOT register `IValidator<MergePlayersRequest>`. The `ValidationEndpointFilter<T>` in `AdminEndpoints.cs` (line 108) uses `ctx.HttpContext.RequestServices.GetService<IValidator<TRequest>>()` (not `GetRequiredService`). When the service is not found, the filter returns `null` and silently calls `next(ctx)` — passing the request through without any validation. The design contract explicitly states these checks "short-circuit before the merge SERIALIZABLE transaction opens (T-10-04-05)." Without the registration:
- A request with `SourcePlayerId == Guid.Empty` or `TargetPlayerId == Guid.Empty` bypasses the empty-GUID guard and opens a SERIALIZABLE transaction before receiving a `KeyNotFoundException` from the DB.
- A self-merge (`SourcePlayerId == TargetPlayerId`) bypasses the early short-circuit and reaches `MergeTransactionBodyAsync` Step 2, which catches it after loading two player rows and opening a transaction.

**Fix:** Add the missing validator registration in `AdminBuilderExtensions.cs` alongside the existing four registrations:
```csharp
builder.Services.AddScoped<IValidator<MergePlayersRequest>, MergePlayersRequestValidator>();
```

---

### CR-02: Redis cleanup deletes a non-existent key — actual presence key never removed

**File:** `src/GameKit.Auth/Services/AccountMergeService.cs:654`
**Issue:** `RunRedisCleanupAsync` deletes `$"gamekit:player:{sourcePlayerId}"`. This key does not exist in any part of the codebase. The actual presence key written by `RedisPresenceProvider` (in `GameKit.Presence`) is formatted by `PresenceRedisKeys.Player(playerId)` as `"presence:{playerId}"` — no `"gamekit:"` prefix, no `"player:"` segment. The `KeyDeleteAsync` call is therefore a no-op: it targets a key that has never been set, the correct key `"presence:{sourcePlayerId}"` remains alive in Redis, and the source player continues to appear as online or in-match in the presence system until their TTL expires. The comment at line 645 ("Keys will TTL-expire naturally") describes the *fallback*, not the intended active-cleanup path.

**Fix:**
```csharp
// Replace:
await db.KeyDeleteAsync($"gamekit:player:{sourcePlayerId}").ConfigureAwait(false);

// With (matches PresenceRedisKeys.Player format):
await db.KeyDeleteAsync($"presence:{sourcePlayerId}").ConfigureAwait(false);
```
If `GameKit.Auth` is not permitted to take a hard dependency on `GameKit.Presence` (to avoid a circular dependency), define the key constant locally with a comment pointing to `PresenceRedisKeys.Player`, mirroring the `AccountMergeAction` / `AdminAuditActions.AccountMerge` pattern already used in this service.

---

### CR-03: `season_rank_archive` re-point creates duplicate leaderboard entries for the target player

**File:** `src/GameKit.Auth/Services/AccountMergeService.cs:525-532`
**Issue:** Step 10 blindly re-points all source player season archive rows to the target player:
```sql
UPDATE gamekit.season_rank_archive SET "PlayerId" = {targetPlayerId} WHERE "PlayerId" = {sourcePlayerId}
```
Unlike `player_ranks` (which has a `UNIQUE(PlayerId, LadderId)` constraint and receives full conflict-resolution logic in Steps 9a–9c), `season_rank_archive` has no uniqueness constraint on `(PlayerId, SeasonId, LadderId)`. `EndSeasonService` writes exactly one archive row per player per season per ladder. If both source and target played on ladder L in season S, the target will have **two** archive rows for `(L, S)` after the merge. `TopAsync` and `AroundAsync` archived-season leaderboard queries aggregate over this table — the target player will appear twice, their virtual rank will be duplicated, and rank positions for other players will be shifted. This is silent data corruption of historical leaderboards.

**Fix:** Before the blind re-point, delete source archive rows that would conflict with an existing target row:
```csharp
await _ctx.Database.ExecuteSqlAsync(
    $"""
    DELETE FROM gamekit.season_rank_archive
    WHERE "PlayerId" = {sourcePlayerId}
      AND ("SeasonId", "LadderId") IN (
        SELECT "SeasonId", "LadderId"
        FROM gamekit.season_rank_archive
        WHERE "PlayerId" = {targetPlayerId}
      )
    """,
    ct).ConfigureAwait(false);

// Then re-point the non-conflicting source-only rows:
await _ctx.Database.ExecuteSqlAsync(
    $"""
    UPDATE gamekit.season_rank_archive
    SET "PlayerId" = {targetPlayerId}
    WHERE "PlayerId" = {sourcePlayerId}
    """,
    ct).ConfigureAwait(false);
```
Alternatively (and preferably for data quality), apply the same conflict-resolution strategy as `player_ranks` — keep the higher rating row and merge W/L/D. The correct approach depends on the spec for what a merged player's archived-season history should represent, but at minimum the DELETE-before-UPDATE eliminates the duplicate-entry corruption.

---

## Warnings

### WR-01: `AllowInsecureParametersForTesting` has no runtime environment guard

**File:** `src/GameKit.Auth.Argon2/Configuration/GameKitArgon2Options.cs:65`
**Issue:** The `AllowInsecureParametersForTesting` flag bypasses the entire OWASP minimum-parameter guard in `Argon2BuilderExtensions.UseArgon2`. Its documentation says "Must NOT be set in production" but there is no code-level check to enforce this. A deployment misconfiguration — `AllowInsecureParametersForTesting = true` in an `appsettings.Production.json` or via an env var — would silently allow production password hashes to be computed with parameters orders of magnitude weaker than OWASP minimums (e.g. `MemoryCost = 64`, `TimeCost = 1`) without any startup error or log warning.

**Fix:** Add a startup guard that warns loudly (or throws, if a stricter stance is preferred) when the flag is set in a non-Development environment:
```csharp
if (opts.AllowInsecureParametersForTesting)
{
    var env = builder.Services.BuildServiceProvider()
        .GetService<IHostEnvironment>();
    if (env is not null && !env.IsDevelopment())
    {
        throw new InvalidOperationException(
            "GameKitArgon2Options.AllowInsecureParametersForTesting is set outside a Development " +
            "environment. This flag must not be set in production — it disables OWASP password " +
            "hashing security floors.");
    }
}
```
If resolving `IHostEnvironment` mid-registration is undesirable, at minimum log a `Critical`-level message to surface the misconfiguration in production observability.

---

### WR-02: TOCTOU race — concurrent Committed completion between outer read and tx body produces incorrect 409

**File:** `src/GameKit.Auth/Services/AccountMergeService.cs:114-161` and `266-275`
**Issue:** The crash-resume ladder reads `existing.Status` outside the SERIALIZABLE transaction (line 114). When `existing.Status == MergeStatus.Pending`, the code falls through to the retry loop. If, between that read and the execution of `MergeTransactionBodyAsync`, a concurrent request completes the merge (status advances to `Committed` or `RedisCleaned` and `source.MergedIntoPlayerId` is set), Step 2's guard at line 271 fires:
```csharp
if (source.MergedIntoPlayerId.HasValue)
    throw new MergeConflictException(MergeConflictReason.SourceAlreadyMerged, ...);
```
This exception propagates through the retry loop (only `PostgresException` is caught) and reaches `MergePlayersAsync`, which maps it to a `409 Conflict { "error": "sourcealreadymerged" }`. The correct response for the same-target idempotent re-entry is `200 OK { "status": "already_merged" }`. Under normal single-instance usage this race is unlikely, but in multi-instance deployments (load-balanced API servers) it is reachable.

**Fix:** Add a re-check inside the `catch` block (or before the throw at line 272) that queries `AccountMerge` for a completed row with the same source+target, and returns `AlreadyMerged` instead of re-throwing:
```csharp
if (source.MergedIntoPlayerId.HasValue)
{
    // Concurrent request completed the merge while this tx was opening.
    // Re-read the account_merges row to confirm same-target idempotency.
    var completed = await _ctx.Set<AccountMerge>()
        .AsNoTracking()
        .FirstOrDefaultAsync(am => am.SourcePlayerId == sourcePlayerId, ct)
        .ConfigureAwait(false);
    if (completed is not null && completed.TargetPlayerId == targetPlayerId)
        return completed.Id; // caller will return AlreadyMerged
    throw new MergeConflictException(MergeConflictReason.SourceAlreadyMerged, ...);
}
```
This requires threading an "already completed" signal from the tx body back to `MergeAsync`; the simplest approach is a dedicated sentinel return value or a flag parameter.

---

## Info

### IN-01: Unreachable `throw` after SERIALIZABLE retry loop

**File:** `src/GameKit.Auth/Services/AccountMergeService.cs:229`
**Issue:** The statement `throw new InvalidOperationException("AccountMergeService: SERIALIZABLE retries exhausted.");` at line 229 is unreachable. The `for` loop at line 166 covers `attempt = 0..MaxRetries-1`. On the last iteration (`attempt == MaxRetries - 1`), a `40001` failure hits the condition `pg.SqlState == "40001" && attempt < MaxRetries - 1` which is `false`, so the exception is re-thrown at line 225 and exits the loop via an exception propagation path. All other exit paths inside the loop (`return` on success, `throw` on 23505 without a concurrent row, `throw` on non-retryable postgres error) also exit the loop before line 229 is reached. Any C# flow-analysis tool or IDE will mark line 229 as unreachable.

**Fix:** Remove the unreachable throw, or alternatively restructure the loop so the exhausted-retries case is explicitly the post-loop exit condition (rethrowing the last exception).

---

_Reviewed: 2026-06-06T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
