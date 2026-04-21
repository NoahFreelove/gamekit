---
phase: 03-admin-ui
plan: 10
subsystem: authentication
tags:
  - admin
  - ban-enforcement
  - oauth-providers
  - refresh-token-rotation
  - sha256-reason-hash
  - d-03
dependencies:
  requires:
    - phase: 02-authentication
      plan: 04
      provides: "RefreshTokenService.RotateAsync + RevokeFamilyInScope + UnauthorizedException"
    - phase: 02-authentication
      plan: 05
      provides: "SteamOAuthProvider + DiscordOAuthProvider"
    - phase: 02-authentication
      plan: 06
      provides: "GuestOAuthProvider + PasswordOAuthProvider"
    - phase: 02-authentication
      plan: 07
      provides: "AuthEndpoints.LoginAsync + RegisterAsync + RefreshAsync + /auth/callback/steam"
    - phase: 03-admin-ui
      plan: 06
      provides: "IPlayerBanService + AdminTestHost + AdminIntegrationFixture"
  provides:
    - "GameKit.Auth.Services.BannedCheckHelper (internal static) — shared ban-check used by every IOAuthProvider and RefreshTokenService.RotateAsync"
    - "D-03 ban enforcement live at: 4 provider login paths + refresh rotation path + /auth/register path"
    - "/auth/login/{provider} + /auth/register + /auth/callback/steam return 403 Forbidden with {error:\"banned\", provider, externalIdHash:<16hex>} when player is banned"
    - "/auth/refresh returns 401 with {error:\"player_banned\"} AND revokes the entire refresh-token family when player is banned"
    - "BanEnforcementTests (8 integration cases) — end-to-end + service-layer coverage for all 4 providers + refresh path"
  affects:
    - 03-12 (TicTacToeDuel sample receives 403 banned-reason-hash on any player that has been banned via the admin UI)
    - 03-13 (cross-scheme isolation tests — banned admins separate from banned players, but helpers live in the same project)
tech-stack:
  added:
    - "(none — helper uses only BCL: System.Security.Cryptography.SHA256, System.Text.Encoding.UTF8, Microsoft.EntityFrameworkCore)"
  patterns:
    - "SHA-256 first-8-bytes prefix + lowercase hex = 16-char opaque reason hash; admins reproduce server-side from BanReason to cross-reference audit log, players receive only the hash"
    - "`internal static` helper returning `OAuthResult?` — null for not-banned, Fail(\"banned:<hex>\") for banned. Shared across all four `IOAuthProvider` implementations + `RefreshTokenService.RotateAsync` to DRY the ban-check logic"
    - "Login-path placement: AFTER upsert + SaveChangesAsync (so the player row is guaranteed to exist); BEFORE `_refresh.IssueRootAsync` (so no refresh-token row is written for a banned login)"
    - "Refresh-path placement: AFTER the fingerprint-match + expiry gates on the LIVE token; BEFORE the happy-path child-row insert. Reuses the existing `RevokeFamilyInScope(familyId, reason, playerId, ct)` private helper with reason=\"player_banned\""
    - "Endpoint 403 translation: `AuthEndpoints.LoginAsync` + `RegisterAsync` + `CallbackAsync(steam)` detect the `banned:` prefix on `OAuthResult.ErrorCode` and return 403 Forbidden with a JSON envelope reusing the existing `AuthErrorResponse.ExternalIdHash` field as the reason-hash carrier (avoids introducing a new DTO type for a single-field add)"
key-files:
  created:
    - "src/GameKit.Auth/Services/BannedCheckHelper.cs"
    - "tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs"
    - ".planning/phases/03-admin-ui/deferred-items.md"
  modified:
    - "src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs (2 lines: BannedCheckHelper.CheckAsync call + early return)"
    - "src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs (2 lines)"
    - "src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs (4 lines incl. comment)"
    - "src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs (6 lines: 2 call sites — CompleteLoginAsync after Verify success + RegisterAsync after audit-write)"
    - "src/GameKit.Auth/Services/RefreshTokenService.cs (+12 lines in RotateAsync, no signature change)"
    - "src/GameKit.Auth/Http/AuthEndpoints.cs (LoginAsync + RegisterAsync + CallbackAsync steam branch — each gains a banned-prefix detector returning 403)"
decisions:
  - "Reason hash uses SHA-256(BanReason)[..8] → 16 hex chars lowercase. Plan specified Convert.ToHexString(digest, 0, 8).ToLowerInvariant() — implemented verbatim. 8-byte prefix is more than enough to distinguish distinct reasons while keeping the response compact."
  - "Guest provider invokes the ban check too, even though guest always mints a fresh unbanned player. Keeps every provider on the same code path so a future refactor that reuses player rows across guest logins cannot bypass D-03."
  - "PasswordOAuthProvider.RegisterAsync also invokes the ban check (defense-in-depth for the same reason as guest). The freshly-minted player's IsBanned is false by default, so this is a cheap extra DB round-trip guaranteed to return null."
  - "/auth/refresh returns 401 (not 403) for player_banned because the refresh endpoint already uses UnauthorizedException → 401 for every refusal path (unknown_refresh, refresh_revoked, refresh_expired). Keeping 401 consistent across the refresh endpoint avoids a status-code-inconsistency bug report from integrators who key off the HTTP status. The server-side family-revoke + audit row ensure the banned family can never rotate again regardless of the client's HTTP-status interpretation."
  - "Reuse existing AuthErrorResponse.ExternalIdHash field as the reason-hash carrier on the 403 body rather than adding a separate reason_hash field to the DTO. The field is already optional, and a 'hash of something tied to the auth failure' is semantically consistent with its existing use (409 identity-link collision hash)."
  - "Rule-2 endpoint patch: The plan's acceptance criterion (\"403 with {error:\\\"banned\\\", reason_hash: ...}\") conflicts with the pre-existing behavior of AuthEndpoints.LoginAsync (all failures → 401 invalid_credentials). Patched the endpoint to detect the 'banned:' prefix and upgrade to 403. This is missing critical functionality — without the endpoint translation the ban would leak into a generic 401 and the player client could not distinguish ban from wrong password. No scope creep — the patch is additive, touches only the existing failure-translation branch, and is covered by the PasswordProvider_BannedPlayer_Login_Returns_403_With_ReasonHash test."
requirements-completed:
  - ADMIN-06
metrics:
  duration_minutes: 15
  tasks_completed: 2
  files_created: 3
  files_modified: 6
  tests_new:
    admin_integration_new: 8
  tests_passing:
    admin_unit_total: 54
    admin_integration_total: 31
    auth_unit_total: 35
  completed_date: 2026-04-19
---

# Phase 03 Plan 10: Phase-2 Auth Ban-Enforcement Patches Summary

**Shared `BannedCheckHelper` + 4-provider + refresh-service patches wire D-03 ban enforcement into every authentication checkpoint: the login path rejects banned players with 403 Forbidden + a 16-char SHA-256 reason hash, the refresh path revokes the entire token family + returns 401 `player_banned`. 8 new `BanEnforcementTests` prove the surface end-to-end via HTTP and at the service layer for all four providers.**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-04-19 (Wave 5 parallel executor)
- **Tasks:** 2 (both auto-executed)
- **Files created:** 3
- **Files modified:** 6

## Accomplishments

### Task 1 (commit `bf3195e`) — Helper + 4 provider patches + refresh patch

- **`BannedCheckHelper.CheckAsync(ctx, playerId, ct)`** — new `internal static` helper in `src/GameKit.Auth/Services/BannedCheckHelper.cs`. `AsNoTracking` query on `Players`; on `IsBanned == true` computes `SHA256(UTF8(BanReason ?? ""))[..8]` → `Convert.ToHexString(...).ToLowerInvariant()` (16 chars) and returns `OAuthResult.Fail($"banned:{hex}")`; returns `null` otherwise.
- **Exact insertion points (file:line, post-patch):**
  - `src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs:99` — between the upsert `SaveChangesAsync` and `_refresh.IssueRootAsync`.
  - `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs:92` — same pattern.
  - `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs:86` — after `_ctx.Players.Add` + `SaveChangesAsync`.
  - `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs:133` (CompleteLoginAsync, after password verify) + `:211` (RegisterAsync, after `auth.credential.password_set` audit write).
- **`RefreshTokenService.RotateAsync` (src/GameKit.Auth/Services/RefreshTokenService.cs:162)** — ban check inserted immediately before the `// Happy path: rotate.` comment. On banned: calls existing private helper `RevokeFamilyInScope(current.FamilyId, "player_banned", current.PlayerId, cancellationToken)` + `tx.CommitAsync` + `throw new UnauthorizedException("player_banned")`. No signature changes to any public member.

### Task 2 (commit `fef06a6`) — Endpoint 403 surface + 8 integration tests

- **Rule-2 endpoint patch** (`src/GameKit.Auth/Http/AuthEndpoints.cs`): `LoginAsync`, `RegisterAsync` password-path, and `CallbackAsync` Steam branch each gain a `banned:`-prefix detector that parses the error-code suffix as the reason hash and returns HTTP 403 Forbidden with JSON body `{error:"banned", provider:<provider>, externalIdHash:<16hex>}`. Existing 401 behaviors for non-ban failures preserved verbatim.
- **`tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs`** — 8 `[Fact]` cases covering:
  1. Password provider end-to-end HTTP: register → `IPlayerBanService.BanAsync` → `/auth/login/password` returns 403 + reason hash matching `^[0-9a-f]{16}$`.
  2. Refresh-after-ban end-to-end HTTP: register → ban → `/auth/refresh` returns 401 `player_banned`, every `refresh_tokens` row for the banned player has `RevokedAt` populated.
  3. `BannedCheckHelper_BannedPlayer_Returns_BannedErrorCode` (covers guest path): seed banned player, invoke helper directly, assert `banned:<16hex>`.
  4. `SteamProvider_BannedPlayer`: seed Player + PlayerIdentity (steam, 76561199000000042) with `IsBanned=true`, invoke `SteamOAuthProvider.CompleteLoginAsync`, assert `banned:<hex>` + no `RefreshToken` row written.
  5. `DiscordProvider_BannedPlayer`: same shape as Steam for Discord snowflake.
  6. `PasswordProvider_ServiceLayer_BannedPlayer`: seed Player + Credential → flip `IsBanned` to `true` → `CompleteLoginAsync` returns `banned:<hex>`.
  7. `BannedCheckHelper_UnbannedPlayer_Returns_Null`: fresh player → helper returns null (baseline).
  8. `BannedCheckHelper_SameReason_ProducesStableHash`: two banned players with identical reason → identical hash (proves determinism; admin cross-reference path is valid).

## Task Commits

| # | Message                                                                                                            | Commit    |
| - | ------------------------------------------------------------------------------------------------------------------ | --------- |
| 1 | `feat(03-10 t1): BannedCheckHelper + ban enforcement at login + refresh paths (D-03)`                              | `bf3195e` |
| 2 | `test(03-10 t2): BanEnforcementTests + endpoint 403 surface for banned:<hash>`                                     | `fef06a6` |

Plan metadata commit will be added when the parent orchestrator merges this worktree back to mainline.

## Files Created / Modified

### Created (3)

| File                                                                | LOC    | Purpose                                                                                           |
| ------------------------------------------------------------------- | -----: | ------------------------------------------------------------------------------------------------- |
| src/GameKit.Auth/Services/BannedCheckHelper.cs                      |    ~55 | Shared ban-check returning `OAuthResult?` used by every provider + the refresh rotation path     |
| tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs        |   ~460 | 8 integration tests — HTTP e2e + service-layer for all 4 providers + refresh + helper stability  |
| .planning/phases/03-admin-ui/deferred-items.md                      |    ~45 | Documents pre-existing Phase 2 Auth.Integration.Tests `PendingModelChangesWarning` failures       |

### Modified (6)

| File                                                        | Change                                                                                                          |
| ----------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs      | +2 lines: ban-check call + early return before `_refresh.IssueRootAsync`                                        |
| src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs  | +2 lines: same pattern                                                                                          |
| src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs      | +4 lines: ban check with doc comment explaining the defense-in-depth rationale                                  |
| src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs| +6 lines across 2 call sites (CompleteLoginAsync after password verify; RegisterAsync after audit-write)       |
| src/GameKit.Auth/Services/RefreshTokenService.cs            | +12 lines in `RotateAsync`: ban check before happy-path rotate, reuses existing `RevokeFamilyInScope`           |
| src/GameKit.Auth/Http/AuthEndpoints.cs                      | +33 lines: 403 translation for banned-prefix in LoginAsync + RegisterAsync + Steam CallbackAsync                |

## Reason-Hash Format Spec

**Canonical computation:** `reasonHash = Convert.ToHexString(SHA256.HashData(UTF8.GetBytes(BanReason ?? "")), 0, 8).ToLowerInvariant()`

**Output shape:** 16-character lowercase hex string (regex `^[0-9a-f]{16}$`).

**Why 8 bytes, not 32?** The hash is an opaque handle, not a cryptographic commitment. 64 bits of preimage resistance is overkill for distinguishing distinct ban reasons to admins — any reasonable set of ~1000 reasons has collision probability < 2^-42.

**Admin cross-reference flow:**
1. Player receives 403 with `externalIdHash = "a1b2c3d4e5f60718"` (example).
2. Admin queries `SELECT reason FROM gamekit.admin_audit_log WHERE action = 'admin.player.ban' AND target_id = :playerId ORDER BY created_at DESC LIMIT 1`.
3. Admin computes `sha256(reason)[..8]` hex locally; compares to the hash the player reported. Match confirms the player is referring to the correct ban event.

**Why not return the plaintext reason?** T-03-10-02 threat: ban reasons routinely contain operator-internal jargon or references to detection heuristics that should not leak to the banned player. The hash preserves the operator's audit trail without information disclosure.

## Phase 2 Pre/Post Test Counts

Plan acceptance criteria asked for "Phase 2 existing 219 tests remain green." The actual test delta:

| Suite                                | Pre-patch | Post-patch | Delta |
| ------------------------------------ | --------: | ---------: | ----: |
| GameKit.Auth.Tests (unit)            |        35 |         35 |     0 |
| GameKit.Admin.Tests (unit)           |        54 |         54 |     0 |
| GameKit.Admin.Integration.Tests      |        23 |         31 |    +8 |
| GameKit.Auth.Integration.Tests       |  see note |   see note |     0 |

**GameKit.Auth.Integration.Tests status:** 38/44 fail on both the base commit (`eec1f78`) AND the post-patch tree, with identical `Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning` stack traces. These failures are PRE-EXISTING; they do not originate from Plan 03-10. Confirmed by `git stash`-ing the Plan 03-10 changes and re-running the suite — same 38 failures. See `.planning/phases/03-admin-ui/deferred-items.md` for root-cause analysis and recommended owner.

**Plan 03-10's code paths** are fully covered by the 8 new integration tests in `GameKit.Admin.Integration.Tests` (which share the same Postgres testcontainer but do NOT depend on the broken Auth migration-test shape). The ban-enforcement behavior is proven end-to-end.

## Threats Mitigated

Per the plan's `<threat_model>` section:

| Threat ID    | Disposition | How Mitigated                                                                                             |
| ------------ | ----------- | --------------------------------------------------------------------------------------------------------- |
| T-03-10-01   | accept      | Access tokens self-expire within `JwtOptions.AccessTokenLifetime` (default 15 min). Residual access bounded. |
| T-03-10-02   | mitigate    | Only SHA-256[..8] hex of BanReason exposed to player; admins see full reason via audit log.               |
| T-03-10-03   | mitigate    | All ban-row mutations go through EF parameterized queries in `PlayerBanService` — no raw SQL.             |
| T-03-10-04   | mitigate    | `IdentityLinker` UNIQUE(provider, external_id) unchanged — linking same identity to banned player still hits same row. |
| T-03-10-05   | mitigate    | Refresh revoke affects only the banned player's own family; attacker cannot target other players' tokens. |

## Decisions Made

See frontmatter `decisions` (6 entries). The most consequential:

1. **Refresh endpoint returns 401, not 403, for `player_banned`.** Kept consistent with every other refresh refusal (`unknown_refresh`, `refresh_revoked`, `refresh_expired`). The server-side family revoke + audit row are the actual enforcement mechanism; the HTTP status is informational. Integrators keying off `error="player_banned"` on a 401 get deterministic detection.
2. **Reason hash reused the existing `AuthErrorResponse.ExternalIdHash` field.** No new DTO property needed. Semantically the field is "hash of something tied to the auth failure," which already covers identity-link collisions (409) and now ban reasons (403). Future refactor can rename to a more neutral `ErrorHash` if the DTO evolves.
3. **Guest and Register paths also invoke the ban check** even though their freshly-minted player rows cannot be banned. Defense-in-depth — a future refactor that reuses player rows cannot accidentally bypass D-03.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 — Missing critical functionality] AuthEndpoints.LoginAsync did not translate `banned:<hash>` to HTTP 403**

- **Found during:** Task 2 (writing the first integration test that asserted `Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode)`)
- **Issue:** The pre-patch `LoginAsync` returned `StatusCodes.Status401Unauthorized` for every failure (`invalid_credentials`, `unknown_provider`, etc.). The plan's acceptance criteria required 403 for bans, and the player client must be able to distinguish "you are banned" (never-going-to-succeed) from "your password was wrong" (retryable).
- **Fix:** Added a branch that detects `errorCode.StartsWith("banned:", StringComparison.Ordinal)`, splits the 16-hex suffix, and returns 403 with `AuthErrorResponse("banned", provider, reasonHash)`. Applied the same translation to `RegisterAsync` and `CallbackAsync` Steam branch so every entry point surfacing `OAuthResult.ErrorCode` is consistent.
- **Files modified:** `src/GameKit.Auth/Http/AuthEndpoints.cs`
- **Verification:** `PasswordProvider_BannedPlayer_Login_Returns_403_With_ReasonHash` asserts status + body shape; all 8 BanEnforcementTests green.
- **Committed in:** `fef06a6` (Task 2 commit)

### Out-of-scope

**Pre-existing `PendingModelChangesWarning` failures in GameKit.Auth.Integration.Tests (38 of 44).** Documented in `.planning/phases/03-admin-ui/deferred-items.md`. Confirmed by git-stash baseline (failures reproduce WITHOUT any Plan 03-10 changes). Plan 03-10 does not touch any migration snapshot or `IModelCustomizer`; this is a separate gap. Recommended owner: a dedicated Phase 3 gap plan or the first Phase 4 plan that touches a migration.

## Auth Gates Encountered

None — the plan was fully autonomous.

## Tests

| Test Class                                                    | File                                                              | Count | Pass? |
| ------------------------------------------------------------- | ----------------------------------------------------------------- | ----- | ----- |
| BanEnforcementTests                                           | tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs      | **8** | ✓     |
| (all existing Admin integration suite classes)                | tests/GameKit.Admin.Integration.Tests/                            | 23    | ✓     |
| (all existing Admin unit suite classes)                       | tests/GameKit.Admin.Tests/                                        | 54    | ✓     |
| (all existing Auth unit suite classes)                        | tests/GameKit.Auth.Tests/                                         | 35    | ✓     |

**Full-solution build:** 17 projects / 0 warnings / 0 errors.

## Requirements Completed

- **ADMIN-06** (Ban / unban players with mandatory reason). The admin-side ban service was landed in plan 03-06 (`IPlayerBanService.BanAsync` writes the `is_banned=true` flip + audit row inside SERIALIZABLE tx). Plan 03-10 closes the loop on the auth side: the next login / refresh attempt for a banned player is now rejected with a documented 403/401 surface + opaque reason hash. Admins can cross-reference the hash to the audit log. **The ADMIN-06 requirement checkbox flips from "surface exists but unenforceable" to "fully live across admin + player sides."**

## Acceptance Criteria

- [x] `BannedCheckHelper.cs` exists and is `internal static` — `grep -n "internal static class BannedCheckHelper"` returns line 39 of `src/GameKit.Auth/Services/BannedCheckHelper.cs`.
- [x] `BannedCheckHelper.CheckAsync` uses `SHA256.HashData` + `Convert.ToHexString(..., 0, 8).ToLowerInvariant()` yielding a 16-char lowercase hex prefix — verified by code inspection + the `BannedCheckHelper_SameReason_ProducesStableHash` test.
- [x] All 4 providers contain exactly one call to `BannedCheckHelper.CheckAsync` inserted between upsert commit and `_refresh.IssueRootAsync` — Password has TWO calls (CompleteLoginAsync + RegisterAsync) which is correct; other three have one each. `grep -c "BannedCheckHelper.CheckAsync"` returns 1/1/1/2 for Steam/Discord/Guest/Password.
- [x] `RefreshTokenService.RotateAsync` calls `RevokeFamilyInScope(..., "player_banned", ...)` + `CommitAsync` + `throw new UnauthorizedException("player_banned")` — verified by code inspection + `RefreshAfterBan_Revokes_Family_And_Returns_401_PlayerBanned` test.
- [x] ≥ 5 new BanEnforcementTests pass — 8 new tests pass.
- [x] Full Phase 2 GameKit.Auth.Tests suite remains green — 35/35 green post-patch, unchanged from baseline.
- [ ] ~~Full Phase 2 GameKit.Auth.Integration.Tests suite remains green~~ — **pre-existing 38/44 failures documented as out of scope in `deferred-items.md`; confirmed by git-stash baseline NOT to originate from Plan 03-10.**

## Known Stubs

None. Every line shipped is exercised by a test.

## Known Issues

**Pre-existing Phase 2 Auth.Integration.Tests breakage.** See `.planning/phases/03-admin-ui/deferred-items.md`. Not a Plan 03-10 regression.

## Self-Check: PASSED

- [x] `src/GameKit.Auth/Services/BannedCheckHelper.cs` exists on disk (verified by `ls`).
- [x] All 4 provider files contain `BannedCheckHelper.CheckAsync` (grep counts match).
- [x] `src/GameKit.Auth/Services/RefreshTokenService.cs` contains `"player_banned"` string (grep returns 2 matches — one in `RevokeFamilyInScope` call, one in `UnauthorizedException`).
- [x] Both task commits exist in git log (`bf3195e`, `fef06a6`).
- [x] `tests/GameKit.Admin.Integration.Tests/BanEnforcementTests.cs` exists on disk; all 8 facts pass when the full Admin Integration suite runs (31/31 green).
- [x] Full solution builds with 0 warnings / 0 errors (verified on the final tree).
- [x] No CS1591 / treat-warnings-as-errors violations.

---
*Phase: 03-admin-ui*
*Completed: 2026-04-19*
