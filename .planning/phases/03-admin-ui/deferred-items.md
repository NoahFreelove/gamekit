# Phase 03 — Deferred Items

Items discovered during plan execution that are outside the scope of the current plan. These are NOT regressions introduced by the executing plan — they were already present on the base branch.

## Pre-existing Phase 2 Auth.Integration.Tests failures

**Discovered by:** Plan 03-10 executor, 2026-04-19

**Symptom:** Running `dotnet test tests/GameKit.Auth.Integration.Tests/` against base commit `eec1f78` (Phase 3 Wave 4 complete) yields 38 failures out of 44 tests with the error:

```
System.InvalidOperationException : An error was generated for warning
'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning':
The model for context 'GameKitDbContext' has pending changes. Add a new
migration before updating the database.
```

**Affected classes (seen in failure output):**
- `SteamProviderTests` — all cases
- `DiscordProviderTests` — callback/OAuth cases
- `GuestProviderTests`
- `PasswordProviderTests`
- `IdentityLinkerTests`
- `GuestUpgradeServiceTests`
- `RefreshTokenServiceTests` (some cases)
- `IsGuestResolverTests`
- `PlayerIdentityUniqueTests`
- `AuthSchemaTests`
- `AuthEndpointsE2ETests`

**Root cause (suspected):** The `TestHelpers.ApplyMigrations` / class-local `ApplyMigrations` methods build a DbContext whose model resolution (through the Auth migration customizer or the runtime query customizer) drifts from the persisted migration snapshot after Phase 3's admin migration work (03-02 adds `AdminUser` + the `role` CHECK constraint). EF 10's `ValidateMigrations` is stricter about pending-model-changes warnings by default than EF 9 was.

**Why confirmed pre-existing, not a Plan 03-10 regression:** The same failures reproduce on a clean `git stash` of Plan 03-10's changes (tested before proceeding). No Plan 03-10 modification touches the Auth migration model or any `IModelCustomizer`; the only Plan 03-10 changes are additive (a new helper file, ~3 lines per provider, ~10 lines in RefreshTokenService).

**Verification performed for Plan 03-10:**
- `GameKit.Auth` project builds clean (0 warnings, 0 errors)
- `GameKit.Auth.Tests` (unit tests) — 35/35 pass
- `grep` verification of every Plan 03-10 acceptance criterion on the modified source files (see 03-10-SUMMARY.md)
- A dedicated `BanEnforcementTests` integration test class was authored per the plan but shares the same Testcontainers-Postgres harness as the broken pre-existing suite; the ban-enforcement test cases that depend on that harness are documented in the summary as relying on the pre-existing fix landing first.

**Recommended owner:** A dedicated Phase 3 gap plan (or the first Phase 4 plan that touches a migration) should re-run the full Auth integration suite and fix the `ValidateMigrations` pending-model-changes gap. Options:
1. Regenerate the Auth migration snapshot (`dotnet ef migrations add` on the current model)
2. Configure the warning to log-not-throw via `optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))` at the test-host layer (hides the bug; not recommended long-term)
3. Audit `AuthMigrationModelCustomizer` + `AuthRuntimeQueryCustomizer` for divergence between migration-time and query-time model shapes

**Not actionable by Plan 03-10** — scope boundary. The ban-enforcement patches in Plan 03-10 do not touch the Auth migration model.
