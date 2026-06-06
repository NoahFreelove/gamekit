# Phase 10: Account Merge (Isolated High-Risk) - Context

**Gathered:** 2026-06-06
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Two distinct `player_id`s can be irreversibly merged via a SERIALIZABLE transaction with an idempotency table that enables crash-and-resume; the operation is superadmin-only and fully audited.

**Depends on:** Phase 8 (the `player_ranks` merge strategy reads `player_ranks.rating` to determine which rank row to keep; that schema must be finalized and frozen before this phase modifies it), Phase 7 (new provider identity rows from Google/Apple/Epic must be covered by the merge FK re-pointing logic).

**Requirements:** AUTH-23, AUTH-24, AUTH-25, AUTH-26

**Success Criteria (what must be TRUE):**
1. A process killed mid-merge can be resumed: the `account_merges` table state machine (`pending → committed → redis_cleaned`) allows an identical re-request to pick up from the last committed checkpoint rather than starting over or producing a duplicate.
2. After a successful merge, the source player's `player_identities`, `player_credentials`, and `session_participants` rows all reference the target `player_id`; all source refresh tokens are revoked; the source `players` row is soft-deleted with a `merged_into_player_id` tombstone.
3. Rank conflict resolution follows the "keep higher-rated row per ladder" policy: a player with a higher source rating ends up with source's rating after merge; wins/losses/draws are summed across both accounts.
4. The merge is recorded in `admin_audit_log` with before/after JSON; the `actor_id` FK uses `ON DELETE SET NULL` so tombstoning the source player never orphans the audit history.
5. The merge endpoint requires the `gamekit.admin.superadmin` policy; the API response never includes the source `player_id`.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices are at Claude's discretion — discuss phase was skipped per user setting. Use ROADMAP phase goal, success criteria, and codebase conventions to guide decisions. This is the milestone's highest-risk phase (irreversible data operation): favor crash-safety, idempotency, and auditability over brevity. Resolve the two ARCHITECTURE.md-noted open questions during planning: (a) `party_members` unique-constraint conflict path when source + target are in the same party (explicit abort-merge or remove-source-member policy); (b) `admin_audit_log.actor_id` FK behavior on source-player tombstone (ON DELETE SET NULL, per ARCHITECTURE.md Q3).

</decisions>

<code_context>
## Existing Code Insights

Codebase context will be gathered during plan-phase research. Key surfaces: GameKit.Auth (player_identities, player_credentials, refresh_tokens, IdentityLinker SERIALIZABLE precedent, GuestUpgradeService 23505 handling), GameKit.Core (players, session_participants, soft-delete/GDPR precedent), GameKit.Rankings (player_ranks rating/wins/losses/draws), GameKit.Admin.UI (admin_audit_log, AdminAuditWriter, superadmin policy gamekit.admin.superadmin, AdminUserService SERIALIZABLE LastSuperadmin precedent). New provider identity rows from Phase 7 (Google/Apple/Epic) must be covered by FK re-pointing.

</code_context>

<specifics>
## Specific Ideas

No specific requirements beyond the success criteria — discuss phase skipped. The `account_merges` idempotency table is owned by whichever package the merge service lives in (likely GameKit.Auth or GameKit.Admin.UI) — resolve in planning per the per-package migration boundary; never modify Core tables except adding the `merged_into_player_id` tombstone column + FK (which, like Phase 9's ParticipationFraction, must be owned by the package that owns the `players`/`session_participants` Core entities — i.e. Core owns the tombstone column).

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
