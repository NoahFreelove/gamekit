---
phase: 17-backup-dr-migration-ops
plan: "01"
subsystem: migrations
tags: [dr, migrations, down-policy, ordering-markers, ef-core]
status: complete

dependency_graph:
  requires: []
  provides:
    - "DR-04 Down() policy enforced across all 19 migration Down() methods (14 existing + 5 new markers)"
    - "DR-05/DR-07 timestamp ordering foundation: Core(20260622)<Auth(20260623)<Admin(20260624)<Rankings(20260625)<Matchmaking(20260626)<Lobby(20260627)"
  affects:
    - "Plan 17-02: GK0003 Roslyn analyzer can now compile green since all Down() bodies throw NotSupportedException"
    - "Plan 17-02: MigrationTimestampTests passes since per-package latest timestamps now ascend correctly"

tech_stack:
  added: []
  patterns:
    - "DR-04: All migration Down() methods throw NotSupportedException pointing to docs/runbooks/postgres-backup-restore.md"
    - "Ordering-marker migrations: empty Up(), throwing Down(), [Migration(\"ts_ClassName\")] attribute in main .cs, no Designer.cs needed"

key_files:
  modified:
    - src/GameKit.Core/Migrations/20260415000000_CoreInitial.cs
    - src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs
    - src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.cs
    - src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs
    - src/GameKit.Core/Migrations/20260622000000_AddGameSessionIdempotencyKey.cs
    - src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs
    - src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.cs
    - src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.cs
    - src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.cs
    - src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs
    - src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs
    - src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs
    - src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs
    - src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.cs
  created:
    - src/GameKit.Auth/Migrations/20260623000000_DrOrderingMarker.cs
    - src/GameKit.Admin.UI/Migrations/20260624000000_DrOrderingMarker.cs
    - src/GameKit.Rankings/Migrations/20260625000000_DrOrderingMarker.cs
    - src/GameKit.Matchmaking/Migrations/20260626000000_DrOrderingMarker.cs
    - src/GameKit.Lobby/Data/Migrations/20260627000000_DrOrderingMarker.cs

decisions:
  - "Added [Migration(\"ts_DrOrderingMarker\")] attribute directly in each marker's main .cs file — no Designer.cs or snapshot change needed for zero-DDL migrations (mirrors AddAuditActorIdFk pattern)"
  - "Added 'using System;' to 6 migration files that lacked it (AddSessionParticipationFraction, AddAuditActorIdFk, AddGameSessionIdempotencyKey, AuthPasswordHashLength, RankingsDecayPlacement, MatchmakingBackfillRegions) rather than fully qualifying System.NotSupportedException"

metrics:
  duration: "~5 minutes"
  completed: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_modified: 14
  files_created: 5
---

# Phase 17 Plan 01: Down() Conversion + Ordering Markers Summary

**One-liner:** Replace all 14 destructive migration `Down()` bodies with `NotSupportedException` throws and add 5 no-op ordering-marker migrations to fix per-package timestamp ascent.

## What Was Built

### Task 1: DR-04 Down() Conversion (14 files)

Every existing migration `Down()` body across all six packages now contains exactly one statement: `throw new NotSupportedException(...)` with a message directing operators to `docs/runbooks/postgres-backup-restore.md`.

Files converted (13 had destructive ops, 1 had an empty no-op body — all now throw per DR-04 policy):

| Package | File | Previous Down() |
|---------|------|-----------------|
| Core | CoreInitial | DropTable ×4 |
| Core | AddSessionParticipationFraction | DropColumn |
| Core | AddMergedIntoPlayerId | DropForeignKey + DropColumn ×2 |
| Core | AddAuditActorIdFk | Empty no-op comment |
| Core | AddGameSessionIdempotencyKey | Raw SQL DROP INDEX + DropColumn |
| Auth | AuthInitial | DropTable ×3 |
| Auth | AuthPasswordHashLength | AlterColumn (varchar shrink) |
| Auth | AddAccountMerges | DropTable |
| Admin.UI | AdminInitial | DropTable |
| Rankings | RankingsInitial | Raw SQL DROP CONSTRAINT + DropTable ×7 |
| Rankings | RankingsDecayPlacement | Raw SQL DROP INDEX + DropColumn ×3 |
| Matchmaking | MatchmakingInitial | DropTable ×5 |
| Matchmaking | MatchmakingBackfillRegions | DropColumn |
| Lobby | LobbyInitial | DropTable ×2 |

`using System;` was added to 6 files that lacked it.

### Task 2: DR-05/DR-07 Ordering Marker Migrations (5 new files)

Five zero-DDL migrations hand-authored to anchor the per-package latest-timestamp chain in the canonical application order:

| Package | File | Timestamp |
|---------|------|-----------|
| Auth | 20260623000000_DrOrderingMarker.cs | 20260623 |
| Admin.UI | 20260624000000_DrOrderingMarker.cs | 20260624 |
| Rankings | 20260625000000_DrOrderingMarker.cs | 20260625 |
| Matchmaking | 20260626000000_DrOrderingMarker.cs | 20260626 |
| Lobby | 20260627000000_DrOrderingMarker.cs | 20260627 |

Each marker: empty `Up()` (zero DDL), DR-04-compliant throwing `Down()`, `[Migration("ts_DrOrderingMarker")]` attribute in the main `.cs` file. No Designer.cs or model snapshot changes were added — this mirrors the `AddAuditActorIdFk` pattern for zero-model-delta migrations.

Post-marker timestamp order: Core(20260622) < Auth(20260623) < Admin(20260624) < Rankings(20260625) < Matchmaking(20260626) < Lobby(20260627). Strictly ascending. `MigrationTimestampTests` (Plan 17-02) will pass.

### Task 3: Build Verification

`dotnet build GameKit.sln --configuration Release -warnaserror -p:NuGetAudit=false` — **0 errors, 0 warnings**. All 6 affected packages compile cleanly with the converted Down() bodies and 5 hand-written marker migrations.

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None — this plan makes no UI or data-flow changes.

## Threat Flags

None — this plan only changes the Down() path (never-run in forward-migration scenarios) and adds zero-DDL markers.

## Self-Check: PASSED

- All 14 converted migration files verified with `grep -q "throw new NotSupportedException"` — FOUND in all 14.
- All 5 marker files verified to exist with correct class and throwing Down() — FOUND.
- Build commit 3f1c4a6 verified in git log — FOUND.
- Task 1 commit 77bc6fb verified in git log — FOUND.
- No destructive ops (DropTable/DropColumn/DropForeignKey/raw DROP) remain in any Down() body.
