---
phase: 03-admin-ui
plan: 02
subsystem: admin-ui
tags:
  - admin-ui
  - data-layer
  - ef-core
  - migrations
  - postgres
  - advisory-lock
  - wave-1
dependencies:
  requires:
    - phase: 03-01
      provides: tests/GameKit.Admin.Integration.Tests + AdminCollection (Postgres + Redis fixture wiring)
    - phase: 02-08
      provides: GameKitDbContext app-provider wiring + AuthMigrationHostedService precedent (FOLLOW-UP-02-03-01 closed)
    - phase: 01-04
      provides: GameKitDbContext + IModelBuilderExtension + MigrationRunner.MigrateWithLockAsync(ctx, key)
  provides:
    - AdminUser entity + AdminUserConfiguration (citext Username, ck_admin_users_role CHECK, no FK to players)
    - AdminMigrationConstants — __ef_migrations_admin history table + AdvisoryLockKey -2101739634L (live-verified)
    - AdminModelBuilderExtension : IModelBuilderExtension (lazy-resolved via app provider, plan 03-03 will register)
    - AdminMigrationModelCustomizer (excludes 4 Core + 3 Auth entities; Admin migration scoped to admin_users only)
    - AdminDesignTimeDbContextFactory (dotnet ef CLI entry point; mirrors AuthDesignTimeDbContextFactory)
    - AdminMigrationHostedService (IHostedService applying __ef_migrations_admin under Admin advisory lock; plan 03-03 wires AddHostedService)
    - 20260419000000_AdminInitial migration (creates only admin_users + check + unique index; no Core/Auth touch)
    - AdminAdvisoryLockKeyTests + AdminSchemaTests (3 integration tests, all green on Postgres 17.9 Testcontainers)
  affects:
    - 03-03 (AddGameKitAdmin will TryAddEnumerable AdminModelBuilderExtension + AddHostedService AdminMigrationHostedService AFTER AuthMigrationHostedService)
    - 03-04 (Admin login endpoint queries AdminUser via gamekit.admin_users + ck_admin_users_role + ix_admin_users_username)
    - 03-06 (AdminAuthService implementation + first-superadmin promotion path)
    - 03-11 (CLI gamekit admin create writes to admin_users)
    - 03-13 (AdminTestHost will MigrateAsync three times: Core then Auth then Admin)
tech-stack:
  added:
    - "Microsoft.EntityFrameworkCore + Relational + Design (10.0.6) + Npgsql.EntityFrameworkCore.PostgreSQL (10.0.1) — added to GameKit.Admin.UI.csproj for the per-package data layer"
    - "GameKit.Auth ProjectReference added to GameKit.Admin.UI.csproj per W5 dependency direction (CLAUDE.md GameKit.Admin.UI block)"
    - "dotnet-ef CLI tool upgraded 10.0.5 -> 10.0.6 (matches runtime EF Core; required so dotnet ef migrations add resolves the matching design assemblies)"
  patterns:
    - "Per-package migration shape (SP-3): AdminInitial mirrors AuthInitial — distinct history table + distinct advisory-lock key + IHostedService applying under that lock + IDesignTimeDbContextFactory + RelationalModelCustomizer subclass that ExcludeFromMigrations every entity owned by another package"
    - "PascalCase column names in Postgres CHECK constraint expressions MUST be quoted (\"Role\") — Postgres folds unquoted identifiers to lowercase, errors with 42703 if column was created PascalCase"
    - "ICollectionFixture<T> in xUnit 2.9 cannot resolve fixture-into-fixture constructor injection — composite fixtures (AdminIntegrationFixture, AuthIntegrationFixture) must be constructed by hand inside WebApplicationFactory bootstrap code, NOT registered directly on a [CollectionDefinition]"
    - "AdminMigrationModelCustomizer's exclusion list is one entry longer than AuthMigrationModelCustomizer's: 4 Core entities (Player/GameSession/SessionParticipant/AdminAuditLog) PLUS 3 Auth entities (PlayerIdentity/PlayerCredential/RefreshToken). Helper method (ExcludeEntity) keeps the registration loop tidy."
    - "AdminSchemaTests does NOT register AuthModelBuilderExtension via DI (Auth's marker is internal sealed and not friend to GameKit.Admin.Integration.Tests) — equivalent because AuthMigrationModelCustomizer applies Auth configs directly during the migration pass"
key-files:
  created:
    - src/GameKit.Admin.UI/Entities/AdminUser.cs
    - src/GameKit.Admin.UI/Data/Configurations/AdminUserConfiguration.cs
    - src/GameKit.Admin.UI/Data/AdminModelBuilderExtension.cs
    - src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs
    - src/GameKit.Admin.UI/Data/AdminMigrationModelCustomizer.cs
    - src/GameKit.Admin.UI/Data/AdminDesignTimeDbContextFactory.cs
    - src/GameKit.Admin.UI/Data/AdminMigrationHostedService.cs
    - src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.cs
    - src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.Designer.cs
    - src/GameKit.Admin.UI/Migrations/GameKitDbContextModelSnapshot.cs
    - tests/GameKit.Admin.Integration.Tests/AdminAdvisoryLockKeyTests.cs
    - tests/GameKit.Admin.Integration.Tests/AdminSchemaTests.cs
  modified:
    - src/GameKit.Admin.UI/GameKit.Admin.UI.csproj
    - tests/GameKit.TestFixtures/CollectionDefinitions.cs
    - tests/GameKit.Admin.Integration.Tests/CollectionDefinitions.cs
decisions:
  - "AdminMigrationConstants.AdvisoryLockKey = -2101739634L (live Postgres 17.9 hashtext('gamekit.admin.migrations')::bigint via Testcontainers) — distinct from Core (1800940027L) and Auth (-298890956L)"
  - "GameKit.Admin.UI csproj gains GameKit.Auth ProjectReference per W5 (CLAUDE.md plan-01 entry); required because AdminMigrationConstants XML doc cref AuthMigrationConstants and AdminMigrationModelCustomizer ExcludeFromMigrations Auth's three entity types"
  - "AdminMigrationModelCustomizer is a separate file (not collocated with AdminDesignTimeDbContextFactory like Auth) — chose readability for the longer 7-entity exclusion list per plan Step 1 option (a)"
  - "Migration timestamp 20260419000000 (Phase-1/Phase-2 deterministic-timestamp convention; Auth used 20260418000000, Admin one day later for cross-package ordering when Core->Auth->Admin order matters)"
  - "AdminMigrationConstants.AdvisoryLockKey first-run placeholder pattern: ship 0L, rely on AdminAdvisoryLockKeyTests to fail-and-print the live value, then commit the live value (matches Phase-2 02-02 precedent for AuthMigrationConstants)"
  - "Admin CHECK constraint expression must quote PascalCase column identifier (\"Role\") — Postgres folds unquoted identifiers to lowercase; the AuthInitial migration has no CHECK constraints so the Phase-2 plan didn't surface this gotcha. Plan literal corrected as Rule-1 deviation."
  - "ICollectionFixture<AdminIntegrationFixture> dropped from BOTH AdminCollection re-declarations — xUnit 2.9 ICollectionFixture<T> requires T to have a parameterless constructor; AdminIntegrationFixture's PostgresFixture+RedisFixture ctor cannot be satisfied at collection scope. Composite preserved for plans 03-04+/03-07/03-13 to construct manually (matches AuthIntegrationFixture usage today)."
  - "dotnet-ef tool upgraded 10.0.5 -> 10.0.6 to match the runtime EF Core version pinned in Directory.Packages.props (CLI design assemblies must align with runtime to avoid silent codegen drift)"
  - "AdminSchemaTests applies migrations in three passes (Core then Auth then Admin) — each pass uses a distinct DbContext with its own MigrationsAssembly + history table + customizer; no inter-pass DI sharing required"
metrics:
  duration_minutes: 22
  tasks_completed: 3
  files_created: 12
  files_modified: 3
  tests_passing:
    integration: 3
    unit_smoke: 1
  completed_date: 2026-04-19
requirements_completed:
  - ADMIN-04
---

# Phase 03 Plan 02: Admin Data Layer + Migration Summary

**AdminUser entity + per-package `__ef_migrations_admin` history (advisory lock -2101739634L, live-verified) + AdminInitial migration creating only `admin_users` + 3 integration tests (live-verify lock + schema isolation post-migration).**

## Performance

- **Duration:** 22 min
- **Started:** 2026-04-19T03:57:00Z
- **Completed:** 2026-04-19T04:19:13Z
- **Tasks:** 3
- **Files created:** 12
- **Files modified:** 3
- **Tests added:** 3 integration

## Accomplishments

- Shipped the Admin.UI data layer mirroring the Phase-2 Auth data layer one-for-one, with the three required swaps (history table `__ef_migrations_admin`, advisory key `-2101739634L`, customizer exclusion list extended to also cover Auth's three entities).
- Live-verified `AdminMigrationConstants.AdvisoryLockKey` against Postgres 17.9 (Testcontainers); replaced placeholder `0L` with the actual hashtext result `-2101739634L` and asserted distinctness from Core (`1800940027L`) and Auth (`-298890956L`).
- Generated `20260419000000_AdminInitial` migration that creates ONLY the `admin_users` table (no Core or Auth tables) with the `ck_admin_users_role` CHECK constraint and `ix_admin_users_username` UNIQUE index.
- Added two integration tests (`AdminAdvisoryLockKeyTests`, `AdminSchemaTests`) — total Admin-Integration suite is now 3/0/0 (3 passed, 0 failed, 0 skipped) on Testcontainers.
- Closed the only Wave-1 dependency between 03-02 and 03-03: 03-03 (`AddGameKitAdmin` builder + Razor SDK) can now register `AdminModelBuilderExtension` + `AdminMigrationHostedService` without re-implementing the data layer.

## Task Commits

1. **Task 1: AdminUser entity + EF configuration + ModelBuilderExtension + MigrationConstants** — `5dfe081` (feat)
2. **Task 2: AdminMigrationModelCustomizer + Design-Time Factory + Hosted Service + AdminInitial migration** — `cd223ab` (feat)
3. **Task 3: Live-verify advisory-lock key + assert admin_users schema post-migration** — `a5c75ed` (test)

**Plan metadata:** _(this commit, see Final Commit below)_

## Files Created / Modified (authoritative list)

### Created (12)

- `src/GameKit.Admin.UI/Entities/AdminUser.cs` — entity with Id/Username/PasswordHash/Role/CreatedAt/LastLoginAt/FailedLoginCount/LockedUntil
- `src/GameKit.Admin.UI/Data/Configurations/AdminUserConfiguration.cs` — `internal sealed`; citext Username; `ck_admin_users_role` CHECK; UNIQUE `ix_admin_users_username`; no FK to players
- `src/GameKit.Admin.UI/Data/AdminModelBuilderExtension.cs` — `internal sealed`; applies `AdminUserConfiguration` to the shared model when resolved via app provider
- `src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs` — `MigrationsHistoryTable = "__ef_migrations_admin"`; `AdvisoryLockKey = -2101739634L` (live-verified)
- `src/GameKit.Admin.UI/Data/AdminMigrationModelCustomizer.cs` — `public sealed RelationalModelCustomizer` subclass; applies `AdminUserConfiguration` directly + ExcludeFromMigrations on 4 Core + 3 Auth entities
- `src/GameKit.Admin.UI/Data/AdminDesignTimeDbContextFactory.cs` — `IDesignTimeDbContextFactory<GameKitDbContext>`; reads `GAMEKIT_MIGRATIONS_CONNECTION` env var with fallback; ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>
- `src/GameKit.Admin.UI/Data/AdminMigrationHostedService.cs` — `internal sealed IHostedService`; applies `__ef_migrations_admin` under `AdminMigrationConstants.AdvisoryLockKey` via `MigrationRunner.MigrateWithLockAsync`
- `src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.cs` — Up creates only admin_users + ck_admin_users_role + ix_admin_users_username; Down drops admin_users
- `src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.Designer.cs` — companion designer file (snapshot of model AT this migration timestamp)
- `src/GameKit.Admin.UI/Migrations/GameKitDbContextModelSnapshot.cs` — current model snapshot; AdminUser + Core entities (ExcludeFromMigrations); Auth entities NOT in snapshot (they don't enter the design-time model)
- `tests/GameKit.Admin.Integration.Tests/AdminAdvisoryLockKeyTests.cs` — 2 facts: live-hashtext match + distinctness from Core + Auth keys (SP-13)
- `tests/GameKit.Admin.Integration.Tests/AdminSchemaTests.cs` — 1 fact: 3-pass migration (Core/Auth/Admin) + schema assertion (admin_users + check + unique + 3 history tables coexist) (SP-14)

### Modified (3)

- `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj` — added EF Core (10.0.6) + Relational + Design (PrivateAssets=all) + Npgsql.EntityFrameworkCore.PostgreSQL (10.0.1) PackageReferences; added GameKit.Auth ProjectReference per W5
- `tests/GameKit.TestFixtures/CollectionDefinitions.cs` — dropped `ICollectionFixture<AdminIntegrationFixture>` from AdminCollection (Rule-3 fix; xUnit 2.9 cannot satisfy its constructor at collection scope)
- `tests/GameKit.Admin.Integration.Tests/CollectionDefinitions.cs` — same drop in the per-assembly re-declaration

## Decisions Made

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | `AdvisoryLockKey = -2101739634L` (negative, live-verified) | `hashtext('gamekit.admin.migrations')` returns `int4`; cast to `bigint` preserves the sign. Postgres advisory locks accept any `bigint`. Distinct from Core's `1800940027L` and Auth's `-298890956L`. |
| 2 | Add GameKit.Auth ProjectReference to GameKit.Admin.UI.csproj | XML-doc cref to `AuthMigrationConstants` + `AdminMigrationModelCustomizer` references Auth entity types. CLAUDE.md W5 explicitly permits this dep direction. |
| 3 | Separate `AdminMigrationModelCustomizer.cs` file (not collocated with the design-time factory like Auth) | Plan Step 1 option (a). Readability win for the 7-entity exclusion list (Auth's customizer is shorter and was acceptable inline). |
| 4 | Migration timestamp `20260419000000` (deterministic) | Phase-1/Phase-2 convention. Auth = `20260418000000`; Admin one day later — preserves cross-package ordering for any test that applies all three packages' migrations. |
| 5 | Quote `"Role"` in CHECK-constraint expression | Postgres folds unquoted identifiers to lowercase; column was created PascalCase `Role`, so unquoted `role` raises 42703. Surfaced when AdminSchemaTests's first run hit `Npgsql.PostgresException : 42703: column "role" does not exist`. |
| 6 | Drop `ICollectionFixture<AdminIntegrationFixture>` from both AdminCollection re-declarations | xUnit 2.9 cannot satisfy `AdminIntegrationFixture(PostgresFixture, RedisFixture)` constructor at collection-fixture scope (no fixture-into-fixture injection). Mirrors Phase-2 `AuthIntegrationFixture` usage (constructed by hand in WebApplicationFactory bootstrap, never injected by xUnit). |
| 7 | dotnet-ef CLI 10.0.5 → 10.0.6 | Runtime EF Core is 10.0.6 (Directory.Packages.props); CLI must align so design assemblies match runtime. |
| 8 | AdminSchemaTests does NOT register `AuthModelBuilderExtension` via DI | `AuthModelBuilderExtension` is `internal sealed` and `GameKit.Admin.Integration.Tests` is not in Auth's `InternalsVisibleTo` allowlist. Equivalent because each migration pass uses its own package-scoped `RelationalModelCustomizer`. |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] xUnit 2.9 ICollectionFixture<T> cannot resolve fixture-into-fixture constructor injection**
- **Found during:** Task 3 (first run of `AdminAdvisoryLockKeyTests`).
- **Issue:** Both tests in the new class failed with `Collection fixture type 'GameKit.TestFixtures.AdminIntegrationFixture' had one or more unresolved constructor arguments: PostgresFixture postgres, RedisFixture redis`. The 03-01 plan registered `AdminIntegrationFixture` as an `ICollectionFixture<...>` on `AdminCollection`, but xUnit 2.9 cannot satisfy non-default constructors on collection fixtures.
- **Fix:** Removed `ICollectionFixture<AdminIntegrationFixture>` from BOTH the `GameKit.TestFixtures.AdminCollection` and the per-assembly `GameKit.Admin.Integration.Tests.AdminCollection`. The composite type is preserved for plans 03-04 / 03-07 / 03-13 to construct manually inside their WebApplicationFactory bootstrap (matches the existing Phase-2 `AuthIntegrationFixture` usage — never injected by xUnit either).
- **Files modified:** `tests/GameKit.TestFixtures/CollectionDefinitions.cs`, `tests/GameKit.Admin.Integration.Tests/CollectionDefinitions.cs`.
- **Verification:** All 3 integration tests pass after the change. Full solution still builds clean (17 projects, 0 warnings, 0 errors).
- **Committed in:** `a5c75ed` (Task 3 commit).

**2. [Rule 1 - Bug] Plan literal `"role IN (...)"` in CHECK constraint failed at runtime — column is PascalCase `Role`**
- **Found during:** Task 3 (first run of `AdminSchemaTests`).
- **Issue:** EF emitted the column as `Role` (PascalCase, unmodified from the C# property name); Postgres folds the unquoted `role` in the CHECK expression to lowercase, then errors `Npgsql.PostgresException : 42703: column "role" does not exist`. The plan's literal CHECK expression came directly from RESEARCH and PATTERNS — no live test had ever exercised it (Phase-2 Auth has no CHECK constraints, so this gotcha didn't surface earlier).
- **Fix:** Updated `AdminUserConfiguration.cs` CHECK expression to `"\"Role\" IN ('admin','superadmin')"` (quoted PascalCase). Regenerated the migration via `dotnet ef migrations add` (then renamed back to deterministic `20260419000000` timestamp + updated `[Migration("...")]` attribute).
- **Files modified:** `src/GameKit.Admin.UI/Data/Configurations/AdminUserConfiguration.cs`, `src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.cs` (regenerated), `.Designer.cs` + `GameKitDbContextModelSnapshot.cs` (regenerated).
- **Verification:** AdminSchemaTests now passes 3/0/0; the CHECK constraint is intact in `information_schema.check_constraints` (asserted directly in the test).
- **Committed in:** `a5c75ed` (Task 3 commit).

**3. [Rule 1 - Bug] AdminSchemaTests cannot register `AuthModelBuilderExtension` via DI — type is `internal sealed`**
- **Found during:** Task 3 (compile failure when first attempting to mirror `AuthSchemaTests` line-for-line).
- **Issue:** Plan Step 3 instructed mirroring `AuthSchemaTests` verbatim, including the `services.TryAddEnumerable(...AuthModelBuilderExtension)` line. But `AuthModelBuilderExtension` is `internal sealed` in the `GameKit.Auth` assembly, and `GameKit.Admin.Integration.Tests` is NOT in Auth's `InternalsVisibleTo` allowlist (only `GameKit.Auth.Tests` and `GameKit.Auth.Integration.Tests` are). Compile error: `error CS0122: 'AuthModelBuilderExtension' is inaccessible due to its protection level`.
- **Fix:** Removed the `AuthModelBuilderExtension` registration from `AdminSchemaTests`. Equivalent because each migration pass uses its own `RelationalModelCustomizer` (`AuthMigrationModelCustomizer` for the Auth pass, `AdminMigrationModelCustomizer` for the Admin pass) which applies sibling configurations directly without DI plumbing. Also dropped the now-unused `Microsoft.Extensions.DependencyInjection.Extensions` import and the `sp` parameter on `BuildAuthMigrationContext` (no `UseApplicationServiceProvider` needed).
- **Files modified:** `tests/GameKit.Admin.Integration.Tests/AdminSchemaTests.cs`.
- **Verification:** Test compiles + passes; migration apply order Core→Auth→Admin still works (asserted by the test's existence checks for all three history tables + admin_users).
- **Committed in:** `a5c75ed` (Task 3 commit).

**4. [Rule 3 - Blocking] dotnet-ef CLI version mismatch with runtime EF Core**
- **Found during:** Task 2 (preparing to run `dotnet ef migrations add`).
- **Issue:** Globally-installed `dotnet-ef` was 10.0.5; runtime EF Core is 10.0.6 (Directory.Packages.props). Mismatched CLI/runtime can produce subtly different snapshots.
- **Fix:** `dotnet tool update --global dotnet-ef --version 10.0.6`.
- **Files modified:** none (machine-local tool only).
- **Verification:** `dotnet ef --version` reports 10.0.6; migration generation succeeded.
- **Committed in:** Pre-Task-2 setup; documented in Task 2 commit (`cd223ab`).

**5. [Rule 3 - Blocking] Admin.UI csproj missing EF Core PackageReferences (CLI requires Microsoft.EntityFrameworkCore.Design on the startup project)**
- **Found during:** Task 2 (`dotnet ef migrations add` failed: `Your startup project 'GameKit.Admin.UI' doesn't reference Microsoft.EntityFrameworkCore.Design.`).
- **Issue:** The Admin.UI csproj from Phase 1 was a stub with only the Core ProjectReference; it didn't ship with EF Core dependencies because no data layer existed yet.
- **Fix:** Added `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Npgsql.EntityFrameworkCore.PostgreSQL`, and `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all) PackageReferences to `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj`. Mirrors the Auth csproj.
- **Files modified:** `src/GameKit.Admin.UI/GameKit.Admin.UI.csproj`.
- **Verification:** `dotnet ef migrations add AdminInitial` succeeded; `dotnet build` clean.
- **Committed in:** `cd223ab` (Task 2 commit).

---

**Total deviations:** 5 auto-fixed (3 Rule-3 blocking, 2 Rule-1 bugs).
**Impact on plan:** All five were necessary for correctness or completion; none changed the plan's scope or success criteria. Both Rule-1 bugs were dormant gotchas inherited from PATTERNS literals (CHECK syntax, internal type access) that only surfaced at runtime / compile time during Task 3.

## Issues Encountered

- The `AdminIntegrationFixture` design pattern from plan 03-01 was technically broken (xUnit 2.9 cannot inject fixtures into other fixtures via `ICollectionFixture<T>`). Fixed in this plan; flagged for plans 03-04/03-07/03-13 to construct the composite by hand inside their WebApplicationFactory bootstrap (matches how Phase-2 actually uses `AuthIntegrationFixture`).
- The placeholder-then-live-verify pattern for the advisory lock key worked exactly as documented in 02-02 — first test run reported `Expected: 0; Actual: -2101739634`, second run after pinning the value passed.

## Threat Flags

None. Plan threat register T-03-02-01 through T-03-02-06 are all addressed:

- T-03-02-01 (Tampering: arbitrary role) — `ck_admin_users_role` CHECK constraint enforced at DB level and asserted by AdminSchemaTests.
- T-03-02-02 (Spoofing: duplicate username) — `ix_admin_users_username` UNIQUE on citext column; asserted by AdminSchemaTests via `pg_indexes` query.
- T-03-02-03 (EoP: advisory-lock collision) — `AdminAdvisoryLockKeyTests.AdminKey_Is_Distinct_From_Core_And_Auth_Keys` asserts distinctness; live-hashtext test catches drift.
- T-03-02-04 (Tampering: Admin migration alters Core/Auth tables) — `AdminMigrationModelCustomizer` ExcludeFromMigrations on all 7 entities; verified by grepping `20260419000000_AdminInitial.cs` for forbidden table references (none found) and by AdminSchemaTests asserting Core+Auth history tables coexist.
- T-03-02-05 (Info Disclosure: password_hash exposure) — accepted as residual; explicit responsibility of plan 03-06.
- T-03-02-06 (DoS: startup migration race) — handled by `MigrationRunner.MigrateWithLockAsync` (Phase-1 verified); inherited.

## Self-Check: PASSED

Verification run after writing SUMMARY.md:

- File existence checks (12 created files):
  - `src/GameKit.Admin.UI/Entities/AdminUser.cs` — FOUND
  - `src/GameKit.Admin.UI/Data/Configurations/AdminUserConfiguration.cs` — FOUND
  - `src/GameKit.Admin.UI/Data/AdminModelBuilderExtension.cs` — FOUND
  - `src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs` — FOUND
  - `src/GameKit.Admin.UI/Data/AdminMigrationModelCustomizer.cs` — FOUND
  - `src/GameKit.Admin.UI/Data/AdminDesignTimeDbContextFactory.cs` — FOUND
  - `src/GameKit.Admin.UI/Data/AdminMigrationHostedService.cs` — FOUND
  - `src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.cs` — FOUND
  - `src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.Designer.cs` — FOUND
  - `src/GameKit.Admin.UI/Migrations/GameKitDbContextModelSnapshot.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/AdminAdvisoryLockKeyTests.cs` — FOUND
  - `tests/GameKit.Admin.Integration.Tests/AdminSchemaTests.cs` — FOUND
- Commit existence checks:
  - `5dfe081` — FOUND (Task 1)
  - `cd223ab` — FOUND (Task 2)
  - `a5c75ed` — FOUND (Task 3)
- Acceptance criteria:
  - `dotnet test tests/GameKit.Admin.Integration.Tests/` — Passed: 3, Failed: 0, Skipped: 0
  - `grep -q '= 0L;' src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs` — ABSENT (placeholder replaced)
  - `grep -q 'PLACEHOLDER' src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs` — ABSENT (placeholder text gone)
  - `grep 'live-verified on Postgres 17.9 via Testcontainers on 2026-04-19' src/GameKit.Admin.UI/Data/AdminMigrationConstants.cs` — FOUND (provenance recorded)
  - `dotnet build GameKit.sln` — 17 projects, 0 warnings, 0 errors

## Next Wave Readiness

- **Plan 03-03 (AddGameKitAdmin builder + Razor SDK + MudBlazor PackageReference + AdminUiMarker)** is unblocked. It can now:
  - `services.TryAddEnumerable<IModelBuilderExtension, AdminModelBuilderExtension>()`
  - `services.AddHostedService<AdminMigrationHostedService>()` AFTER `AddHostedService<AuthMigrationHostedService>()` (registration order = startup order)
  - Reference `AdminMigrationConstants.AdvisoryLockKey` from any future tooling (e.g., a `gamekit migrate --package admin` CLI command in plan 03-11)
- The composite `AdminIntegrationFixture` type still exists at `tests/GameKit.TestFixtures/AdminIntegrationFixture.cs`; plans 03-04 / 03-07 / 03-13 should instantiate it by hand inside their WebApplicationFactory bootstraps (matches the Phase-2 `AuthIntegrationFixture` pattern).

---
*Phase: 03-admin-ui*
*Plan: 02*
*Completed: 2026-04-19*
