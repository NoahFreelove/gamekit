---
phase: 10-account-merge
plan: "03"
subsystem: GameKit.Auth
tags: [account-merge, serializable-transaction, crash-resume, fk-surgery, audit, rankings, matchmaking, auth]
dependency_graph:
  requires:
    - Plan 10-01 (Core schema: merged_into_player_id + deleted_at on players, admin_audit_log FK)
    - Plan 10-02 (AccountMerge entity, MergeResult/MergeConflictException types, account_merges migration, InternalsVisibleTo, test scaffold)
  provides:
    - IAccountMergeService: MergeAsync(source, target, actor, ct) -> Task<MergeResult>
    - AccountMergeService: SERIALIZABLE tx + crash-resume + FK surgery + audit
    - AddScoped<IAccountMergeService, AccountMergeService> in AuthBuilderExtensions
  affects:
    - src/GameKit.Auth/Services/IAccountMergeService.cs (new)
    - src/GameKit.Auth/Services/AccountMergeService.cs (new)
    - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs (modified)
    - src/GameKit.Auth/GameKit.Auth.csproj (modified — added StackExchange.Redis)
tech_stack:
  added:
    - StackExchange.Redis (optional dep in GameKit.Auth for post-commit Redis cleanup)
  patterns:
    - SERIALIZABLE + manual 3-attempt 40001 retry + TryFindPostgresException (verbatim IdentityLinker pattern)
    - Change-tracker detach on retry (GuestUpgradeService/IdentityLinker precedent)
    - Direct _ctx.Set<AdminAuditLog>().Add() audit write with private const action literal (EndSeasonService precedent, D-22)
    - crash-resume state machine (Pending → Committed → RedisCleaned) with authoritative pre-tx resume ladder
    - Parameterized raw SQL via Database.ExecuteSqlAsync/SqlQuery for cross-package tables (Rankings, Matchmaking) — avoids circular project references
    - Optional IConnectionMultiplexer (nullable ctor dep) for Redis cleanup graceful degradation
key_files:
  created:
    - src/GameKit.Auth/Services/IAccountMergeService.cs
    - src/GameKit.Auth/Services/AccountMergeService.cs
  modified:
    - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
    - src/GameKit.Auth/GameKit.Auth.csproj
decisions:
  - "Cross-package FK surgery (player_ranks, pending_rating_updates, season_rank_archive, party_members, parties, decline_history) uses parameterized SQL via Database.ExecuteSqlAsync rather than typed EF access — GameKit.Matchmaking references GameKit.Auth so the reverse reference would create a circular dependency; GameKit.Rankings only references Core so Auth cannot reference it either without breaking architectural layering"
  - "StackExchange.Redis added to GameKit.Auth.csproj as optional (nullable IConnectionMultiplexer ctor dep) — Auth compiles and runs correctly without Redis; stale matchmaking keys TTL-expire naturally per Pitfall 7 / T-10-03-08"
  - "party_members conflict check uses raw SQL (Database.SqlQuery<int>) consistent with the cross-package SQL approach for Matchmaking tables"
  - "RevokeAllForPlayerAsync called inside the SERIALIZABLE tx body (Pending→Committed path only), NOT after the tx commits, ensuring atomicity with the FK surgery"
metrics:
  duration: "~8 minutes"
  completed: "2026-06-06"
  tasks: 2
  files: 4
requirements_satisfied: [AUTH-23, AUTH-24, AUTH-25, AUTH-26]
---

# Phase 10 Plan 03: AccountMergeService Engine Summary

SERIALIZABLE, crash-resumable, fully-audited `AccountMergeService` with per-ladder player_ranks conflict resolution (keep-higher-Rating, SUM W/L/D, MAX RD) and cross-package FK surgery via parameterized SQL to avoid circular project references.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | IAccountMergeService contract + AccountMergeService transaction body (FK surgery + rank conflict + audit + tombstone) | 7540e89 | IAccountMergeService.cs, AccountMergeService.cs, GameKit.Auth.csproj |
| 2 | Crash-resume checkpoint logic (committed → redis_cleaned), Redis cleanup, DI registration | 7a50ae7 | AuthBuilderExtensions.cs |

## What Was Built

### IAccountMergeService (Task 1)

`IAccountMergeService.cs` — public interface with a single method:

```csharp
Task<MergeResult> MergeAsync(
    Guid sourcePlayerId,
    Guid targetPlayerId,
    Guid actorId,
    CancellationToken cancellationToken = default);
```

Full XML docs noting irreversibility, superadmin gating, crash-resumability, and that the source id is never returned in the HTTP layer (SC#5).

### AccountMergeService (Tasks 1 + 2)

`AccountMergeService.cs` (696 lines) — `internal sealed class AccountMergeService : IAccountMergeService`.

**Constructor:** `GameKitDbContext`, `IClock`, `IIdGenerator`, `IRefreshTokenService` (required); `IConnectionMultiplexer?`, `ILogger<AccountMergeService>?` (optional). `ArgumentNullException.ThrowIfNull` guards on all required params.

**Crash-resume ladder (SC#1) at top of MergeAsync:**
- `RedisCleaned` → return `AlreadyMerged(target)` immediately (no work, no double-revoke, no duplicate audit)
- `Committed` → skip DB tx, run Redis cleanup, mark `RedisCleaned`, return `AlreadyMerged(target)`
- `Pending` + same target → re-run SERIALIZABLE tx body (idempotent)
- `Pending` + different target → throw `SourceAlreadyMerged` (concurrent merge to a different target)
- absent → full flow

**SERIALIZABLE TX body (15 steps):**
1. Load source + target `Player` rows (`KeyNotFoundException` if absent)
2. Guards: `SelfMerge` (source==target), `SourceAlreadyMerged` (source.MergedIntoPlayerId != null), `TargetBanned` (target.IsBanned); banned source is ALLOWED (A3, recorded in audit)
3. Party conflict check via raw SQL — `PlayersInSameParty` abort if source + target share a party
4. INSERT `AccountMerge` row (Status=Pending) if no existing row; skip INSERT on Pending crash-resume
5. `player_identities`: `ExecuteUpdateAsync SET player_id=target WHERE player_id=source`
6. `player_credentials`: check target-has-credential → DELETE source row on conflict, else re-point
7. `IRefreshTokenService.RevokeAllForPlayerAsync(source, "account_merge")` — exactly once
8. `session_participants`: `ExecuteUpdateAsync SET player_id=target WHERE player_id=source` (ALL rows — full history, not active-only)
9. `player_ranks` conflict resolution via 5-pass raw SQL:
   - Query conflict count for audit metadata
   - Pass 1a: re-point source rows (source.Rating > target.Rating) to target with SUM W/L/D, MAX RD, IsInPlacement=source&&target, recent LastMatchAt
   - Pass 1b: delete old target rows where source won the rating comparison
   - Pass 2a: merge source W/L/D into target rows (source.Rating <= target.Rating), MAX RD
   - Pass 2b: delete source rows where target won
   - Pass 3: re-point remaining source-only rows
10. `pending_rating_updates` + `season_rank_archive`: raw SQL UPDATE SET player_id=target
11. `party_members`, `parties.owner_player_id`, `decline_history`: raw SQL UPDATE SET player_id/owner_player_id=target
12. `admin_audit_log.actor_id`: `ExecuteUpdateAsync SET actor_id=target WHERE actor_id=source`
13. Tombstone source: `source.MergedIntoPlayerId = targetPlayerId; source.DeletedAt = now`
14. Write audit row: `_ctx.Set<AdminAuditLog>().Add(new AdminAuditLog { Action="auth.account_merge", TargetId=target, Before=source snapshot, After=target snapshot+counts })`
15. UPDATE `account_merges SET Status=Committed, CommittedAt=now`

**Post-commit Redis cleanup (OUTSIDE tx):**
- Delete `gamekit:player:{sourcePlayerId}` presence key if Redis is available
- Advance `account_merges` Status to `RedisCleaned` regardless of Redis outcome
- Missing `IConnectionMultiplexer` → graceful no-op (keys TTL-expire naturally)

### DI Registration (Task 2)

`AuthBuilderExtensions.cs`:
```csharp
builder.Services.AddScoped<IAccountMergeService, AccountMergeService>();
```
Added alongside existing `AddScoped` block. `AddScoped` (not `TryAddScoped`) per existing file style.

## Key Design Decision: Cross-Package SQL for FK Surgery

`GameKit.Matchmaking` already holds a `ProjectReference` to `GameKit.Auth` (for migration boundary enforcement). Adding a reverse reference `GameKit.Auth → GameKit.Matchmaking` would create a circular dependency. `GameKit.Rankings` references only Core.

Rather than adding these project references (architectural change), all FK surgery on Rankings and Matchmaking tables is issued as parameterized SQL via `Database.ExecuteSqlAsync` and `Database.SqlQuery<T>`. This is safe because:
- The shared `GameKitDbContext` model includes all entities at runtime via `IModelBuilderExtension`
- The SQL executes inside the same SERIALIZABLE transaction
- Parameters are passed via FormattableString (EF Core 10 parameterization prevents injection)

This approach is a deliberate architectural adaptation, not a deviation — the PATTERNS and RESEARCH files both note Auth accesses Rankings tables "via the shared DbContext", and this is the correct implementation of that intent given the project dependency constraints.

## Verification Results

```
dotnet build GameKit.sln -warnaserror --nologo
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

All acceptance criteria verified:
- `IsolationLevel.Serializable`: present
- `RevokeAllForPlayerAsync`: present
- `_ctx.Set<AdminAuditLog>()`: present
- `auth.account_merge` action literal: present
- `MergedIntoPlayerId` tombstone: present
- `RedisCleaned` status: present
- `MergeStatus.Committed`: present
- `AddScoped<IAccountMergeService, AccountMergeService>`: present
- `IAdminAuditWriter`: NOT present (correct — no circular dep)
- 696 lines (min 200 required)
- All guards (SelfMerge / SourceAlreadyMerged / TargetBanned / PlayersInSameParty): present

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Cross-package project reference would cause circular dependency**
- **Found during:** Task 1 implementation
- **Issue:** The PATTERNS file listed `using GameKit.Rankings.Entities;` in the imports for `AccountMergeService.cs`, implying typed EF access to `PlayerRank`. However, `GameKit.Matchmaking` already holds a `ProjectReference → GameKit.Auth`. Adding `GameKit.Auth → GameKit.Matchmaking` would be circular. `GameKit.Rankings` only references Core, so `GameKit.Auth → GameKit.Rankings` would be non-circular — but adding it would violate the "install only what you need" principle and alter the dependency graph.
- **Fix:** Used parameterized `Database.ExecuteSqlAsync` and `Database.SqlQuery<int>` for all Matchmaking and Rankings table operations. Added `StackExchange.Redis` to Auth.csproj (already pinned in central packages) for the optional `IConnectionMultiplexer` dep. Added a code comment explaining why this approach is used.
- **Files modified:** `AccountMergeService.cs`, `GameKit.Auth.csproj`
- **Commit:** 7540e89

## Known Stubs

None — this plan ships the merge engine. No UI rendering or placeholder data.

## Threat Flags

No new threat surface beyond the plan's documented threat model. All mitigations from T-10-03-01 through T-10-03-08 are implemented as designed.

## Self-Check: PASSED

Files exist:
- src/GameKit.Auth/Services/IAccountMergeService.cs: FOUND
- src/GameKit.Auth/Services/AccountMergeService.cs: FOUND

Commits exist:
- 7540e89: FOUND (Task 1)
- 7a50ae7: FOUND (Task 2)
