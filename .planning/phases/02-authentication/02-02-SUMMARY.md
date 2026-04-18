---
phase: 02-authentication
plan: 02
subsystem: authentication
tags:
  - authentication
  - entities
  - ef-core
  - postgres
  - migration
  - citext
  - unique-constraint
dependencies:
  requires:
    - phase: 01-foundation-core-migrations-ops-defaults-gpl
      provides: "GameKit.Core entities + GameKitDbContext + IModelBuilderExtension contract + MigrationRunner + CoreDesignTimeFactory + PostgresFixture"
    - phase: 02-authentication
      plan: 01
      provides: "GameKit.Auth.Tests + GameKit.Auth.Integration.Tests xUnit projects + AuthCollection + Directory.Packages.props Auth pins + AuthMarker sentinel"
  provides:
    - "GameKit.Auth.Entities.PlayerIdentity — UUIDv7 keyed; UNIQUE(provider, external_id) anchors D-14 race"
    - "GameKit.Auth.Entities.PlayerCredential — PlayerId-keyed; citext Username + BCrypt hash"
    - "GameKit.Auth.Entities.RefreshToken — SHA-256 TokenHash; FamilyId + (PlayerId, RevokedAt) composite index"
    - "GameKit.Auth.Data.AuthModelBuilderExtension — IModelBuilderExtension sibling-registration glue"
    - "GameKit.Auth.Data.AuthMigrationConstants — AdvisoryLockKey=-298890956L + MigrationsHistoryTable=__ef_migrations_auth"
    - "GameKit.Auth.Data.AuthDesignTimeDbContextFactory — dotnet-ef tooling hook"
    - "GameKit.Auth.Data.AuthMigrationModelCustomizer — reusable customizer for Auth-only model builds"
    - "20260418000000_AuthInitial migration + Auth-only GameKitDbContextModelSnapshot"
    - "Auth schema: player_identities + player_credentials + refresh_tokens in gamekit schema with ON DELETE CASCADE FKs"
    - "citext extension installed via migration (CREATE EXTENSION IF NOT EXISTS citext)"
  affects:
    - 02-03 (options + AddAuth fluent extension — consumes TryAddEnumerable pattern)
    - 02-04 (BCrypt hasher + JWT issuer + RefreshTokenService — mutates refresh_tokens via TokenHash)
    - 02-05 (Steam/Discord providers — insert rows into player_identities)
    - 02-06 (Guest + Password + upgrade service — SERIALIZABLE race anchored by UNIQUE(provider, external_id))
    - 02-07 (HTTP endpoints + WebApplicationFactory tests)
    - 02-08 (TicTacToeDuel sample app — end-to-end 4-provider login success criterion)
tech-stack:
  added:
    - "Microsoft.EntityFrameworkCore 10.0.6 (added to GameKit.Auth.csproj)"
    - "Microsoft.EntityFrameworkCore.Relational 10.0.6"
    - "Microsoft.EntityFrameworkCore.Design 10.0.6 (PrivateAssets=all)"
    - "Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1"
  patterns:
    - "Per-package EF migration history table __ef_migrations_<pkg>"
    - "Per-package Auth-only model snapshot via IModelCustomizer replacement (AuthMigrationModelCustomizer)"
    - "Core entities marked ExcludeFromMigrations() when generating Auth deltas (migration-boundaries rule)"
    - "UNIQUE(provider, external_id) schema constraint as D-14 guest-upgrade race anchor"
    - "SHA-256 TokenHash UNIQUE index — refresh tokens never stored in raw form"
    - "citext column type for case-insensitive username uniqueness"
key-files:
  created:
    - "src/GameKit.Auth/Entities/PlayerIdentity.cs"
    - "src/GameKit.Auth/Entities/PlayerCredential.cs"
    - "src/GameKit.Auth/Entities/RefreshToken.cs"
    - "src/GameKit.Auth/Data/Configurations/PlayerIdentityConfiguration.cs"
    - "src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs"
    - "src/GameKit.Auth/Data/Configurations/RefreshTokenConfiguration.cs"
    - "src/GameKit.Auth/Data/AuthModelBuilderExtension.cs"
    - "src/GameKit.Auth/Data/AuthMigrationConstants.cs"
    - "src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs"
    - "src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs"
    - "src/GameKit.Auth/Migrations/20260418000000_AuthInitial.Designer.cs"
    - "src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs"
    - "tests/GameKit.Auth.Integration.Tests/AuthAdvisoryLockKeyTests.cs"
    - "tests/GameKit.Auth.Integration.Tests/AuthSchemaTests.cs"
    - "tests/GameKit.Auth.Integration.Tests/PlayerIdentityUniqueTests.cs"
  modified:
    - "src/GameKit.Auth/GameKit.Auth.csproj (EF Core + Npgsql + Design-time PackageReferences)"
    - "tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs (local [CollectionDefinition(\"Postgres\")])"
decisions:
  - "AuthMigrationConstants.AdvisoryLockKey = -298890956L (computed live from hashtext('gamekit.auth.migrations')::bigint against Postgres 17.9 via Testcontainers; distinct from Core's 1800940027L)"
  - "Negative advisory-lock key accepted — hashtext returns int4, ::bigint preserves sign; Postgres advisory locks accept any bigint"
  - "AuthMigrationModelCustomizer promoted to top-level public sealed class (reused by both design-time EF CLI and runtime test-time Auth migration contexts)"
  - "AuthModelBuilderExtension bypassed in design-time path — EF's internal service provider does not bridge IEnumerable<IModelBuilderExtension> to ReplaceService constructor injection when DbContext is built ad-hoc"
  - "citext extension installed via explicit migrationBuilder.Sql(\"CREATE EXTENSION IF NOT EXISTS citext;\") prologue (defensive, duplicates the Npgsql:PostgresExtension annotation EF emits)"
  - "Deterministic migration timestamp 20260418000000 (Phase 1 convention — EF CLI's auto-timestamp replaced for cross-package ordering)"
requirements-completed:
  - AUTH-02
  - AUTH-03
  - AUTH-04
  - AUTH-11
metrics:
  duration_minutes: 14
  tasks_completed: 3
  files_created: 15
  files_modified: 2
  tests_passing:
    auth_unit_smoke: 1
    auth_integration: 8
    core_integration: 9
    core_unit: 130
  completed_date: 2026-04-18
---

# Phase 02 Plan 02: Auth Entities + AuthInitial Migration Summary

**Three GameKit.Auth EF Core entities (PlayerIdentity / PlayerCredential / RefreshToken), their configurations with the database-level UNIQUE(provider, external_id) race anchor, AuthMigrationConstants pinned to live-Postgres-verified advisory-lock key -298890956L, per-package AuthInitial migration landing the three Auth tables into a `gamekit` schema that already hosts Core, and three integration tests proving the schema, the lock-key distinctness, and the D-14 race rejection.**

## Performance

- **Duration:** 14 min
- **Started:** 2026-04-18T17:47:25Z
- **Completed:** 2026-04-18T18:02:23Z
- **Tasks:** 3
- **Files created:** 15
- **Files modified:** 2

## Accomplishments

- Three Auth entities shipped (PlayerIdentity / PlayerCredential / RefreshToken) with XML docs on every public member; TreatWarningsAsErrors + CS1591 clean.
- `AuthModelBuilderExtension : IModelBuilderExtension` sibling-registration glue in place; consumed by `GameKitModelCustomizer` at runtime.
- Advisory-lock key for Auth migrations pinned to `-298890956L` after live-Postgres-17.9 verification via Testcontainers — distinct from Core's `1800940027L` so startup migrations cannot deadlock on the shared advisory-lock namespace.
- `20260418000000_AuthInitial` migration emits ONLY the three Auth tables (Core tables intentionally `ExcludeFromMigrations()`) + the UNIQUE(Provider, ExternalId) index that is the D-14 guest-upgrade race anchor, + UNIQUE TokenHash, + (PlayerId, RevokedAt) composite, + FamilyId index, + three FKs `ON DELETE CASCADE` to `players(Id)`, + `CREATE EXTENSION IF NOT EXISTS citext`.
- Per-package `GameKitDbContextModelSnapshot.cs` lives in `src/GameKit.Auth/Migrations/` — contains only Auth entities — preserving the per-package migration-isolation contract (PITFALLS #3).
- Three new integration tests green: `AuthAdvisoryLockKeyTests` (2 facts), `AuthSchemaTests` (1 fact), `PlayerIdentityUniqueTests` (1 fact, proves Postgres raises SqlState 23505 on duplicate identity insert).

## Task Commits

Each task was committed atomically:

1. **Task 1: Entities + EF configurations + AuthModelBuilderExtension** — `a1c52fd` (feat)
2. **Task 2: AuthMigrationConstants + AuthDesignTimeDbContextFactory + AuthAdvisoryLockKeyTests** — `aec8623` (feat)
3. **Task 3: AuthInitial migration + AuthSchemaTests + PlayerIdentityUniqueTests** — `4831c84` (feat)

## Files Created/Modified

### Production code (`src/GameKit.Auth/`)

- `Entities/PlayerIdentity.cs` (44 lines) — UUIDv7 id, FK to players, Provider + ExternalId (UNIQUE together), optional DisplayName/AvatarUrl/Metadata(jsonb), CreatedAt/UpdatedAt timestamps.
- `Entities/PlayerCredential.cs` (30 lines) — PlayerId primary key; citext Username (3–32 chars); BCrypt-sized PasswordHash (<=72); UpdatedAt.
- `Entities/RefreshToken.cs` (51 lines) — UUIDv7 id, FK to players, FamilyId, SHA-256 TokenHash (UNIQUE), nullable ReplacedByTokenHash/DeviceFingerprint/UsedAt/RevokedAt.
- `Data/Configurations/PlayerIdentityConfiguration.cs` (34 lines) — `ToTable("player_identities")`, `HasIndex(Provider, ExternalId).IsUnique()`, `OnDelete(Cascade)`.
- `Data/Configurations/PlayerCredentialConfiguration.cs` (31 lines) — `HasColumnType("citext")` on Username, UNIQUE Username, `OnDelete(Cascade)`.
- `Data/Configurations/RefreshTokenConfiguration.cs` (32 lines) — UNIQUE TokenHash, `(PlayerId, RevokedAt)` composite, FamilyId index, `OnDelete(Cascade)`.
- `Data/AuthModelBuilderExtension.cs` (24 lines) — `IModelBuilderExtension`; applies the three configs.
- `Data/AuthMigrationConstants.cs` (30 lines) — `MigrationsHistoryTable = "__ef_migrations_auth"`, `AdvisoryLockKey = -298890956L`.
- `Data/AuthDesignTimeDbContextFactory.cs` (109 lines) — design-time factory + `AuthMigrationModelCustomizer` (top-level, reused by tests).
- `Migrations/20260418000000_AuthInitial.cs` (161 lines) — three CreateTable calls, six CreateIndex calls, explicit `CREATE EXTENSION IF NOT EXISTS citext` prologue.
- `Migrations/20260418000000_AuthInitial.Designer.cs` (auto-generated, renamed to deterministic timestamp).
- `Migrations/GameKitDbContextModelSnapshot.cs` (auto-generated, Auth-only).
- `GameKit.Auth.csproj` — added `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational`, `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all), `Npgsql.EntityFrameworkCore.PostgreSQL`.

### Tests (`tests/GameKit.Auth.Integration.Tests/`)

- `AuthAdvisoryLockKeyTests.cs` (36 lines) — Two facts: pinned key matches live hashtext; Auth key distinct from Core key.
- `AuthSchemaTests.cs` (114 lines) — Applies Core then Auth migrations; asserts all three Auth tables + `__ef_migrations_auth` exist; asserts `__ef_migrations_core` still intact (co-existence); asserts UNIQUE(Provider, ExternalId) + UNIQUE TokenHash indexes present; asserts citext extension installed.
- `PlayerIdentityUniqueTests.cs` (140 lines) — Seeds two players + one identity for player A; attempts duplicate (steam, external_id) for player B; asserts `DbUpdateException` wrapping `PostgresException` with `SqlState == "23505"`.
- `CollectionDefinitions.cs` — appended local `[CollectionDefinition("Postgres")]` (xUnit1041 requirement; matches Wave-0 `[CollectionDefinition("Auth")]` re-declaration pattern).

## Decisions Made

- **Advisory-lock key = `-298890956L`** (negative). Computed live: `SELECT hashtext('gamekit.auth.migrations')::bigint` on Postgres 17.9 via Testcontainers. `hashtext` returns int4; the `::bigint` cast preserves sign. Postgres advisory locks accept any `bigint`, positive or negative. Distinct from Core's `1800940027L`. The `AuthAdvisoryLockKeyTests.AuthKey_Is_Distinct_From_Core_Key` fact codifies the distinctness rule; `PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation` re-verifies the value on every integration run.
- **`AuthMigrationModelCustomizer` promoted to a public sealed class** at top-level (originally a nested internal class in the plan sketch). This is reused by (a) the design-time EF CLI through `AuthDesignTimeDbContextFactory.ReplaceService`, and (b) the test-time Auth migration context in `AuthSchemaTests`/`PlayerIdentityUniqueTests`. It applies the three Auth entity configurations directly (bypassing DI) and marks every Core entity `ExcludeFromMigrations()` so the per-package Auth snapshot/migration diff contains ONLY Auth tables. This preserves CLAUDE.md's migration-boundaries rule: "packages never modify Core tables in their migrations — only add new tables or FK references".
- **Deterministic migration timestamp `20260418000000`.** EF CLI emitted `20260418175601_AuthInitial`; renamed to `20260418000000_AuthInitial` and updated the `[Migration(...)]` attribute in the Designer partial. Matches Phase 1 convention (STATE.md note "Migration timestamp renamed to 20260415000000 for deterministic cross-package ordering").
- **Explicit `migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;")` prologue** added to `Up()` before the `AlterDatabase().Annotation("Npgsql:PostgresExtension:citext", ",,")` that EF emits automatically. Redundant (both generate the same `CREATE EXTENSION IF NOT EXISTS citext;` SQL) but gives operators reading the migration source an explicit, human-readable cue that the extension is a migration-time dependency.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Blocking] Added local `[CollectionDefinition("Postgres")]` re-declaration in Auth.Integration.Tests**

- **Found during:** Task 2 (first run of `AuthAdvisoryLockKeyTests`)
- **Issue:** xUnit analyzer error `xUnit1041: Fixture argument 'pg' does not have a fixture source (if it comes from a collection definition, ensure the definition is in the same assembly as the test)`. Same root cause as the Wave-0 `AuthCollection` re-declaration (Plan 02-01 Task 3 deviation #2).
- **Fix:** Appended `[CollectionDefinition("Postgres")]` class declaring `ICollectionFixture<PostgresFixture>` to `tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs`. Matches Phase 1's local-re-declaration pattern in `tests/GameKit.Core.Integration.Tests/CollectionDefinitions.cs`.
- **Files modified:** `tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs`
- **Committed in:** `aec8623` (Task 2 commit)

**2. [Rule 1 — Bug] AuthInitial migration initially emitted Core tables instead of Auth tables**

- **Found during:** Task 3 (first `dotnet ef migrations add` run)
- **Issue:** The plan sketch of `AuthDesignTimeDbContextFactory` assumed that registering `AuthModelBuilderExtension` via `TryAddEnumerable` + `UseApplicationServiceProvider` would propagate through `GameKitModelCustomizer.Customize` at design time. It does NOT — when EF instantiates a `ReplaceService`d customizer, its constructor-injected `IEnumerable<IModelBuilderExtension>` is resolved from EF's internal service provider, which never sees the app's singletons. Result: the Auth migration diff saw Core entities (always loaded via `ApplyConfigurationsFromAssembly` in `GameKitDbContext.OnModelCreating`) but no Auth entities — exact inverse of the intended Auth-only snapshot — violating CLAUDE.md's "packages never modify Core tables in their migrations" rule.
- **Fix:** Replaced `GameKitModelCustomizer` at design-time with a dedicated `AuthMigrationModelCustomizer : RelationalModelCustomizer` that (a) calls `base.Customize` (which runs `OnModelCreating` + Core entity configs), (b) applies the three Auth entity configurations directly (no DI), and (c) enumerates every Core entity (`Player`, `GameSession`, `SessionParticipant`, `AdminAuditLog`) and calls `modelBuilder.Entity(type).ToTable(..., t => t.ExcludeFromMigrations())`. The regenerated migration contained ONLY Auth tables + indexes + FKs. Verified by `grep -c CreateTable` → 3.
- **Files modified:** `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` (customizer refactored), regenerated `Migrations/20260418000000_AuthInitial.cs` and `.Designer.cs` + `GameKitDbContextModelSnapshot.cs`
- **Committed in:** `4831c84` (Task 3 commit)

**3. [Rule 1 — Bug] Test-time Auth migration context failed `PendingModelChangesWarning`**

- **Found during:** Task 3 (`AuthSchemaTests` + `PlayerIdentityUniqueTests` first test run)
- **Issue:** With the fix from deviation #2, the design-time factory produced a valid Auth-only snapshot. But at test time the `BuildAuthMigrationContext` helper used the runtime `GameKitModelCustomizer`, which registered Core entities in the model (via `ApplyConfigurationsFromAssembly`). EF's Migrator validated the model against the Auth snapshot (which excludes Core entities) and raised `PendingModelChangesWarning` — blocking `MigrateAsync`.
- **Fix:** Promoted `AuthMigrationModelCustomizer` to a top-level public sealed class (originally a nested internal class) so test code can also `.ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()`. After the refactor, both `AuthSchemaTests.BuildAuthMigrationContext` and `PlayerIdentityUniqueTests.ApplyCoreAndAuthMigrations` use `AuthMigrationModelCustomizer`. `MigrateAsync` succeeds; both tests green.
- **Files modified:** `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` (extracted customizer), `tests/GameKit.Auth.Integration.Tests/AuthSchemaTests.cs`, `tests/GameKit.Auth.Integration.Tests/PlayerIdentityUniqueTests.cs`
- **Committed in:** `4831c84` (Task 3 commit)

**4. [Rule 1 — Bug] `OpenContext` in PlayerIdentityUniqueTests could not see PlayerIdentity in the runtime model**

- **Found during:** Task 3 (`PlayerIdentityUniqueTests.Concurrent_Insert_...` run)
- **Issue:** After fixing #3, `AuthSchemaTests` passed but `PlayerIdentityUniqueTests` failed with `Cannot create a DbSet for 'PlayerIdentity' because this type is not included in the model for the context`. Root cause: EF Core's `AddDbContext` path does not reliably forward `IEnumerable<IModelBuilderExtension>` to a `ReplaceService`d customizer's constructor when that customizer is resolved through EF's internal service provider rather than the application's. This is a pre-existing architectural gap in the Phase-1 `GameKitModelCustomizer` + `TryAddEnumerable` design that did not surface in Phase 1 (no sibling package was testing cross-assembly entities via DI).
- **Fix (tactical, scoped to this test):** Added an `AuthRuntimeQueryCustomizer : RelationalModelCustomizer` inside `PlayerIdentityUniqueTests.cs` that instantiates `new AuthModelBuilderExtension()` directly and applies it after `base.Customize`. Used this via `.ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>()` on the options builder passed to `new GameKitDbContext(opts)`. Both facts now pass (the UNIQUE-constraint race fact raises Postgres SqlState 23505 as required).
- **Flagged for follow-up:** The runtime `GameKitModelCustomizer` + `TryAddEnumerable` DI plumbing should be re-audited in Plan 02-03 (AddAuth fluent extension) — the production `UseGameKit`/`AddGameKit` path probably needs an explicit `UseApplicationServiceProvider` call (matching `GameKitApplicationBuilderExtensions.BuildMigrationContext` line 87) to ensure sibling-package entities appear in the query-side model. If that audit confirms the gap, 02-03 will fix it at the Core+Auth runtime layer and this local customizer in the test can be removed.
- **Files modified:** `tests/GameKit.Auth.Integration.Tests/PlayerIdentityUniqueTests.cs`
- **Committed in:** `4831c84` (Task 3 commit)

---

**Total deviations:** 4 auto-fixed (1 Rule 3 blocking, 3 Rule 1 bugs).
**Impact on plan:** All four deviations were necessary for correctness. Deviation #4 exposed a latent architectural concern (sibling-package DI resolution in `GameKitModelCustomizer`) that Plan 02-03 should address. Deviation #2 and #3 produced a durable, reusable `AuthMigrationModelCustomizer` pattern future sibling packages (Matchmaking, Rankings, Presence) can adopt verbatim when they ship their own per-package migrations. No scope creep beyond the plan's stated objective.

## Issues Encountered

- EF Core `10.0.5` CLI tooling (vs. `10.0.6` runtime) emitted a non-blocking nag: `The Entity Framework tools version '10.0.5' is older than that of the runtime '10.0.6'. Update the tools for the latest features and bug fixes.` Cosmetic only — migration generation succeeded. Deferred: consider installing `dotnet-ef 10.0.6` via `dotnet tool update --global dotnet-ef` as a follow-up quick task.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- **02-03 (Options + AddAuth fluent extension + egress allow-list):** Can build on the TryAddEnumerable pattern for `AuthModelBuilderExtension` and on the three Auth entities ready for `DbContext` mutation. Should audit the runtime `GameKitModelCustomizer` DI path (see deviation #4) and, if needed, wire `UseApplicationServiceProvider` into `AddGameKit`/`AddAuth` so sibling-package entities appear in the query model without the `AuthRuntimeQueryCustomizer` workaround.
- **02-04 (BCrypt hasher + JWT issuer + RefreshTokenService):** `RefreshToken` table + UNIQUE TokenHash index are in place; rotation-chain FKs (`ReplacedByTokenHash`) ready to populate.
- **02-05 (Steam/Discord providers):** `PlayerIdentity.Provider` (`steam`, `discord`) + `ExternalId` columns are ready for insertion; UNIQUE(provider, external_id) schema constraint will reject provider-side duplicates.
- **02-06 (Guest upgrade + password register):** The D-14 race anchor (UNIQUE(provider, external_id)) is live and has been proven to raise Postgres SqlState 23505 on concurrent-duplicate inserts. The service layer's SERIALIZABLE transaction + 23505-catch pattern can be implemented directly on top.
- **02-07 (HTTP endpoints):** schema is ready; `WebApplicationFactory<Program>` bootstrap from Wave-0 can be consumed once endpoints are wired.
- **02-08 (TicTacToeDuel sample app):** Schema path to ROADMAP Success Criterion #1 (e2e 4-provider login) is unblocked — every login row now has a persistent `player_identities` or `player_credentials` home.

## Known Stubs

None — every file in this plan performs real work. The placeholder `AdvisoryLockKey = 0L` was intentional during Task 2a and corrected within the same task (committed only AFTER the pin was verified by `AuthAdvisoryLockKeyTests`).

## Threat Flags

None beyond those already enumerated in the plan's `<threat_model>`. The three Auth entities + their migration introduce no new network endpoints, auth paths, or trust boundaries. The Rule-2 mitigations required by `T-02-01`, `T-02-02`, `T-02-03`, `T-02-09`, `T-02-14` in the plan's threat register are implemented as schema-level guards and verified by the three integration tests.

## Self-Check: PASSED

**Files verified present on disk:**
- FOUND: src/GameKit.Auth/Entities/PlayerIdentity.cs
- FOUND: src/GameKit.Auth/Entities/PlayerCredential.cs
- FOUND: src/GameKit.Auth/Entities/RefreshToken.cs
- FOUND: src/GameKit.Auth/Data/Configurations/PlayerIdentityConfiguration.cs
- FOUND: src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs
- FOUND: src/GameKit.Auth/Data/Configurations/RefreshTokenConfiguration.cs
- FOUND: src/GameKit.Auth/Data/AuthModelBuilderExtension.cs
- FOUND: src/GameKit.Auth/Data/AuthMigrationConstants.cs
- FOUND: src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs
- FOUND: src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs
- FOUND: src/GameKit.Auth/Migrations/20260418000000_AuthInitial.Designer.cs
- FOUND: src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs
- FOUND: tests/GameKit.Auth.Integration.Tests/AuthAdvisoryLockKeyTests.cs
- FOUND: tests/GameKit.Auth.Integration.Tests/AuthSchemaTests.cs
- FOUND: tests/GameKit.Auth.Integration.Tests/PlayerIdentityUniqueTests.cs

**Commits verified in git log:**
- FOUND: a1c52fd (Task 1 — entities + EF configs + AuthModelBuilderExtension)
- FOUND: aec8623 (Task 2 — AuthMigrationConstants + AuthDesignTimeDbContextFactory + AuthAdvisoryLockKeyTests)
- FOUND: 4831c84 (Task 3 — AuthInitial migration + AuthSchemaTests + PlayerIdentityUniqueTests)

---
*Phase: 02-authentication*
*Completed: 2026-04-18*
