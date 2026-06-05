---
phase: 02
phase_name: Authentication
mapped: 2026-04-18
status: Ready for planning
---

# Phase 2: Authentication — Pattern Map

**Mapped:** 2026-04-18
**Files analyzed:** ~60 (src/GameKit.Auth/**, tests/**, Directory.Packages.props, sample)
**Analogs found:** 48 exact/role-match / 60 total. 12 files have NO analog (JWT issuance, OAuth, BCrypt, rate-limit registration, egress handler, WireMock fixture) — those fall back to RESEARCH.md §6/§8/§14 sketches.

All Phase-1 analog paths in this document are absolute under `/home/noah/Desktop/projects/gamekit/`.

---

## File Classification

### src/GameKit.Auth/ — new files

| Target File | Role | Data Flow | Closest Analog | Match Quality | RESEARCH § |
|-------------|------|-----------|----------------|---------------|------------|
| `src/GameKit.Auth/GameKitAuthOptions.cs` | options class (root) | config-in | `src/GameKit.Core/GameKitOptions.cs` | exact | §5, §8.6 |
| `src/GameKit.Auth/JwtOptions.cs` | options class (nested) | config-in | `src/GameKit.Core/GameKitOptions.cs` (simple POCO) | role-match | §6.2, §8.9 |
| `src/GameKit.Auth/SteamOptions.cs` | options class (nested) | config-in | `src/GameKit.Core/GameKitOptions.cs` | role-match | §5, §8.2 |
| `src/GameKit.Auth/DiscordOptions.cs` | options class (nested) | config-in | `src/GameKit.Core/GameKitOptions.cs` | role-match | §5, §8.3 |
| `src/GameKit.Auth/PasswordOptions.cs` | options class (nested) | config-in | `src/GameKit.Core/GameKitOptions.cs` | role-match | §5 |
| `src/GameKit.Auth/AssemblyInfo.cs` | assembly metadata | n/a | `src/GameKit.Core/AssemblyInfo.cs` | exact | n/a |
| `src/GameKit.Auth/GameKit.Auth.csproj` | csproj (already scaffolded) | n/a | `src/GameKit.Core/GameKit.Core.csproj` | exact | §4 |
| `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` | fluent builder extension | DI registration | `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` | exact (adapted for `this IGameKitBuilder`) | §6.2 |
| `src/GameKit.Auth/Builder/AuthServiceCollectionExtensions.cs` | internal DI plumbing | DI registration | `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` | exact | §6.2 |
| `src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs` | middleware wiring | pipeline ordering | `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` | exact | §8.1 |
| `src/GameKit.Auth/Data/AuthModelBuilderExtension.cs` | IModelBuilderExtension impl | EF model contribution | `src/GameKit.Core/Data/GameKitModelCustomizer.cs` + `src/GameKit.Core/Data/IModelBuilderExtension.cs` docstring | exact | §14.3 |
| `src/GameKit.Auth/Data/AuthMigrationConstants.cs` | constants class | n/a | `src/GameKit.Core/Data/GameKitMigrationConstants.cs` | exact (separate lock key) | §5, §8.12 #9 |
| `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` | IDesignTimeDbContextFactory | CLI-bootstrapped | `src/GameKit.Core/Data/CoreDesignTimeFactory.cs` | exact | §14.4 |
| `src/GameKit.Auth/Data/Configurations/PlayerIdentityConfiguration.cs` | EF config | model mapping | `src/GameKit.Core/Data/Configurations/PlayerConfiguration.cs` + `SessionParticipantConfiguration.cs` | exact | §14.2 |
| `src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs` | EF config | model mapping | `src/GameKit.Core/Data/Configurations/PlayerConfiguration.cs` | exact | §14.x |
| `src/GameKit.Auth/Data/Configurations/RefreshTokenConfiguration.cs` | EF config | model mapping | `src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs` + `SessionParticipantConfiguration.cs` | exact | §14.1 |
| `src/GameKit.Auth/Entities/PlayerIdentity.cs` | entity (aggregate) | ORM data-row | `src/GameKit.Core/Entities/Player.cs` | exact | §14.2 |
| `src/GameKit.Auth/Entities/PlayerCredential.cs` | entity | ORM data-row | `src/GameKit.Core/Entities/Player.cs` | exact | §14.x |
| `src/GameKit.Auth/Entities/RefreshToken.cs` | entity | ORM data-row | `src/GameKit.Core/Entities/AdminAuditLog.cs` | exact | §14.1 |
| `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs` | EF migration | DDL apply | `src/GameKit.Core/Migrations/20260415000000_CoreInitial.cs` | exact (EF CLI generated) | §5 |
| `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.Designer.cs` | EF migration designer | metadata | `src/GameKit.Core/Migrations/20260415000000_CoreInitial.Designer.cs` | exact (CLI generated) | — |
| `src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs` | EF model snapshot (per-pkg) | metadata | `src/GameKit.Core/Migrations/GameKitDbContextModelSnapshot.cs` | exact (Auth tables only) | §8.12 #1 |
| `src/GameKit.Auth/Http/AuthEndpoints.cs` | minimal-API endpoint group | request-response | `src/GameKit.Core/Http/PlayerEndpoints.cs` + sample `DemoEndpoints.cs` | exact | §14.5 |
| `src/GameKit.Auth/Http/Contracts/LoginRequest.cs` (+ siblings) | DTO records | transport | `samples/TicTacToeDuel/Http/DemoContracts.cs` | role-match | §14.5 |
| `src/GameKit.Auth/Http/Validators/*.cs` | FluentValidation validators | validation | **NO ANALOG** — FluentValidation first use | new | §14.6 |
| `src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs` | endpoint filter | cross-cutting | **NO ANALOG** — first endpoint filter | new | §14.6 |
| `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitPolicies.cs` | rate-limit registration | middleware config | `src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs` (names only) | role-match (no concrete limiter yet) | §8.7 |
| `src/GameKit.Auth/Providers/IOAuthProvider.cs` | strategy interface | contract | `src/GameKit.Core/Data/IModelBuilderExtension.cs` (pluggable-strategy pattern) | role-match | §6.2 step 3 |
| `src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs` | strategy impl | request-response | **NO ANALOG** — net-new | new | §8.2 |
| `src/GameKit.Auth/Providers/Steam/SteamOpenIdVerifier.cs` | HTTP client adapter | request-response | **NO ANALOG** — net-new | new | §8.2 |
| `src/GameKit.Auth/Providers/Steam/SteamConstants.cs` | constants class | n/a | `src/GameKit.Core/Data/GameKitMigrationConstants.cs` | role-match | §8.2 |
| `src/GameKit.Auth/Providers/Steam/SteamBackchannelPostConfigure.cs` | IPostConfigureOptions | DI-time config | **NO ANALOG** — net-new | new | §6.3 |
| `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` | strategy impl | request-response | **NO ANALOG** — net-new | new | §8.3 |
| `src/GameKit.Auth/Providers/Discord/DiscordBackchannelPostConfigure.cs` | IPostConfigureOptions | DI-time config | **NO ANALOG** — net-new | new | §6.3 |
| `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs` | strategy impl | request-response | `src/GameKit.Core/Services/GdprDeleteService.cs` (svc + db + clock + ids pattern) | role-match | §5, §6.2 |
| `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` | strategy impl | request-response | `src/GameKit.Core/Services/GdprDeleteService.cs` | role-match | §5 |
| `src/GameKit.Auth/Services/IJwtIssuer.cs` + `JwtIssuer.cs` | auth service | transform | **NO ANALOG** — net-new | new | §8.9 |
| `src/GameKit.Auth/Services/IPasswordHasher.cs` + `BCryptPasswordHasher.cs` | auth service | transform | **NO ANALOG** — net-new | new | §7, §4 |
| `src/GameKit.Auth/Services/IRefreshTokenService.cs` + `RefreshTokenService.cs` | auth service (tx) | CRUD + transform | `src/GameKit.Core/Services/GdprDeleteService.cs` (SERIALIZABLE tx pattern) | role-match | §6.4, §8.4 |
| `src/GameKit.Auth/Services/IIsGuestResolver.cs` + `IsGuestResolver.cs` | auth service | query | `src/GameKit.Core/Services/PlayerDisplayNameResolver.cs` | role-match | §1 D-13 |
| `src/GameKit.Auth/Services/IGuestUpgradeService.cs` + `GuestUpgradeService.cs` | auth service (tx) | CRUD-serializable | `src/GameKit.Core/Services/GdprDeleteService.cs` | **exact** (SERIALIZABLE tx + audit log) | §6.5, §8.5 |
| `src/GameKit.Auth/Services/IIdentityLinker.cs` + `IdentityLinker.cs` | auth service (tx) | CRUD-serializable | `src/GameKit.Core/Services/GdprDeleteService.cs` | **exact** | §6.5, §8.5 |
| `src/GameKit.Auth/Services/IExternalIdHasher.cs` + `ExternalIdHasher.cs` | utility service | transform | `src/GameKit.Core/Services/UuidV7IdGenerator.cs` (trivial singleton) | role-match | §1 D-11 |
| `src/GameKit.Auth/Services/IAuthAuditWriter.cs` + `AuthAuditWriter.cs` | auth service | CRUD-insert | inline pattern inside `src/GameKit.Core/Services/GdprDeleteService.cs` (AdminAuditLog write) | exact | §8.10 |
| `src/GameKit.Auth/Egress/EgressAllowListHandler.cs` | DelegatingHandler | middleware | **NO ANALOG** — net-new HTTP handler (Core has zero HttpClient) | new | §8.6 |
| `src/GameKit.Auth/Egress/EgressViolationException.cs` | exception type | — | `tests/GameKit.TestFixtures/EgressViolationException.cs` | **exact** (promote from test fixture to product OR reuse the fixture type) | §8.6 |
| `src/GameKit.Auth/Egress/DefaultAllowedHosts.cs` | constants class | n/a | `src/GameKit.Core/Data/GameKitMigrationConstants.cs` | role-match | §8.6 |

### tests/ — new test files

| Target File | Role | Data Flow | Closest Analog | Match Quality | RESEARCH § |
|-------------|------|-----------|----------------|---------------|------------|
| `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` | unit csproj | — | `tests/GameKit.Core.Tests/GameKit.Core.Tests.csproj` | exact | §2 |
| `tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs` | unit test | — | `tests/GameKit.Core.Tests/Services/UuidV7IdGeneratorTests.cs` | role-match | §2 Layer 1 |
| `tests/GameKit.Auth.Tests/JwtIssuerTests.cs` | unit test | — | `tests/GameKit.Core.Tests/Services/HttpContextCurrentPlayerTests.cs` | role-match | §2 Layer 1 |
| `tests/GameKit.Auth.Tests/RefreshTokenServiceTests.cs` | unit test | — | `tests/GameKit.Core.Tests/Services/GdprDeleteServiceTests.cs` | role-match | §6.4 |
| `tests/GameKit.Auth.Tests/EgressAllowListHandlerTests.cs` | unit test | — | `tests/GameKit.Core.Integration.Tests/EgressGuardTests.cs` | role-match | §8.6 |
| `tests/GameKit.Auth.Tests/AuthBuilderTests.cs` | unit test | — | `tests/GameKit.Core.Tests/Builder/GameKitBuilderTests.cs` | **exact** | §6.2 |
| `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj` | integration csproj | — | `tests/GameKit.Core.Integration.Tests/GameKit.Core.Integration.Tests.csproj` | exact | §2 |
| `tests/GameKit.Auth.Integration.Tests/AuthMigrationTests.cs` | integration test | Testcontainers | `tests/GameKit.Core.Integration.Tests/MigrationDeterminismTests.cs` + `MigrationHistoryIsolationTests.cs` | **exact** | §2 Layer 2 |
| `tests/GameKit.Auth.Integration.Tests/AuthAdvisoryLockKeyTests.cs` | integration test | Testcontainers | `tests/GameKit.Core.Integration.Tests/AdvisoryLockKeyTests.cs` | **exact** | §8.12 #9 |
| `tests/GameKit.Auth.Integration.Tests/PlayerIdentityUniqueTests.cs` | integration test | Testcontainers | `tests/GameKit.Core.Integration.Tests/RoleIsolationTests.cs` (Npgsql + SqlState assertion) | role-match | §2 Layer 2 |
| `tests/GameKit.Auth.Integration.Tests/GuestUpgradeRaceTests.cs` | integration test | Testcontainers | `tests/GameKit.Core.Integration.Tests/GdprDeleteTombstoneTests.cs` (SERIALIZABLE + seed + assert) | role-match | §6.5, §8.5 |
| `tests/GameKit.Auth.Integration.Tests/RefreshRotationTests.cs` | integration test | Testcontainers | `tests/GameKit.Core.Integration.Tests/GdprDeleteTombstoneTests.cs` | role-match | §6.4 |
| `tests/GameKit.Auth.Integration.Tests/RefreshTokenRoleIsolationTests.cs` | integration test | Testcontainers | `tests/GameKit.Core.Integration.Tests/RoleIsolationTests.cs` | **exact** | §8.12 #2 |
| `tests/GameKit.Auth.Integration.Tests/SteamLoginTests.cs` (+ forgery) | e2e test | WireMock+TC | **NO ANALOG** (WireMock first use) | new | §8.8 |
| `tests/GameKit.Auth.Integration.Tests/DiscordLoginTests.cs` | e2e test | WireMock+TC | **NO ANALOG** | new | §8.8 |
| `tests/GameKit.Auth.Integration.Tests/RateLimitTests.cs` | e2e test | WebAppFactory | **NO ANALOG** | new | §8.7 |
| `tests/GameKit.TestFixtures/WireMockFixture.cs` | test fixture | IAsyncLifetime | `tests/GameKit.TestFixtures/PostgresFixture.cs` + `RedisFixture.cs` | **exact** (IAsyncLifetime shell) | §8.8 |
| `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs` | test fixture (composite) | IAsyncLifetime | `tests/GameKit.TestFixtures/CollectionDefinitions.cs` (PostgresAndRedisCollection) | role-match | §8.8 |
| `tests/GameKit.TestFixtures/CollectionDefinitions.cs` (modified) | collection def | xUnit | existing file — add `[CollectionDefinition("Auth")]` | exact | §8.8 |

### Root / sample — modified files

| Target File | Role | Data Flow | Closest Analog | Match Quality | RESEARCH § |
|-------------|------|-----------|----------------|---------------|------------|
| `Directory.Packages.props` (modified) | NuGet central pin | — | existing file — append new PackageVersion entries | exact | §4 |
| `samples/TicTacToeDuel/Program.cs` (modified) | sample Program | DI wiring | existing `samples/TicTacToeDuel/Program.cs` — add `.AddAuth(...)` + `app.UseGameKitAuth()` + `app.MapAuth()` | exact | §6.2 |
| `samples/TicTacToeDuel/Http/DemoEndpoints.cs` (modified) | sample endpoints | request-response | existing file — delete `/demo/players/register` (replaced by `/auth/register`) | exact | Phase 1 demo carve-out |
| `samples/TicTacToeDuel/wwwroot/index.html` (modified) | sample SPA | client | existing file — add fetch + localStorage + X-GameKit-Device | role-match | §8.8, §8.12 #12 |
| `samples/TicTacToeDuel/appsettings.Development.json` (modified) | sample config | — | existing — add Jwt:* and Steam:*/Discord:* keys | role-match | §6.2 |
| `src/GameKit.Auth/GameKit.Auth.csproj` (modified) | csproj | — | existing scaffold (1-liner) — extend with package refs | exact | §4 |
| `src/GameKit.Auth/AssemblyInfo.cs` (modified) | assembly metadata | — | `src/GameKit.Core/AssemblyInfo.cs` | exact | — |

---

## Pattern Assignments

### `src/GameKit.Auth/Entities/PlayerIdentity.cs` + `PlayerCredential.cs` + `RefreshToken.cs` (entities, ORM data-row)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Entities/Player.cs` (simple POCO) and `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Entities/AdminAuditLog.cs` (bigger surface, JsonDocument, nullable fields).

**SPDX header pattern** (`Player.cs` lines 1-2):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

**Class shape — sealed, public, settable properties with XML doc** (`Player.cs` lines 18-27):
```csharp
public sealed class Player
{
    /// <summary>Player id — UUIDv7 (time-ordered) generated by the registered <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>Public display name. Uniqueness is not enforced at the schema level (v1 decision).</summary>
    public required string DisplayName { get; set; }

    /// <summary>UTC timestamp at which the player row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
```

**JsonDocument metadata field** (`Player.cs` lines 41-46):
```csharp
/// <summary>
/// Sparse, non-relational metadata (JSONB). Per CORE-17, this column is for infrequently-written,
/// non-queryable game-specific annotations...
/// </summary>
public JsonDocument? Metadata { get; set; }
```

**Nullable-after-event DateTimeOffset pattern** (`AdminAuditLog.cs` lines 18-19, `Player.cs` line 30):
```csharp
public Guid? ActorId { get; set; }
public DateTimeOffset? LastSeenAt { get; set; }
```

Apply: `RefreshToken` follows `AdminAuditLog` shape (multiple nullable timestamps + `Provider` string); `PlayerIdentity` follows `Player` shape (required string fields + JsonDocument metadata); `PlayerCredential` is the smallest (PK=PlayerId, PasswordHash, UpdatedAt).

---

### `src/GameKit.Auth/Data/Configurations/PlayerIdentityConfiguration.cs` (EF config)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Data/Configurations/SessionParticipantConfiguration.cs` (FK to Player + unique index + SET NULL vs CASCADE choices).

**Imports + class shape** (`SessionParticipantConfiguration.cs` lines 1-8, 18-22):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameKit.Core.Data.Configurations;

internal sealed class SessionParticipantConfiguration : IEntityTypeConfiguration<SessionParticipant>
{
    public void Configure(EntityTypeBuilder<SessionParticipant> b)
    {
        b.ToTable("session_participants");
```

**Id + ValueGeneratedNever pattern** (`PlayerConfiguration.cs` lines 18-19):
```csharp
b.HasKey(p => p.Id);
b.Property(p => p.Id).ValueGeneratedNever(); // UUIDv7 assigned by IIdGenerator at service layer
```

**FK to Player — the CASCADE variant** (differs from Core's SET NULL; for Auth, identities/credentials/refreshes have no meaning without their player, so CASCADE — per CONTEXT `<code_context>` "player_identities.player_id FK will use ON DELETE CASCADE"):
```csharp
b.HasOne<Player>()
    .WithMany()
    .HasForeignKey(p => p.PlayerId)
    .OnDelete(DeleteBehavior.Cascade);   // <-- CASCADE, not SetNull
```

(Contrast with `SessionParticipantConfiguration.cs` line 48 `OnDelete(DeleteBehavior.SetNull)` for the GDPR fan-out rule on Core tables.)

**Unique composite index** (anchor for D-14 race) — reuse the `HasIndex(...).IsUnique()` pattern. There is no existing analog for a multi-column UNIQUE in Core, but `AdminAuditLogConfiguration.cs` lines 31-33 show the multi-column (non-unique) index shape we extend:
```csharp
b.HasIndex(a => new { a.TargetType, a.TargetId });   // non-unique pattern to extend with .IsUnique()
```

---

### `src/GameKit.Auth/Data/Configurations/RefreshTokenConfiguration.cs` (EF config)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs` (multi-index table, non-FK-heavy).

**Full pattern to mirror** (`AdminAuditLogConfiguration.cs` lines 1-35):
```csharp
internal sealed class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> b)
    {
        b.ToTable("admin_audit_log");

        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();

        b.Property(a => a.Action).IsRequired().HasMaxLength(64);
        b.Property(a => a.TargetType).IsRequired().HasMaxLength(64);
        b.Property(a => a.Before).HasColumnType("jsonb");
        b.Property(a => a.CreatedAt).IsRequired();

        b.HasIndex(a => a.CreatedAt);
        b.HasIndex(a => new { a.TargetType, a.TargetId });
        b.HasIndex(a => a.ActorId);
    }
}
```

Apply: `RefreshToken` uses `HasIndex(r => r.TokenHash).IsUnique()` + `HasIndex(r => new { r.PlayerId, r.RevokedAt })` + `HasIndex(r => r.FamilyId)` + `HasOne<Player>()...OnDelete(Cascade)` per RESEARCH §14.1.

---

### `src/GameKit.Auth/Data/AuthModelBuilderExtension.cs` (IModelBuilderExtension impl)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Data/IModelBuilderExtension.cs` (contract) + the auto-registration pattern in `GameKitDbContext.OnModelCreating`.

**Contract docstring** (`IModelBuilderExtension.cs` lines 13-21):
```csharp
/// <remarks>
/// Register at startup via:
/// <code>
/// services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IModelBuilderExtension, AuthModelBuilderExtension&gt;());
/// </code>
/// Implementations must only ADD entities or ADD FK columns referencing existing Core entities.
/// Never modify Core-owned entity configurations...
/// </remarks>
```

**Apply pattern** (ported from `GameKitDbContext.cs` line 47 `ApplyConfigurationsFromAssembly` — but since AuthModelBuilderExtension only owns 3 configs, applying each by name is clearer per RESEARCH §14.3):
```csharp
internal sealed class AuthModelBuilderExtension : IModelBuilderExtension
{
    public void ApplyTo(ModelBuilder mb)
    {
        mb.ApplyConfiguration(new PlayerIdentityConfiguration());
        mb.ApplyConfiguration(new PlayerCredentialConfiguration());
        mb.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
```

---

### `src/GameKit.Auth/Data/AuthMigrationConstants.cs` (constants)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Data/GameKitMigrationConstants.cs` (full file, 38 lines).

**Full pattern to copy with key swaps** (`GameKitMigrationConstants.cs` lines 10-38):
```csharp
public static class GameKitMigrationConstants
{
    public const string SchemaName = "gamekit";

    /// <summary>
    /// Per-package migrations history table for <c>GameKit.Core</c>. Per PITFALLS.md #3,
    /// each GameKit package uses its own history table (naming convention: <c>__ef_migrations_{package}</c>)
    /// so cross-package model snapshots do not collide.
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_core";

    /// <summary>
    /// Pinned Postgres advisory-lock key for migration serialization. Value is
    /// <c>SELECT hashtext('gamekit.migrations')::bigint</c> — deterministic across Postgres versions
    /// per Postgres documented behavior...
    /// </summary>
    public const long AdvisoryLockKey = 1800940027L;
}
```

Auth version (RESEARCH §14.4, §8.12 #9):
- `MigrationsHistoryTable = "__ef_migrations_auth"`
- `AdvisoryLockKey = hashtext('gamekit.auth.migrations')::bigint` — to be computed against a live Postgres 17.9 container in 02-02 test, then pinned as a literal `long`. **Distinct** from Core's `1800940027L` to avoid startup deadlock.

---

### `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` (IDesignTimeDbContextFactory)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Data/CoreDesignTimeFactory.cs` (full file, 46 lines).

**Full pattern to copy with 2 name swaps** (`CoreDesignTimeFactory.cs` lines 21-45):
```csharp
public sealed class CoreDesignTimeFactory : IDesignTimeDbContextFactory<GameKitDbContext>
{
    public GameKitDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";

        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKitMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer, GameKitModelCustomizer>();

        // Core-only snapshot at design time — sibling extensions are absent by construction.
        return new GameKitDbContext(optionsBuilder.Options);
    }
}
```

Auth swaps:
- Line 33: `typeof(GameKitDbContext).Assembly.FullName` → `typeof(AuthDesignTimeDbContextFactory).Assembly.FullName` (per RESEARCH §14.4) — tells EF to emit Auth migrations into `GameKit.Auth.dll`.
- Line 34-36: swap `GameKitMigrationConstants` → `AuthMigrationConstants` for history table, keep `GameKitMigrationConstants.SchemaName` for the shared schema.
- Keep `ReplaceService<IModelCustomizer, GameKitModelCustomizer>()` — the customizer iterates all registered `IModelBuilderExtension`s; at design time with no service provider wired, only Auth's extension is picked up (which is what we want — isolated Auth-only snapshot).

---

### `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` (fluent extension)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` (full file, 76 lines).

**Method signature pattern** — but attached to `this IGameKitBuilder` rather than `this IServiceCollection` (per CONTEXT `<code_context>` "extend with `.AddAuth(opts => ...)` fluent method"):

**Argument-null guard + options construct + validate + register** (`GameKitServiceCollectionExtensions.cs` lines 27-42):
```csharp
public static IGameKitBuilder AddGameKit(
    this IServiceCollection services,
    Action<GameKitOptions> configure)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configure);

    var opts = new GameKitOptions();
    configure(opts);

    if (string.IsNullOrWhiteSpace(opts.ConnectionString))
        throw new ArgumentException(
            $"{nameof(GameKitOptions)}.{nameof(GameKitOptions.ConnectionString)} must be set.",
            nameof(configure));

    services.AddSingleton(opts);
```

Auth adaptation (RESEARCH §6.2 lines 434-524 gives the full sketch); key adaptations:
- Signature: `public static IGameKitBuilder AddAuth(this IGameKitBuilder builder, Action<GameKitAuthOptions> configure)` — returns builder for further chaining.
- Validate: throw on missing `opts.Jwt.Issuer`, `opts.Jwt.Audience`, and file-unreadable `opts.Jwt.PrivateKeyPemPath` (fail-fast per §8.12 #11).
- `builder.Services.AddSingleton(opts)` mirrors Core line 42.

**Singleton / scoped lifetime split** (`GameKitServiceCollectionExtensions.cs` lines 44-59):
```csharp
services.AddSingleton<IClock, SystemClock>();
services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();

services.AddHttpContextAccessor();
services.AddScoped<ICurrentPlayer, HttpContextCurrentPlayer>();

services.AddAuthorization();

services.AddMemoryCache();
services.AddScoped<IPlayerDisplayNameResolver, PlayerDisplayNameResolver>();

services.AddScoped<IGdprDeleteService, GdprDeleteService>();

services.AddSingleton<IGameKitRateLimitPolicies, GameKitRateLimitPolicies>();
```

Apply: Auth registers `IPasswordHasher` as **Singleton** (stateless), `IExternalIdHasher` as **Singleton**, and `IJwtIssuer`/`IRefreshTokenService`/`IIsGuestResolver`/`IGuestUpgradeService`/`IIdentityLinker`/`IAuthAuditWriter` as **Scoped** (they touch `GameKitDbContext`, which is scoped).

**IModelBuilderExtension registration** (not in Core itself, but the contract docstring — `IModelBuilderExtension.cs` lines 16):
```csharp
services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());
```

This is the required idiom — Core's `GameKitModelCustomizer` resolves `IEnumerable<IModelBuilderExtension>`, so `TryAddEnumerable` (not `AddSingleton`) avoids duplicate registration under repeat `.AddAuth(...)` calls.

---

### `src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs` (middleware wiring)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` (full file).

**Extension signature + null-guard + body** (`GameKitApplicationBuilderExtensions.cs` lines 30-64):
```csharp
public static IApplicationBuilder UseGameKit(this IApplicationBuilder app)
{
    ArgumentNullException.ThrowIfNull(app);

    var opts = app.ApplicationServices.GetRequiredService<GameKitOptions>();
    if (opts.AutoMigrate)
    {
        using var scope = app.ApplicationServices.CreateScope();
        // ... MigrationRunner.MigrateWithLockAsync ...
    }

    app.UseAuthorization();
    return app;
}
```

**Endpoint mapping sibling** (`GameKitApplicationBuilderExtensions.cs` lines 67-72):
```csharp
public static IEndpointRouteBuilder MapGameKit(this IEndpointRouteBuilder routes)
{
    ArgumentNullException.ThrowIfNull(routes);
    routes.MapPlayers();
    return routes;
}
```

Auth adaptation (RESEARCH §8.1, §8.12 #6 — middleware ordering fix):
```csharp
public static IApplicationBuilder UseGameKitAuth(this IApplicationBuilder app)
{
    ArgumentNullException.ThrowIfNull(app);
    app.UseAuthentication();          // MUST be called BEFORE UseAuthorization from UseGameKit()
    return app;
}

public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder routes)
{
    ArgumentNullException.ThrowIfNull(routes);
    var policies = routes.ServiceProvider.GetRequiredService<IGameKitRateLimitPolicies>();
    routes.MapAuthEndpoints(policies);    // implemented in Http/AuthEndpoints.cs
    return routes;
}
```

Also: **Auth migrations must run under its own advisory lock.** Reuse `MigrationRunner.MigrateWithLockAsync` pattern from `MigrationRunner.cs` — but parameterize on lock key so Auth can pass `AuthMigrationConstants.AdvisoryLockKey`. Alternatively (simpler): `UseGameKit()` applies migrations for the shared `GameKitDbContext` once, which covers both Core and Auth tables because both packages register migrations into the same context. The Auth history table (`__ef_migrations_auth`) isolates the migration records; the advisory lock key is shared with Core. **Planner 02-03 decides** — either approach is defensible; RESEARCH §8.12 #9 prefers a distinct Auth lock key, which means Auth must run its own `MigrateWithLockAsync` pass.

---

### `src/GameKit.Auth/Http/AuthEndpoints.cs` (minimal-API endpoint group)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Http/PlayerEndpoints.cs` + `/home/noah/Desktop/projects/gamekit/samples/TicTacToeDuel/Http/DemoEndpoints.cs`.

**Group + route-group + tags + require-auth pattern** (`PlayerEndpoints.cs` lines 14-53):
```csharp
public static class PlayerEndpoints
{
    public static RouteGroupBuilder MapPlayers(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/players").WithTags("GameKit.Core");

        group.MapGet("/", async (GameKitDbContext db, int skip, int take, CancellationToken ct) =>
        {
            // ... query ...
            return Results.Ok(rows);
        })
        .RequireAuthorization();

        return group;
    }
}
```

**Minimal-API handler with body parameter + validation + DB + error handling** (`DemoEndpoints.cs` lines 44-79):
```csharp
private static async Task<IResult> RegisterPlayerAsync(
    RegisterPlayerRequest req,
    GameKitDbContext db,
    IClock clock,
    IIdGenerator ids,
    CancellationToken ct)
{
    if (req is null || string.IsNullOrWhiteSpace(req.DisplayName))
        return Results.BadRequest(new { error = "displayName is required" });

    var id = ids.NewId();
    db.Players.Add(new Player { Id = id, DisplayName = name, CreatedAt = clock.UtcNow });

    try
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
    catch (DbUpdateException ex)
    {
        return Results.Problem(
            title: "failed to register player",
            detail: ex.InnerException?.Message ?? ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Ok(new RegisterPlayerResponse(id, name));
}
```

Apply: Auth endpoints follow RESEARCH §14.5 — `/auth/login/{provider}`, `/auth/refresh`, `/auth/register`, `/auth/logout`, `/auth/logout/all`, `/auth/me`, `/auth/challenge/{provider}` (GET 302), `/auth/callback/{provider}` (GET), `/auth/link/{provider}`. Use `.AddEndpointFilter<ValidationEndpointFilter<T>>()` per handler and `.RequireRateLimiting(policies.AuthLogin)` per RESEARCH §8.7. `/auth/link`, `/auth/logout`, `/auth/logout/all`, `/auth/me` all carry `.RequireAuthorization()`.

---

### `src/GameKit.Auth/Http/Contracts/*.cs` (DTOs)

**Analog:** `/home/noah/Desktop/projects/gamekit/samples/TicTacToeDuel/Http/DemoContracts.cs` (read selectively for the `record` style).

Let me read the DemoContracts file for exact shape.

See RESEARCH §14.5 for `TokenResponse`, `LoginRequest`, `RefreshRequest`, `RegisterRequest`, `AuthErrorResponse`. All are `public sealed record` types with primary-constructor properties. SPDX + copyright header per LicenseHeaderTests.

---

### `src/GameKit.Auth/Services/IRefreshTokenService.cs` + `RefreshTokenService.cs`, `IGuestUpgradeService.cs` + `GuestUpgradeService.cs`, `IIdentityLinker.cs` + `IdentityLinker.cs` (transactional auth services)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Services/GdprDeleteService.cs` (full file, 85 lines) — the pattern for SERIALIZABLE transactional services with audit log + entity mutation.

**Full transactional service class shape** (`GdprDeleteService.cs` lines 16-85):
```csharp
internal sealed class GdprDeleteService : IGdprDeleteService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public GdprDeleteService(GameKitDbContext ctx, IClock clock, IIdGenerator ids)
    {
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
    }

    public async Task DeletePlayerAsync(Guid playerId, Guid? actorId, string reason, CancellationToken cancellationToken = default)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);

        // Snapshot the player state BEFORE deletion for the audit row.
        var snapshot = await _ctx.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => new { p.Id, p.DisplayName, p.CreatedAt, p.IsBanned })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
            throw new PlayerNotFoundException(playerId);

        var before = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));

        _ctx.AdminAuditLog.Add(new AdminAuditLog
        {
            Id = _ids.NewId(),
            ActorId = actorId,
            Action = "gdpr.delete",
            TargetType = "player",
            TargetId = playerId,
            Before = before,
            After = null,
            Reason = reason,
            CreatedAt = _clock.UtcNow,
        });
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // ... mutation ...

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

Apply everywhere:
- **Constructor DI**: `(GameKitDbContext ctx, IClock clock, IIdGenerator ids, ...)`. Add `IAuthAuditWriter` and `IPasswordHasher` as needed.
- **Transaction open**: `await using var tx = await _ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)` — but `RefreshTokenService` uses `ReadCommitted` per RESEARCH §6.4 (row-level UPDATE semantics are sufficient there; SERIALIZABLE only for guest-upgrade / identity-link per §6.5).
- **40001 retry + 23505 hard-fail branches** (RESEARCH §6.5 + §8.5): wrap in a retry loop up to 3 attempts, catch `DbUpdateException ex when ex.InnerException is PostgresException pg`, branch on `pg.SqlState == "40001"` vs `pg.SqlState == "23505"`. Core's `GdprDeleteService` does NOT have this branching — it's Auth-specific.
- **Audit log write** follows the exact `_ctx.AdminAuditLog.Add(new AdminAuditLog { ... })` pattern; the action strings come from RESEARCH §8.10 table.
- **ConfigureAwait(false)**: every `await` in `GdprDeleteService` uses it — match that style.

---

### `src/GameKit.Auth/Services/IAuthAuditWriter.cs` + `AuthAuditWriter.cs` (CRUD-insert writer)

**Analog:** inline pattern inside `GdprDeleteService.cs` lines 53-69.

**Inline audit-write block to extract** (`GdprDeleteService.cs` lines 57-68):
```csharp
_ctx.AdminAuditLog.Add(new AdminAuditLog
{
    Id = _ids.NewId(),
    ActorId = actorId,
    Action = "gdpr.delete",
    TargetType = "player",
    TargetId = playerId,
    Before = before,
    After = null,
    Reason = reason,
    CreatedAt = _clock.UtcNow,
});
await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
```

Wrap as `Task WriteAsync(string action, Guid? actorId, Guid? targetId, string targetType, object? after, string? reason, CancellationToken ct)`; serialize `after` to `JsonDocument` via `JsonDocument.Parse(JsonSerializer.Serialize(after))` per Core's pattern. Auth writes 10 distinct action types per RESEARCH §8.10.

Caveat (RESEARCH §8.10 `fingerprint_hint`): never pass raw device UUIDs to `WriteAsync`; hash first via `IExternalIdHasher` or equivalent.

---

### `src/GameKit.Auth/Services/IIsGuestResolver.cs` + `IsGuestResolver.cs` (query service)

**Analog:** `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Services/PlayerDisplayNameResolver.cs` (not read above; pattern implied by DI registration at `GameKitServiceCollectionExtensions.cs:57`).

**Pattern** — a scoped service with a single read-only query:
```csharp
internal sealed class IsGuestResolver : IIsGuestResolver
{
    private readonly GameKitDbContext _ctx;
    public IsGuestResolver(GameKitDbContext ctx) => _ctx = ctx;

    public async Task<bool> IsGuestAsync(Guid playerId, CancellationToken ct)
    {
        var hasIdentity = await _ctx.Set<PlayerIdentity>().AnyAsync(i => i.PlayerId == playerId, ct);
        if (hasIdentity) return false;
        var hasCredential = await _ctx.Set<PlayerCredential>().AnyAsync(c => c.PlayerId == playerId, ct);
        return !hasCredential;
    }
}
```

Per D-13: call this **inside the same transaction** as JWT issuance. `JwtIssuer.IssueAsync(...)` takes `IIsGuestResolver` and calls it before building claims.

---

### `src/GameKit.Auth/Egress/EgressAllowListHandler.cs` (DelegatingHandler)

**Analog:** **None in src/**. Phase 1 Core has zero `HttpClient` references (enforced by `EgressGuardTests.Layer1_Core_Assembly_References_No_Http_Assembly`). Auth is net-new — use RESEARCH §8.6 sketch verbatim (reproduced here):

```csharp
public sealed class EgressAllowListHandler : DelegatingHandler
{
    private readonly HashSet<string> _allowed;
    public EgressAllowListHandler(GameKitAuthOptions opts) =>
        _allowed = new(opts.AllowedProviderHosts, StringComparer.OrdinalIgnoreCase);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var host = request.RequestUri?.Host;
        if (host is null || !_allowed.Contains(host))
            throw new EgressViolationException(
                $"Outbound call to '{host}' is not on the GameKit.Auth allow-list. " +
                "Add the host to GameKitAuthOptions.AllowedProviderHosts if intentional.");
        return base.SendAsync(request, ct);
    }
}
```

---

### `src/GameKit.Auth/Egress/EgressViolationException.cs`

**Analog:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.TestFixtures/EgressViolationException.cs` (full file, 28 lines).

**Full file to promote / mirror** (lines 12-27):
```csharp
public sealed class EgressViolationException : Exception
{
    public string Host { get; }
    public int Port { get; }

    public EgressViolationException(string host, int port)
        : base($"Egress violation: outbound HTTP connect attempted to {host}:{port}")
    {
        Host = host;
        Port = port;
    }
}
```

**Decision for planner:** The Auth allow-list version takes a single message string (not host+port — the DelegatingHandler has URI, not socket). **Either** promote this exception to `GameKit.Core` (breaking — test fixture consumers must re-ref Core) **or** ship a distinct `EgressViolationException` in `GameKit.Auth.Egress` namespace. RESEARCH §5 suggests reusing the fixture's; the simplest path is to ship a new Auth-namespaced exception whose ctor takes `(string message)` — the test fixture's host+port variant is preserved for its existing use.

---

### `src/GameKit.Auth/Services/IJwtIssuer.cs` + `JwtIssuer.cs`, `IPasswordHasher.cs` + `BCryptPasswordHasher.cs` (net-new, no analog)

**Use RESEARCH sketches directly:**
- `JwtIssuer` — §1 + §8.9 (`new JwtSecurityToken(opts.Jwt.Issuer, ...)`, `SigningCredentials(key, SecurityAlgorithms.RsaSha256)`, claims `sub = playerId`, `sid = familyId`, `is_guest = bool`, `provider`, + standard `jti`/`iat`/`exp`).
- `BCryptPasswordHasher` — §4 / §7 (`BCrypt.HashPassword(pw, opts.Password.BCryptWorkFactor)` / `BCrypt.Verify(pw, hash)`). Pin `BCrypt.Net-Next` 4.1.0 (RESEARCH §4 recommendation; **bump from 4.0.3 in CLAUDE.md**).

`MapInboundClaims = false` on JwtBearer so `sub` is preserved literally — the Phase 1 `HttpContextCurrentPlayer.cs` reads `user.FindFirst("gamekit_player_id")` first and falls back to `ClaimTypes.NameIdentifier`, so Auth can choose whether to emit a `gamekit_player_id` claim or rely on `sub` (RESEARCH §15 open question #6).

---

### `tests/GameKit.TestFixtures/WireMockFixture.cs` (test fixture)

**Analog:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.TestFixtures/PostgresFixture.cs` (full file, 63 lines) + `RedisFixture.cs` (46 lines).

**IAsyncLifetime shell + container start/stop** (`PostgresFixture.cs` lines 17-62):
```csharp
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string AppConnectionString { get; private set; } = string.Empty;
    // ... other connection strings ...

    public async Task InitializeAsync()
    {
        var initDir = Path.Combine(GitRootLocator.FindRepoRoot(), "docker", "postgres", "init");

        _container = new PostgreSqlBuilder("postgres:17.9")
            .WithUsername("postgres")
            .WithPassword("postgres_test")
            .WithDatabase("postgres")
            .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
            .Build();

        await _container.StartAsync();
        // ... read mapped port, build connection strings ...
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
```

Apply: `WireMockFixture` uses `WireMockServer.Start()` instead of a Testcontainers container (RESEARCH §8.8). Keep the `IAsyncLifetime` shape, expose `Server`, `SteamBaseUrl`, `DiscordBaseUrl` properties. Dispose via `Server.Stop(); Server.Dispose()`.

---

### `tests/GameKit.TestFixtures/CollectionDefinitions.cs` (modify)

**Analog (self):** `/home/noah/Desktop/projects/gamekit/tests/GameKit.TestFixtures/CollectionDefinitions.cs` (full file, 19 lines).

**Current pattern** (lines 9-19):
```csharp
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }

[CollectionDefinition("PostgresAndRedis")]
public sealed class PostgresAndRedisCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture> { }
```

Add per RESEARCH §8.8:
```csharp
[CollectionDefinition("Auth")]
public sealed class AuthCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture>,
      ICollectionFixture<WireMockFixture> { }
```

---

### `tests/GameKit.Auth.Integration.Tests/AuthMigrationTests.cs`, `AuthAdvisoryLockKeyTests.cs` (Testcontainers integration)

**Analog for migration-history test:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.Core.Integration.Tests/MigrationHistoryIsolationTests.cs` (full file, 62 lines).

**Full pattern** (lines 18-62):
```csharp
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class MigrationHistoryIsolationTests
{
    private readonly PostgresFixture _pg;
    public MigrationHistoryIsolationTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task History_Table_Is_EfMigrationsCore_In_Gamekit_Schema()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        await using var sp = services.BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(
                scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT to_regclass('gamekit.__ef_migrations_core') IS NOT NULL";
            var result = (bool)(await c.ExecuteScalarAsync() ?? false);
            Assert.True(result, "gamekit.__ef_migrations_core must exist");
        }
    }
}
```

Auth adaptation: `AddGameKit(...)` **then** `.AddAuth(...)` in the service registration; assert both `gamekit.__ef_migrations_core` AND `gamekit.__ef_migrations_auth` tables exist; assert all 3 Auth tables (`player_identities`, `player_credentials`, `refresh_tokens`) exist with the correct columns.

**Analog for advisory-lock test:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.Core.Integration.Tests/AdvisoryLockKeyTests.cs` (full file, 37 lines).

**Full pattern** (lines 22-36):
```csharp
[Fact]
public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
{
    await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT hashtext('gamekit.migrations')::bigint";

    var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

    Assert.Equal(GameKitMigrationConstants.AdvisoryLockKey, computed);
}
```

Auth version swaps `'gamekit.migrations'` → `'gamekit.auth.migrations'` and `GameKitMigrationConstants.AdvisoryLockKey` → `AuthMigrationConstants.AdvisoryLockKey`. Also asserts the two values are **distinct** (Auth != Core) per RESEARCH §8.12 #9.

---

### `tests/GameKit.Auth.Integration.Tests/GuestUpgradeRaceTests.cs`, `PlayerIdentityUniqueTests.cs`, `RefreshTokenRoleIsolationTests.cs`

**Analog for seed+tx+assert:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.Core.Integration.Tests/GdprDeleteTombstoneTests.cs` (full file, 127 lines).

**Seed pattern** (lines 47-83):
```csharp
var now = DateTimeOffset.UtcNow;
var playerA = Guid.CreateVersion7();
var sessionId = Guid.CreateVersion7();

await using (var scope = sp.CreateAsyncScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
    ctx.Players.AddRange(
        new Player { Id = playerA, DisplayName = "Alice", CreatedAt = now },
        new Player { Id = playerB, DisplayName = "Bob", CreatedAt = now });
    // ... session + participants ...
    await ctx.SaveChangesAsync();
}

// Exercise the service
await using (var scope = sp.CreateAsyncScope())
{
    var gdpr = scope.ServiceProvider.GetRequiredService<IGdprDeleteService>();
    await gdpr.DeletePlayerAsync(playerA, actorId: null, reason: "user request");
}

// Assertions in a separate scope
await using (var scope = sp.CreateAsyncScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
    Assert.Equal(0, await ctx.Players.CountAsync(p => p.Id == playerA));
    // ... count rows, check audit log ...
}
```

Apply: `GuestUpgradeRaceTests` seeds a guest player + a target Steam external_id, then fires two `IIdentityLinker.LinkAsync(...)` calls with `Task.WhenAll` or a `Barrier`. Assert: exactly one throws `IdentityAlreadyLinkedException` (or returns `LinkResult.AlreadyLinkedToOtherPlayer`), exactly one succeeds, exactly one `player_identities` row with the unique `(provider, external_id)` — per RESEARCH §6.5.

**Role-isolation analog:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.Core.Integration.Tests/RoleIsolationTests.cs` (full file, 79 lines).

**Pattern** (lines 28-52):
```csharp
[Fact]
public async Task GamekitReader_Cannot_Insert_Into_GameSessions()
{
    // ... bring up DB ...
    await using var conn = new NpgsqlConnection(_pg.ReaderConnectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText =
        """INSERT INTO gamekit.game_sessions ("Id", "State", "CreatedAt") VALUES (@id, 'Pending', now())""";
    cmd.Parameters.AddWithValue("id", Guid.NewGuid());

    var ex = await Assert.ThrowsAsync<PostgresException>(
        () => cmd.ExecuteNonQueryAsync());
    Assert.Equal("42501", ex.SqlState);
}
```

Apply: `RefreshTokenRoleIsolationTests` — gamekit_reader gets 42501 INSERT failure into `refresh_tokens`; gamekit_app succeeds. Use `Testcontainers` Postgres fixture via `[Collection("Postgres")]` (same as Core analog).

---

### `tests/GameKit.Auth.Tests/AuthBuilderTests.cs` (unit)

**Analog:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.Core.Tests/Builder/GameKitBuilderTests.cs` (full file, 221 lines — exemplary template).

**Per-service registration assertion** (`GameKitBuilderTests.cs` lines 70-116):
```csharp
[Fact]
public void AddGameKit_RegistersIClockSingleton()
{
    var services = new ServiceCollection();
    services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

    var sp = services.BuildServiceProvider();
    var clock = sp.GetService<IClock>();
    Assert.NotNull(clock);
    Assert.IsType<SystemClock>(clock);
}

[Fact]
public void AddGameKit_RegistersICurrentPlayerScoped()
{
    var services = new ServiceCollection();
    services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

    var sp = services.BuildServiceProvider();
    using var scope = sp.CreateScope();
    var player = scope.ServiceProvider.GetService<ICurrentPlayer>();
    Assert.NotNull(player);
    Assert.IsType<HttpContextCurrentPlayer>(player);
}
```

**Descriptor-level lifetime check** (`GameKitBuilderTests.cs` lines 140-148):
```csharp
[Fact]
public void AddGameKit_RegistersIGdprDeleteServiceScoped()
{
    var services = new ServiceCollection();
    services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IGdprDeleteService));
    Assert.NotNull(descriptor);
    Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
}
```

Apply: `AuthBuilderTests` asserts each of `IPasswordHasher` (Singleton), `IJwtIssuer` (Scoped), `IRefreshTokenService` (Scoped), `IIsGuestResolver` (Scoped), `IGuestUpgradeService` (Scoped), `IIdentityLinker` (Scoped), `IExternalIdHasher` (Singleton), `IAuthAuditWriter` (Scoped), plus named `HttpClient` registrations `"gamekit.auth.provider.steam"` and `"gamekit.auth.provider.discord"` resolve via `IHttpClientFactory`.

---

### `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` + `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj`

**Analog unit:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.Core.Tests/GameKit.Core.Tests.csproj` (full file, 27 lines).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <WarningsAsErrors />
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Moq" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GameKit.Core\GameKit.Core.csproj" />
    <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  </ItemGroup>
</Project>
```

Auth adaptation: swap `GameKit.Core` → `GameKit.Auth` project reference. Pick up `xunit` + `Moq` + test SDK via `tests/Directory.Build.props` inheritance (lines 13-19). Unit-test-only projects don't need `GameKit.TestFixtures` (Postgres) — only Integration does.

**Analog integration:** `/home/noah/Desktop/projects/gamekit/tests/GameKit.Core.Integration.Tests/GameKit.Core.Integration.Tests.csproj` (full file, 16 lines):
```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\GameKit.Core\GameKit.Core.csproj" />
  <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="Npgsql" />
  <PackageReference Include="StackExchange.Redis" />
  <PackageReference Include="Microsoft.Extensions.Hosting" />
</ItemGroup>
```

Auth adaptation: add `GameKit.Auth` project reference, add `WireMock.Net` and `Microsoft.AspNetCore.Mvc.Testing` PackageReferences.

---

### `Directory.Packages.props` (modify)

**Analog (self):** `/home/noah/Desktop/projects/gamekit/Directory.Packages.props` (full file, 49 lines).

**Current pin style to extend** (lines 7-39):
```xml
<ItemGroup>
  <!-- EF Core + Postgres (verified GA on net10.0 2026-04-15) -->
  <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.6" />
  <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.1" />

  <!-- DI + validation -->
  <PackageVersion Include="Scrutor" Version="7.0.0" />
  <PackageVersion Include="FluentValidation" Version="12.1.1" />
  <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
  ...
</ItemGroup>
```

Append per RESEARCH §4 (with the two pin bumps). Note: only the Discord contrib package is pinned; `AspNet.Security.OpenId.Steam` is intentionally NOT added per D-09 (in-house `SteamOpenIdVerifier` replaces the contrib Steam package):
```xml
<!-- Auth stack (Phase 2) -->
<PackageVersion Include="AspNet.Security.OAuth.Discord" Version="10.0.0" />
<PackageVersion Include="BCrypt.Net-Next" Version="4.1.0" />               <!-- bump from 4.0.3 -->
<PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.5.0" />
<PackageVersion Include="Microsoft.IdentityModel.Tokens" Version="8.x" />
<PackageVersion Include="System.IdentityModel.Tokens.Jwt" Version="8.x" />
<PackageVersion Include="WireMock.Net" Version="2.2.0" />
<PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
```

---

### `samples/TicTacToeDuel/Program.cs` (modify)

**Analog (self):** `/home/noah/Desktop/projects/gamekit/samples/TicTacToeDuel/Program.cs` (full file, 28 lines).

**Current Phase-1 Program.cs** (lines 7-28):
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("GameKit")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:GameKit");
    opts.MigrationsConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations");
    opts.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseGameKit();
app.MapGameKit();   // /api/players (RequireAuthorization — 401 in Phase 1)
app.MapDemo();      // /demo/* (anonymous — demo only)

app.Run();
```

Phase-2 modification (RESEARCH §6.2, §8.1, §8.12 #6 middleware ordering):
```csharp
builder.Services.AddGameKit(opts => { /* ... unchanged ... */ })
    .AddAuth(auth =>
    {
        auth.Jwt.Issuer              = "tic-tac-toe-duel";
        auth.Jwt.Audience            = "tic-tac-toe-duel";
        auth.Jwt.PrivateKeyPemPath   = builder.Configuration["GameKit:Auth:Jwt:PrivateKeyPemPath"]!;
        auth.Jwt.PublicKeyPemPath    = builder.Configuration["GameKit:Auth:Jwt:PublicKeyPemPath"]!;
        auth.Steam.ApiKey            = builder.Configuration["GameKit:Auth:Steam:ApiKey"];
        auth.Discord.ClientId        = builder.Configuration["GameKit:Auth:Discord:ClientId"]!;
        auth.Discord.ClientSecret    = builder.Configuration["GameKit:Auth:Discord:ClientSecret"]!;
    });

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseGameKitAuth();     // UseAuthentication — MUST precede UseGameKit's UseAuthorization
app.UseGameKit();
app.MapGameKit();         // /api/players (RequireAuthorization — now functional via JwtBearer)
app.MapAuth();            // /auth/* — new
app.MapDemo();            // /demo/* (sample keeps game endpoints; /demo/players/register removed)

app.Run();
```

---

## Shared Patterns

### SPDX GPL-3.0 header
**Source:** every `.cs` file in `src/GameKit.Core/` (example lines 1-2 of `Player.cs`).
**Apply to:** every new `.cs` file in `src/GameKit.Auth/` and `tests/**` (verified by `LicenseHeaderTests.cs`).
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

### XML doc comments on every public API
**Source:** `Directory.Build.props` `<GenerateDocumentationFile>true</GenerateDocumentationFile>` + `<WarningsAsErrors>CS1591;nullable</WarningsAsErrors>` (lines 8-10).
**Apply to:** every public type, property, method in `src/GameKit.Auth/`. Test projects inherit `NoWarn CS1591` via `tests/Directory.Build.props`.

### Nullable-reference + TreatWarningsAsErrors + Deterministic
**Source:** `Directory.Build.props` lines 5-9, 33.
**Apply to:** inherited automatically by any new csproj — no per-project change needed.

### UUIDv7 ID generation via `IIdGenerator`
**Source:** `src/GameKit.Core/Services/UuidV7IdGenerator.cs` + pattern in `GdprDeleteService.cs:60` (`_ids.NewId()`).
**Apply to:** every Auth entity insert — `new PlayerIdentity { Id = _ids.NewId(), ... }`. Never call `Guid.NewGuid()` directly; use `Guid.CreateVersion7()` only in tests that bypass the service (like `GdprDeleteTombstoneTests.cs:48`).

### `IClock.UtcNow` for every timestamp
**Source:** `SystemClock.cs`; used at `GdprDeleteService.cs:67` (`CreatedAt = _clock.UtcNow`).
**Apply to:** every `IssuedAt`/`ExpiresAt`/`UpdatedAt`/`CreatedAt`/`RevokedAt` assignment. Enables test-time clock mocking.

### SERIALIZABLE transaction boundary for race-sensitive writes
**Source:** `GdprDeleteService.cs:33` (`await _ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)`).
**Apply to:** `GuestUpgradeService.UpgradeAsync`, `IdentityLinker.LinkAsync` (RESEARCH §6.5, §8.5). NOT applied to `RefreshTokenService.RotateAsync` — that uses `ReadCommitted` per RESEARCH §6.4.

### Audit log write pattern for every privileged mutation
**Source:** `GdprDeleteService.cs:53-69` (snapshot + `AdminAuditLog.Add` + `SaveChanges` inside the tx).
**Apply to:** every Auth service write — wrapped inside `IAuthAuditWriter.WriteAsync(action, actorId, targetId, targetType, after, reason, ct)`. 10 action types per RESEARCH §8.10.

### Per-package migration isolation
**Source:** `GameKitServiceCollectionExtensions.cs:63-71` (`MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName); MigrationsHistoryTable("__ef_migrations_core", "gamekit")`).
**Apply to:** `AuthDesignTimeDbContextFactory` uses `typeof(AuthDesignTimeDbContextFactory).Assembly.FullName` + `AuthMigrationConstants.MigrationsHistoryTable` = `"__ef_migrations_auth"`. Both history tables live in the `gamekit` schema.

### `TryAddEnumerable` for sibling registration
**Source:** `IModelBuilderExtension.cs` XML doc (lines 14-17) — canonical idiom documented as the required registration pattern.
**Apply to:** `AuthBuilderExtensions.AddAuth` registers `AuthModelBuilderExtension` via `services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>())`.

### FluentValidation 12 explicit `IValidator<T>` (no auto-MVC wiring)
**Source:** CLAUDE.md "FluentValidation 12 explicit IValidator<T>" rule (not yet used in Phase 1 — first use in Phase 2 via `ValidationEndpointFilter<T>`).
**Apply to:** each `/auth/*` minimal API handler — `.AddEndpointFilter<ValidationEndpointFilter<LoginRequest>>()` per RESEARCH §14.6.

### Endpoint-level rate limiting via Phase-1 policy names
**Source:** `GameKitRateLimitPolicies.cs:9-16` (constants) + `IGameKitRateLimitPolicies.cs`.
**Apply to:** `.RequireRateLimiting(policies.AuthLogin)` / `.AuthRefresh` / `.AuthRegister` on the three endpoints. Auth is the first package to register concrete `PartitionedRateLimiter` instances under these names (RESEARCH §8.7).

### `.ConfigureAwait(false)` on every library `await`
**Source:** `GdprDeleteService.cs` uses `.ConfigureAwait(false)` on every `await` (lines 33, 41, 69, 74, 82) — required by library-code conventions since no sync context exists in ASP.NET Core but the pattern guards against future sync-ctx introduction.
**Apply to:** every `await` in `src/GameKit.Auth/**`. Tests omit it (see `GdprDeleteTombstoneTests.cs` — no ConfigureAwait, xUnit is fine without).

### Egress allow-list
**Source:** RESEARCH §8.6 (NEW — Phase 1 Core has zero HttpClient; Phase 2 introduces it).
**Apply to:** every named HttpClient in Auth (`"gamekit.auth.provider.steam"`, `"gamekit.auth.provider.discord"`) — `.AddHttpMessageHandler<EgressAllowListHandler>()`. Plus the aspnet-contrib `Backchannel` override via `IPostConfigureOptions<TOptions>` per RESEARCH §6.3.

### `[Collection("...")]` + `[Trait("Category", "Integration")]` on integration tests
**Source:** `MigrationHistoryIsolationTests.cs:18-19`, `RoleIsolationTests.cs:19-20`, `GdprDeleteTombstoneTests.cs:23-24`.
**Apply to:** every `tests/GameKit.Auth.Integration.Tests/*.cs` — use `[Collection("Auth")]` when the test needs WireMock, `[Collection("Postgres")]` for migration-only tests, `[Trait("Category", "Integration")]` always.

### AssemblyInfo.cs `[assembly: InternalsVisibleTo]` for test access
**Source:** `src/GameKit.Core/AssemblyInfo.cs` (full file, 10 lines):
```csharp
[assembly: InternalsVisibleTo("GameKit.Core.Tests")]
[assembly: InternalsVisibleTo("GameKit.Core.Integration.Tests")]
[assembly: InternalsVisibleTo("GameKit.Integration.Tests")]
```
**Apply to:** `src/GameKit.Auth/AssemblyInfo.cs` — add `[assembly: InternalsVisibleTo("GameKit.Auth.Tests")]` + `"GameKit.Auth.Integration.Tests"`.

---

## NEW patterns (no direct analog)

Files whose closest analog is either non-existent in Phase 1 or too distant to pattern-match. Planner should use the RESEARCH.md sketch as the primary source.

| File | Why No Analog | RESEARCH Reference |
|------|---------------|--------------------|
| `src/GameKit.Auth/Services/JwtIssuer.cs` | Phase 1 has no crypto or JWT code | §6.2 lines 477-499, §8.9 |
| `src/GameKit.Auth/Services/BCryptPasswordHasher.cs` | Phase 1 has no password hashing | §7, §4 (BCrypt.Net-Next 4.1.0) |
| `src/GameKit.Auth/Egress/EgressAllowListHandler.cs` | Core forbids `System.Net.Http` (`EgressGuardTests.Layer1_*`) | §8.6 |
| `src/GameKit.Auth/Http/EndpointFilters/ValidationEndpointFilter.cs` | FluentValidation not yet wired | §14.6 |
| `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitPolicies.cs` | Core only defines the **names**, not the concrete `PartitionedRateLimiter` | §8.7 |
| `src/GameKit.Auth/Providers/Steam/SteamOpenIdVerifier.cs` | No OpenID implementation exists in Phase 1 | §8.2 |
| `src/GameKit.Auth/Providers/Steam/SteamBackchannelPostConfigure.cs` | aspnet-contrib wiring is Phase 2–first | §6.3 |
| `src/GameKit.Auth/Providers/Discord/DiscordBackchannelPostConfigure.cs` | same | §6.3 |
| `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` | aspnet-contrib consumer — no Phase 1 precedent | §8.3 |
| `tests/GameKit.Auth.Integration.Tests/SteamLoginTests.cs`, `SteamForgeryTests.cs` | WireMock.Net first use | §8.8 |
| `tests/GameKit.Auth.Integration.Tests/DiscordLoginTests.cs` | WireMock.Net first use | §8.8 |
| `tests/GameKit.Auth.Integration.Tests/RateLimitTests.cs` | `WebApplicationFactory` first use | §8.7 |

For each "NEW" file, the planner MUST cite the RESEARCH.md section in the plan's action block and ship the sketch code from that section verbatim unless the executor surfaces a specific deviation.

---

## Metadata

**Analog search scope:** `src/GameKit.Core/**`, `src/GameKit.Auth/**` (existing scaffold), `tests/**`, `samples/TicTacToeDuel/**`, `Directory.Build.props`, `Directory.Packages.props`, repo root.
**Files scanned:** ~60 C# files + 6 project/props files.
**Pattern extraction date:** 2026-04-18

---

## PATTERN MAPPING COMPLETE
