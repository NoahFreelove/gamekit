---
phase: 10-account-merge
fixed_at: 2026-06-06T19:10:00Z
review_path: .planning/phases/10-account-merge/10-REVIEW.md
iteration: 1
findings_in_scope: 6
fixed: 6
skipped: 0
status: all_fixed
---

# Phase 10: Code Review Fix Report

**Fixed at:** 2026-06-06T19:10:00Z
**Source review:** `.planning/phases/10-account-merge/10-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 6
- Fixed: 6
- Skipped: 0

## Fixed Issues

### CR-01: `MergePlayersRequestValidator` never registered — validation silently bypassed

**Files modified:** `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs`, `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeEndpointTests.cs`
**Commit:** `63edf51`
**Applied fix:** Added `builder.Services.AddScoped<IValidator<MergePlayersRequest>, MergePlayersRequestValidator>();` alongside the four existing validator registrations at line 208 of `AdminBuilderExtensions.cs`. Added two endpoint integration tests: `CR01_EmptySourceGuid_Returns400` (sourcePlayerId = Guid.Empty → 400) and `CR01_SelfMerge_Returns400` (source == target → 400), both proving the validator now runs before the SERIALIZABLE transaction opens.

---

### CR-02: Redis cleanup deletes a non-existent key — actual presence key never removed

**Files modified:** `src/GameKit.Auth/Services/AccountMergeService.cs`, `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs`
**Commit:** `2c98cd1`
**Applied fix:** Changed `$"gamekit:player:{sourcePlayerId}"` to `$"{PresenceKeyPrefix}{sourcePlayerId}"` where `PresenceKeyPrefix = "presence:"` (private const). Added `PresenceKeyPrefix` alongside the existing `AccountMergeAction` const with a sync-comment pointing to `PresenceRedisKeys.Player` in `GameKit.Presence`. Added two integration tests: `CR02_RedisPresenceKey_DeletedAfterMerge` (seeds `presence:{sourceId}` and asserts deletion) and `CR02_Merge_Succeeds_WhenNoPresenceKeyInRedis` (graceful no-op when key absent).

---

### CR-03: `season_rank_archive` re-point creates duplicate leaderboard entries for the target player

**Files modified:** `src/GameKit.Auth/Services/AccountMergeService.cs`, `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs`
**Commit:** `2c98cd1`
**Applied fix:** Replaced the single blind `UPDATE season_rank_archive SET PlayerId = target WHERE PlayerId = source` with three CTE-based SQL passes mirroring the `player_ranks` conflict-resolution strategy: (A) for conflicting `(SeasonId, LadderId)` pairs where `source.Rating > target.Rating` — delete the target row and re-point the source row in one CTE; (B) for pairs where `source.Rating <= target.Rating` — delete the source row (target already has the higher rating); (C) re-point remaining source-only rows. Added three integration tests covering all three code paths.

---

### WR-01: `AllowInsecureParametersForTesting` has no runtime environment guard

**Files modified:** `src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs`, `src/GameKit.Auth.Argon2/Configuration/GameKitArgon2Options.cs`, `src/GameKit.Auth.Argon2/Services/Argon2InsecureParamGuardHostedService.cs` (new), `tests/GameKit.Auth.Argon2.Tests/Argon2HasherTests.cs`
**Commit:** `c42e2d6`
**Applied fix:** Created `Argon2InsecureParamGuardHostedService` (`IHostedService`, `internal sealed`) that fires at `IHost.StartAsync` (before Kestrel accepts traffic) and throws `InvalidOperationException` when `AllowInsecureParametersForTesting` is `true` and the host environment is NOT Development. Registered it via `builder.Services.AddHostedService<Argon2InsecureParamGuardHostedService>()` in `UseArgon2()`. Updated `GameKitArgon2Options.AllowInsecureParametersForTesting` XML doc to state the Development-only constraint. Added three unit tests via a `StubHostEnvironment` implementation.

---

### WR-02: TOCTOU race — concurrent Committed completion between outer read and tx body produces incorrect 409

**Files modified:** `src/GameKit.Auth/Services/AccountMergeService.cs`, `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs`
**Commit:** `2c98cd1`
**Applied fix:** In `MergeTransactionBodyAsync` Step 2, when `source.MergedIntoPlayerId.HasValue`, added a check: if `source.MergedIntoPlayerId.Value == targetPlayerId`, return `Guid.Empty` (sentinel) instead of throwing `SourceAlreadyMerged`. In `MergeAsync`'s retry loop, added a guard immediately after `MergeTransactionBodyAsync` returns: if `mergeRowId == Guid.Empty`, rollback the empty tx and return `MergeResult.AlreadyMerged(targetPlayerId)`. Only throws `SourceAlreadyMerged` when the merge was to a different target. Added integration test `WR02_SameTarget_SourceAlreadyTombstoned_ReturnsAlreadyMerged`.

---

### IN-01: Unreachable `throw` after SERIALIZABLE retry loop

**Files modified:** `src/GameKit.Auth/Services/AccountMergeService.cs`
**Commit:** `2c98cd1`
**Applied fix:** Replaced the bare `throw new InvalidOperationException("SERIALIZABLE retries exhausted")` (which was already unreachable but served compiler control-flow completeness) with the same throw preceded by a comment block explaining why it is unreachable: on the last attempt a 40001 falls through to `throw;` inside the catch, all other paths return or throw before reaching the end of the loop. The throw is preserved for C# control-flow analysis.

---

## Skipped Issues

None — all six findings were fixed.

---

_Fixed: 2026-06-06T19:10:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
