---
phase: 10-account-merge
plan: "01"
subsystem: GameKit.Core
tags: [migrations, schema, entity-framework, foreign-keys, account-merge, gdpr]
dependency_graph:
  requires: []
  provides:
    - merged_into_player_id column on players (uuid, nullable, self-FK ON DELETE SET NULL)
    - deleted_at column on players (timestamptz, nullable, merge tombstone)
    - FK_admin_audit_log_players_ActorId ON DELETE SET NULL
  affects:
    - GameKit.Core.Entities.Player
    - GameKit.Core.Data.Configurations.PlayerConfiguration
    - GameKit.Core.Data.Configurations.AdminAuditLogConfiguration
    - GameKit.Core.Migrations (two new migrations + snapshot)
tech_stack:
  added: []
  patterns:
    - Per-package migration boundary (Core owns players + admin_audit_log columns)
    - Deterministic migration timestamps (Phase 1 convention)
    - Self-referential FK ON DELETE SET NULL for tombstone safety
key_files:
  created:
    - src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.cs
    - src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs
  modified:
    - src/GameKit.Core/Entities/Player.cs
    - src/GameKit.Core/Data/Configurations/PlayerConfiguration.cs
    - src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs
    - src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs
decisions:
  - "Core owns both migrations (1800940027L advisory lock) because Core is the sole owner of players and admin_audit_log tables per per-package migration boundary rule"
  - "Self-referential FK on merged_into_player_id uses ON DELETE SET NULL so a future GDPR hard-delete of the target player nulls the tombstone reference without RESTRICTing the deletion (T-10-01-02)"
  - "admin_audit_log.actor_id FK uses ON DELETE SET NULL so tombstoning the source player never orphans audit history (SC#4, T-10-01-03)"
metrics:
  duration: "~8 minutes"
  completed: "2026-06-06"
  tasks: 3
  files: 6
requirements_satisfied: [AUTH-23, AUTH-26]
---

# Phase 10 Plan 01: Core Schema Migrations for Account Merge Summary

Two deterministic-timestamp Core migrations that land the `merged_into_player_id` + `deleted_at` tombstone columns on `players` (SC#2) and the `admin_audit_log.actor_id` FK ON DELETE SET NULL (SC#4), owned exclusively by Core under advisory lock 1800940027.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Add MergedIntoPlayerId + DeletedAt to Player entity + EF config | e133a79 | Player.cs, PlayerConfiguration.cs |
| 2 | Add admin_audit_log.actor_id FK to EF config (ON DELETE SET NULL) | 93beccb | AdminAuditLogConfiguration.cs |
| 3 | Write two Core migrations + update snapshot | cbaa184 | 20260606000000_AddMergedIntoPlayerId.cs, 20260606100000_AddAuditActorIdFk.cs, GameKitDbContextModelSnapshot.cs |

## What Was Built

### Player Entity Changes (Task 1)

`Player.cs` receives two nullable properties after `BanReason`:

- `Guid? MergedIntoPlayerId` — when non-null, this player is a merge tombstone pointing at the surviving target player
- `DateTimeOffset? DeletedAt` — UTC timestamp of the merge soft-delete; null for active players

The class-level `<remarks>` were updated to clarify that `DeletedAt` is exclusively for merge tombstones and that GDPR erasure remains a hard-delete (design decision D-13 is unchanged).

`PlayerConfiguration.cs` maps both properties and adds a self-referential FK:

```csharp
b.HasOne<Player>()
    .WithMany()
    .HasForeignKey(p => p.MergedIntoPlayerId)
    .OnDelete(DeleteBehavior.SetNull);
```

### AdminAuditLog FK (Task 2)

`AdminAuditLogConfiguration.cs` adds the previously-missing referential constraint:

```csharp
b.HasOne<Player>()
    .WithMany()
    .HasForeignKey(a => a.ActorId)
    .OnDelete(DeleteBehavior.SetNull);
```

The existing `b.HasIndex(a => a.ActorId)` is retained; EF reuses it as the FK index. No other package's MigrationModelCustomizer was changed — AdminMigrationModelCustomizer already excludes `AdminAuditLog`, so this Core-owned FK is invisible to Admin/Rankings/Matchmaking migration diffs.

### Core Migrations (Task 3)

**`20260606000000_AddMergedIntoPlayerId`** (timestamp ordered after `20260519000000_AddSessionParticipationFraction`):
- `AddColumn<Guid>("MergedIntoPlayerId", "players", "gamekit", nullable: true)`
- `AddColumn<DateTimeOffset>("DeletedAt", "players", "gamekit", nullable: true)`
- `AddForeignKey("FK_players_players_MergedIntoPlayerId", onDelete: SetNull)`
- Down: drops FK then both columns

**`20260606100000_AddAuditActorIdFk`**:
- `AddForeignKey("FK_admin_audit_log_players_ActorId", "admin_audit_log", "ActorId" → "players.Id", onDelete: SetNull)`
- Down: drops the FK

**Snapshot** (`GameKitDbContextModelSnapshot.cs`) updated to include:
- `DeletedAt` and `MergedIntoPlayerId` properties on `Player`
- `HasIndex("MergedIntoPlayerId")` on `Player`
- `HasOne<Player>().HasForeignKey("MergedIntoPlayerId").OnDelete(SetNull)` relationship for `Player`
- `HasOne<Player>().HasForeignKey("ActorId").OnDelete(SetNull)` relationship for `AdminAuditLog`

## Verification Results

```
dotnet build GameKit.sln -warnaserror --nologo
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

All three tasks compiled cleanly. CS1591-as-error gate passed (XML docs on all new public members). No pending model changes (snapshot in sync with model).

## Deviations from Plan

None — plan executed exactly as written.

## Known Stubs

None — this plan is pure schema/migration work with no UI rendering or data-source wiring.

## Threat Flags

No new threat surface beyond the plan's documented threat model. Both FKs use ON DELETE SET NULL which mitigates T-10-01-02 and T-10-01-03 as designed.

## Self-Check: PASSED

Files exist:
- src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.cs: FOUND
- src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs: FOUND

Commits exist:
- e133a79: FOUND
- 93beccb: FOUND
- cbaa184: FOUND
