# Phase 10: Account Merge (Isolated High-Risk) — Research

**Researched:** 2026-06-06
**Domain:** Cross-package SERIALIZABLE transaction, crash-and-resume idempotency, FK surgery, audit
**Confidence:** HIGH — all findings grounded in actual codebase reads; no external sources needed

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
None explicit — discuss phase was skipped. All implementation choices are at Claude's discretion.

### Claude's Discretion
All implementation choices are at Claude's discretion — discuss phase was skipped per user setting.
Use ROADMAP phase goal, success criteria, and codebase conventions to guide decisions. This is the
milestone's highest-risk phase (irreversible data operation): favor crash-safety, idempotency, and
auditability over brevity. Resolve the two ARCHITECTURE.md-noted open questions during planning:
(a) `party_members` unique-constraint conflict path when source + target are in the same party
(explicit abort-merge or remove-source-member policy); (b) `admin_audit_log.actor_id` FK behavior
on source-player tombstone (ON DELETE SET NULL, per ARCHITECTURE.md Q3).

### Deferred Ideas (OUT OF SCOPE)
None — discuss phase skipped.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| AUTH-23 | Account merge combines two distinct player_ids via SERIALIZABLE tx + advisory lock, re-homing FK references across player_identities, player_credentials, refresh_tokens, player_ranks, matchmaking_tickets, party_members, session_participants, admin_audit_log | FK inventory section; SERIALIZABLE + advisory lock design |
| AUTH-24 | account_merges idempotency/history table (statuses pending / committed / redis_cleaned) enabling crash-and-resume; merge is idempotent under retry | State machine section; idempotency key design |
| AUTH-25 | Merge conflict policy — player_ranks: keep higher-rated row per ladder (sum W/L/D, max RD); revoke ALL secondary-account refresh tokens; tombstone secondary player_id with merged_into_player_id; explicit banned-player merge policy | Conflict resolution section; per-table action table |
| AUTH-26 | Merge recorded in admin_audit_log (actor, before/after JSON); audit FK behavior ON DELETE SET NULL so tombstoning never orphans audit history | SC#4 audit FK section; migration ownership |
</phase_requirements>

---

## Summary

Phase 10 implements an irreversible, superadmin-gated merge of two distinct `player_id`s. The
operation re-points all FK references from the source player to the target player, conflict-resolves
per-ladder `player_ranks` rows, revokes source refresh tokens, and tombstones the source `players`
row with a `merged_into_player_id` column. A crash-resumable state machine table (`account_merges`)
makes the entire operation idempotent under retry.

The design resolves two previously-open architecture questions. For `party_members` conflict
(source + target in the same party): the research recommendation is **abort-merge** rather than
remove-source-member, because silently removing a party member is a user-visible side effect with
no clean undo, and the caller (admin) can be asked to remove the player from the party first. For
`admin_audit_log.actor_id`: the column currently has **no FK** to `players` — only a bare index.
A new Core migration is required to add `FK_admin_audit_log_players_ActorId ON DELETE SET NULL`
(per CONTEXT.md decision, per ARCHITECTURE.md Q3, per SC#4). This is owned by Core under the
per-package migration boundary rule (Core owns `admin_audit_log`).

The merge service itself lives in **GameKit.Auth** because it owns the primary FK surfaces
(`player_identities`, `player_credentials`, `refresh_tokens`) and the `account_merges` table.
The Admin endpoint calls `IAccountMergeService` via DI — no circular dep (Admin already
references Auth). The audit row is written directly via `_ctx.Set<AdminAuditLog>()`, not through
`IAdminAuditWriter`, following the `EndSeasonService` precedent to avoid a circular dependency.

**Primary recommendation:** Build `AccountMergeService` in `GameKit.Auth` using the `SerializationFailureRetry.Build` Polly pipeline (already in Rankings — Auth needs its own copy or a shared helper), `IRefreshTokenService.RevokeAllForPlayerAsync` for token cleanup, and an `account_merges` state machine table with columns that track `pending → committed → redis_cleaned`.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| `IAccountMergeService` interface + `AccountMergeService` | `GameKit.Auth` | — | Owns player_identities, player_credentials, refresh_tokens |
| `account_merges` migration | `GameKit.Auth` (existing advisory lock key) | — | New migration in existing Auth package — reuses -298890956L |
| `merged_into_player_id` column on `players` | `GameKit.Core` (Core migration) | — | Core owns the `players` table; per-package boundary rule |
| `admin_audit_log.actor_id` FK ON DELETE SET NULL | `GameKit.Core` (new Core migration) | — | Core owns `admin_audit_log`; currently has no FK on actor_id |
| Admin HTTP endpoint `POST /admin/api/players/merge` | `GameKit.Admin.UI` | `GameKit.Auth` via DI | Superadmin policy gate; Admin already references Auth |
| `player_ranks` conflict resolution | `GameKit.Auth` (reads via shared DbContext) | — | Auth accesses all tables through single shared DbContext |
| Redis cleanup (stale matchmaking keys) | `AccountMergeService` final step | — | `redis_cleaned` state tracks this last checkpoint |

---

## Standard Stack

No new packages. This phase is entirely within the existing dependency graph.

| Artifact | Package | Notes |
|----------|---------|-------|
| SERIALIZABLE tx + retry | `GameKit.Auth` | Use `TryFindPostgresException` + manual loop (mirrors `IdentityLinker`/`GuestUpgradeService`) **OR** extract `SerializationFailureRetry` equivalently in Auth |
| `IRefreshTokenService.RevokeAllForPlayerAsync` | `GameKit.Auth` | Already exists — revokes all token families for a player ID |
| `_ctx.Set<AdminAuditLog>()` direct write | `GameKit.Auth` | EndSeasonService precedent; avoids circular dep |
| `MigrationRunner.MigrateWithLockAsync` | `GameKit.Core` (public API) | Auth migration hosted service already uses this |
| `AdminPolicies.Superadmin` | `GameKit.Admin.UI` | Constant = `"gamekit.admin.superadmin"` |
| `AntiforgeryValidationFilter` | `GameKit.Admin.UI` | DRY-clone into Admin endpoint filters (or reuse existing) |

**Installation:** No new NuGet packages required.

---

## Package Legitimacy Audit

> No new external packages are introduced in this phase.

| Package | Registry | Age | Downloads | Source Repo | slopcheck | Disposition |
|---------|----------|-----|-----------|-------------|-----------|-------------|
| — | — | — | — | — | — | No new deps |

---

## Architecture Patterns

### System Architecture Diagram

```
Admin HTTP POST /admin/api/players/merge
  │  [Requires: gamekit.admin.superadmin + AntiforgeryValidationFilter]
  │
  ▼
AdminMergeEndpoint (GameKit.Admin.UI)
  │  Calls IAccountMergeService.MergeAsync(sourceId, targetId, actorId, ct)
  │
  ▼
AccountMergeService (GameKit.Auth)
  │
  ├── 1. Advisory lock: pg_advisory_lock(source XOR target) or order-locked SELECT FOR UPDATE
  │       [SERIALIZABLE tx open]
  │
  ├── 2. Idempotency check: SELECT FROM account_merges WHERE source=src AND target=tgt
  │       └─ if status=committed|redis_cleaned → return AlreadyMerged (idempotent)
  │       └─ if status=pending → resume from checkpoint
  │
  ├── 3. Guard checks: source != target; source not already merged (merged_into_player_id NOT NULL)
  │       banned-player policy check
  │
  ├── 4. INSERT account_merges (id, source_player_id, target_player_id, status=pending, ...)
  │
  ├── 5. FK re-pointing (inside SERIALIZABLE tx):
  │       UPDATE player_identities SET player_id=target WHERE player_id=source
  │       UPDATE player_credentials SET player_id=target WHERE player_id=source
  │       [UNIQUE(Username) collision → source has no credential (1-per-player PK) OR conflict=same-player → no-op]
  │
  ├── 6. Refresh token revocation:
  │       IRefreshTokenService.RevokeAllForPlayerAsync(sourceId, "account_merge")
  │       [uses ExecuteUpdate on refresh_tokens WHERE player_id=source AND revoked_at IS NULL]
  │
  ├── 7. player_ranks conflict resolution (per ladder):
  │       For each ladder with rows for BOTH source AND target:
  │         if source.Rating > target.Rating → UPDATE player_ranks SET player_id=target WHERE player_id=source AND ladder_id=L; DELETE target row
  │         else → DELETE source row (keep target)
  │         In either case: UPDATE surviving row SET wins += other.wins, losses += other.losses, draws += other.draws
  │       For ladders with only source row: UPDATE player_ranks SET player_id=target WHERE player_id=source
  │
  ├── 8. session_participants (ON DELETE SET NULL by FK — no re-pointing needed for past sessions)
  │       GDPR FK is already SET NULL. However for ACTIVE sessions we UPDATE SET player_id=target
  │       [UNIQUE(session_id, player_id) collision → both players in same session → abort-merge]
  │
  ├── 9. party_members:
  │       Check for same-party conflict: SELECT count FROM party_members WHERE party_id IN
  │         (SELECT party_id FROM party_members WHERE player_id=source)
  │         AND player_id=target
  │       If conflict found → ABORT-MERGE (return PartyConflict error; admin must remove player from party first)
  │       Else: UPDATE party_members SET player_id=target WHERE player_id=source
  │       Also: UPDATE parties SET owner_player_id=target WHERE owner_player_id=source
  │
  ├── 10. decline_history: player_id FK is ON DELETE CASCADE → re-point or leave (analytics only)
  │        UPDATE decline_history SET player_id=target WHERE player_id=source
  │
  ├── 11. pending_rating_updates: player_id is NULLABLE (SET NULL on GDPR delete)
  │        UPDATE pending_rating_updates SET player_id=target WHERE player_id=source
  │
  ├── 12. season_rank_archive: player_id is NULLABLE (SET NULL on GDPR delete)
  │        UPDATE season_rank_archive SET player_id=target WHERE player_id=source AND player_id IS NOT NULL
  │
  ├── 13. admin_audit_log: actor_id has NEW FK ON DELETE SET NULL (after Core migration)
  │        UPDATE admin_audit_log SET actor_id=target WHERE actor_id=source
  │        [Pre-migration: actor_id is bare column — safe to UPDATE regardless]
  │
  ├── 14. players: soft-delete source with tombstone
  │        UPDATE players SET merged_into_player_id=target, deleted_at=now WHERE id=source
  │        [merged_into_player_id column added by new Core migration 20260606000000_AddMergedIntoPlayerId]
  │
  ├── 15. INSERT admin_audit_log (action="auth.account_merge", target_id=target_player_id, before={source snapshot}, after={target snapshot})
  │
  ├── 16. UPDATE account_merges SET status=committed WHERE id=merge_id
  │
  ├── 17. COMMIT transaction
  │
  └── 18. Redis cleanup (OUTSIDE tx — checkpoint redis_cleaned):
            DEL/ZREM any stale Redis keys keyed on source player_id
            (matchmaking queue entries — mm:queue:{ladder}:{pool} sorted sets — are TTL-expiring)
            UPDATE account_merges SET status=redis_cleaned WHERE id=merge_id
            [If process dies here, resume marks it redis_cleaned on re-entry via idempotency check]
```

### Recommended Project Structure

No new projects. Changes span existing packages:

```
src/GameKit.Core/
  ├── Entities/Player.cs                          ← add MergedIntoPlayerId + DeletedAt properties
  ├── Data/Configurations/PlayerConfiguration.cs  ← map new columns
  ├── Migrations/20260606000000_AddMergedIntoPlayerId.cs  ← new Core migration
  └── Migrations/20260606100000_AddAuditActorIdFk.cs     ← new Core migration

src/GameKit.Auth/
  ├── Services/IAccountMergeService.cs            ← NEW public interface
  ├── Services/AccountMergeService.cs             ← NEW implementation
  ├── Services/MergeResult.cs                     ← NEW result type/enum
  ├── Entities/AccountMerge.cs                    ← NEW entity
  ├── Data/Configurations/AccountMergeConfiguration.cs  ← NEW
  ├── Data/AuthMigrationModelCustomizer.cs        ← add AccountMerge to model
  ├── Migrations/20260606200000_AddAccountMerges.cs     ← new Auth migration
  └── Builder/AuthBuilderExtensions.cs            ← register IAccountMergeService

src/GameKit.Admin.UI/
  ├── Http/AdminEndpoints.cs                      ← add POST /players/merge endpoint
  ├── Http/Contracts/MergePlayersRequest.cs       ← NEW DTO
  └── Http/Validators/MergePlayersRequestValidator.cs  ← NEW validator
```

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Postgres 40001 retry | Custom retry loop inside MergeService | `SerializationFailureRetry.Build()` (Rankings) — or replicate the loop pattern from `IdentityLinker`/`GuestUpgradeService` | Exponential backoff + jitter, logging, Polly v8 |
| Refresh token revocation | Manual DELETE WHERE player_id=source | `IRefreshTokenService.RevokeAllForPlayerAsync(sourceId, "account_merge")` | Already implemented; sets RevokedAt + writes audit |
| Audit row serialization | Manual JsonSerializer.Serialize | `_ctx.Set<AdminAuditLog>().Add(...)` with `JsonDocument.Parse(JsonSerializer.Serialize(snapshot))` | Matches GdprDeleteService + EndSeasonService pattern |
| Advisory lock | Application-level mutex | `SELECT FOR UPDATE` on both player rows ordered by ID | Deadlock-safe per DB-level ordering |

---

## FK Completeness: Every Table with a FK to `players.id`

This is the complete list derived from source code inspection. Every row must be accounted for in the merge plan.

| Table | Package | Column | Delete Behavior (current) | Merge Action |
|-------|---------|--------|--------------------------|--------------|
| `player_identities` | Auth | `player_id` | CASCADE | UPDATE SET player_id=target (no UNIQUE conflict — UNIQUE is on (provider, external_id), not player_id) |
| `player_credentials` | Auth | `player_id` (PK) | CASCADE | UPDATE SET player_id=target; source has at most 1 row (PK=PlayerId); if target also has a credential, UNIQUE(Username) conflict means both are already distinct users — abort or keep both by checking first |
| `refresh_tokens` | Auth | `player_id` | CASCADE | REVOKE via `IRefreshTokenService.RevokeAllForPlayerAsync(sourceId)` — do not re-point, the tokens are for the source identity which is being retired |
| `session_participants` | Core | `player_id` | SET NULL | UPDATE WHERE player_id=source AND session active; for completed sessions, SET NULL behavior is fine (GDPR precedent). UNIQUE(session_id, player_id) collision on active session → abort-merge |
| `player_ranks` | Rankings | `player_id` | CASCADE | Conflict resolution (see below); UNIQUE(player_id, ladder_id) requires manual merge strategy |
| `pending_rating_updates` | Rankings | `player_id` (nullable) | SET NULL | UPDATE SET player_id=target WHERE player_id=source AND player_id IS NOT NULL |
| `season_rank_archive` | Rankings | `player_id` (nullable) | SET NULL | UPDATE SET player_id=target WHERE player_id=source AND player_id IS NOT NULL |
| `party_members` | Matchmaking | `player_id` | RESTRICT | Check for same-party conflict first → if found, ABORT. Else UPDATE SET player_id=target. UNIQUE(party_id, player_id) |
| `parties` | Matchmaking | `owner_player_id` | CASCADE | UPDATE SET owner_player_id=target WHERE owner_player_id=source (owner transfer, not removal) |
| `decline_history` | Matchmaking | `player_id` | CASCADE | UPDATE SET player_id=target (analytics; no unique constraint issue) |
| `admin_audit_log` | Core | `actor_id` (nullable) | **NONE currently — bare index** | UPDATE SET actor_id=target WHERE actor_id=source; after Core migration adds FK with ON DELETE SET NULL, this is handled at DB level on hard-delete |
| `matchmaking_tickets` | Matchmaking | NO direct player_id FK | — | No direct player FK; tickets reference Party (which gets owner re-pointed); stale Redis tickets TTL-expire naturally |

**VERIFIED: `admin_audit_log.actor_id` has NO FK to `players`.**
Source: `src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs` line 22:
`b.Property(a => a.ActorId);` — no `HasOne<Player>()`, no `HasForeignKey`. Only `b.HasIndex(a => a.ActorId)`.
Source confirmed by `CoreInitial` migration: `admin_audit_log` table has no FK constraint for `ActorId`.

**Required: Two new Core migrations:**
1. `20260606000000_AddMergedIntoPlayerId` — adds `merged_into_player_id uuid REFERENCES gamekit.players(id) ON DELETE SET NULL` and a nullable `deleted_at timestamptz` to `players`.
2. `20260606100000_AddAuditActorIdFk` — adds `FK_admin_audit_log_players_ActorId ON DELETE SET NULL` to `admin_audit_log`.

Both are Core migrations (Core owns both tables). The `merged_into_player_id` FK points to `players.id` and uses `ON DELETE SET NULL` so that if the _target_ player is later GDPR-deleted, the tombstone reference becomes NULL (not a hard constraint violation). [VERIFIED: codebase read]

---

## The `account_merges` State Machine (SC#1)

### Entity Design

```csharp
// GameKit.Auth/Entities/AccountMerge.cs
public sealed class AccountMerge
{
    public Guid Id { get; set; }                      // UUIDv7 from IIdGenerator
    public Guid SourcePlayerId { get; set; }          // player that was absorbed
    public Guid TargetPlayerId { get; set; }          // player that survives
    public MergeStatus Status { get; set; }           // pending=0, committed=1, redis_cleaned=2
    public Guid? ActorId { get; set; }                // admin who triggered; null for system
    public DateTimeOffset RequestedAt { get; set; }  // when first requested
    public DateTimeOffset? CommittedAt { get; set; } // when tx committed
    public DateTimeOffset? RedisCleanedAt { get; set; }
    public JsonDocument? Metadata { get; set; }      // spare JSONB (e.g. dry-run flag)
}

public enum MergeStatus { Pending = 0, Committed = 1, RedisCleaned = 2 }
```

### Table Schema (Auth migration)

```sql
-- Auth migration 20260606200000_AddAccountMerges
CREATE TABLE gamekit.account_merges (
    "Id"               uuid         PRIMARY KEY,
    "SourcePlayerId"   uuid         NOT NULL,
    "TargetPlayerId"   uuid         NOT NULL REFERENCES gamekit.players("Id") ON DELETE RESTRICT,
    "Status"           integer      NOT NULL DEFAULT 0,
    "ActorId"          uuid,
    "RequestedAt"      timestamptz  NOT NULL,
    "CommittedAt"      timestamptz,
    "RedisCleanedAt"   timestamptz,
    "Metadata"         jsonb
);
CREATE INDEX idx_account_merges_source ON gamekit.account_merges ("SourcePlayerId");
CREATE INDEX idx_account_merges_target ON gamekit.account_merges ("TargetPlayerId");
-- Prevent merging the same source twice (source can only be absorbed into one target)
CREATE UNIQUE INDEX idx_account_merges_source_unique ON gamekit.account_merges ("SourcePlayerId");
```

**Note on `TargetPlayerId` FK:** Uses `ON DELETE RESTRICT` so you cannot delete the target player while a merge record points at them. [ASSUMED]

**Note on `SourcePlayerId`:** NOT a FK — the source player will be soft-deleted (marked `merged_into_player_id`), so a FK would prevent the tombstone or require SET NULL, which would orphan the record. Storing the UUID without FK is intentional; the unique index prevents double-merge. [ASSUMED]

### Idempotency Key

The idempotency key is `(source_player_id, target_player_id)`. The UNIQUE index on `SourcePlayerId` enforces that a source can only be merged once (irreversible). The logic:

1. Check for existing `account_merges` row WHERE `SourcePlayerId = source`:
   - If found with any status (pending/committed/redis_cleaned) → **return `AlreadyMerged`** (idempotent). Even `pending` means a previous attempt started — resume if the caller re-requests with the same (source, target) pair.
   - If found with a different `TargetPlayerId` → reject (source was merged to a different target, or the UNIQUE index prevents this case entirely).
2. If `pending` and same target: re-run the full merge transaction (safe because the first SERIALIZABLE tx either committed or rolled back; if it committed, all the UPDATEs are already done and the re-run will find no rows to update for most steps).

### Crash-Resume

A process killed mid-merge can be in one of three states:

| State at crash | What happened | Resume action |
|----------------|--------------|---------------|
| Before `account_merges` INSERT | Nothing persisted | Full retry from scratch |
| After INSERT (status=pending) but before COMMIT | All updates rolled back with the tx | Re-run the SERIALIZABLE tx; the `account_merges` row with status=pending is the signal to resume |
| After COMMIT (status=committed) but before Redis cleanup | DB is fully consistent | Skip the tx; run only Redis cleanup; mark redis_cleaned |

The resume path: on re-request with (source, target), the service reads the existing `account_merges` row, checks status:
- `pending` → re-run the SERIALIZABLE transaction body (all the UPDATEs are idempotent — updating a row to the same value is a no-op)
- `committed` → skip to Redis cleanup only
- `redis_cleaned` → return `AlreadyMerged` [ASSUMED]

---

## `player_ranks` Conflict Resolution (SC#3)

**Per-ladder algorithm when both source and target have a rank row on ladder L:**

```
sourceRank = SELECT * FROM player_ranks WHERE player_id = source AND ladder_id = L
targetRank = SELECT * FROM player_ranks WHERE player_id = target AND ladder_id = L

if sourceRank.Rating > targetRank.Rating:
    -- Source has better rating; copy source stats into a "winner row" for target
    UPDATE player_ranks
      SET player_id = target,                      -- re-point to target
          wins  = sourceRank.Wins + targetRank.Wins,
          losses = sourceRank.Losses + targetRank.Losses,
          draws  = sourceRank.Draws + targetRank.Draws,
          -- Keep source rating (it's higher) — no change needed since we moved the source row
          -- Also preserve: Rating, RatingDeviation, Volatility, IsInPlacement, PlacementMatchesRemaining
    WHERE player_id = source AND ladder_id = L;
    DELETE FROM player_ranks WHERE player_id = target AND ladder_id = L
      AND id != (the row we just updated);
    -- Net result: source's high-rating row is now owned by target; target's old row deleted
else:
    -- Target has equal or better rating; add source stats to target row and delete source
    UPDATE player_ranks
      SET wins   = wins + sourceRank.Wins,
          losses = losses + sourceRank.Losses,
          draws  = draws + sourceRank.Draws,
          -- Rating/RD/Volatility unchanged (target's values are kept)
    WHERE player_id = target AND ladder_id = L;
    DELETE FROM player_ranks WHERE player_id = source AND ladder_id = L;
```

**For ladders with only a source row (no target row):** Simple re-point.
```
UPDATE player_ranks SET player_id = target WHERE player_id = source AND ladder_id = L
```

**Rating Deviation (RD) / Volatility:** SC#3 specifies "max RD" — take the higher RD when merging two rows where source is kept. When source row is moved to target, source's RD is preserved naturally. When source is merged into target's row, take `MAX(sourceRD, targetRD)` for the surviving row's RD. [ASSUMED — "max RD" is a conservative choice; reduces confidence in the merged account's rating]

**`IsInPlacement` / `PlacementMatchesRemaining`:** If either row is NOT in placement (IsInPlacement=false), the surviving row should also not be in placement (a merged account that completed placement on at least one account should not restart placement). Logic: `IsInPlacement = source.IsInPlacement AND target.IsInPlacement`. [ASSUMED]

**`LastMatchAt`:** Take the more recent of the two. [ASSUMED]

**Implementation note:** Auth reads `player_ranks` via the shared `GameKitDbContext`. No cross-package method call needed — the shared DbContext model includes Rankings entities (accessible to Auth through EF's single-model design).

---

## Package Ownership Decision (Design Question 1)

**Recommendation: `GameKit.Auth` owns `IAccountMergeService` + `AccountMergeService` + `account_merges` migration.**

Rationale (verified against codebase):
- Auth already owns `player_identities`, `player_credentials`, `refresh_tokens` — the primary FK surfaces re-pointed by merge.
- Auth already has the SERIALIZABLE + 40001 retry pattern in `IdentityLinker` and `GuestUpgradeService`.
- Auth already has `IRefreshTokenService.RevokeAllForPlayerAsync` — the exact method needed for step 6.
- Admin.UI already has a `ProjectReference` to Auth (declared in `GameKit.Admin.UI.csproj`). The admin endpoint calls `IAccountMergeService` injected from Auth — zero new cross-package dependency.
- The audit row is written via `_ctx.Set<AdminAuditLog>()` (not `IAdminAuditWriter`) following the `EndSeasonService` precedent from STATE.md line 211.
- The `account_merges` migration uses the existing Auth advisory lock key (`-298890956L`) — no new key needed.

**Migration timestamps (Auth):**
- Existing: `20260418000000_AuthInitial`, `20260418100000_AuthPasswordHashLength`
- New: `20260606200000_AddAccountMerges` — follows the deterministic convention; one day after the Phase 10 date, with a sub-day offset that doesn't conflict with Auth's existing 100-increment pattern.

**Migration timestamps (Core):**
- Existing: `20260415000000_CoreInitial`, `20260519000000_AddSessionParticipationFraction`
- New: `20260606000000_AddMergedIntoPlayerId` — adds `merged_into_player_id` + `deleted_at` columns to `players`
- New: `20260606100000_AddAuditActorIdFk` — adds `FK_admin_audit_log_players_ActorId` ON DELETE SET NULL

Both Core migrations use the Core advisory lock key (`1800940027L`).

---

## `party_members` Conflict Resolution (Design Question Open — Resolved Here)

**Decision: ABORT-MERGE when source and target are both members of the same active party.**

Rationale:
- Silently removing the source player from the party is a visible side effect with no undo.
- Party membership is a current-state concern — it means the source player is actively in a game context.
- The admin initiating the merge can be asked to remove the source player from the party first.
- This mirrors the LastSuperadmin exception pattern: fail explicitly with a clear error rather than silently mutating a shared resource.

**Implementation:** Before re-pointing `party_members`, query for same-party conflicts:
```sql
SELECT pm_source.party_id
FROM party_members pm_source
JOIN party_members pm_target ON pm_source.party_id = pm_target.party_id
WHERE pm_source.player_id = @source AND pm_target.player_id = @target
```
If any rows returned, throw `MergeConflictException(MergeConflictReason.PlayersInSameParty)` and roll back.

**`parties.owner_player_id`:** ON DELETE CASCADE currently. During merge, simply `UPDATE parties SET owner_player_id=target WHERE owner_player_id=source`. No unique constraint issue. [VERIFIED: `PartyConfiguration.cs` confirms `owner_player_id` is NOT UNIQUE]

---

## Admin Endpoint Design (SC#5)

**Endpoint:** `POST /admin/api/players/merge`

**Filter chain:**
```csharp
group.MapPost("/players/merge", MergePlayersAsync)
    .RequireAuthorization(AdminPolicies.Superadmin)   // "gamekit.admin.superadmin"
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .AddEndpointFilter<ValidationEndpointFilter<MergePlayersRequest>>();
```

**Request DTO:**
```csharp
public sealed record MergePlayersRequest(
    Guid SourcePlayerId,   // player to absorb
    Guid TargetPlayerId);  // player that survives
```

**Response shape:** Returns the **target player ID only** — never includes `SourcePlayerId` in the response body (T-mitigation: do not leak the merged-away ID in the HTTP response layer).

```csharp
public sealed record MergePlayersResponse(
    Guid TargetPlayerId,
    string Status);   // "merged" | "already_merged"
```

**Rate limiting:** Add a dedicated rate-limit policy `gamekit:admin:merge` (e.g., 5/min/IP) in `AdminRateLimitRegistrations` — merges are destructive and should not be automated in bulk. [ASSUMED — rate-limit value is discretionary]

---

## `merged_into_player_id` Tombstone (SC#2)

**Per CONTEXT.md and the Phase 9 lesson (Core owns columns on Core tables):**

The `merged_into_player_id` column is added to `players` by a Core migration. `GameKit.Core` owns the `players` table.

**Player entity changes:**
```csharp
// Addition to src/GameKit.Core/Entities/Player.cs
/// <summary>When non-null, this player has been merged into the referenced target player.</summary>
public Guid? MergedIntoPlayerId { get; set; }

/// <summary>UTC timestamp of soft-delete (merger or any future tombstone). Null for active players.</summary>
public DateTimeOffset? DeletedAt { get; set; }
```

**EF configuration addition (PlayerConfiguration.cs):**
```csharp
b.Property(p => p.MergedIntoPlayerId);
b.Property(p => p.DeletedAt);

b.HasOne<Player>()
    .WithMany()
    .HasForeignKey(p => p.MergedIntoPlayerId)
    .OnDelete(DeleteBehavior.SetNull);  // if target is GDPR-deleted, tombstone becomes null
```

**Auth migration model customizer (AuthMigrationModelCustomizer):** Must exclude the two new Player columns from Auth's migration diff (they are Core-owned — no change needed there; Auth's customizer already uses `ExcludeFromMigrations` for the whole Player entity, so new columns on Player are invisible to Auth's migrations automatically).

---

## `admin_audit_log.actor_id` FK (SC#4) — Critical Gap

**Current state (VERIFIED):** `admin_audit_log.actor_id` is a nullable `uuid` column with an index but NO foreign key constraint to `players`. Source: `AdminAuditLogConfiguration.cs` line 22: `b.Property(a => a.ActorId);` with no `HasOne<Player>()`. The `CoreInitial` migration confirms: `admin_audit_log` table has no FK constraints.

**Required action:** A new Core migration adds the FK:
```sql
-- Migration 20260606100000_AddAuditActorIdFk
ALTER TABLE gamekit.admin_audit_log
  ADD CONSTRAINT "FK_admin_audit_log_players_ActorId"
  FOREIGN KEY ("ActorId") REFERENCES gamekit.players("Id")
  ON DELETE SET NULL;
```

**EF configuration update (AdminAuditLogConfiguration.cs):**
```csharp
b.HasOne<Player>()
    .WithMany()
    .HasForeignKey(a => a.ActorId)
    .OnDelete(DeleteBehavior.SetNull);
```

**Impact on merge service:** After this FK is in place, `UPDATE admin_audit_log SET actor_id=target WHERE actor_id=source` still runs explicitly inside the merge transaction (re-pointing historical audit rows). The ON DELETE SET NULL behavior is for future tombstoning of the _target_ player, not for the merge itself.

**Impact on all Migration Model Customizers:** Auth, Admin, Rankings, and Matchmaking migration model customizers include `AdminAuditLog` in their exclusion lists (via `ExcludeFromMigrations`). A new `HasOne<Player>()` navigation in the Core model snapshot will be part of the Admin snapshot diff — but since all packages exclude `AdminAuditLog` via `ExcludeFromMigrations`, only Core will emit this migration. [VERIFIED: AdminMigrationModelCustomizer.cs line 47 excludes `typeof(AdminAuditLog)`]

---

## Before/After Audit JSON (SC#4)

The merge audit record uses action `"auth.account_merge"` and captures both the source and target player state. Since `IAuthAuditWriter` lacks a `before` parameter, the merge service writes the audit row **directly** (following the GdprDeleteService + EndSeasonService precedent):

```csharp
_ctx.Set<AdminAuditLog>().Add(new AdminAuditLog
{
    Id = _ids.NewId(),
    ActorId = actorId,   // admin's player_id (or admin_user.id? → use admin's player_id for consistency)
    Action = "auth.account_merge",
    TargetType = "player",
    TargetId = targetPlayerId,    // surviving player — never the source
    Before = JsonDocument.Parse(JsonSerializer.Serialize(new {
        source_player_id = sourcePlayerId,
        source_display_name = sourcePlayer.DisplayName,
        source_created_at = sourcePlayer.CreatedAt,
        source_identity_count = identityCount,   // how many identities were re-pointed
    })),
    After = JsonDocument.Parse(JsonSerializer.Serialize(new {
        target_player_id = targetPlayerId,
        target_display_name = targetPlayer.DisplayName,
        identities_merged = identityCount,
        tokens_revoked = revokedCount,
        ranks_merged = ranksMergedCount,
    })),
    Reason = null,
    CreatedAt = _clock.UtcNow,
});
```

**Response security (SC#5):** The HTTP response from the admin endpoint must NOT include `SourcePlayerId`. The audit record's `TargetId` is the surviving player. Any query that reads the audit log should treat `Before.source_player_id` as restricted to superadmin visibility.

---

## Banned Player Merge Policy

AUTH-25 requires an "explicit banned-player merge policy." The recommended policy: **allow merge even if source is banned, but not if target is banned.** Rationale:
- If the source is banned, merging it into the target preserves the ban state on the target (i.e., if IsBanned was on source, that player's session was already blocked; the target player may be legitimate).
- If the target is banned, the merge would absorb a legitimate player's identities into a banned account, which is clearly wrong.
- Special case: if the source is banned, also carry over the ban to the target (or leave the ban decision to the admin as a post-merge action).

**Simplest safe policy:** ABORT if target `IsBanned = true`. Log a warning if source `IsBanned = true` (admin is deliberately merging a banned account — proceed but note it in the audit metadata). [ASSUMED — this is a discretionary choice; the planner should note it as a decision point]

---

## Common Pitfalls

### Pitfall 1: UNIQUE(Username) on `player_credentials`

**What goes wrong:** The source player has a `player_credentials` row with `Username = "alice"`. The target player also has `Username = "bob"`. Both have PK = `player_id`. When merging, updating `player_credentials SET player_id = target WHERE player_id = source` violates the PK constraint because target's PK row already exists.

**Why it happens:** `player_credentials` has `PlayerId` as its PK (one credential per player). Source and target cannot both have rows after merge — one must be deleted.

**How to avoid:** Before updating, check if target already has a credential row. If it does, the source player's credential (`Username = "alice"`) and target player's credential (`Username = "bob"`) both exist — the target's credential takes precedence. DELETE the source credential row rather than re-pointing. If the source has a credential and the target does not, do the re-point normally.

**Warning signs:** `23505` PostgresException on `player_credentials` primary key during merge.

---

### Pitfall 2: `player_ranks` UNIQUE(player_id, ladder_id) prevents naive re-point

**What goes wrong:** Both source and target have a rank row on ladder L. `UPDATE player_ranks SET player_id=target WHERE player_id=source` hits the UNIQUE constraint.

**How to avoid:** Per the conflict-resolution algorithm above, use a SELECT-then-decide approach: if source has a higher rating, move the source row to target (after deleting the existing target row); otherwise, aggregate stats into the target row and delete the source row.

**Warning signs:** `23505` on `player_ranks` UNIQUE `IX_player_ranks_PlayerId_LadderId`.

---

### Pitfall 3: `party_members` UNIQUE(party_id, player_id)

**What goes wrong:** Same as above — both source and target are members of the same party. `UPDATE party_members SET player_id=target WHERE player_id=source` hits UNIQUE(party_id, player_id).

**How to avoid:** Check for same-party membership before re-pointing (see party_members section). Abort-merge if conflict detected.

**Warning signs:** `23505` on `party_members` unique index.

---

### Pitfall 4: Source FK deletion blocked by `party_members` RESTRICT

**What goes wrong:** When deleting the source `players` row (to finalize the tombstone), Postgres throws a FK violation because `party_members.player_id` has `ON DELETE RESTRICT`. The source player cannot be deleted while they are still in a party.

**How to avoid:** This phase does NOT hard-delete the source player — it soft-deletes via `merged_into_player_id` + `deleted_at` columns. The `players` row remains. No FK violation occurs. The GDPR delete service handles hard deletion as a separate operation.

**Warning signs:** If someone tries to hard-delete a merged player without clearing party membership first.

---

### Pitfall 5: DetachEntities after SERIALIZABLE retry

**What goes wrong:** On a `40001` retry, the EF change tracker still has the entities from the failed transaction in a partially-modified state. Retrying without clearing the change tracker causes duplicate entity issues.

**How to avoid:** Mirror `GuestUpgradeService` exactly — on catch of 40001, iterate `_ctx.ChangeTracker.Entries()` and set all states to `EntityState.Detached` before continuing to the next retry attempt. [VERIFIED: GuestUpgradeService.cs lines 114-117]

---

### Pitfall 6: `session_participants` re-point

**What goes wrong:** Both source and target participated in the same historical session. `UPDATE session_participants SET player_id=target WHERE player_id=source` would put target_player_id twice in the same session, violating UNIQUE(session_id, player_id).

**Note on actual constraint:** `SessionParticipantConfiguration.cs` does NOT define a UNIQUE index on `(session_id, player_id)` — the EF config only shows `HasIndex(p => p.SessionId)` and `HasIndex(p => p.PlayerId)`. A duplicate is technically allowed by the schema. However, duplicate participant rows in the same session would corrupt match-history display. The safe choice is to check for conflicts and abort-merge (or skip re-pointing completed session rows and let SET NULL be handled naturally on the eventual GDPR delete).

**Recommended approach:** For completed sessions, do NOT re-point session_participants. The source player's historical session data remains valid with their original player_id. Only re-point active sessions (where State = 'Active'). This preserves historical integrity while handling the live-session case.

---

### Pitfall 7: Redis cleanup is NOT transactional

**What goes wrong:** Redis cleanup cannot be inside the Postgres SERIALIZABLE transaction. If the process dies between DB commit and Redis cleanup, the Redis state is stale until cleanup runs on resume.

**How to avoid:** The `redis_cleaned` status in `account_merges` is the checkpoint. On resume (re-request with the same source/target), detect `status=committed` and jump directly to Redis cleanup. Redis keys for stale matchmaking entries (sorted sets) will expire naturally via TTL if cleanup doesn't run immediately.

**Warning signs:** Stale Redis sorted-set entries for `mm:queue:{ladderId}:{poolName}` containing the source player's ticket score. These expire naturally but could cause a phantom match proposal. In practice, the source player's refresh tokens are revoked (step 6) so they cannot accept any proposal.

---

## Code Examples

### SERIALIZABLE transaction pattern (canonical — IdentityLinker)

```csharp
// Source: src/GameKit.Auth/Services/IdentityLinker.cs
for (var attempt = 0; attempt < MaxRetries; attempt++)
{
    await using var tx = await _ctx.Database
        .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
        .ConfigureAwait(false);
    try
    {
        // ... transaction body ...
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
    catch (Exception ex) when (TryFindPostgresException(ex) is { } pg)
    {
        await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in _ctx.ChangeTracker.Entries())
            entry.State = EntityState.Detached;

        if (pg.SqlState == "23505") { /* handle unique violation */ }
        if (pg.SqlState == "40001" && attempt < MaxRetries - 1) continue;
        throw;
    }
}
```

### RevokeAllForPlayerAsync signature (existing, use as-is)

```csharp
// Source: src/GameKit.Auth/Services/IRefreshTokenService.cs
Task RevokeAllForPlayerAsync(Guid playerId, string reason, CancellationToken cancellationToken = default);
// reason string for account_merge: "account_merge"
```

### Direct AdminAuditLog write (EndSeasonService precedent)

```csharp
// Source: src/GameKit.Rankings/Services/EndSeasonService.cs
_ctx.Set<AdminAuditLog>().Add(new AdminAuditLog
{
    Id = _ids.NewId(),
    ActorId = actorId,
    Action = LadderEndSeasonAction,   // private const string duplicate
    TargetType = "player",
    TargetId = targetPlayerId,
    Before = JsonDocument.Parse(JsonSerializer.Serialize(beforeSnapshot)),
    After  = JsonDocument.Parse(JsonSerializer.Serialize(afterSnapshot)),
    Reason = null,
    CreatedAt = _clock.UtcNow,
});
await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
```

### SerializationFailureRetry.Build pattern (Rankings — replicate in Auth)

```csharp
// Source: src/GameKit.Rankings/Services/SerializationFailureRetry.cs
_serializationRetry = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<DbUpdateException>(ex => ex.InnerException is PostgresException { SqlState: "40001" })
            .Handle<PostgresException>(ex => ex.SqlState == "40001"),
    })
    .Build();
```

### AdminPolicies.Superadmin endpoint filter chain (existing pattern)

```csharp
// Source: src/GameKit.Admin.UI/Http/AdminEndpoints.cs
group.MapPost("/players/{id:guid}/gdpr-delete", GdprDeletePlayerAsync)
    .RequireAuthorization(AdminPolicies.Superadmin)
    .AddEndpointFilter<AntiforgeryValidationFilter>();
// Merge follows same pattern with a validator on top.
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Per-request SERIALIZABLE retry (manual loop) | `SerializationFailureRetry.Build()` Polly pipeline | Phase 8 (Rankings) | Auth should replicate the Polly pattern for AccountMergeService rather than the manual loop |
| `IAdminAuditWriter` (Admin.UI dep) for cross-package audit | `_ctx.Set<AdminAuditLog>()` direct write | Phase 4 (EndSeasonService) | `AccountMergeService` must NOT reference `IAdminAuditWriter` |
| Core column ownership via any package migration | Only the owning package adds columns to its tables | Phase 9 precedent (ParticipationFraction) | `merged_into_player_id` MUST be a Core migration |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `account_merges.SourcePlayerId` UNIQUE index prevents double-merge (not enforced by FK) | State machine design | If no unique constraint, a source could be merged into two targets; add UNIQUE at SQL level |
| A2 | `TargetPlayerId` FK uses ON DELETE RESTRICT | account_merges schema | If SET NULL, merge records become orphaned when target is GDPR-deleted |
| A3 | Banned source policy: allow but note in metadata; abort if target is banned | Banned player policy | Admin may want stricter behavior; flag as planner decision point |
| A4 | Rate-limit merge endpoint at 5/min/IP | Admin endpoint | Specific rate value is discretionary |
| A5 | `IsInPlacement` for merged rank row: true only if both source and target are in placement | player_ranks conflict resolution | Conservative interpretation; could set false if either has completed placement |
| A6 | `RatingDeviation` for merged rank row: take MAX(source.RD, target.RD) | player_ranks conflict resolution | Alternative: use the winning row's RD unchanged |
| A7 | `LastMatchAt`: take more recent of two | player_ranks conflict resolution | Could take the winning row's value |
| A8 | Do NOT re-point completed `session_participants` rows; only active session rows | session_participants action | If re-pointing all rows is desired, add UNIQUE check to avoid duplicates |
| A9 | `MergeConflictException(PartyConflict)` is the right abort signal for party conflict | party_members conflict | Naming is discretionary |
| A10 | `redis_cleaned` is the final status; no further state transitions | State machine | If more cleanup is needed later (e.g., Lobby), additional states may be required |
| A11 | Audit action literal `"auth.account_merge"` duplicated in `AccountMergeService` as a private const, with a sync-comment pointing to future `AdminAuditActions.AccountMerge` constant | Audit row | Naming is discretionary |

---

## Open Questions (RESOLVED)

1. **Should `AccountMergeService` use the manual retry loop or `SerializationFailureRetry.Build`?**
   - Manual loop: mirrors existing Auth services (`IdentityLinker`, `GuestUpgradeService`), no new Polly dependency in Auth
   - Polly pipeline: already in Rankings; cleaner retry logging
   - Recommendation: Use the manual loop pattern (consistent with Auth's existing style). Both are correct; this is a style decision.

2. **Should `player_credentials` source credential be deleted or rejected?**
   - If source has `Username="alice"` and target already has `Username="bob"`, delete source credential (target player keeps their own username).
   - If source has a credential and target has none, re-point (target gets the source username — this is an unusual but valid case when source was the "primary" account with a password login).
   - Recommendation: Check first; if target already has a credential, DELETE source credential (target's username takes precedence). This is a planner decision worth flagging.

3. **Should `decline_history` be re-pointed or left as-is?**
   - Re-pointing it means the target player inherits the source's match-decline cooldown history — arguably correct (they're the same person). Not re-pointing means the source's cooldown records become orphaned after the source player is soft-deleted.
   - Recommendation: Re-point (UPDATE WHERE player_id=source) since `decline_history` ON DELETE CASCADE is designed for hard-delete, and we're soft-deleting.

---

## Environment Availability

> Step 2.6: SKIPPED (no new external dependencies; all tools are within existing test infrastructure)

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 + Testcontainers 4.11.0 (Postgres 17.9) |
| Config file | `tests/xunit.runner.json` |
| Quick run command | `dotnet test tests/GameKit.Auth.Integration.Tests -x --filter "Category=Integration"` |
| Full suite command | `dotnet test tests/ --no-build` |

### Test Project

The merge integration tests live in a new test project `tests/GameKit.Auth.AccountMerge.Integration.Tests/`. This project needs:
- `ProjectReference` to `GameKit.Auth` (for `IAccountMergeService`)
- `ProjectReference` to `GameKit.Rankings` (to apply Rankings model for `player_ranks` queries)
- `ProjectReference` to `GameKit.Matchmaking` (for `party_members` queries)
- `ProjectReference` to `GameKit.Admin.UI` (for the HTTP endpoint tests)
- `ProjectReference` to `GameKit.TestFixtures`

Alternatively, tests can live in the existing `GameKit.Auth.Integration.Tests` project with an added `ProjectReference` to Rankings and Matchmaking — but the `TestHelpers.ApplyMigrations` would need to be extended to apply Rankings and Matchmaking migrations too. Creating a new test project is cleaner.

**InternalsVisibleTo grants needed:**
- `GameKit.Auth/AssemblyInfo.cs` — add `[assembly: InternalsVisibleTo("GameKit.Auth.AccountMerge.Integration.Tests")]`
- `GameKit.Rankings/AssemblyInfo.cs` — add the same grant (to apply `RankingsModelBuilderExtension`)
- `GameKit.Matchmaking/AssemblyInfo.cs` — add the same grant (to apply `MatchmakingModelBuilderExtension`)

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | Notes |
|--------|----------|-----------|-------------------|-------|
| AUTH-23 | FK re-pointing across all tables | integration | `dotnet test ... -x --filter "DisplayName~SC#1"` | Testcontainers; verify each table's player_id after merge |
| AUTH-23 | session_participants UNIQUE conflict → abort | integration | `--filter "DisplayName~SessionConflict"` | Seed two participants in same active session |
| AUTH-24 | Crash-and-resume: pending → re-run | integration | `--filter "DisplayName~SC#2_CrashResume"` | Insert pending row; call merge again; verify idempotent |
| AUTH-24 | Crash-and-resume: committed → Redis cleanup only | integration | `--filter "DisplayName~SC#2_Committed"` | Insert committed row; call merge; verify no DB re-run |
| AUTH-24 | AlreadyMerged on re-request (redis_cleaned) | integration | `--filter "DisplayName~SC#2_AlreadyMerged"` | Full merge; retry; assert AlreadyMerged |
| AUTH-25 | player_ranks keep higher-rated source | integration | `--filter "DisplayName~SC#3_HigherSource"` | Seed source with higher rating; verify target inherits it |
| AUTH-25 | player_ranks keep higher-rated target | integration | `--filter "DisplayName~SC#3_HigherTarget"` | Seed target with higher rating; verify target unchanged + sums |
| AUTH-25 | player_ranks W/L/D summed | integration | `--filter "DisplayName~SC#3_WinsSummed"` | Verify wins+losses+draws sum across merge |
| AUTH-25 | Refresh tokens revoked for source | integration | `--filter "DisplayName~SC#3_TokensRevoked"` | Verify all source token rows have RevokedAt set |
| AUTH-25 | party_members same-party conflict → PartyConflict | integration | `--filter "DisplayName~SC#3_PartyConflict"` | Seed source+target in same party; verify abort |
| AUTH-26 | admin_audit_log row written with before/after | integration | `--filter "DisplayName~SC#4_AuditRow"` | Verify audit row exists with action="auth.account_merge" |
| AUTH-26 | actor_id FK ON DELETE SET NULL | integration | `--filter "DisplayName~SC#4_ActorIdFk"` | Verify FK exists; verify tombstoning does not orphan audit |
| AUTH-26 | Superadmin policy rejects non-superadmin | integration | `--filter "DisplayName~SC#5_AuthZ"` | POST with admin-role cookie → 403 |
| AUTH-26 | Response never contains source player_id | integration | `--filter "DisplayName~SC#5_ResponseShape"` | Assert response body does not contain source UUID |

### Sampling Rate
- **Per task commit:** `dotnet test tests/GameKit.Auth.AccountMerge.Integration.Tests -x`
- **Per wave merge:** Full `dotnet test tests/`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/GameKit.Auth.AccountMerge.Integration.Tests/` — new project, does not exist
- [ ] `tests/GameKit.Auth.AccountMerge.Integration.Tests/TestHelpers.cs` — migration application for Core + Auth + Rankings + Matchmaking
- [ ] `tests/GameKit.Auth.AccountMerge.Integration.Tests/CollectionDefinitions.cs`
- [ ] `GameKit.Auth/AssemblyInfo.cs` — add `InternalsVisibleTo` for new test project
- [ ] `GameKit.Rankings/AssemblyInfo.cs` — add `InternalsVisibleTo` for new test project
- [ ] `GameKit.Matchmaking/AssemblyInfo.cs` — add `InternalsVisibleTo` for new test project

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | (merge is admin-gated, not player-auth) |
| V3 Session Management | yes | All source refresh tokens revoked via `RevokeAllForPlayerAsync` |
| V4 Access Control | yes | `AdminPolicies.Superadmin` on endpoint; `AntiforgeryValidationFilter` |
| V5 Input Validation | yes | `MergePlayersRequestValidator` — source != target; both non-empty GUIDs |
| V6 Cryptography | no | No new cryptographic operations |

### STRIDE Threat Model (seed)

| Threat | STRIDE | Mitigation |
|--------|--------|-----------|
| Admin incorrectly merges two unrelated players | Tampering | Superadmin-only; antiforgery; audit with before/after JSON; irreversibility is documented |
| Response leaks source player_id (privacy) | Information Disclosure | Response shape never includes `SourcePlayerId`; audit row's before-JSON restricted to superadmin |
| Concurrent merges race on same source | Elevation of Privilege | SERIALIZABLE tx + UNIQUE index on `account_merges.SourcePlayerId`; first tx wins |
| Partial merge leaves orphaned identities | Tampering | All steps inside single SERIALIZABLE tx; partial state only exists if process kills between steps 17 and 18 (Redis cleanup), which is resumable |
| Attacker enumerates source player IDs from merge endpoint response | Information Disclosure | Response includes only `TargetPlayerId`; no source ID in success or error response |
| Admin merges player with existing ban into legitimate account | Tampering | Banned-target guard: abort if target is banned (A3 above); source-banned policy documented |
| Replay of merge request (admin re-clicks) | Repudiation | `AlreadyMerged` idempotent response; audit trail shows single merge row |

---

## Sources

### Primary (HIGH confidence — direct codebase reads)
- `src/GameKit.Auth/Services/IdentityLinker.cs` — SERIALIZABLE + 40001 retry + TryFindPostgresException + change-tracker detach
- `src/GameKit.Auth/Services/GuestUpgradeService.cs` — SERIALIZABLE + 23505 + change-tracker detach
- `src/GameKit.Auth/Services/IRefreshTokenService.cs` — `RevokeAllForPlayerAsync` signature
- `src/GameKit.Auth/Services/RefreshTokenService.cs` — SHA-256 token storage, revocation semantics
- `src/GameKit.Auth/Entities/PlayerIdentity.cs` — UNIQUE(provider, external_id) not on player_id
- `src/GameKit.Auth/Entities/PlayerCredential.cs` — PK = PlayerId (one credential per player)
- `src/GameKit.Auth/Entities/RefreshToken.cs` — `PlayerId` FK, `FamilyId`, revocation fields
- `src/GameKit.Auth/Data/Configurations/PlayerIdentityConfiguration.cs` — ON DELETE CASCADE
- `src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs` — ON DELETE CASCADE, UNIQUE(Username) citext
- `src/GameKit.Auth/Data/Configurations/RefreshTokenConfiguration.cs` — ON DELETE CASCADE
- `src/GameKit.Auth/Data/AuthMigrationConstants.cs` — advisory lock key = -298890956L
- `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` — migration pattern to replicate
- `src/GameKit.Core/Entities/Player.cs` — no `merged_into_player_id` yet; no `deleted_at` yet
- `src/GameKit.Core/Entities/AdminAuditLog.cs` — `ActorId` is nullable Guid, no FK navigation
- `src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs` — CONFIRMS: no HasOne<Player>() on ActorId
- `src/GameKit.Core/Data/Configurations/PlayerConfiguration.cs` — `players` table; no merge columns
- `src/GameKit.Core/Data/Configurations/SessionParticipantConfiguration.cs` — ON DELETE SET NULL on PlayerId
- `src/GameKit.Core/Migrations/20260415000000_CoreInitial.cs` — CONFIRMS: admin_audit_log has no FK on ActorId
- `src/GameKit.Core/Services/GdprDeleteService.cs` — SERIALIZABLE + direct AdminAuditLog write pattern
- `src/GameKit.Core/Data/MigrationRunner.cs` — `MigrateWithLockAsync(ctx, advisoryLockKey, ct)` public API
- `src/GameKit.Core/Data/GameKitMigrationConstants.cs` — Core advisory lock = 1800940027L; SchemaName = "gamekit"
- `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs` — deterministic migration timestamp convention
- `src/GameKit.Rankings/Entities/PlayerRank.cs` — Rating, RatingDeviation, Volatility, Wins, Losses, Draws, IsInPlacement, PlacementMatchesRemaining, LastDecayAt
- `src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs` — UNIQUE(PlayerId, LadderId), ON DELETE CASCADE
- `src/GameKit.Rankings/Data/Configurations/PendingRatingUpdateConfiguration.cs` — PlayerId nullable, ON DELETE SET NULL
- `src/GameKit.Rankings/Data/Configurations/SeasonRankArchiveConfiguration.cs` — PlayerId nullable, ON DELETE SET NULL
- `src/GameKit.Rankings/Services/EndSeasonService.cs` — `_ctx.Set<AdminAuditLog>()` precedent for cross-package audit write; private const action literal
- `src/GameKit.Rankings/Services/SerializationFailureRetry.cs` — Polly retry pipeline to replicate in Auth
- `src/GameKit.Matchmaking/Data/Configurations/PartyMemberConfiguration.cs` — UNIQUE(PartyId, PlayerId), ON DELETE RESTRICT
- `src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs` — ON DELETE CASCADE on OwnerPlayerId
- `src/GameKit.Matchmaking/Data/Configurations/DeclineHistoryConfiguration.cs` — ON DELETE CASCADE
- `src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs` — NO direct player FK
- `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs` — no PlayerId column
- `src/GameKit.Admin.UI/Services/AdminAuditWriter.cs` — IAdminAuditWriter has `before` parameter; Auth's IAuthAuditWriter does not
- `src/GameKit.Admin.UI/Services/AdminAuditActions.cs` — action name constants; `auth.account_merge` not yet defined
- `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` — Superadmin = "gamekit.admin.superadmin"
- `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` — endpoint filter chain pattern for superadmin + antiforgery
- `src/GameKit.Auth/AssemblyInfo.cs` — existing InternalsVisibleTo grants (new grant needed for merge test project)
- `src/GameKit.Rankings/AssemblyInfo.cs` — existing InternalsVisibleTo grants (new grant needed)
- `.planning/STATE.md` — advisory lock keys, SERIALIZABLE decisions, EndSeasonService precedent
- `.planning/research/ARCHITECTURE.md` — Q3 actor_id ON DELETE SET NULL, Q on party_members, account_merges design sketch

### Secondary (MEDIUM confidence)
- AUTH-23/24/25/26 requirements text — precise wording of SC#3 "keep higher-rated row per ladder, sum W/L/D"

---

## Metadata

**Confidence breakdown:**
- FK completeness inventory: HIGH — verified by reading every EF configuration file
- admin_audit_log FK gap: HIGH — confirmed by reading both configuration and migration
- Package ownership decision: HIGH — follows ARCHITECTURE.md Q3 recommendation
- player_ranks conflict algorithm: MEDIUM — algorithm derived from SC#3 wording; specific edge cases (A5/A6/A7) are ASSUMED
- party_members conflict policy: HIGH — follows CONTEXT.md explicit instruction + ARCHITECTURE.md open question
- Migration timestamps: HIGH — follows established deterministic convention
- Test project structure: MEDIUM — new project naming follows established pattern but structure is new

**Research date:** 2026-06-06
**Valid until:** 2026-07-06 (stable domain, no external moving parts)
