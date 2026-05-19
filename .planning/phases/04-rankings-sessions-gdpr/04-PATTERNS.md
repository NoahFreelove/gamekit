---
phase: 04
phase_name: Rankings + Sessions Wiring + GDPR Export
mapped: 2026-05-15
status: ready_for_planning
---

# Phase 4 — Pattern Map

**Mapped:** 2026-05-15
**Files analyzed:** 65 new / 5 modified
**Analogs found:** 62 / 65 (3 genuinely new — Glicko-2 source, Redis-locked ticker, REPEATABLE READ export handler)

> Anchored to `04-CONTEXT.md` (D-01..D-23), `04-RESEARCH.md` §Recommended Project Structure (lines 300–416) + §Plan Decomposition (lines 1356–1369), `04-VALIDATION.md` Wave-0 list.
> Every entry below points at a concrete Phase-1/2/3 file. Phases 1–3 are GA — these patterns are line-for-line reusable.

---

## File Classification

### `src/GameKit.Rankings/` — package skeleton

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `GameKit.Rankings.csproj` (populate stub) | config | n/a | `src/GameKit.Auth/GameKit.Auth.csproj` | exact |
| `Builder/RankingsBuilderExtensions.cs` | builder/DI | build-time | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:30-100` | exact |
| `Builder/RankingsApplicationBuilderExtensions.cs` | app-builder | build-time | `src/GameKit.Auth/Builder/AuthApplicationBuilderExtensions.cs` | exact |
| `Builder/IGameKitRankingsBuilder.cs` | builder interface | build-time | (new — composes with `IGameKitBuilder` per Phase-1 pattern) | partial |
| `GameKitRankingsOptions.cs` | options | static config | `src/GameKit.Auth/GameKitAuthOptions.cs:12-41` | exact |
| `Data/RankingsMigrationConstants.cs` | config | static | `src/GameKit.Auth/Data/AuthMigrationConstants.cs:11-35` | exact |
| `Data/RankingsDesignTimeDbContextFactory.cs` | EF design-time | tooling | `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs:41-117` | exact |
| `Data/RankingsMigrationHostedService.cs` | IHostedService | startup | `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:29-86` | exact |
| `Data/RankingsModelBuilderExtension.cs` | model extension | build-time | `src/GameKit.Auth/Data/AuthModelBuilderExtension.cs:15-24` | exact |
| `Data/Configurations/LadderConfiguration.cs` | EF mapping | static | `src/GameKit.Auth/Data/Configurations/RefreshTokenConfiguration.cs:12-36` | exact |
| `Data/Configurations/PlayerRankConfiguration.cs` | EF mapping | static | same | exact |
| `Data/Configurations/LadderSeasonConfiguration.cs` | EF mapping | static | same | exact |
| `Data/Configurations/SeasonRankArchiveConfiguration.cs` | EF mapping | static | same | exact |
| `Data/Configurations/ServiceTokenConfiguration.cs` | EF mapping | static | same | exact |
| `Data/Configurations/PendingRatingUpdateConfiguration.cs` | EF mapping | static | same | exact |
| `Data/Configurations/SessionCompleteIdempotencyConfiguration.cs` | EF mapping | static | same | exact |
| `Entities/Ladder.cs` | entity | static | `src/GameKit.Auth/Entities/RefreshToken.cs:14-48` | exact |
| `Entities/PlayerRank.cs` | entity | static | same | exact |
| `Entities/LadderSeason.cs` | entity | static | same | exact |
| `Entities/SeasonRankArchive.cs` | entity | static | same | exact |
| `Entities/ServiceToken.cs` | entity | static | `src/GameKit.Auth/Entities/RefreshToken.cs:14-48` (token-hash storage discipline) | exact |
| `Entities/PendingRatingUpdate.cs` | entity | static | same | exact |
| `Entities/SessionCompleteIdempotency.cs` | entity | static | same | exact |
| `Entities/SeasonResetPolicy.cs` (enum) | enum | static | `src/GameKit.Core/Entities/SessionResult.cs` | exact |
| `Migrations/20260515000000_RankingsInitial.cs` | EF migration | tooling | `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs:13-159` | exact |
| `Migrations/20260515000000_RankingsInitial.Designer.cs` | EF migration | tooling | `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.Designer.cs` | exact |
| `Migrations/GameKitDbContextModelSnapshot.cs` | EF migration | tooling | `src/GameKit.Auth/Migrations/GameKitDbContextModelSnapshot.cs` | exact |

### Glicko-2 algorithm (RANK-04/05/06)

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `Glicko2/Rating.cs` | vendored algorithm | pure CPU | (none — net new vendored source) | no analog |
| `Glicko2/RatingCalculator.cs` | vendored algorithm | pure CPU | (none — net new vendored source) | no analog |
| `Glicko2/RatingPeriodResults.cs` | vendored algorithm | pure CPU | (none — net new vendored source) | no analog |
| `Glicko2/Result.cs` | vendored algorithm | pure CPU | (none — net new vendored source) | no analog |
| `Algorithms/IRankingAlgorithm.cs` | strategy port | pure CPU | `src/GameKit.Auth/Providers/IOAuthProvider.cs` (Scrutor-discovered strategy port) | partial |
| `Algorithms/Glicko2Algorithm.cs` | strategy adapter | pure CPU | `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` (strategy adapter shape) | partial |

### Service-token auth + CLI

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `Authentication/ServiceTokenAuthenticationHandler.cs` | AuthenticationHandler | request-response | `src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs:17-51` (custom auth event handler shape) | role-match |
| `Authentication/ServiceTokenAuthenticationOptions.cs` | options class | static | `src/GameKit.Auth/JwtOptions.cs` | exact |
| `Authentication/ServiceTokenAuthenticationDefaults.cs` | scheme constants | static | `src/GameKit.Admin.UI/Authentication/AdminAuthenticationSchemeConstants.cs:11-24` | exact |
| `Authentication/ServiceTokenAuthorizationPolicy.cs` | policy registration | build-time | `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` | exact |
| `Services/IServiceTokenService.cs` | service interface | CRUD | `src/GameKit.Auth/Services/IRefreshTokenService.cs` | exact |
| `Services/ServiceTokenService.cs` | service impl | CRUD | `src/GameKit.Auth/Services/RefreshTokenService.cs:280-292` (Sha256Hex + GenerateRaw) | exact |
| `src/GameKit.Cli/Commands/ServiceTokenIssueCommand.cs` | CLI verb | one-shot | `src/GameKit.Cli/Commands/AdminCreateCommand.cs:37-249` | exact |
| `src/GameKit.Cli/Commands/ServiceTokenRevokeCommand.cs` | CLI verb | one-shot | same | exact |
| `src/GameKit.Cli/Commands/ServiceTokenListCommand.cs` | CLI verb | one-shot | same | exact |
| `src/GameKit.Cli/Program.cs` (modify) | CLI config | static | `src/GameKit.Cli/Program.cs:7-21` (existing `admin` branch) | exact |

### Session-complete + idempotency (RANK-11)

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `src/GameKit.Core/Http/SessionEndpoints.cs` (NEW) | HTTP endpoint | request-response | `src/GameKit.Auth/Http/AuthEndpoints.cs:42-80` | exact |
| `src/GameKit.Core/Services/IPostSessionCompleteHandler.cs` (NEW port) | port interface | event-driven | `src/GameKit.Auth/Providers/IOAuthProvider.cs` (cross-package port shape) | role-match |
| `Services/PendingRatingUpdatesAdapter.cs` | port adapter | CRUD | `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` (adapter shape) | partial |
| `Http/Contracts/SessionCompleteRequest.cs` | DTO | static | `src/GameKit.Admin.UI/Http/Contracts/CreateAdminRequest.cs` | exact |
| `Http/Contracts/SessionCompleteResponse.cs` | DTO | static | same | exact |
| `Http/Validators/SessionCompleteRequestValidator.cs` | FluentValidation | validation | `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs:14-24` | exact |
| `Http/EndpointFilters/IdempotencyKeyEndpointFilter.cs` | endpoint filter | request-response | `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs:26-46` | exact |
| `Json/CanonicalJsonHasher.cs` | utility | pure CPU | `src/GameKit.Auth/Services/RefreshTokenService.cs:280-284` (Sha256Hex helper) | role-match |
| `Http/RateLimiting/RankingsRateLimitRegistrations.cs` | rate-limit policy | request-response | `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs:22-97` | exact |
| `src/GameKit.Core/RateLimiting/IGameKitRateLimitPolicies.cs` (modify — add `SessionsComplete`) | interface | static | `src/GameKit.Core/RateLimiting/IGameKitRateLimitPolicies.cs:16-32` | exact |
| `src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs` (modify) | impl | static | `src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs:7-38` | exact |

### Rankings ticker + cleanup (RANK-04/05/06 + D-08 TTL)

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `Services/RankingsTickerService.cs` | BackgroundService | pub-sub / batch | `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:29-86` (closest IHostedService — start/stop shape) | partial — BackgroundService + Polly + Redis lock combo is genuinely new |
| `Services/RankingsTickerLeaseHelper.cs` | Redis helper | pub-sub | (none — first Redis usage in codebase) | no analog |
| `Services/IdempotencyCleanupService.cs` | BackgroundService | batch | same as ticker | partial |
| `Services/StartupLadderUpserter.cs` | IHostedService | startup | `src/GameKit.Admin.UI/Authentication/SuperadminGateHostedService.cs:30-79` | exact |

### Leaderboard + season + rank-adjust (RANK-08/10/12)

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `Services/ILeaderboardService.cs` | service interface | CRUD | `src/GameKit.Admin.UI/Services/IPlayerSearchService.cs` | exact |
| `Services/LeaderboardService.cs` | service impl | CRUD | `src/GameKit.Admin.UI/Services/PlayerSearchService.cs` | exact |
| `Services/IEndSeasonService.cs` | service interface | transactional mutate | `src/GameKit.Admin.UI/Services/IPlayerBanService.cs` | exact |
| `Services/EndSeasonService.cs` | service impl | transactional mutate + audit | `src/GameKit.Admin.UI/Services/PlayerBanService.cs:22-133` | exact |
| `Services/IRankAdjustService.cs` | service interface | transactional mutate | `src/GameKit.Admin.UI/Services/IPlayerBanService.cs` | exact |
| `Services/RankAdjustService.cs` | service impl | SERIALIZABLE tx + audit | `src/GameKit.Admin.UI/Services/PlayerBanService.cs:42-90` | exact |
| `Http/Contracts/RankAdjustRequest.cs` | DTO | static | `src/GameKit.Admin.UI/Http/Contracts/CreateAdminRequest.cs` | exact |
| `Http/Contracts/EndSeasonRequest.cs` | DTO | static | same | exact |
| `Http/Contracts/LeaderboardRowDto.cs` | DTO | static | same | exact |
| `Http/Validators/RankAdjustRequestValidator.cs` | FluentValidation | validation | `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs:14-24` | exact |
| `Http/Validators/EndSeasonRequestValidator.cs` | FluentValidation | validation | same | exact |
| `Http/RankingsEndpoints.cs` | HTTP endpoints | request-response | `src/GameKit.Admin.UI/Http/AdminEndpoints.cs:51-80` | exact |

### GDPR export (RANK-13)

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `Services/IGdprExportService.cs` | service interface | snapshot read | `src/GameKit.Core/Services/IGdprDeleteService.cs` | exact |
| `Services/GdprExportService.cs` | service impl | REPEATABLE READ tx | `src/GameKit.Auth/Services/RefreshTokenService.cs:99-101` (BeginTransactionAsync(ReadCommitted) — but isolation level is genuinely new) | partial |
| `Http/Contracts/GdprExportResponse.cs` | DTO | static | `src/GameKit.Admin.UI/Http/Contracts/CreateAdminRequest.cs` | exact |
| `Http/EndpointFilters/ResponseSizeCapFilter.cs` | endpoint filter | request-response | `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs:26-46` | exact |
| `src/GameKit.Core/Http/PlayerEndpoints.cs` (modify — add `GET /export`) | endpoint | request-response | `src/GameKit.Core/Http/PlayerEndpoints.cs:19-53` | exact |

### Admin UI wiring (RANK-12 + D-11 end-season + rank-adjust)

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor` | Blazor component | request-response | `src/GameKit.Admin.UI/Components/Dialogs/BanPlayerDialog.razor:19-129` | exact |
| `src/GameKit.Admin.UI/Components/Dialogs/EndSeasonDialog.razor` | Blazor component | request-response | same | exact |
| `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor` (modify lines 123–134) | wiring | UI | `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor:115-136` | exact |
| `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs` (modify — add `end-season`) | registration | static | `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs:39` (existing `rank-adjust` row) | exact |
| `src/GameKit.Admin.UI/Services/AdminAuditActions.cs` (modify — add `LadderEndSeason`) | constants | static | `src/GameKit.Admin.UI/Services/AdminAuditActions.cs:11-37` | exact |
| `src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs` (modify — add end-season template) | UI projection | static | `src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs:36-66` | exact |
| `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` (modify — add `/players/{id}/rank-adjust` + `/ladders/{id}/end-season` + `/players/{id}/export`) | endpoint | request-response | `src/GameKit.Admin.UI/Http/AdminEndpoints.cs:60-80` | exact |

### Tests (Wave 0)

| New File | Role | Data Flow | Closest Analog | Match Quality |
|----------|------|-----------|----------------|---------------|
| `tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj` | test config | n/a | `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` | exact |
| `tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj` | test config | n/a | `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj` | exact |
| `tests/GameKit.TestFixtures/RankingsFixture.cs` | composite fixture | n/a | `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs:13-31` | exact |
| `tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json` | test fixture | static | (none — Glickman PDF is the source) | no analog |
| `tests/GameKit.Rankings.Tests/Glicko2WorkedExampleTests.cs` | unit test | n/a | `tests/GameKit.Auth.Tests/JwtIssuerTests.cs` | partial |
| `tests/GameKit.Rankings.Integration.Tests/RankingsAdvisoryLockKeyTests.cs` | integration test | n/a | `tests/GameKit.Auth.Integration.Tests/AuthAdvisoryLockKeyTests.cs:16-39` | exact |
| `tests/GameKit.Rankings.Integration.Tests/SessionCompleteIdempotencyTests.cs` | integration (SC#2) | n/a | `tests/GameKit.Auth.Integration.Tests/AuthEndpointsE2ETests.cs` | partial |

> **Reuse note:** `tests/GameKit.TestFixtures/RedisFixture.cs` already exists (created Phase 2 for the `AuthIntegrationFixture` composite). Phase 4 only consumes it — no new write.

---

## Pattern Assignments

### `src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs` (EF design-time, tooling)

**Analog:** `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs:41-117`
**Delta:** identical shape; new exclusion list includes Auth + Admin entities (Rankings has `ProjectReference` to Core only per RESEARCH §Recommended Project Structure, so Auth/Admin typeof()s are conditionally omitted as documented in the analog file's comments lines 459–462).

**Imports pattern (lines 1–12):**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Auth.Data.Configurations;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Auth.Data;
```

**Core design-time factory pattern (lines 41–64):**
```csharp
public sealed class AuthDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
{
    public GameKitDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";

        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthDesignTimeDbContextFactory).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>();

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
```

**Migration customizer pattern (lines 82–117) — exclude every non-Rankings entity:**
```csharp
public sealed class AuthMigrationModelCustomizer : RelationalModelCustomizer
{
    public AuthMigrationModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());

        var coreEntityTypes = new[]
        {
            typeof(Player), typeof(GameSession),
            typeof(SessionParticipant), typeof(AdminAuditLog),
        };
        foreach (var type in coreEntityTypes)
        {
            var entity = modelBuilder.Model.FindEntityType(type);
            if (entity is null) continue;
            modelBuilder.Entity(type).ToTable(entity.GetTableName()!, entity.GetSchema(),
                t => t.ExcludeFromMigrations());
        }
    }
}
```

---

### `src/GameKit.Rankings/Data/RankingsMigrationHostedService.cs` (IHostedService, startup)

**Analog:** `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:29-86`
**Delta:** none — swap "Auth" for "Rankings" verbatim. Register via `AddHostedService<RankingsMigrationHostedService>()` in `RankingsBuilderExtensions.AddRankings`.

**Excerpt (lines 29–66):**
```csharp
internal sealed class AuthMigrationHostedService : IHostedService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly ILogger<AuthMigrationHostedService> _logger;

    public AuthMigrationHostedService(GameKitOptions gameKitOpts, ILogger<AuthMigrationHostedService> logger)
    { _gameKitOpts = gameKitOpts; _logger = logger; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_gameKitOpts.AutoMigrate)
        {
            _logger.LogInformation(
                "AutoMigrate=false — skipping Auth migration apply. Run migrations out-of-band before accepting traffic.");
            return;
        }

        var connectionString = !string.IsNullOrWhiteSpace(_gameKitOpts.MigrationsConnectionString)
            ? _gameKitOpts.MigrationsConnectionString!
            : _gameKitOpts.ConnectionString;

        await using var ctx = BuildAuthMigrationContext(connectionString);
        _logger.LogInformation("Applying Auth migrations (history table {Table}).",
            AuthMigrationConstants.MigrationsHistoryTable);

        await MigrationRunner
            .MigrateWithLockAsync(ctx, AuthMigrationConstants.AdvisoryLockKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Auth migrations applied successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    // ...
}
```

---

### `src/GameKit.Rankings/Data/RankingsModelBuilderExtension.cs` (model extension, build-time)

**Analog:** `src/GameKit.Auth/Data/AuthModelBuilderExtension.cs:15-24`
**Delta:** add the seven Rankings entity configurations from RESEARCH §Recommended Project Structure lines 318–325.

**Excerpt (lines 15–24):**
```csharp
internal sealed class AuthModelBuilderExtension : IModelBuilderExtension
{
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
```

Register in `RankingsBuilderExtensions.AddRankings` via:
```csharp
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IModelBuilderExtension, RankingsModelBuilderExtension>());
```

---

### `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs` (config, static)

**Analog:** `src/GameKit.Auth/Data/AuthMigrationConstants.cs:11-35`
**Delta:** new `MigrationsHistoryTable = "__ef_migrations_rankings"` + new pinned `AdvisoryLockKey` computed as `SELECT hashtext('gamekit.rankings.migrations')::bigint` against live Postgres 17.9 in the Wave-0 integration test (mirror of `AuthAdvisoryLockKeyTests:23-32`).

**Excerpt (lines 11–35):**
```csharp
public static class AuthMigrationConstants
{
    public const string MigrationsHistoryTable = "__ef_migrations_auth";

    /// <summary>
    /// MUST differ from <see cref="GameKitMigrationConstants.AdvisoryLockKey"/> so Core and Auth
    /// migrations do not deadlock at startup (PITFALLS §8.12 #9).
    /// </summary>
    public const long AdvisoryLockKey = -298890956L;
}
```

---

### `src/GameKit.Rankings/Data/Configurations/*.cs` (EF mappings)

**Analog:** `src/GameKit.Auth/Data/Configurations/RefreshTokenConfiguration.cs:12-36`
**Delta:** seven configurations to add. The double-precision pin (RANK-03, SC#3) is applied on `PlayerRank.{Rating,RatingDeviation,Volatility}` + `SeasonRankArchive.{...}` via `b.Property(r => r.Rating).HasColumnType("double precision")` — see RESEARCH §930-960 for the exact configuration shape.

**Excerpt (lines 12–36) — base shape:**
```csharp
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.TokenHash).IsRequired().HasMaxLength(64);
        // ...
        b.HasIndex(r => r.TokenHash).IsUnique();
        b.HasIndex(r => new { r.PlayerId, r.RevokedAt });

        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(r => r.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

---

### `src/GameKit.Rankings/Entities/ServiceToken.cs` (entity, static)

**Analog:** `src/GameKit.Auth/Entities/RefreshToken.cs:14-48`
**Delta:** no `FamilyId` / `ReplacedByTokenHash`; add `Name` (operator-supplied label), `LastUsedAt`. Mirror token-hash storage discipline verbatim.

**Excerpt (lines 14–48):**
```csharp
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public Guid FamilyId { get; set; }

    /// <summary>SHA-256 hex (64 chars) of the raw refresh token. Raw value is never stored.</summary>
    public required string TokenHash { get; set; }

    public string? DeviceFingerprint { get; set; }
    public required string Provider { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
```

---

### `src/GameKit.Rankings/Services/ServiceTokenService.cs` (CRUD)

**Analog:** `src/GameKit.Auth/Services/RefreshTokenService.cs:280-292` (Sha256Hex + GenerateRaw helpers)
**Delta:** ServiceToken has no rotation, no families — just issue (insert + return raw once) and revoke (set RevokedAt). Reuse `GenerateRaw` and `Sha256Hex` verbatim.

**Excerpt (lines 280–292):**
```csharp
private static string Sha256Hex(string raw)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

private static string GenerateRaw()
{
    // 256-bit CSRNG; URL-safe base64.
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

---

### `src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationHandler.cs` (request-response)

**Analog:** RESEARCH §Pattern 2 lines 473–544 (canonical shape) + `src/GameKit.Admin.UI/Authentication/AdminCookieEvents.cs:17-51` (event-handler shape)
**Delta:** custom `AuthenticationHandler<TOptions>` is net new (no `AuthenticationHandler` subclass exists in repo today — admin uses the framework `AddCookie`). Use RESEARCH §Pattern 2 verbatim. Mitigate Pitfall §10 (hot DB read) with a one-minute in-process cache (acknowledged in RESEARCH §853–860).

**Code from RESEARCH §Pattern 2 (canonical pattern, paste into new file):**
```csharp
public sealed class ServiceTokenAuthenticationHandler
    : AuthenticationHandler<ServiceTokenAuthenticationOptions>
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;

    public ServiceTokenAuthenticationHandler(
        IOptionsMonitor<ServiceTokenAuthenticationOptions> opts,
        ILoggerFactory log, UrlEncoder enc,
        GameKitDbContext ctx, IClock clock) : base(opts, log, enc)
    { _ctx = ctx; _clock = clock; }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var auth))
            return AuthenticateResult.NoResult();
        var raw = auth.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();
        var token = raw.AsSpan("Bearer ".Length).TrimStart().ToString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        var row = await _ctx.Set<ServiceToken>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash).ConfigureAwait(false);
        if (row is null || row.RevokedAt is not null
            || (row.ExpiresAt is { } exp && exp < _clock.UtcNow))
            return AuthenticateResult.Fail("invalid_service_token");

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, row.Id.ToString()),
            new Claim(ClaimTypes.Name, row.Name),
            new Claim(ClaimTypes.Role, "service-account"),
        }, Scheme.Name);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
```

---

### `src/GameKit.Cli/Commands/ServiceTokenIssueCommand.cs` (CLI verb)

**Analog:** `src/GameKit.Cli/Commands/AdminCreateCommand.cs:37-249`
**Delta:** swap `AdminUser` for `ServiceToken`; raw token printed exactly once on stdout (mirror `BCryptPasswordHasher.Hash` step but with `GenerateRaw` → `Sha256Hex` storage). Wire the new `service-token` branch in `Program.cs:13-19` (existing `admin` branch is the template).

**Excerpt (lines 37–124) — Settings + Execute shape:**
```csharp
internal sealed class AdminCreateCommand : AsyncCommand<AdminCreateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-u|--username <USERNAME>")]
        [Description("Username (3-32 chars, case-insensitive). Prompted when omitted.")]
        public string? Username { get; init; }

        [CommandOption("-c|--connection-string <CONN>")]
        [Description("Postgres connection string (gamekit_owner role recommended).")]
        public string? ConnectionString { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var conn = settings.ConnectionString
            ?? Environment.GetEnvironmentVariable("GAMEKIT_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
            return Fail("No connection string. Pass --connection-string or set GAMEKIT_CONNECTION.");

        // Build a DbContext wired via ReplaceService<IModelCustomizer, AdminCliModelCustomizer>.
        var dbOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(conn!)
            .ReplaceService<IModelCustomizer, AdminCliModelCustomizer>()
            .Options;
        await using var dbCtx = new GameKitDbContext(dbOpts);
        // ...
    }
}
```

**ServiceTokenIssue must:** print raw token to stdout exactly once (manual-verification per `04-VALIDATION.md` §Manual-Only Verifications). Print pattern from `AdminCreateCommand:164-168`:
```csharp
AnsiConsole.MarkupLine("[green]OK[/] - service token created. Copy the raw token NOW; it will not be shown again:");
AnsiConsole.MarkupLine($"  [bold]{raw}[/]");
```

---

### `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` (port adapter)

**Analog (port shape):** `src/GameKit.Auth/Providers/IOAuthProvider.cs` + `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs`
**Delta:** implements `IPostSessionCompleteHandler` (defined in Core per D-22 + RESEARCH Open Q6). Inserts rows into `pending_rating_updates` per Phase-4 RESEARCH Open Q2 schema. No transaction here — runs inside the session-complete handler's open transaction (analog: `AdminAuditWriter` rides the caller's tx — `src/GameKit.Admin.UI/Services/AdminAuditWriter.cs:14-69`).

**Excerpt (lines 14–67) — adapter that rides caller's transaction:**
```csharp
public sealed class AdminAuditWriter : IAdminAuditWriter
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    public AdminAuditWriter(GameKitDbContext ctx, IClock clock, IIdGenerator ids)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        _ctx = ctx; _clock = clock; _ids = ids;
    }

    public async Task WriteAsync(string action, string targetType, Guid? targetId, Guid actorId,
        object? before, object? after, string? reason, CancellationToken ct = default)
    {
        _ctx.Set<AdminAuditLog>().Add(new AdminAuditLog { /* ... */ });
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
```

---

### `src/GameKit.Core/Http/SessionEndpoints.cs` + `SessionCompleteEndpoint` (request-response)

**Analog:** `src/GameKit.Auth/Http/AuthEndpoints.cs:42-80` (endpoint group + filter chain)
**Delta:** session-complete is gated by the `ServiceTokenAuthenticationDefaults.SchemeName` scheme (D-05), not JwtBearer. Use `.RequireAuthorization("RequiresServiceToken")` from RESEARCH §Pattern 2 lines 538–544. Add `.AddEndpointFilter<IdempotencyKeyEndpointFilter>` BEFORE `ValidationEndpointFilter`.

**Excerpt (lines 42–80):**
```csharp
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes,
        IGameKitRateLimitPolicies policies)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(policies);

        var grp = routes.MapGroup("/auth").WithTags("GameKit.Auth");

        grp.MapPost("/login/{provider}", LoginAsync)
            .AddEndpointFilter<ValidationEndpointFilter<LoginRequest>>()
            .RequireRateLimiting(policies.AuthLogin);

        grp.MapPost("/refresh", RefreshAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RefreshRequest>>()
            .RequireRateLimiting(policies.AuthRefresh);
        // ...
    }
}
```

**SessionComplete handler core flow** — use RESEARCH §Pattern 4 lines 594–672 verbatim. Critical bits: state-conditional `ExecuteUpdateAsync` on `game_sessions.state`, idempotency-row dedup INSIDE the transaction, snapshot `rating_before` from current `player_ranks`, enqueue via `IPostSessionCompleteHandler.OnCompletedAsync`.

---

### `src/GameKit.Rankings/Http/EndpointFilters/IdempotencyKeyEndpointFilter.cs` (request-response)

**Analog:** `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs:26-46`
**Delta:** read `Idempotency-Key` header; reject `400 BadRequest` when missing (D-08 makes it mandatory). DB-level dedup lives in `SessionCompleteService` (RESEARCH §Pattern 4) — this filter only enforces the header presence + shape.

**Excerpt (lines 26–46):**
```csharp
public sealed class AntiforgeryValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "csrf_validation_failed" });
        }
        return await next(context).ConfigureAwait(false);
    }
}
```

---

### `src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs` (request-response)

**Analog:** `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs:22-97`
**Delta:** single policy `gamekit:sessions:complete`, 300 req/min (D-10), partition key = service-token id (not IP — service tokens identify the caller). New constant `SessionsComplete` added to `IGameKitRateLimitPolicies` + `GameKitRateLimitPolicies` (CORE-12 extension; see `src/GameKit.Core/RateLimiting/IGameKitRateLimitPolicies.cs:16-32`).

**Excerpt (lines 22–97):**
```csharp
public static class AuthRateLimitRegistrations
{
    public const int LoginPermitLimit = 10;
    public static TimeSpan Window => TimeSpan.FromMinutes(1);

    public static IServiceCollection AddAuthRateLimits(
        this IServiceCollection services, IGameKitRateLimitPolicies names)
    {
        services.AddRateLimiter(opt =>
        {
            opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opt.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ctx.HttpContext.Response.ContentType = "application/problem+json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://gamekit.dev/errors/rate-limit",
                    title = "Too Many Requests", status = 429,
                }, ct).ConfigureAwait(false);
            };

            AddPolicy(opt, names.AuthLogin, permit: LoginPermitLimit, window: Window);
        });
        return services;
    }

    private static void AddPolicy(RateLimiterOptions opt, string name, int permit, TimeSpan window)
    {
        opt.AddPolicy(name, httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var fp = httpContext.Request.Headers["X-GameKit-Device"].ToString();
            var partitionKey = string.IsNullOrEmpty(fp) ? ip : $"{ip}:{fp}";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partitionKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permit, Window = window,
                    QueueLimit = 0, AutoReplenishment = true,
                });
        });
    }
}
```

---

### `src/GameKit.Rankings/Services/EndSeasonService.cs` + `RankAdjustService.cs` (transactional mutate + audit)

**Analog:** `src/GameKit.Admin.UI/Services/PlayerBanService.cs:22-133`
**Delta:** end-season opens SERIALIZABLE tx (D-11), runs the three reset variants from D-12, writes `LadderEndSeason` audit row through `IAdminAuditWriter`. Rank-adjust is shorter — single UPDATE + audit row (D-19/D-20).

**Excerpt (lines 42–90) — the SERIALIZABLE + audit pattern to mirror verbatim:**
```csharp
public async Task BanAsync(Guid playerId, Guid actorId, string reason, CancellationToken ct)
{
    ArgumentException.ThrowIfNullOrEmpty(reason);

    await using var tx = await _ctx.Database
        .BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

    var player = await _ctx.Set<Player>()
        .FirstOrDefaultAsync(p => p.Id == playerId, ct).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Player {playerId} not found.");

    var before = new { is_banned = player.IsBanned, banned_at = player.BannedAt, ban_reason = player.BanReason };

    player.IsBanned = true;
    player.BannedAt = _clock.UtcNow;
    player.BanReason = reason;
    await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);

    await _audit.WriteAsync(
        action: AdminAuditActions.PlayerBan,
        targetType: "player",
        targetId: playerId,
        actorId: actorId,
        before: before,
        after: new { is_banned = player.IsBanned, banned_at = player.BannedAt, ban_reason = player.BanReason },
        reason: reason,
        cancellationToken: ct).ConfigureAwait(false);

    await tx.CommitAsync(ct).ConfigureAwait(false);
}
```

This pattern anchors **SC#6** (RANK-12 — `AdminRankAdjustTransactionTests`). Identical shape: open tx → snapshot before → mutate → SaveChanges → audit write (rides the tx) → commit. A faulty audit writer that throws after UPDATE rolls back the UPDATE — this is what the SC#6 test asserts.

---

### `src/GameKit.Rankings/Services/GdprExportService.cs` (REPEATABLE READ tx)

**Analog (partial):** `src/GameKit.Auth/Services/RefreshTokenService.cs:99-101` (BeginTransactionAsync pattern — but uses ReadCommitted)
**Delta:** isolation level is `RepeatableRead` (D-17) + extra `SET TRANSACTION READ ONLY` raw SQL (RESEARCH §Pattern 3 lines 562–565). The exact handler shape is RESEARCH §Pattern 3 lines 557–591 — paste verbatim into `GdprExportService.ExportAsync`. Mitigates Pitfall §5 (Npgsql doesn't auto-promote SELECT to deferred snapshot).

**Code from RESEARCH §Pattern 3 (canonical):**
```csharp
public async Task<GdprExportResponse> ExportAsync(Guid playerId, CancellationToken ct)
{
    await using var tx = await _ctx.Database
        .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);

    await _ctx.Database
        .ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct).ConfigureAwait(false);

    var player = await _ctx.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playerId, ct);
    if (player is null) { await tx.CommitAsync(ct); return null!; }  // caller maps to 404

    var identities  = await _ctx.Set<PlayerIdentity>().AsNoTracking()
        .Where(i => i.PlayerId == playerId).ToListAsync(ct);
    var sessions    = await _ctx.SessionParticipants.AsNoTracking()
        .Where(sp => sp.PlayerId == playerId)
        .Join(_ctx.GameSessions, sp => sp.SessionId, gs => gs.Id, (sp, gs) => new {
            session_id = gs.Id, ladder_id = gs.LadderId, sp.Team, sp.Result,
            rating_before = sp.RatingBefore, rating_after = sp.RatingAfter,
            completed_at = gs.CompletedAt
        }).ToListAsync(ct);
    // ...
    await tx.CommitAsync(ct).ConfigureAwait(false);

    var json = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
    if (json.Length > _opts.GdprExport.MaxBytes)
        throw new PayloadTooLargeException(json.Length, _opts.GdprExport.MaxBytes);
    return dto;
}
```

**Pitfall §7 mitigation (P0):** the `.Where(sp => sp.PlayerId == playerId)` filter excludes `PlayerId IS NULL` rows from GDPR-cascaded tombstones — under Postgres semantics `NULL == @playerId` is `UNKNOWN`, not `TRUE`. This is correct as-written; a contract test asserts no NULL leakage.

---

### `src/GameKit.Core/Http/PlayerEndpoints.cs` (modify — add `GET /export`)

**Analog:** `src/GameKit.Core/Http/PlayerEndpoints.cs:19-53`
**Delta:** add a second `MapGet("/{id}/export")` to the existing `/api/players` group. Authorize via player JWT scheme + `sub` claim must match `{id}`. Admin variant lives in `AdminEndpoints` (D-16).

**Excerpt (lines 19–53):**
```csharp
public static RouteGroupBuilder MapPlayers(this IEndpointRouteBuilder routes)
{
    var group = routes.MapGroup("/api/players").WithTags("GameKit.Core");

    group.MapGet("/", async (GameKitDbContext db, int skip, int take, CancellationToken ct) =>
    {
        var clampedTake = take <= 0 ? 50 : take > 200 ? 200 : take;
        var clampedSkip = skip < 0 ? 0 : skip;

        var rows = await db.Players.AsNoTracking()
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Skip(clampedSkip).Take(clampedTake)
            .Select(p => new { id = p.Id, displayName = p.DisplayName, /* ... */ })
            .ToListAsync(ct);
        return Results.Ok(rows);
    })
    .RequireAuthorization();

    return group;
}
```

---

### `src/GameKit.Admin.UI/Components/Dialogs/RankAdjustDialog.razor` + `EndSeasonDialog.razor` (Blazor)

**Analog:** `src/GameKit.Admin.UI/Components/Dialogs/BanPlayerDialog.razor:19-129`
**Delta:**
- `RankAdjustDialog` accepts a player + ladder + new-rating + reason. Three input fields, not one. Inject `IRankAdjustService` (Rankings package). `Validator = FluentValidation.IValidator<RankAdjustRequest>`.
- `EndSeasonDialog` accepts a ladder name (target). User types the ladder name to confirm — pattern from `GdprDeleteDialog.razor` (already exists; "type X to confirm" gate).

**Excerpt (lines 19–129) — full template structure to mirror:**
```razor
@namespace GameKit.Admin.UI.Components.Dialogs
@using GameKit.Admin.UI.Http.Contracts
@inject IPlayerBanService BanService
@inject FluentValidation.IValidator<BanPlayerRequest> Validator
@inject AuthenticationStateProvider AuthState

<MudDialog Class="dialog dialog-danger">
    <TitleContent><span>Ban @DisplayName?</span></TitleContent>
    <DialogContent>
        <MudText Typo="Typo.body2" Class="dialog-body">
            Banned players cannot sign in. /* ... */
        </MudText>
        @if (!string.IsNullOrEmpty(_errorMessage))
        {
            <MudAlert Severity="Severity.Error" role="alert">@_errorMessage</MudAlert>
        }
        <MudTextField T="string" @bind-Value="_reason"
                      Label="Reason (visible to admins only)"
                      HelperText="3–512 characters. Stored verbatim in the audit log."
                      Lines="3" Error="@_reasonError" ErrorText="@_reasonErrorMessage" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel" Disabled="@_submitting">Cancel</MudButton>
        <MudButton Color="Color.Error" OnClick="SubmitAsync" Disabled="@_submitting">Ban player</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance? MudDialog { get; set; }
    [Parameter, EditorRequired] public Guid PlayerId { get; set; }
    [Parameter, EditorRequired] public string DisplayName { get; set; } = string.Empty;

    private string _reason = string.Empty;
    private bool _submitting;

    private async Task SubmitAsync()
    {
        var req = new BanPlayerRequest(_reason);
        var validation = await Validator.ValidateAsync(req);
        if (!validation.IsValid) { /* inline field error */ return; }

        var actorId = await GetActorIdAsync();
        await BanService.BanAsync(PlayerId, actorId, req.Reason, CancellationToken.None);
        MudDialog?.Close(DialogResult.Ok(true));
    }

    private async Task<Guid> GetActorIdAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        var nameId = state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(nameId, out var id)
            ? id : throw new InvalidOperationException("Admin id claim is missing");
    }
}
```

**Key contract from this analog (preserve verbatim in new dialogs):**
- Dialog calls the service via DI (NOT HTTP round-trip — see comment block lines 7–14).
- FluentValidation validator injected and invoked inline before service call.
- Actor id pulled from `AuthenticationStateProvider` claim — never trusted from the dialog's parameters.

---

### `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor` (modify lines 123–134)

**Analog:** `src/GameKit.Admin.UI/Components/Layout/MainLayout.razor:115-136`
**Delta:** add two new switch arms to `OpenDialog`:
- `"rank-adjust" => typeof(RankAdjustDialog)`
- `"end-season"  => typeof(EndSeasonDialog)`

**Excerpt (lines 115–136) — exact site of modification:**
```csharp
[JSInvokable]
public async Task OpenDialog(string commandId, string targetId, string targetName)
{
    var pid = Guid.TryParse(targetId, out var parsed) ? parsed : Guid.Empty;
    var parameters = new DialogParameters
    {
        ["PlayerId"] = pid,
        ["DisplayName"] = targetName ?? string.Empty
    };
    var dialogType = commandId switch
    {
        "ban"          => typeof(BanPlayerDialog),
        "unban"        => typeof(UnbanPlayerDialog),
        "gdpr-delete"  => typeof(GdprDeleteDialog),
        "create-admin" => typeof(CreateAdminDialog),
        "delete-admin" => typeof(DeleteAdminDialog),
        // rank-adjust + rotate-signing-key route to dialogs that may not yet exist in v1;
        // they are filtered out at the registry layer (Plan 04) for this release.
        _ => null
    };
    if (dialogType is null) return;
    await Dialogs.ShowAsync(dialogType, $"{commandId}: {targetName}", parameters);
}
```

The comment on line 130–131 is load-bearing — it explicitly anchors Phase 4's wiring task. Delete the comment when the two new arms land.

---

### `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs` (modify — add `end-season`)

**Analog:** `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs:39` (existing `rank-adjust` row)
**Delta:** insert one new row in the actions block. `rank-adjust` is already there (line 39); only `end-season` is net new.

**Excerpt (lines 35–47):**
```csharp
public static IReadOnlyList<AdminCommand> AllCommands { get; } = new List<AdminCommand>
{
    // Actions ------------------------------------------------------------
    new("ban",                "Ban player",            "actions", RequiresSuperadmin: false, RequiresTarget: true),
    new("unban",              "Unban player",          "actions", RequiresSuperadmin: false, RequiresTarget: true),
    new("gdpr-delete",        "GDPR-delete player",    "actions", RequiresSuperadmin: true,  RequiresTarget: true),
    new("rank-adjust",        "Adjust player rank",    "actions", RequiresSuperadmin: true,  RequiresTarget: true),
    // ADD HERE (Phase 4):
    // new("end-season",      "End ladder season",     "actions", RequiresSuperadmin: true,  RequiresTarget: true),
    // ...
};
```

---

### `src/GameKit.Admin.UI/Services/AdminAuditActions.cs` (modify — add `LadderEndSeason`)

**Analog:** `src/GameKit.Admin.UI/Services/AdminAuditActions.cs:11-37`
**Delta:** add one new const. `PlayerRankAdjust` already exists (line 22).

**Excerpt (lines 11–37):**
```csharp
public static class AdminAuditActions
{
    public const string PlayerBan = "admin.player.ban";
    public const string PlayerUnban = "admin.player.unban";
    public const string PlayerGdprDelete = "admin.player.gdpr_delete";

    /// <summary>A player's rating was manually adjusted (superadmin-only; Phase 4 surface).</summary>
    public const string PlayerRankAdjust = "admin.player.rank_adjust";

    public const string AdminCreate = "admin.admin.create";
    public const string AdminDelete = "admin.admin.delete";
    // ADD HERE (Phase 4):
    // public const string LadderEndSeason = "admin.ladder.end_season";
}
```

---

### `src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs` (modify — add end-season template)

**Analog:** `src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs:36-66` (existing `PlayerRankAdjust` template anchors the rank-adjust delta extraction shape — reuse verbatim)
**Delta:** add one new `Registry` entry for `LadderEndSeason`. Modifier = the ladder name (already in `targetName`).

**Excerpt (lines 36–66):**
```csharp
private static readonly IReadOnlyDictionary<string, Func<SentenceContext, SentenceModel>> Registry =
    new Dictionary<string, Func<SentenceContext, SentenceModel>>(StringComparer.Ordinal)
    {
        [AdminAuditActions.PlayerBan] = ctx =>
            new SentenceModel(ctx.ActorName, "banned", ctx.TargetName ?? "(unknown player)", null, ctx.Reason),

        [AdminAuditActions.PlayerRankAdjust] = ctx =>
            new SentenceModel(
                ctx.ActorName, "adjusted rank for",
                ctx.TargetName ?? "(unknown player)",
                ExtractRatingDelta(ctx.Before, ctx.After),
                ctx.Reason),

        // ADD HERE (Phase 4):
        // [AdminAuditActions.LadderEndSeason] = ctx =>
        //     new SentenceModel(ctx.ActorName, "ended season for ladder", ctx.TargetName ?? "(unknown ladder)", null, ctx.Reason),
    };
```

---

### `tests/GameKit.TestFixtures/RankingsFixture.cs` (composite fixture)

**Analog:** `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs:13-31`
**Delta:** identical shape; bundles `PostgresFixture` + the existing `RedisFixture`. No WireMock needed (Rankings has no outbound HTTP).

**Excerpt (lines 13–31):**
```csharp
public sealed class AuthIntegrationFixture
{
    public PostgresFixture Postgres { get; }
    public RedisFixture Redis { get; }
    public WireMockFixture WireMock { get; }

    public AuthIntegrationFixture(PostgresFixture postgres, RedisFixture redis, WireMockFixture wireMock)
    {
        Postgres = postgres;
        Redis = redis;
        WireMock = wireMock;
    }
}
```

---

### `tests/GameKit.Rankings.Integration.Tests/RankingsAdvisoryLockKeyTests.cs`

**Analog:** `tests/GameKit.Auth.Integration.Tests/AuthAdvisoryLockKeyTests.cs:16-39`
**Delta:** swap the hashtext input string to `'gamekit.rankings.migrations'` and add distinctness assertions against Core, Auth, and Admin keys.

**Excerpt (lines 16–39):**
```csharp
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class AuthAdvisoryLockKeyTests
{
    private readonly PostgresFixture _pg;
    public AuthAdvisoryLockKeyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hashtext('gamekit.auth.migrations')::bigint";
        var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(AuthMigrationConstants.AdvisoryLockKey, computed);
    }

    [Fact]
    public void AuthKey_Is_Distinct_From_Core_Key()
    {
        Assert.NotEqual(GameKitMigrationConstants.AdvisoryLockKey, AuthMigrationConstants.AdvisoryLockKey);
    }
}
```

---

## Shared Patterns

### License header (every new `.cs` file)

**Source:** every file under `src/` and `tests/`
**Apply to:** every new file (including vendored Glicko-2 sources, which add an extra "Portions BSD-{2|3}-Clause" line per RESEARCH §961-984)

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

For vendored Glicko-2 sources:
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
// Portions BSD-{2|3}-Clause, MaartenStaa 2015 (see THIRD-PARTY-NOTICES.md)
```

License is verified by `tests/GameKit.Core.Tests/LicenseHeaderTests.cs` — the Wave-0 task must add a Rankings discovery rule there.

---

### Token-hash storage discipline

**Source:** `src/GameKit.Auth/Services/RefreshTokenService.cs:280-292`
**Apply to:** `ServiceToken` (Phase 4) — issue → `GenerateRaw` → `Sha256Hex` → store hash only; print raw to stdout exactly once on CLI issue.

```csharp
private static string Sha256Hex(string raw)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

private static string GenerateRaw()
{
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

---

### Transactional mutate + audit pattern (SC#6 anchor)

**Source:** `src/GameKit.Admin.UI/Services/PlayerBanService.cs:42-90`
**Apply to:** `RankAdjustService` (RANK-12, SC#6), `EndSeasonService` (D-11)

Open `IsolationLevel.Serializable` tx → snapshot Before → mutate → `SaveChangesAsync` → audit write (rides the same `DbContext` scope) → `tx.CommitAsync`. A faulty audit write rolls the mutation back.

---

### FluentValidation + endpoint filter pattern

**Source:** `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs:14-24` + `src/GameKit.Admin.UI/Http/EndpointFilters/ValidationEndpointFilter.cs:20-40`
**Apply to:** every new request DTO in Phase 4 (`SessionCompleteRequest`, `RankAdjustRequest`, `EndSeasonRequest`)

```csharp
// Validator
public sealed class BanPlayerRequestValidator : AbstractValidator<BanPlayerRequest>
{
    public BanPlayerRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .MinimumLength(3).WithMessage("Reason must be at least 3 characters.")
            .MaximumLength(512).WithMessage("Reason is too long (max 512 characters).");
    }
}

// Wired via .AddEndpointFilter<ValidationEndpointFilter<TRequest>>() on the endpoint.
```

The literal `3` and `512` character limits from `BanPlayerRequestValidator` are the same bounds RANK-12 D-19 specifies for `reason`. Reuse the exact messages so SC#6 integration tests can share assertion strings.

---

### Per-package migration boundary (Pitfall §3 mitigation)

**Source:** `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs:82-116` + `src/GameKit.Auth/Data/AuthMigrationHostedService.cs:68-84`
**Apply to:** `RankingsDesignTimeDbContextFactory` + `RankingsMigrationHostedService` + `RankingsMigrationModelCustomizer`

Two non-negotiables:
1. Migration `DbContextOptionsBuilder` must `.ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()` — never rely on `IServiceProvider` to inject the model-builder extension. Pitfall §3 documents the global-cache landmine that bit Phase 3.
2. The migration customizer must `ExcludeFromMigrations()` every Core/Auth/Admin entity individually. RESEARCH §Pattern 1 lines 449–469 has the exact loop.

The FK constraint `fk_game_sessions_ladders` is added via raw `migrationBuilder.Sql(...)` in the Rankings `InitialCreate.Up()` (Pitfall §4) — NOT via the model fluent API. This is mandatory.

---

### Antiforgery + admin-policy gate on admin mutations

**Source:** `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs:26-46`
**Apply to:** every new `/admin/api/*` endpoint added in Phase 4 (`/players/{id}/rank-adjust`, `/ladders/{id}/end-season`, `/players/{id}/export`).

Phase-4 RESEARCH Open Q4 directs: duplicate the antiforgery filter into Rankings (or expose it as `public` from Admin.UI). Either way, the same filter shape applies — see excerpt above in "Pattern Assignments / IdempotencyKeyEndpointFilter".

---

## No Analog Found

| File | Role | Data Flow | Reason | Mitigation |
|------|------|-----------|--------|------------|
| `src/GameKit.Rankings/Glicko2/Rating.cs` + 3 siblings | vendored algorithm | pure CPU | First vendored-with-attribution source in repo | Use RESEARCH §961-984 vendoring header pattern; license verification is a Wave-0 task |
| `src/GameKit.Rankings/Services/RankingsTickerService.cs` | BackgroundService | batch | No `BackgroundService` exists in repo today (only `IHostedService` instances) | Use RESEARCH §Pattern 5 (Redis lock) + STACK.md §1 (Polly retry/backoff). Closest IHostedService start/stop shape = `AuthMigrationHostedService:29-66` |
| `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` | Redis helper | pub-sub | First StackExchange.Redis usage in source code (test fixture exists but no production caller) | RESEARCH §Pattern 5 lines 682-700 is the canonical pattern. Use `IDatabase.LockTake / LockExtend / LockRelease` — do NOT hand-roll `SET NX PX` |
| `tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json` | test fixture | static | Source is Glickman 2012 PDF, not a code analog | Wave-0 task transcribes the worked example (player ratings 1500/1400/1550/1700 + outcomes win/loss/loss) per `04-VALIDATION.md` SC#5 anchor |

---

## Metadata

**Analog search scope:**
- `src/GameKit.Core/` — 30 `.cs` files scanned
- `src/GameKit.Auth/` — 50 `.cs` files scanned
- `src/GameKit.Admin.UI/` — 50 `.cs` files + Razor templates scanned
- `src/GameKit.Cli/` — 3 command sources scanned
- `src/GameKit.Rankings/` — confirmed empty stub (csproj + AssemblyInfo only)
- `tests/GameKit.TestFixtures/` — confirmed `RedisFixture.cs` already exists (reuse, no new write)

**Files scanned:** ~140 production sources + ~30 test sources
**Pattern extraction date:** 2026-05-15
**Cross-references:** `04-CONTEXT.md` D-01..D-23; `04-RESEARCH.md` §Recommended Project Structure lines 300–416, §Patterns 1–5 lines 418–700, §Plan Decomposition lines 1356–1369; `04-VALIDATION.md` Wave-0 list lines 78–86.

---

## PATTERN MAPPING COMPLETE
