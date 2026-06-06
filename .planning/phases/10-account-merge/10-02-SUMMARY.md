---
phase: 10-account-merge
plan: "02"
subsystem: GameKit.Auth
tags: [migrations, schema, entity-framework, account-merge, auth, integration-tests, crash-resume]
dependency_graph:
  requires:
    - Plan 10-01 (Core schema: merged_into_player_id + deleted_at on players, admin_audit_log FK)
  provides:
    - AccountMerge entity + MergeStatus enum (Pending/Committed/RedisCleaned)
    - account_merges table with UNIQUE(SourcePlayerId) + FK TargetPlayerId RESTRICT
    - MergeResult + MergeConflictException result/exception types
    - AuthMigrationModelCustomizer extended with AccountMergeConfiguration
    - Auth migration 20260606200000_AddAccountMerges under advisory lock -298890956
    - InternalsVisibleTo grants on Auth, Rankings, Matchmaking
    - GameKit.Auth.AccountMerge.Integration.Tests project with cross-package ApplyMigrations
  affects:
    - src/GameKit.Auth/Entities/AccountMerge.cs
    - src/GameKit.Auth/Services/MergeResult.cs
    - src/GameKit.Auth/Services/MergeConflictException.cs
    - src/GameKit.Auth/Data/Configurations/AccountMergeConfiguration.cs
    - src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs
    - src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.cs
    - src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs
    - src/GameKit.Auth/AssemblyInfo.cs
    - src/GameKit.Rankings/AssemblyInfo.cs
    - src/GameKit.Matchmaking/AssemblyInfo.cs
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/
tech_stack:
  added: []
  patterns:
    - Integer-backed MergeStatus enum (no HasConversion<string>) per project convention
    - UNIQUE index on SourcePlayerId for SC#1 double-merge prevention at DB level
    - FK TargetPlayerId ON DELETE RESTRICT (surviving player cannot be GDPR-deleted while merge record exists)
    - No FK on SourcePlayerId (bare UUID — source is soft-deleted, GDPR hard-delete must not block)
    - Per-package migration boundary (Auth owns account_merges, advisory lock -298890956)
    - Deterministic migration timestamp 20260606200000 per Phase 1 convention
    - Auth snapshot updated with Plan-01 Player/AdminAuditLog FK relationships
    - Cross-package ApplyMigrations: Core -> Auth -> Rankings -> Matchmaking with PendingModelChangesWarning suppression
key_files:
  created:
    - src/GameKit.Auth/Entities/AccountMerge.cs
    - src/GameKit.Auth/Services/MergeResult.cs
    - src/GameKit.Auth/Services/MergeConflictException.cs
    - src/GameKit.Auth/Data/Configurations/AccountMergeConfiguration.cs
    - src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.cs
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/GameKit.Auth.AccountMerge.Integration.Tests.csproj
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/CollectionDefinitions.cs
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/TestHelpers.cs
  modified:
    - src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs
    - src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs
    - src/GameKit.Auth/AssemblyInfo.cs
    - src/GameKit.Rankings/AssemblyInfo.cs
    - src/GameKit.Matchmaking/AssemblyInfo.cs
    - GameKit.sln
decisions:
  - "MergeConflictReason excludes SessionConflict per plan check — all session_participants re-point unconditionally in Plan 03; no need for a conflict case at the precondition layer"
  - "No FK on AccountMerge.SourcePlayerId — source player is soft-deleted (tombstoned), but GDPR hard-delete must be able to remove the source row later without being blocked by a constraint"
  - "Auth snapshot updated to include Player.DeletedAt + Player.MergedIntoPlayerId + AdminAuditLog.ActorId FK (from Plan-01 Core changes) so EF reports no pending Auth model changes"
  - "ApplyMigrations applies Core+Auth+Rankings+Matchmaking (not Admin) because account_merges FK references players (Core) not admin_users (Admin) and the test suite does not need admin_users to function"
metrics:
  duration: "~12 minutes"
  completed: "2026-06-06"
  tasks: 3
  files: 14
requirements_satisfied: [AUTH-24]
---

# Phase 10 Plan 02: Auth Schema + Migration + Test Scaffold for Account Merge Summary

Auth-side data foundation: `AccountMerge` entity + `account_merges` state-machine table with integer-backed `MergeStatus` and UNIQUE(SourcePlayerId) double-merge guard, EF configuration registered in `AuthMigrationModelCustomizer`, deterministic Auth migration `20260606200000` under advisory lock -298890956, and the Wave-0 cross-package integration test scaffold with `ApplyMigrations` covering Core + Auth + Rankings + Matchmaking.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | AccountMerge entity, MergeStatus enum, MergeResult, MergeConflictException | 05d4c8b | AccountMerge.cs, MergeResult.cs, MergeConflictException.cs |
| 2 | AccountMergeConfiguration + register in AuthMigrationModelCustomizer + Auth migration 20260606200000 | eaa5d8a | AccountMergeConfiguration.cs, AuthDesignTimeDbContextFactory.cs, 20260606200000_AddAccountMerges.cs, GameKitDbContextModelSnapshot.cs |
| 3 | InternalsVisibleTo grants + new AccountMerge integration test project + cross-package ApplyMigrations scaffold | 3dc5359 | AssemblyInfo.cs (Auth, Rankings, Matchmaking), GameKit.Auth.AccountMerge.Integration.Tests/ (3 files), GameKit.sln |

## What Was Built

### Task 1: Entity + Result Types

**`AccountMerge.cs`** — 9-property sealed class + `MergeStatus` enum:
- `Id`, `SourcePlayerId`, `TargetPlayerId`, `Status`, `ActorId`, `RequestedAt`, `CommittedAt`, `RedisCleanedAt`, `Metadata`
- `MergeStatus { Pending = 0, Committed = 1, RedisCleaned = 2 }` — integer-backed, no `HasConversion`

**`MergeResult.cs`** — `MergeResultKind { Merged = 0, AlreadyMerged = 1 }` + sealed class with `Merged(Guid)` / `AlreadyMerged(Guid)` static factories, exposing `Kind` and `TargetPlayerId` (never source — SC#5).

**`MergeConflictException.cs`** — `MergeConflictReason { PlayersInSameParty, TargetBanned, SelfMerge, SourceAlreadyMerged }` + sealed exception class with `Reason` property.

### Task 2: EF Configuration + Migration

**`AccountMergeConfiguration.cs`** — maps `account_merges` with:
- `UNIQUE(SourcePlayerId)` — SC#1 double-merge prevention (T-10-02-01)
- `INDEX(TargetPlayerId)` — lookup by surviving player
- `FK_account_merges_players_TargetPlayerId ON DELETE RESTRICT` — target cannot be GDPR-deleted while merge record exists (T-10-02-02)
- `Status` as `integer` (no `HasConversion`)
- `Metadata` as `jsonb`
- **No FK on SourcePlayerId** — bare UUID column, GDPR hard-delete of source row must not be blocked

**`AuthMigrationModelCustomizer.Customize`** now calls `ApplyConfiguration(new AccountMergeConfiguration())`.

**`20260606200000_AddAccountMerges.cs`** — deterministic timestamp migration creates `gamekit.account_merges` with PK, FK, UNIQUE index, plain index. `Down()` drops the table.

**Auth model snapshot** updated with:
- Full `AccountMerge` entity definition
- `Player` entity updated with `DeletedAt`, `MergedIntoPlayerId` (from Plan 01 Core changes)
- `AdminAuditLog` entity FK `HasOne<Player>().HasForeignKey("ActorId").OnDelete(SetNull)` (from Plan 01)
- All Core entities marked `ExcludeFromMigrations()` per per-package boundary rule

### Task 3: InternalsVisibleTo + Test Scaffold

**Three AssemblyInfo.cs files** grant `InternalsVisibleTo("GameKit.Auth.AccountMerge.Integration.Tests")`.

**`GameKit.Auth.AccountMerge.Integration.Tests.csproj`** — zero new package pins; all refs via CPM. Added to `GameKit.sln` via `dotnet sln add`.

**`CollectionDefinitions.cs`** — `AccountMerge` collection (Postgres + Redis) + `Postgres` collection.

**`TestHelpers.ApplyMigrations`** applies four migration trains in dependency order:
1. Core — under `GameKitMigrationConstants.AdvisoryLockKey` (1800940027)
2. Auth — `authCtx.Database.MigrateAsync()` via `AuthMigrationModelCustomizer`
3. Rankings — `MigrationRunner.MigrateWithLockAsync` + `RankingsMigrationConstants.AdvisoryLockKey` (-156812172)
4. Matchmaking — `MigrationRunner.MigrateWithLockAsync` + `MatchmakingMigrationConstants.AdvisoryLockKey` (388956820)

`PendingModelChangesWarning` suppressed at each step per the established precedent from `GameKit.Auth.Integration.Tests/TestHelpers.cs`.

## Verification Results

```
dotnet build GameKit.sln -warnaserror --nologo
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

CS1591-as-error gate passed on all new public APIs. Auth snapshot in sync with the `AuthMigrationModelCustomizer` model.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CS1574 cref to unresolved IAccountMergeService.MergeAsync**
- **Found during:** Task 1 build
- **Issue:** `MergeResult.cs` used `<see cref="IAccountMergeService.MergeAsync"/>` — the interface does not exist until Plan 03
- **Fix:** Changed to `<c>IAccountMergeService.MergeAsync</c>` (plain text cref)
- **Files modified:** `src/GameKit.Auth/Services/MergeResult.cs`
- **Commit:** 05d4c8b

**2. [Rule 2 - Missing functionality] Auth snapshot outdated re: Plan-01 Player/AdminAuditLog FKs**
- **Found during:** Task 2 snapshot construction
- **Issue:** The pre-existing Auth snapshot had the old `Player` entity shape (missing `DeletedAt`, `MergedIntoPlayerId`, `HasIndex("MergedIntoPlayerId")`) and missing `AdminAuditLog` ActorId FK — both added by Plan 01 to Core. Without updating the Auth snapshot, `dotnet ef` would report pending Auth model changes even after the new Auth migration is applied.
- **Fix:** Updated Auth snapshot to include Plan-01 Player columns + FKs and the new AccountMerge entity; all Core entities marked `ExcludeFromMigrations()`.
- **Files modified:** `src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs`
- **Commit:** eaa5d8a

## Known Stubs

None — this plan is pure schema/migration/test-scaffold work with no UI rendering or data-source wiring.

## Threat Flags

No new threat surface beyond the plan's documented threat model. UNIQUE(SourcePlayerId) mitigates T-10-02-01 and FK ON DELETE RESTRICT mitigates T-10-02-02 as designed.

## Self-Check: PASSED

Files exist:
- src/GameKit.Auth/Entities/AccountMerge.cs: FOUND
- src/GameKit.Auth/Services/MergeResult.cs: FOUND
- src/GameKit.Auth/Services/MergeConflictException.cs: FOUND
- src/GameKit.Auth/Data/Configurations/AccountMergeConfiguration.cs: FOUND
- src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.cs: FOUND
- tests/GameKit.Auth.AccountMerge.Integration.Tests/TestHelpers.cs: FOUND

Commits exist:
- 05d4c8b: FOUND (Task 1)
- eaa5d8a: FOUND (Task 2)
- 3dc5359: FOUND (Task 3)
