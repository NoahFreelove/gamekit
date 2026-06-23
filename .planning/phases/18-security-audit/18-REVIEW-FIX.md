---
phase: 18-security-audit
fixed_at: 2026-06-23T11:45:00Z
review_path: .planning/phases/18-security-audit/18-REVIEW.md
iteration: 1
findings_in_scope: 5
fixed: 5
skipped: 0
status: all_fixed
---

# Phase 18: Code Review Fix Report

**Fixed at:** 2026-06-23T11:45:00Z
**Source review:** .planning/phases/18-security-audit/18-REVIEW.md
**Iteration:** 1

**Summary:**
- Findings in scope: 5
- Fixed: 5
- Skipped: 0

## Fixed Issues

### CR-01 + WR-01 + WR-02: Fail-closed egress guard, no BuildServiceProvider, fix duplicate summary

**Files modified:**
`src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs`,
`src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs`,
`tests/GameKit.Auth.Apple.Tests/AppleProviderTests.cs`,
`tests/GameKit.Auth.Google.Tests/GoogleProviderTests.cs`

**Commit:** f18ed5c

**Applied fix (CR-01):** Replaced the `if (authOpts is not null)` null-guard in both
`AddApple` and `AddGoogle` with a fail-closed `??  throw new InvalidOperationException(...)`
that fires immediately if `AddAuth` has not been called. The error message names both methods
involved and the required call order. The inner backchannel null-guard (`if (resolvedOpts is not null)`)
was removed entirely since `authOpts` is already asserted non-null at the top of each method
and is reused via lambda closure.

**Applied fix (WR-01):** All four `BuildServiceProvider()` calls (two per file) replaced with a
direct `IServiceCollection` descriptor scan:
```csharp
builder.Services
    .Where(d => d.ServiceType == typeof(GameKitAuthOptions) && d.ImplementationInstance is not null)
    .Select(d => (GameKitAuthOptions?)d.ImplementationInstance)
    .FirstOrDefault()
```
This recovers the exact singleton instance registered by `AddAuth()` without constructing a
`ServiceProvider`, eliminating the startup diagnostic and the undisposed objects. The inner
`resolvedOpts` second scan was removed by reusing the outer `authOpts` variable in the lambda
closure.

**Applied fix (WR-02):** Removed the duplicate `<summary>` XML doc block on both
`AppleProviderHosts` and `GoogleProviderHosts`. Each now has a single `<summary>` merging
the descriptive content, with the host-specific detail (token endpoint, host list) moved into
`<remarks>`.

**New tests added:**
- `AddApple_WithoutAddAuth_Throws_InvalidOperationException` — asserts the fail-closed throw
- `AddApple_AfterAddAuth_RegistersAppleHostOnAllowList` — asserts the happy-path allowlist wiring
- `AddGoogle_WithoutAddAuth_Throws_InvalidOperationException` — asserts the fail-closed throw
- `AddGoogle_AfterAddAuth_RegistersGoogleHostsOnAllowList` — asserts all three Google hosts wired

All 8 Apple tests and 5 Google tests pass. EgressAuditTests (19 tests) all pass.

---

### CR-02: Correct GameKitModelCacheKeyFactory XML doc

**Files modified:** `src/GameKit.Core/Data/GameKitModelCacheKeyFactory.cs`

**Commit:** 45fd3a1

**Applied fix:** Replaced the false production-registration claim ("Registered via
`ReplaceService<IModelCacheKeyFactory, GameKitModelCacheKeyFactory>()` inside `AddGameKit`")
with accurate documentation stating:
- The factory is test-fixture-only (registered in `GdprDeleteCoverageTests`, not in production)
- Production does not need it because migration contexts each use a distinct `IModelCustomizer`
  type, making EF cache keys already distinct without the custom factory
- Consumers who write integration tests sharing an in-process model cache can use it in their
  own test `AddDbContext` call

Chose Option A (minimal — correct the doc, do not wire into production) as production has no
model cache collision risk per the reviewed cache-key analysis.

---

### IN-01: Correct MatchmakingGdprDeleteExtension comment

**Files modified:** `src/GameKit.Matchmaking/Services/MatchmakingGdprDeleteExtension.cs`

**Commit:** d776ec8

**Applied fix:** Updated both the XML doc `<summary>`/`<remarks>` and the inline comment in
`DeletePlayerDataAsync` to accurately reflect that the delete removes ALL `party_members` rows
for the player (`WHERE PlayerId = playerId`), covering BOTH owner and non-owner memberships.
The previous comments said "non-owner only", which was misleading. The updated comments explain:
- Why owner memberships are also deleted here (the RESTRICT FK on `party_members.PlayerId`
  applies to all memberships regardless of owner role)
- How this interacts with the Postgres CASCADE (the cascade attempt on `parties.OwnerPlayerId`
  finds the owned-party member rows already gone — harmless)
- What this hook must NOT do (delete the `parties` rows themselves)

GdprDeleteCoverageTests integration test passes with the corrected comments (comment-only change,
no behavioral difference).

---

## Build and Test Results

**Full solution build:** `dotnet build GameKit.sln -warnaserror` — 0 warnings, 0 errors

**EgressAuditTests:** 19/19 passed

**Apple provider tests (including new CR-01 tests):** 8/8 passed
- `AddApple_WithoutAddAuth_Throws_InvalidOperationException` — PASSED
- `AddApple_AfterAddAuth_RegistersAppleHostOnAllowList` — PASSED

**Google provider tests (including new CR-01 tests):** 5/5 passed
- `AddGoogle_WithoutAddAuth_Throws_InvalidOperationException` — PASSED
- `AddGoogle_AfterAddAuth_RegistersGoogleHostsOnAllowList` — PASSED

**GdprDeleteCoverageTests:** 1/1 passed

---

_Fixed: 2026-06-23T11:45:00Z_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
