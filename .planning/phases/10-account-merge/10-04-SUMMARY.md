---
phase: 10-account-merge
plan: "04"
subsystem: auth
tags: [account-merge, admin, antiforgery, testcontainers, integration-tests, migrations, postgresql, redis]

requires:
  - phase: 10-account-merge
    provides: AccountMergeService (MergeAsync engine, crash-resume, FK surgery, Redis cleanup)
  - phase: 10-account-merge
    provides: Core + Auth migrations (players.deleted_at, players.merged_into_player_id, account_merges table)

provides:
  - POST /admin/api/players/merge endpoint (superadmin-only, antiforgery, rate-limited)
  - MergePlayersRequest/MergePlayersResponse DTOs (SourcePlayerId absent from response)
  - MergePlayersRequestValidator (FluentValidation)
  - AdminMergePolicy rate-limit policy (sliding window)
  - AdminAuditActions.AccountMerge constant
  - SC#1–#5 Testcontainers integration suite (19 tests, all green)
  - Three EF Core migration Designer.cs files (discovery metadata for Phase-10 migrations)

affects: [11-gdpr-export, admin-ui, future-phases-using-merge]

tech-stack:
  added: []
  patterns:
    - "Test host MigrateAsync must run ALL migration packages the service code path touches (not just direct-owner packages)"
    - "Antiforgery two-token: HarvestAntiforgeryTokenAsync must capture gk_admin_csrf Set-Cookie alongside session cookie"
    - "admin_audit_log.ActorId is a bare nullable UUID — stores both player IDs and admin user IDs; no FK to players"
    - "MergeEndpointRuntimeQueryCustomizer: applies Auth + Rankings + Matchmaking extensions so runtime DbContext queries all merge tables"

key-files:
  created:
    - src/GameKit.Admin.UI/Http/Contracts/MergePlayersRequest.cs
    - src/GameKit.Admin.UI/Http/Validators/MergePlayersRequestValidator.cs
    - src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.Designer.cs
    - src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.Designer.cs
    - src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.Designer.cs
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs
    - tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeEndpointTests.cs
  modified:
    - src/GameKit.Admin.UI/Http/AdminEndpoints.cs
    - src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs
    - src/GameKit.Admin.UI/Services/AdminAuditActions.cs
    - src/GameKit.Admin.UI/AssemblyInfo.cs
    - src/GameKit.Auth/Services/AccountMergeService.cs
    - src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs
    - src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs
    - src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs
    - src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs

key-decisions:
  - "SourcePlayerId never in HTTP response — MergePlayersResponse only carries TargetPlayerId + status string (T-10-04-03/SC#5)"
  - "admin_audit_log.ActorId has no FK to players — admin users are not in the players table; FK caused 23503 on every admin login"
  - "AddAuditActorIdFk migration made no-op — rationale documented in migration XML doc and AdminAuditLogConfiguration"
  - "MergeTestHost runs Core+Auth+Rankings+Matchmaking+Admin migrations — AccountMergeService queries party_members (Matchmaking)"

patterns-established:
  - "SC#5 endpoint test host: MigrateAsync must mirror all migration packages the service needs at runtime"
  - "HarvestAntiforgeryTokenAsync uses SendAsync (not GetStringAsync) to capture Set-Cookie headers from the response"

requirements-completed: []

duration: ~4h (across two sessions)
completed: 2026-06-06
---

# Phase 10 Plan 04: Superadmin Merge Endpoint + SC#1–#5 Integration Suite Summary

**Superadmin POST /admin/api/players/merge endpoint with antiforgery + rate-limiting, and 19-test Testcontainers suite (SC#1–#5) all green against real Postgres + Redis**

## Performance

- **Duration:** ~4h (two sessions)
- **Started:** 2026-06-06T18:00:00Z
- **Completed:** 2026-06-06T22:11:23Z
- **Tasks:** 3
- **Files modified/created:** 18

## Accomplishments

- `POST /admin/api/players/merge` endpoint: superadmin-only, antiforgery, `AdminMergePolicy` sliding-window rate-limit, `ValidationEndpointFilter<MergePlayersRequest>`, response NEVER includes `SourcePlayerId` (SC#5/T-10-04-03)
- 19 Testcontainers integration tests all green: SC#1 (idempotency), SC#2 (FK surgery), SC#3 (ratings + party conflict), SC#4 (audit), SC#5 (authz + response shape), Guard tests (self/source-already-merged/banned-target)
- Three EF Core migration `.Designer.cs` files created — without them, EF cannot discover Phase-10 migrations and the `players.deleted_at` / `players.merged_into_player_id` / `account_merges` columns never appear, causing all 19 tests to fail with 42703

## Task Commits

1. **Task 1: MergePlayers DTO + validator + rate-limit + audit action** — `582bdef` (feat)
2. **Task 2: POST /admin/api/players/merge endpoint** — `ffcd45a` (feat)
3. **Task 3: SC#1–#5 Testcontainers integration suite** — `2f4cb0e` (test)

## Files Created/Modified

- `src/GameKit.Admin.UI/Http/Contracts/MergePlayersRequest.cs` — request/response DTOs; response omits SourcePlayerId
- `src/GameKit.Admin.UI/Http/Validators/MergePlayersRequestValidator.cs` — FluentValidation (non-empty GUIDs, not equal)
- `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs` — added `AdminMergePolicy` sliding-window policy
- `src/GameKit.Admin.UI/Services/AdminAuditActions.cs` — added `AccountMerge = "auth.account_merge"` constant
- `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` — added `MergePlayersAsync` handler + route registration
- `src/GameKit.Admin.UI/AssemblyInfo.cs` — added `InternalsVisibleTo("GameKit.Auth.AccountMerge.Integration.Tests")`
- `src/GameKit.Auth/Services/AccountMergeService.cs` — two Rule 1 bug fixes (see Deviations)
- `src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs` — removed FK to players (see Deviations)
- `src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs` — made no-op migration
- `src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.Designer.cs` — NEW: EF migration metadata
- `src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.Designer.cs` — NEW: EF migration metadata
- `src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.Designer.cs` — NEW: EF migration metadata
- `src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs` — removed AdminAuditLog→Player FK relation
- `src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs` — removed FK relation + fixed AccountMerge.Status HasDefaultValue mismatch
- `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeServiceTests.cs` — NEW: SC#1–#4 + Guard tests
- `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeEndpointTests.cs` — NEW: SC#5 endpoint tests

## Decisions Made

- `SourcePlayerId` is never present in the HTTP response, error body, or conflict reason — returning a retired identity after tombstoning violates T-10-04-03/SC#5
- `admin_audit_log.ActorId` carries no FK to `players` — actor_id stores both player IDs (from the merge service) AND admin user IDs (from admin login, ban, GDPR, etc.). Admin users live in `admin_users`, not `players`, so a FK causes 23503 on every admin-initiated audit row
- `AddAuditActorIdFk` migration left in place as a no-op (empty Up/Down) — removing it from the migration history table would cause migration chain inconsistency; making it no-op preserves the chain while applying no schema change

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] player_ranks unique constraint violation on higher-source-rating merge path**
- **Found during:** Task 3 (SC#3 higher-source-rating test)
- **Issue:** `AccountMergeService` Pass 1a tried to `UPDATE gamekit.player_ranks SET PlayerId=targetPlayerId WHERE PlayerId=sourcePlayerId` for source-wins-by-rating ladders while the old target row still existed → 23505 (duplicate key `IX_player_ranks_PlayerId_LadderId`)
- **Fix:** Rewrote to a single CTE: `DELETE FROM player_ranks WHERE PlayerId=targetPlayerId RETURNING stats` + `UPDATE player_ranks SET PlayerId=targetPlayerId, Wins+=tgt_wins, ... WHERE PlayerId=sourcePlayerId` — atomically removes target row and re-attributes source row in one statement
- **Files modified:** `src/GameKit.Auth/Services/AccountMergeService.cs`
- **Verification:** SC#3 "Higher source rating wins" passes; SC#3 "Higher target rating wins" still passes
- **Committed in:** `2f4cb0e` (Task 3 commit)

**2. [Rule 1 - Bug] SourceAlreadyMerged guard only checked for Status==Pending**
- **Found during:** Task 3 (Guard: already-merged source test)
- **Issue:** The `existingMerge.TargetPlayerId != targetPlayerId` check that should throw `MergeConflictException(SourceAlreadyMerged)` was inside the `Status==Pending` branch — a completed merge (RedisCleaned/Committed) with a different target returned `AlreadyMerged` instead of throwing
- **Fix:** Moved the `targetPlayerId != existing.TargetPlayerId` check before the status branches so it applies unconditionally
- **Files modified:** `src/GameKit.Auth/Services/AccountMergeService.cs`
- **Verification:** Guard test "already-merged source → MergeConflictException(SourceAlreadyMerged)" passes for all status values
- **Committed in:** `2f4cb0e` (Task 3 commit)

**3. [Rule 1 - Bug] `AddAuditActorIdFk` migration created a FK that violates every admin login**
- **Found during:** Task 3 (SC#5 endpoint tests — 23503 on admin login audit INSERT)
- **Issue:** Plan 10-04 intended to add a FK from `admin_audit_log.ActorId` to `players.Id` for referential integrity. But admin users (who sign into the admin UI) are NOT in the `players` table — they're in `admin_users`. Every admin login, ban, GDPR export, etc. writes an audit row with `ActorId = admin_user.Id`. With the FK active, ALL admin-initiated actions fail with `23503: insert or update on table "admin_audit_log" violates foreign key constraint "FK_admin_audit_log_players_ActorId"`
- **Fix:** Made `AddAuditActorIdFk` migration a no-op (empty Up/Down with rationale in XML doc). Removed the `HasOne<Player>().WithMany().HasForeignKey(a => a.ActorId)` from `AdminAuditLogConfiguration`. Updated Core and Auth `GameKitDbContextModelSnapshot.cs` and all affected `.Designer.cs` files to remove the FK relation. Updated SC#4 test assertion to verify actor_id UUID is retained (no FK cascade, so the UUID persists after source player is deleted).
- **Files modified:** `src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs`, `src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs`, `src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs`, `src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs`, `src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.Designer.cs`
- **Verification:** All SC#5 endpoint tests pass; SC#4 actor-id test updated and passes
- **Committed in:** `2f4cb0e` (Task 3 commit)

**4. [Rule 1 - Bug] Auth snapshot `AccountMerge.Status` had spurious `HasDefaultValue(0)` causing PendingModelChangesWarning**
- **Found during:** Task 3 (SC#5 MigrateAsync failing with PendingModelChangesWarning-as-error)
- **Issue:** `AccountMergeConfiguration` does not call `HasDefaultValue()` for `Status` (runtime default is CLR default = 0), but the Auth snapshot and `AddAccountMerges.Designer.cs` had `ValueGeneratedOnAdd().HasDefaultValue(0)` → EF detected model/snapshot divergence
- **Fix:** Removed `ValueGeneratedOnAdd().HasDefaultValue(0)` from Auth snapshot and Designer.cs
- **Files modified:** `src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs`, `src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.Designer.cs`
- **Verification:** PendingModelChangesWarning no longer thrown; all migrations apply cleanly
- **Committed in:** `2f4cb0e` (Task 3 commit)

**5. [Rule 1 - Bug] SC#5 MergeTestHost.MigrateAsync missing Rankings and Matchmaking migrations**
- **Found during:** Task 3 (SC#5 tests — 42P01: relation "gamekit.party_members" does not exist)
- **Issue:** `AccountMergeService.MergeTransactionBodyAsync` queries `party_members` (same-party conflict check) and `player_ranks` (FK surgery). The SC#5 endpoint host only ran Core + Auth + Admin migrations, leaving both tables absent.
- **Fix:** Added Rankings and Matchmaking migration steps to `MergeTestHost.MigrateAsync`. Updated `MergeEndpointRuntimeQueryCustomizer` to also apply `RankingsModelBuilderExtension` and `MatchmakingModelBuilderExtension` so the runtime DbContext can query all merge-path tables.
- **Files modified:** `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeEndpointTests.cs`
- **Verification:** All 19 tests pass
- **Committed in:** `2f4cb0e` (Task 3 commit)

**6. [Rule 1 - Bug] HarvestAntiforgeryTokenAsync not capturing CSRF cookie**
- **Found during:** Task 3 (SC#5 tests — antiforgery validation fails with 400, cookie not propagated)
- **Issue:** ASP.NET Core antiforgery uses two-token validation: cookie token (`gk_admin_csrf`) + header token (`X-GameKit-Admin-CSRF`). `HarvestAntiforgeryTokenAsync` used `GetStringAsync` which discards response headers, so the `Set-Cookie: gk_admin_csrf=...` returned by `GET /admin/login` was never captured and not sent on subsequent mutation requests.
- **Fix:** Rewrote `HarvestAntiforgeryTokenAsync` to use `SendAsync`, extract `Set-Cookie` headers from the response (same pattern as `LoginAsAdminAsync`), and merge them into `client.DefaultRequestHeaders["Cookie"]` alongside the existing session cookie.
- **Files modified:** `tests/GameKit.Auth.AccountMerge.Integration.Tests/AccountMergeEndpointTests.cs`
- **Verification:** SC#5 authz tests pass (superadmin → 200, admin-role → 403)
- **Committed in:** `2f4cb0e` (Task 3 commit)

---

**Total deviations:** 6 auto-fixed (5 Rule 1 bugs, 1 Rule 1 test-infrastructure bug)
**Impact on plan:** All fixes were necessary for correctness. Fixes 1–4 are production service/migration bugs that would manifest in deployed environments. Fix 5–6 are test infrastructure bugs caused by the complexity of multi-package migration and antiforgery two-token flows. No scope creep.

## Issues Encountered

- Missing EF Core `.Designer.cs` files for all three Phase-10 migrations caused all 19 tests to fail on first run (42703 column not found). EF Core migration discovery requires both the `.cs` and `.Designer.cs` file; without Designer.cs the migration is invisible and the schema is never updated.
- `TestServer.CreateClient()` has no cookie jar — session and CSRF cookies must be manually harvested from response headers and injected into `DefaultRequestHeaders["Cookie"]`.

## Known Stubs

None — all service paths are fully wired. The merge endpoint calls the real `IAccountMergeService` against real Postgres + Redis.

## Threat Flags

None — no new network endpoints beyond `POST /admin/api/players/merge` which was the explicit plan scope. The endpoint is gated behind `AdminPolicies.Superadmin`, antiforgery, and rate-limiting.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- Phase 10 account-merge is complete (Plans 01–04 all shipped)
- `POST /admin/api/players/merge` is live in the Admin UI for superadmins
- The endpoint, service, and full test suite are production-ready
- `admin_audit_log.ActorId` design decision (no FK, bare UUID) documented in migration and entity config — future audit phases should follow the same pattern

## Self-Check

---
*Phase: 10-account-merge*
*Completed: 2026-06-06*
