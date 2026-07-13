---
phase: 18
plan: "02"
subsystem: security
tags: [gdpr, delete, fk, restrict, extension-pattern, efcore, model-cache]
dependency_graph:
  requires: [18-01]
  provides: [IGdprDeleteExtension, AuthGdprDeleteExtension, MatchmakingGdprDeleteExtension, GameKitModelCacheKeyFactory]
  affects: [GameKit.Core, GameKit.Auth, GameKit.Matchmaking]
tech_stack:
  added:
    - IGdprDeleteExtension interface (GameKit.Core.Services)
    - GameKitModelCacheKeyFactory for EF model cache key disambiguation
  patterns:
    - Option A extension hook pattern (mirrors IModelBuilderExtension)
    - TryAddEnumerable(Scoped) for GDPR extension registration
    - ReplaceService<IModelCacheKeyFactory> scoped to test runtime context
key_files:
  created:
    - src/GameKit.Core/Services/IGdprDeleteExtension.cs
    - src/GameKit.Core/Data/GameKitModelCacheKeyFactory.cs
    - src/GameKit.Auth/Services/AuthGdprDeleteExtension.cs
    - src/GameKit.Matchmaking/Services/MatchmakingGdprDeleteExtension.cs
    - tests/GameKit.Core.Integration.Tests/GdprDeleteCoverageTests.cs
  modified:
    - src/GameKit.Core/Services/GdprDeleteService.cs
    - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
    - src/GameKit.Auth/Data/AuthModelBuilderExtension.cs
    - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs
    - tests/GameKit.Core.Integration.Tests/GameKit.Core.Integration.Tests.csproj
    - tests/GameKit.Core.Tests/Services/GdprDeleteServiceTests.cs
decisions:
  - "Option A IGdprDeleteExtension hook pattern: Core defines interface, Auth/Matchmaking register implementations via TryAddEnumerable(Scoped). GameKit.Core has zero upward references to sibling packages."
  - "Extensions invoked inside SERIALIZABLE transaction between audit SaveChanges and players ExecuteDeleteAsync. Extensions MUST NOT open own transactions or call CommitAsync."
  - "GameKitModelCacheKeyFactory scoped to test BuildServiceProvider only (not AddGameKit) to avoid breaking migration contexts that register extensions in their migration service providers."
  - "AuthModelBuilderExtension.ApplyTo extended to include AccountMergeConfiguration (Rule 1 fix: entity added in Plan 10-02 was missing from runtime model)."
metrics:
  duration: "~45 minutes (continued from previous session)"
  completed: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_changed: 11
status: complete
---

# Phase 18 Plan 02: SEC-04 GDPR Delete Completeness Summary

Fixes two Postgres FK RESTRICT violations that prevented `DeletePlayerAsync` from completing and adds an all-tables integration test proving zero residual rows post-erasure.

## What Was Built

**Task 1 — IGdprDeleteExtension interface + GdprDeleteService wiring (9c725b1)**

Added `IGdprDeleteExtension` to `GameKit.Core.Services` with full XML documentation of the transaction contract (no own transaction, no CommitAsync). `GdprDeleteService` accepts `IEnumerable<IGdprDeleteExtension>` and invokes each inside the SERIALIZABLE transaction, between the audit `SaveChangesAsync` call and the `players.ExecuteDeleteAsync` call. GameKit.Core has no new ProjectReference to Auth or Matchmaking.

**Task 2 — Auth + Matchmaking implementations + DI registration (e08517d)**

- `AuthGdprDeleteExtension` (GameKit.Auth, internal sealed): deletes `account_merges` WHERE `TargetPlayerId = playerId` (SEC-04 GAP 2). Registered via `TryAddEnumerable(Scoped<IGdprDeleteExtension, AuthGdprDeleteExtension>)` in `AddAuth`.
- `MatchmakingGdprDeleteExtension` (GameKit.Matchmaking, internal sealed): deletes `party_members` WHERE `PlayerId = playerId` (SEC-04 GAP 1 — non-owner memberships only; owned party CASCADE is handled by Postgres). Registered via `TryAddEnumerable(Scoped<IGdprDeleteExtension, MatchmakingGdprDeleteExtension>)` in `AddMatchmaking`.

**Task 3 — GdprDeleteCoverageTests all-FK-tables integration test (30a9a9a)**

Seeds a player across every FK table (players, player_credentials, player_identities, refresh_tokens, account_merges, game_sessions, session_participants, ladders, player_ranks, lobbies, lobby_members, parties[owned], party_members[owner+non-owner], matchmaking_tickets, decline_history), calls `IGdprDeleteService.DeletePlayerAsync`, and asserts:
- Zero residual rows in all CASCADE/DELETE tables
- RESTRICT tables (account_merges, party_members) cleaned by extensions
- SET NULL tombstones preserved in session_participants (PlayerId=null) and matchmaking_tickets (PartyId=null, SC#4)
- Audit log row present
- Bystander player and their party survive

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] AuthModelBuilderExtension missing AccountMergeConfiguration**

- **Found during:** Task 3 (runtime context threw "Cannot create a DbSet for 'AccountMerge'")
- **Issue:** `AuthModelBuilderExtension.ApplyTo` applied PlayerIdentity, PlayerCredential, RefreshToken but NOT AccountMerge. AccountMerge was added in Plan 10-02 but was never added to the runtime model extension.
- **Fix:** Added `modelBuilder.ApplyConfiguration(new AccountMergeConfiguration())` to `AuthModelBuilderExtension.ApplyTo`. Updated class XML docs to reflect the 4-entity list.
- **Files modified:** `src/GameKit.Auth/Data/AuthModelBuilderExtension.cs`
- **Commit:** 30a9a9a

**2. [Rule 3 - Blocking] GdprDeleteServiceTests constructor args**

- **Found during:** Task 3 full-suite gate (Core.Tests build error)
- **Issue:** Task 1 added `IEnumerable<IGdprDeleteExtension>` as a required constructor parameter to `GdprDeleteService`. Unit tests in `GdprDeleteServiceTests` passed only 3 args.
- **Fix:** Changed all 3 `new GdprDeleteService(ctx, clock.Object, ids.Object)` calls to pass `Array.Empty<IGdprDeleteExtension>()` as the fourth argument. Added `using System.Collections.Generic;`.
- **Files modified:** `tests/GameKit.Core.Tests/Services/GdprDeleteServiceTests.cs`
- **Commit:** 30a9a9a

**3. [Rule 1 - Bug] EF Core model cache collision between migration and runtime contexts**

- **Found during:** Task 3 (runtime context initially threw "Cannot create a DbSet for 'Ladder'")
- **Issue:** EF Core's default model cache key `(contextType, modelCustomizerType, designTime)` is identical for Core-only migration contexts and the full-runtime context. The first-built Core-only model was reused for the runtime context, which lacks all sibling-package entity types.
- **Fix:** Added `GameKitModelCacheKeyFactory` (new file in `GameKit.Core.Data`) that incorporates the set of registered `IModelBuilderExtension` type names into the cache key. Applied via `ReplaceService<IModelCacheKeyFactory, GameKitModelCacheKeyFactory>()` scoped ONLY to `GdprDeleteCoverageTests.BuildServiceProvider` (not in `AddGameKit`) to avoid interfering with migration contexts in other tests.
- **Files modified:** `src/GameKit.Core/Data/GameKitModelCacheKeyFactory.cs` (new), `tests/GameKit.Core.Integration.Tests/GdprDeleteCoverageTests.cs` (BuildServiceProvider override)
- **Commit:** 30a9a9a

## Test Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| GameKit.Core.Tests | 163/163 | 163/163 | 0 |
| GameKit.Auth.Tests | 37/37 | 37/37 | 0 |
| GameKit.Core.Integration.Tests | 16/18* | 17/18* | +1 (GdprDeleteCoverageTests GREEN) |
| GameKit.Auth.Integration.Tests | 46/46 | 46/46 | 0 |
| GameKit.Matchmaking.Integration.Tests | 84/84 | 84/84 | 0 |

*Pre-existing failure: `Migrate_Twice_Is_Idempotent` (stale assertion, Core has 5 migrations; pre-dates Phase 13, documented in MEMORY.md — ignore in full-suite gates)

## Security Impact

- **SEC-04 GAP 1 FIXED:** `party_members.PlayerId = RESTRICT` no longer blocks `DeletePlayerAsync` for players who are non-owner party members. `MatchmakingGdprDeleteExtension` removes the rows inside the SERIALIZABLE transaction.
- **SEC-04 GAP 2 FIXED:** `account_merges.TargetPlayerId = RESTRICT` no longer blocks `DeletePlayerAsync` for the surviving player in an account merge. `AuthGdprDeleteExtension` removes the rows inside the SERIALIZABLE transaction.
- **GDPR completeness:** All FK tables are now covered. Orphaned PII rows are impossible after a successful `DeletePlayerAsync` call.

## Self-Check: PASSED

- `src/GameKit.Core/Services/IGdprDeleteExtension.cs` exists: FOUND
- `src/GameKit.Core/Data/GameKitModelCacheKeyFactory.cs` exists: FOUND
- `src/GameKit.Auth/Services/AuthGdprDeleteExtension.cs` exists: FOUND
- `src/GameKit.Matchmaking/Services/MatchmakingGdprDeleteExtension.cs` exists: FOUND
- `tests/GameKit.Core.Integration.Tests/GdprDeleteCoverageTests.cs` exists: FOUND
- Commit 9c725b1 exists: FOUND
- Commit e08517d exists: FOUND
- Commit 30a9a9a exists: FOUND
- GdprDeleteCoverageTests: PASSED
- GdprDeleteTombstoneTests: PASSED
