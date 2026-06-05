---
phase: 07-core-rating-seam-stateless-auth-packages
plan: 06
subsystem: auth
tags: [argon2, password-hashing, rehash, migration, testcontainers, integration-test]

requires:
  - phase: 07-02
    provides: IPasswordHasher.NeedsRehash + GameKit.Auth.Argon2 package + UseArgon2() builder extension

provides:
  - PasswordOAuthProvider.CompleteLoginAsync rehash-on-verify block (AUTH-18 call site)
  - ArgonRehashOnVerifyTests: Testcontainers Postgres proof of BCrypt→Argon2 hash migration
  - AuthPasswordHashLength migration: password_hash column extended to varchar(512)

affects:
  - 07-07 through 07-end: any plan that needs to run PasswordOAuthProvider login tests
  - Future integration test plans that call TestHelpers.ApplyMigrations

tech-stack:
  added: []
  patterns:
    - "AuthPasswordHashLength migration: Auth-owned ALTER COLUMN migration under Auth advisory lock"
    - "TestHelpers.ApplyMigrations PendingModelChangesWarning suppression pattern"

key-files:
  created:
    - src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.cs
    - src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.Designer.cs
    - tests/GameKit.Auth.Integration.Tests/ArgonRehashOnVerifyTests.cs
  modified:
    - src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs
    - src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs
    - src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs
    - tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj
    - tests/GameKit.Auth.Integration.Tests/TestHelpers.cs

key-decisions:
  - "PasswordHash column extended from varchar(72) to varchar(512): BCrypt is 60 chars, Argon2id is ~80-120 chars; 512 provides headroom for future hashers (AUTH-18 requires the column to grow)"
  - "PendingModelChangesWarning suppressed in TestHelpers.ApplyMigrations Core migration step: the Auth entities appearing in the runtime model while the Core snapshot lacks them is the intentional per-package migration boundary — not a real pending change"
  - "Plan stated ZERO migration; a migration was necessary because the original varchar(72) assumption was incorrect for Argon2id; Rule 1 auto-fix applied"

patterns-established:
  - "Rehash-on-verify: NeedsRehash guard after Verify success, before BannedCheck; reload tracked entity by PK before UPDATE; SaveChangesAsync in same request scope (T-07-06-01)"

requirements-completed: [AUTH-18]

duration: 7min
completed: 2026-06-05
---

# Phase 7 Plan 06: Argon2 Rehash-on-Verify Summary

**BCrypt→Argon2id transparent rehash wired in PasswordOAuthProvider.CompleteLoginAsync, proven end-to-end with Testcontainers Postgres; password_hash column extended to varchar(512) for Argon2 hash storage**

## Performance

- **Duration:** 7 min
- **Started:** 2026-06-05T22:13:19Z
- **Completed:** 2026-06-05T22:20:XX Z
- **Tasks:** 2 (TDD: RED + GREEN phases)
- **Files modified:** 8

## Accomplishments

- AUTH-18 call site wired: `PasswordOAuthProvider.CompleteLoginAsync` now calls `_hasher.NeedsRehash(credential.PasswordHash)` after a successful Verify and before BannedCheckHelper; when true, reloads a tracked `PlayerCredential` by PlayerId and persists `_hasher.Hash(password)` in the same request scope
- `ArgonRehashOnVerifyTests` (2 xUnit Fact tests) proved end-to-end: BCrypt hash migrates to `$argon2id$` on login under Argon2-configured host; BCrypt control stays unchanged under default host; both re-read from a fresh DbContext scope proving UPDATE durability
- Auth migration `20260418100000_AuthPasswordHashLength` extends `player_credentials.password_hash` from varchar(72) to varchar(512) via `ALTER COLUMN` with no data loss
- `TestHelpers.ApplyMigrations` now suppresses `RelationalEventId.PendingModelChangesWarning` for the Core migration step, unblocking all Auth integration tests that were broken by EF Core 10's new strict model-change detection

## Task Commits

1. **Task 1: Wire rehash-on-verify in PasswordOAuthProvider** - `8d396e0` (feat)
2. **Task 2 RED: ArgonRehashOnVerifyTests (failing)** - `1901eb1` (test)
3. **Task 2 GREEN: Migrations + TestHelpers fix** - `703f664` (feat)

## Files Created/Modified

- `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` - Added NeedsRehash guard block (AUTH-18 call site)
- `src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.cs` - Auth migration extending password_hash to varchar(512)
- `src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.Designer.cs` - EF-generated migration designer file
- `src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs` - Updated snapshot to reflect varchar(512)
- `src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs` - HasMaxLength(72) → HasMaxLength(512)
- `tests/GameKit.Auth.Integration.Tests/ArgonRehashOnVerifyTests.cs` - New Testcontainers integration test
- `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj` - Added GameKit.Auth.Argon2 ProjectReference
- `tests/GameKit.Auth.Integration.Tests/TestHelpers.cs` - Suppress PendingModelChangesWarning; add Microsoft.EntityFrameworkCore.Diagnostics using

## Decisions Made

- **PasswordHash column extended from varchar(72) to varchar(512):** BCrypt hashes are exactly 60 chars, but Argon2id encoded strings are ~80-120 chars depending on parameters. The original varchar(72) was set when BCrypt was the only supported hasher. The plan said "ZERO migration" assuming the column was already large enough — this was an incorrect assumption. Rule 1 auto-fix applied to extend the column.
- **PendingModelChangesWarning suppressed in TestHelpers:** EF Core 10 upgraded this from an informational log to a hard error. Because `ApplyMigrations` registers `AuthModelBuilderExtension` in the same DI container that runs Core migrations, the Core migration context sees Auth entities (via OnModelCreating extension resolution) while the Core snapshot only knows Core entities. This is intentional — per PITFALLS.md #3 the per-package migration boundary means each package's snapshot only covers its own entities.
- **Auth advisory lock pattern unchanged:** The new migration `20260418100000_AuthPasswordHashLength` follows the existing Auth migration naming convention and will be picked up automatically by `TestHelpers.ApplyMigrations` since it scans all migrations in the Auth assembly.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] password_hash column varchar(72) too short for Argon2id hashes**
- **Found during:** Task 2 (ArgonRehashOnVerifyTests GREEN phase)
- **Issue:** `character varying(72)` constraint in `player_credentials` rejects Argon2id hashes (~96 chars at minimum test params m=1024,t=1); `SaveChangesAsync` threw PostgresException 22001 (value too long)
- **Fix:** Extended `PlayerCredentialConfiguration.HasMaxLength` from 72 to 512; added Auth migration `20260418100000_AuthPasswordHashLength` with `AlterColumn<string>(maxLength: 512)` + updated snapshot
- **Files modified:** `PlayerCredentialConfiguration.cs`, `GameKitDbContextModelSnapshot.cs`, two new migration files
- **Verification:** Both `ArgonRehashOnVerifyTests` pass; existing `PasswordProviderTests` (3 tests) still pass
- **Committed in:** `703f664`

**2. [Rule 1 - Bug] TestHelpers.ApplyMigrations broke by EF Core 10 PendingModelChangesWarning**
- **Found during:** Task 2 (first test run)
- **Issue:** EF Core 10 upgraded `PendingModelChangesWarning` to a hard error; the Core migration step in `ApplyMigrations` sees Auth entities in the runtime model (via `AuthModelBuilderExtension` registered in the same service collection) while the Core snapshot only knows Core entities — this is the intentional per-package migration boundary (PITFALLS.md #3) but EF treats it as pending changes
- **Fix:** Added `ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` to the re-registered DbContext in `ApplyMigrations`; added `using Microsoft.EntityFrameworkCore.Diagnostics`
- **Files modified:** `TestHelpers.cs`
- **Verification:** All existing Auth integration tests pass after the fix
- **Committed in:** `703f664`

---

**Total deviations:** 2 auto-fixed (2x Rule 1 bugs)
**Impact on plan:** Both auto-fixes were necessary to get the GREEN phase working. The column-length fix is a legitimate Auth schema change (no Core tables modified). The TestHelpers fix is a test infrastructure fix with no production code impact.

## Issues Encountered

The plan stated "ZERO database migration" based on the assumption that the existing `player_credentials.password_hash` column was large enough for Argon2 hashes. The column was varchar(72) — sized for BCrypt which produces 60-char hashes. Argon2id encoded strings are ~96+ chars minimum. A migration was required.

## Known Stubs

None — all data paths are fully wired.

## Threat Flags

None — no new network endpoints, auth paths, or file access patterns introduced.

## Self-Check

## Self-Check: PASSED

- `/home/noah/Desktop/projects/gamekit/src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` FOUND
- `/home/noah/Desktop/projects/gamekit/tests/GameKit.Auth.Integration.Tests/ArgonRehashOnVerifyTests.cs` FOUND
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.cs` FOUND
- Commit 8d396e0 FOUND
- Commit 1901eb1 FOUND
- Commit 703f664 FOUND

## Next Phase Readiness

- AUTH-18 is fully satisfied: BCrypt→Argon2 rehash-on-login is wired at the call site and proven end-to-end against real Postgres
- `player_credentials.password_hash` can now hold any future password hasher's output up to 512 chars
- All existing Auth integration tests pass with the TestHelpers fix
- Phase 7 plan 06 complete; Wave 2 can proceed to remaining plans

---
*Phase: 07-core-rating-seam-stateless-auth-packages*
*Completed: 2026-06-05*
