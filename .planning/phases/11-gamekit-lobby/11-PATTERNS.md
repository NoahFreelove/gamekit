# Phase 11: GameKit.Lobby (New Package) - Pattern Map

**Mapped:** 2026-06-06
**Files analyzed:** 28 (new/modified across src + tests)
**Analogs found:** 26 / 28 (2 greenfield — no codebase analog for SignalR Hub and two-TestServer harness)

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Lobby/GameKit.Lobby.csproj` | config | — | `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` | exact |
| `src/GameKit.Lobby/AssemblyInfo.cs` | config | — | `src/GameKit.Matchmaking/AssemblyInfo.cs` | exact |
| `src/GameKit.Lobby/GameKitLobbyOptions.cs` | config | — | `src/GameKit.Matchmaking/GameKitMatchmakingOptions.cs` | role-match |
| `src/GameKit.Lobby/LobbyOptionsValidator.cs` | config | — | `src/GameKit.Matchmaking/MatchmakingOptionsValidator.cs` | role-match |
| `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` | config | request-response | `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` | exact |
| `src/GameKit.Lobby/Builder/LobbyApplicationBuilderExtensions.cs` | config | request-response | `src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs` | exact |
| `src/GameKit.Lobby/Data/LobbyMigrationConstants.cs` | config | — | `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` | exact |
| `src/GameKit.Lobby/Data/LobbyMigrationHostedService.cs` | service | batch | `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs` | exact |
| `src/GameKit.Lobby/Data/LobbyDesignTimeDbContextFactory.cs` | config | — | `src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs` | exact |
| `src/GameKit.Lobby/Data/LobbyMigrationModelCustomizer.cs` | config | — | `src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs` (inner class) | exact |
| `src/GameKit.Lobby/Data/LobbyModelBuilderExtension.cs` | config | — | `src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs` | exact |
| `src/GameKit.Lobby/Data/Configurations/LobbyConfiguration.cs` | model | CRUD | `src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs` | role-match |
| `src/GameKit.Lobby/Data/Configurations/LobbyMemberConfiguration.cs` | model | CRUD | `src/GameKit.Matchmaking/Data/Configurations/PartyMemberConfiguration.cs` | exact |
| `src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.cs` | migration | batch | `src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` | role-match |
| `src/GameKit.Lobby/Entities/Lobby.cs` | model | CRUD | `src/GameKit.Matchmaking/Entities/Party.cs` | role-match |
| `src/GameKit.Lobby/Entities/LobbyMember.cs` | model | CRUD | `src/GameKit.Matchmaking/Entities/PartyMember.cs` | role-match |
| `src/GameKit.Lobby/Entities/LobbyState.cs` | model | — | `src/GameKit.Matchmaking/Entities/PartyState.cs` | exact |
| `src/GameKit.Lobby/Hubs/LobbyHub.cs` | controller | event-driven | `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` (TryGetPlayerId) | partial-match (greenfield) |
| `src/GameKit.Lobby/Hubs/ILobbyClient.cs` | model | event-driven | `src/GameKit.Matchmaking/Services/IMatchmakingService.cs` | partial-match |
| `src/GameKit.Lobby/Services/ILobbyService.cs` | service | CRUD | `src/GameKit.Matchmaking/Services/IPartyService.cs` | role-match |
| `src/GameKit.Lobby/Services/LobbyService.cs` | service | CRUD | `src/GameKit.Matchmaking/Services/PartyService.cs` + `SerializationFailureRetry.cs` | role-match |
| `src/GameKit.Lobby/Services/ILobbyMessageHandler.cs` | service | event-driven | `src/GameKit.Matchmaking/Services/IChaosInterceptor.cs` (optional seam) | role-match |
| `src/GameKit.Lobby/Http/LobbyEndpoints.cs` | controller | request-response | `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` | exact |
| `src/GameKit.Lobby/Http/Contracts/CreateLobbyRequest.cs` | model | request-response | `src/GameKit.Matchmaking/Http/Contracts/CreatePartyRequest.cs` | role-match |
| `Directory.Packages.props` | config | — | existing file (add one entry) | partial-match |
| `tests/GameKit.Lobby.Integration.Tests/*.csproj` | config | — | `tests/GameKit.Matchmaking.Integration.Tests/*.csproj` | exact |
| `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` | test | request-response | `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` | role-match |
| `tests/GameKit.Lobby.Integration.Tests/LobbyAdvisoryLockKeyTests.cs` | test | CRUD | `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs` | exact |

---

## Pattern Assignments

### `src/GameKit.Lobby/GameKit.Lobby.csproj` (config)

**Analog:** `src/GameKit.Matchmaking/GameKit.Matchmaking.csproj` (lines 1-73)

**Core pattern** (lines 1-73 of analog):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>GameKit.Lobby</PackageId>
    <Description>Lobby package for GameKit — ready-checks, ephemeral chat via SignalR, persistent groups (Postgres). Phase 11.</Description>
    <PackageTags>gamekit;lobby;signalr;redis;gpl</PackageTags>
    <RootNamespace>GameKit.Lobby</RootNamespace>
    <AssemblyName>GameKit.Lobby</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GameKit.Core\GameKit.Core.csproj" />
    <ProjectReference Include="..\GameKit.Rankings\GameKit.Rankings.csproj" />
    <ProjectReference Include="..\GameKit.Auth\GameKit.Auth.csproj" />
    <ProjectReference Include="..\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />
    <ProjectReference Include="..\GameKit.Matchmaking\GameKit.Matchmaking.csproj" />
    <ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Npgsql" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="StackExchange.Redis" />
  </ItemGroup>
</Project>
```

**Key differences from Matchmaking:** add `Microsoft.AspNetCore.SignalR.StackExchangeRedis` reference; add `GameKit.Matchmaking` ProjectReference; no `Polly` reference (Lobby reuses Matchmaking's `SerializationFailureRetry` via the package dep).

---

### `src/GameKit.Lobby/AssemblyInfo.cs` (config)

**Analog:** `src/GameKit.Matchmaking/AssemblyInfo.cs` (lines 1-23)

**Core pattern** (lines 1-23):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GameKit.Lobby.Tests")]
[assembly: InternalsVisibleTo("GameKit.Lobby.Integration.Tests")]
```

Also grant `InternalsVisibleTo` for `GameKit.OpenApi.Integration.Tests` (mirrors Matchmaking line 18) if the lobby hub is exercised from OpenApi contract tests.

---

### `src/GameKit.Lobby/Data/LobbyMigrationConstants.cs` (config)

**Analog:** `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` (lines 1-47)

**Core pattern** (lines 1-47):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Lobby.Data;

/// <summary>
/// Migration-related constants for <c>GameKit.Lobby</c>. Pinned alongside all six
/// sibling-package constants so packages cannot collide on history-table name or
/// advisory-lock key.
/// </summary>
public static class LobbyMigrationConstants
{
    /// <summary>
    /// Per-package migrations history table for <c>GameKit.Lobby</c>.
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_lobby";

    /// <summary>
    /// Postgres advisory-lock key for Lobby migration serialization.
    /// Placeholder — MUST be replaced with live-verified value from
    /// <c>SELECT hashtext('gamekit.lobby.migrations')::bigint</c> on Postgres 17.9.
    /// <c>LobbyAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation</c>
    /// is RED until this is updated (Wave 0 gate).
    /// <para>
    /// <b>MUST</b> differ from Core (1800940027), Auth (-298890956), Admin (-2101739634),
    /// Rankings (-156812172), and Matchmaking (388956820).
    /// </para>
    /// </summary>
    public const long AdvisoryLockKey = 0L; // Wave 0: replace with live value
}
```

---

### `src/GameKit.Lobby/Data/LobbyMigrationHostedService.cs` (service, batch)

**Analog:** `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs` (lines 1-100)

**Core pattern** (lines 36-100 of analog):
```csharp
internal sealed class LobbyMigrationHostedService : IHostedService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly ILogger<LobbyMigrationHostedService> _logger;

    public LobbyMigrationHostedService(
        GameKitOptions gameKitOpts,
        ILogger<LobbyMigrationHostedService> logger)
    {
        _gameKitOpts = gameKitOpts;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_gameKitOpts.AutoMigrate)
        {
            _logger.LogInformation(
                "AutoMigrate=false — skipping Lobby migration apply.");
            return;
        }

        var connectionString = !string.IsNullOrWhiteSpace(_gameKitOpts.MigrationsConnectionString)
            ? _gameKitOpts.MigrationsConnectionString!
            : _gameKitOpts.ConnectionString;

        await using var ctx = BuildLobbyMigrationContext(connectionString);
        _logger.LogInformation("Applying Lobby migrations (history table {Table}).",
            LobbyMigrationConstants.MigrationsHistoryTable);

        await MigrationRunner
            .MigrateWithLockAsync(ctx, LobbyMigrationConstants.AdvisoryLockKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Lobby migrations applied successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static GameKitDbContext BuildLobbyMigrationContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(LobbyMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    LobbyMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, LobbyMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
```

Swap every `Matchmaking` reference to `Lobby`. The `internal sealed` access modifier is mandatory (mirrors Matchmaking line 36).

---

### `src/GameKit.Lobby/Data/LobbyDesignTimeDbContextFactory.cs` + `LobbyMigrationModelCustomizer` (config)

**Analog:** `src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs` (lines 1-161)

**Design-time factory pattern** (lines 35-66 of analog):
```csharp
public sealed class LobbyDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
{
    public GameKitDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "GAMEKIT_MIGRATIONS_CONNECTION environment variable is not set. ...");
        }

        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(LobbyDesignTimeDbContextFactory).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    LobbyMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, LobbyMigrationModelCustomizer>();

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
```

**Migration model customizer exclusion list pattern** (lines 86-161 of analog):
```csharp
public sealed class LobbyMigrationModelCustomizer : RelationalModelCustomizer
{
    public LobbyMigrationModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // Apply Lobby entity configurations directly
        modelBuilder.ApplyConfiguration(new LobbyConfiguration());
        modelBuilder.ApplyConfiguration(new LobbyMemberConfiguration());

        // Core entities (4) — Phase 1
        var coreEntityTypes = new[]
        {
            typeof(Player), typeof(GameSession), typeof(SessionParticipant), typeof(AdminAuditLog),
        };
        // Auth entities (3) — Phase 2
        var authEntityTypes = new[]
        {
            typeof(PlayerIdentity), typeof(PlayerCredential), typeof(RefreshToken),
        };
        // Admin.UI entities (1) — Phase 3
        var adminEntityTypes = new[] { typeof(AdminUser) };
        // Rankings entities (7) — Phase 4
        var rankingsEntityTypes = new[]
        {
            typeof(Ladder), typeof(PlayerRank), typeof(PendingRatingUpdate),
            typeof(SessionCompleteIdempotency), typeof(LadderSeason), typeof(SeasonRankArchive),
            typeof(ServiceToken),
        };
        // Matchmaking entities (5) — Phase 5
        var matchmakingEntityTypes = new[]
        {
            typeof(Party), typeof(PartyMember), typeof(MatchmakingTicket),
            typeof(TicketEvent), typeof(DeclineHistory),
        };

        foreach (var type in coreEntityTypes)       ExcludeEntity(modelBuilder, type);
        foreach (var type in authEntityTypes)        ExcludeEntity(modelBuilder, type);
        foreach (var type in adminEntityTypes)       ExcludeEntity(modelBuilder, type);
        foreach (var type in rankingsEntityTypes)    ExcludeEntity(modelBuilder, type);
        foreach (var type in matchmakingEntityTypes) ExcludeEntity(modelBuilder, type);
    }

    private static void ExcludeEntity(ModelBuilder modelBuilder, Type type)
    {
        var entity = modelBuilder.Model.FindEntityType(type);
        if (entity is null) return;
        var tableName = entity.GetTableName()!;
        var schema = entity.GetSchema();
        modelBuilder.Entity(type).ToTable(tableName, schema, t => t.ExcludeFromMigrations());
    }
}
```

**Critical:** The exclusion list is exactly 20 entities (Matchmaking adds 5 over Rankings's 15). `LobbyMigrationModelCustomizer` must enumerate all 20 explicitly so any future entity addition in a prior package produces a CS0246 compile error here.

---

### `src/GameKit.Lobby/Data/LobbyModelBuilderExtension.cs` (config)

**Analog:** `src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs` (lines 1-28)

**Core pattern** (lines 1-28):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Data;
using GameKit.Lobby.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Lobby.Data;

internal sealed class LobbyModelBuilderExtension : IModelBuilderExtension
{
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new LobbyConfiguration());
        modelBuilder.ApplyConfiguration(new LobbyMemberConfiguration());
    }
}
```

---

### `src/GameKit.Lobby/Data/Configurations/LobbyConfiguration.cs` (model, CRUD)

**Analog:** `src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs` (lines 1-51)

**Core pattern** (lines 23-51 of analog — integer enum, snake_case, FK syntax):
```csharp
internal sealed class LobbyConfiguration : IEntityTypeConfiguration<Lobby>
{
    public void Configure(EntityTypeBuilder<Lobby> b)
    {
        b.ToTable("lobbies");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();

        // Integer enum storage — DO NOT add HasConversion<string>() (Phase 5 mandatory).
        b.Property(l => l.State).IsRequired();

        b.Property(l => l.MaxMembers).IsRequired();
        b.Property(l => l.RegionName);
        b.Property(l => l.CreatedAt).IsRequired();
        b.Property(l => l.UpdatedAt).IsRequired();

        // FK → players ON DELETE SET NULL (owner leaves; lobby persists)
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(l => l.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        // FK → ladders ON DELETE SET NULL
        b.HasOne<Ladder>()
            .WithMany()
            .HasForeignKey(l => l.LadderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
```

---

### `src/GameKit.Lobby/Data/Configurations/LobbyMemberConfiguration.cs` (model, CRUD)

**Analog:** `src/GameKit.Matchmaking/Data/Configurations/PartyMemberConfiguration.cs` (lines 1-53)

**Core pattern** (lines 25-53 of analog):
```csharp
internal sealed class LobbyMemberConfiguration : IEntityTypeConfiguration<LobbyMember>
{
    public void Configure(EntityTypeBuilder<LobbyMember> b)
    {
        b.ToTable("lobby_members");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.LobbyId).IsRequired();
        b.Property(m => m.PlayerId).IsRequired();
        b.Property(m => m.Ready).IsRequired();
        b.Property(m => m.JoinedAt).IsRequired();

        // Composite unique constraint (LobbyId, PlayerId)
        b.HasIndex(m => new { m.LobbyId, m.PlayerId }).IsUnique();

        // FK → lobbies ON DELETE CASCADE
        b.HasOne<Lobby>()
            .WithMany()
            .HasForeignKey(m => m.LobbyId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK → players ON DELETE CASCADE (GDPR: player deletion cascades to membership)
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(m => m.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Note:** Matchmaking's `PartyMemberConfiguration` uses `DeleteBehavior.Restrict` for the player FK (audit trail preservation). The lobby uses `Cascade` because the data model (`lobby_members`) has no audit purpose — it is an ephemeral membership record.

---

### `src/GameKit.Lobby/Entities/LobbyState.cs` (model)

**Analog:** `src/GameKit.Matchmaking/Entities/PartyState.cs` (lines 1-29)

**Core pattern** (lines 1-29):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Lobby.Entities;

/// <summary>
/// Lifecycle state of a <see cref="Lobby"/>. Stored as <c>integer</c> at the SQL level
/// (Phase 5 mandatory pattern — HasConversion&lt;string&gt;() is forbidden).
/// </summary>
public enum LobbyState
{
    /// <summary>Lobby exists; accepting new members.</summary>
    Open = 0,

    /// <summary>All-ready check in progress; members must mark ready before matchmaking.</summary>
    ReadyChecking = 1,

    /// <summary>Locked; no new members. Waiting for matchmaking to complete.</summary>
    Closed = 2,

    /// <summary>Matchmaking submitted; terminal state for this session.</summary>
    InGame = 3,
}
```

---

### `src/GameKit.Lobby/Builder/LobbyBuilderExtensions.cs` (config, request-response)

**Analog:** `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` (lines 1-133)

**Imports pattern** (lines 1-11 of analog):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Lobby.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
```

**Core DI registration pattern** (lines 61-132 of analog — copy structure, change symbols):
```csharp
public static class LobbyBuilderExtensions
{
    public static IGameKitBuilder AddLobby(
        this IGameKitBuilder builder,
        Action<GameKitLobbyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 1. Options + validation
        var optsBuilder = builder.Services.AddOptions<GameKitLobbyOptions>();
        if (configure is not null) optsBuilder.Configure(configure);
        optsBuilder.ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GameKitLobbyOptions>, LobbyOptionsValidator>());

        // 2. Lobby model extension
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, LobbyModelBuilderExtension>());

        // 3. Migration runner
        builder.Services.AddHostedService<LobbyMigrationHostedService>();

        // 4. SignalR + Redis backplane (ChannelPrefix pinned in code per LOBBY-06)
        builder.Services.AddSignalR()
            .AddStackExchangeRedis(options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit");
            });
        // IPostConfigureOptions<RedisOptions> defers IConnectionMultiplexer resolution
        // until after DI is built — avoids BuildServiceProvider() at registration time.
        builder.Services.AddSingleton<IPostConfigureOptions<RedisOptions>, LobbyRedisBackplanePostConfigure>();

        // 5. JWT Bearer WebSocket query-string token extraction
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, LobbyJwtBearerPostConfigure>());

        // 6. Lobby services
        builder.Services.AddScoped<ILobbyService, LobbyService>();

        // 7. Optional relay seam (no-op default)
        builder.Services.TryAddSingleton<ILobbyMessageHandler, NullLobbyMessageHandler>();

        return builder;
    }
}
```

---

### `src/GameKit.Lobby/Builder/LobbyApplicationBuilderExtensions.cs` (config, request-response)

**Analog:** `src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs` (lines 1-59)

**Core pattern** (lines 44-58 of analog — `MapMatchmaking` → `MapLobby`, note SignalR uses `MapHub` not endpoint group):
```csharp
public static class LobbyApplicationBuilderExtensions
{
    public static IApplicationBuilder UseGameKitLobby(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }

    public static IEndpointRouteBuilder MapLobby(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        // SignalR hub — requires MapHub<T>, NOT MapGroup/MapPost
        routes.MapHub<LobbyHub>("/hubs/lobby");
        // REST endpoints (POST /api/lobbies, GET, DELETE members)
        routes.MapLobbyEndpoints();
        return routes;
    }
}
```

---

### `src/GameKit.Lobby/Hubs/LobbyHub.cs` (controller, event-driven) — GREENFIELD

**No codebase analog.** This is the project's first SignalR Hub. Closest partial-match: `TryGetPlayerId` in `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` (lines 255-261) for player ID extraction. `DiscordBackchannelPostConfigure.cs` for the `IPostConfigureOptions<T>` shell. RESEARCH.md Pattern 3 is the authoritative pattern.

**Player-id extraction pattern** from `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` (lines 255-261):
```csharp
// In MatchmakingEndpoints (HTTP context — uses http.User):
private static bool TryGetPlayerId(HttpContext http, out Guid playerId)
{
    playerId = default;
    var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? http.User.FindFirst("sub")?.Value;
    return sub is not null && Guid.TryParse(sub, out playerId);
}
```

**Adapt for Hub context** (Hub uses `Context.User`, NOT `http.User`):
```csharp
// Inside LobbyHub — Context.User replaces HttpContext.User
private Guid GetPlayerId()
{
    var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? Context.User?.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var id))
        throw new HubException("Player identity not found in JWT.");
    return id;
}
```

**CRITICAL anti-pattern:** Do NOT inject `ICurrentPlayer` — `HttpContextCurrentPlayer` reads `IHttpContextAccessor.HttpContext` which is null inside SignalR hub method invocations (verified: `src/GameKit.Core/Services/HttpContextCurrentPlayer.cs`).

**`[Authorize]` attribute:** Applied at class level on the Hub. The `LobbyJwtBearerPostConfigure` (IPostConfigureOptions) handles WebSocket handshake token extraction; `[Authorize]` provides the second layer.

---

### `LobbyJwtBearerPostConfigure` (middleware, request-response)

**Analog:** `src/GameKit.Auth/Providers/Discord/DiscordBackchannelPostConfigure.cs` (lines 1-43) — same `IPostConfigureOptions<T>` shell; `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 130-133 for the `TryAddEnumerable` registration pattern.

**IPostConfigureOptions shell pattern** (lines 26-43 of `DiscordBackchannelPostConfigure.cs`):
```csharp
internal sealed class LobbyJwtBearerPostConfigure : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        // Chain with any existing OnMessageReceived (e.g., from consumer or a prior AddLobby call)
        var existingHandler = options.Events?.OnMessageReceived;
        options.Events ??= new JwtBearerEvents();
        options.Events.OnMessageReceived = async context =>
        {
            if (existingHandler is not null)
                await existingHandler(context);

            if (string.IsNullOrEmpty(context.Token))
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/lobby"))
                {
                    context.Token = accessToken;
                }
            }
        };
    }
}
```

**Registration pattern** (analog: `AuthBuilderExtensions.cs` lines 130-133):
```csharp
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, LobbyJwtBearerPostConfigure>());
```

`TryAddEnumerable` allows future packages to chain additional `IPostConfigureOptions<JwtBearerOptions>` without collision.

---

### `LobbyRedisBackplanePostConfigure` (middleware, event-driven)

**Analog:** `src/GameKit.Auth/Providers/Discord/DiscordBackchannelPostConfigure.cs` (lines 26-43) — same `IPostConfigureOptions<T>` shell for deferred resolution.

**Core pattern:**
```csharp
internal sealed class LobbyRedisBackplanePostConfigure : IPostConfigureOptions<RedisOptions>
{
    private readonly IServiceProvider _sp;

    public LobbyRedisBackplanePostConfigure(IServiceProvider sp) => _sp = sp;

    public void PostConfigure(string? name, RedisOptions options)
    {
        var mux = _sp.GetRequiredService<IConnectionMultiplexer>();
        options.ConnectionFactory = _ => Task.FromResult(mux);
    }
}
```

This is registered as `Singleton` in `AddLobby()`. It defers `IConnectionMultiplexer` resolution to after DI is fully built — the consumer registers their `IConnectionMultiplexer` before the app starts; this post-configure wires it into the SignalR backplane at startup time.

---

### `src/GameKit.Lobby/Services/LobbyService.cs` (service, CRUD)

**Analog (SERIALIZABLE retry):** `src/GameKit.Matchmaking/Services/SerializationFailureRetry.cs` (lines 1-60)

**Retry pipeline pattern** (lines 36-59 of analog — copy verbatim, adjust `operationName`):
```csharp
// Reuse Matchmaking's SerializationFailureRetry.Build() directly (Lobby has a
// ProjectReference to Matchmaking). Do NOT duplicate the Polly pipeline.
var pipeline = SerializationFailureRetry.Build(logger, "LobbyMarkReady");
await pipeline.ExecuteAsync(async ct => { /* SERIALIZABLE tx body */ }, cancellationToken);
```

**SERIALIZABLE transaction body pattern** from RESEARCH.md Pattern 4 (mirrors `IdentityLinker`/`AccountMergeService` from Phase 10):
```csharp
await using var tx = await _ctx.Database
    .BeginTransactionAsync(IsolationLevel.Serializable, ct);

// ... read, mutate, check all-ready ...

if (lobby.Members.All(m => m.Ready) && lobby.State == LobbyState.ReadyChecking)
{
    await TryStartMatchmakingAsync(lobby, ct);
}

await tx.CommitAsync(ct);

// Broadcast AFTER commit via IHubContext<LobbyHub, ILobbyClient>
await _hubContext.Clients.Group($"lobby:{lobbyId}")
    .ReceiveStateUpdateAsync(new LobbyStateUpdate(lobby.State));
```

**IPartyService.CreateAsync signature** (verified from `src/GameKit.Matchmaking/Services/IPartyService.cs` line 44):
```csharp
// IPartyService.CreateAsync ONLY accepts a single ownerPlayerId — it does NOT accept
// a list of memberIds. Lobby must call CreateAsync for the owner, then call JoinAsync
// for each additional member, OR create the Party row directly + add PartyMembers
// without going through IPartyService (which enforces single-active-party-per-player
// at the SERIALIZABLE level for each JoinAsync call).
Task<Party> CreateAsync(Guid ownerPlayerId, CancellationToken ct = default);
```

**IMPORTANT — A1 assumption resolved:** `IPartyService.CreateAsync` takes only `ownerPlayerId`, NOT a list of member IDs. `TryStartMatchmakingAsync` in LobbyService must create the Party for the owner, then add members via `JoinAsync` (or use a custom party-creation path). The RESEARCH.md Pattern 4 assumed `CreateAsync(ownerId, memberIds)` — this is incorrect per the actual interface.

**IMatchmakingService.EnqueueAsync signature** (verified from `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` line 91):
```csharp
// Verified call site:
var result = await svc.EnqueueAsync(playerId, req.LadderId, resolvedPool, req.PartyId, ct);
// Signature: (Guid playerId, Guid ladderId, string? poolName, Guid? partyId, CancellationToken ct)
```

---

### `src/GameKit.Lobby/Http/LobbyEndpoints.cs` (controller, request-response)

**Analog:** `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` (lines 1-73 for mapping pattern, lines 255-261 for player-id extraction)

**Imports + mapping pattern** (lines 1-73 of analog):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Security.Claims;
using GameKit.Lobby.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Lobby.Http;

public static class LobbyEndpoints
{
    public static IEndpointRouteBuilder MapLobbyEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/lobbies", CreateLobbyAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidationEndpointFilter<CreateLobbyRequest>>();

        routes.MapGet("/api/lobbies/{lobbyId:guid}", GetLobbyAsync)
            .RequireAuthorization();

        routes.MapDelete("/api/lobbies/{lobbyId:guid}/members/{playerId:guid}", RemoveMemberAsync)
            .RequireAuthorization();

        return routes;
    }
    // ... handlers use same TryGetPlayerId pattern as MatchmakingEndpoints lines 255-261
}
```

---

### `tests/GameKit.Lobby.Integration.Tests/*.csproj` (config)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/*.csproj`

**Core pattern:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>GameKit.Lobby.Integration.Tests</RootNamespace>
    <AssemblyName>GameKit.Lobby.Integration.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <WarningsAsErrors />
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql" />
    <PackageReference Include="StackExchange.Redis" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Testcontainers.Redis" />
    <!-- New: SignalR test client for HubConnectionBuilder -->
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GameKit.Lobby\GameKit.Lobby.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Core\GameKit.Core.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Rankings\GameKit.Rankings.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Auth\GameKit.Auth.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Admin.UI\GameKit.Admin.UI.csproj" />
    <ProjectReference Include="..\..\src\GameKit.Matchmaking\GameKit.Matchmaking.csproj" />
    <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  </ItemGroup>
</Project>
```

---

### `tests/GameKit.Lobby.Integration.Tests/CollectionDefinitions.cs` (config)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` (lines 1-26) — copy verbatim, change namespace and collection names:

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

[CollectionDefinition("Lobby")]
public sealed class LobbyCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }
```

---

### `tests/GameKit.Lobby.Integration.Tests/LobbyAdvisoryLockKeyTests.cs` (test, CRUD)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs` (lines 1-94)

**Core pattern** (lines 44-93 of analog — copy verbatim, swap string + constants):
```csharp
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class LobbyAdvisoryLockKeyTests
{
    private readonly PostgresFixture _pg;
    public LobbyAdvisoryLockKeyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hashtext('gamekit.lobby.migrations')::bigint";
        var computed = (long)(await cmd.ExecuteScalarAsync() ?? 0L);

        Assert.Equal(LobbyMigrationConstants.AdvisoryLockKey, computed);
    }

    [Fact]
    public void LobbyKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Matchmaking_Keys()
    {
        // Symbolic non-equality
        Assert.NotEqual(GameKitMigrationConstants.AdvisoryLockKey,       LobbyMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AuthMigrationConstants.AdvisoryLockKey,          LobbyMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(AdminMigrationConstants.AdvisoryLockKey,         LobbyMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(RankingsMigrationConstants.AdvisoryLockKey,      LobbyMigrationConstants.AdvisoryLockKey);
        Assert.NotEqual(MatchmakingMigrationConstants.AdvisoryLockKey,   LobbyMigrationConstants.AdvisoryLockKey);

        // Defense-in-depth: integer literals
        Assert.NotEqual(1800940027L,  LobbyMigrationConstants.AdvisoryLockKey);  // Core
        Assert.NotEqual(-298890956L,  LobbyMigrationConstants.AdvisoryLockKey);  // Auth
        Assert.NotEqual(-2101739634L, LobbyMigrationConstants.AdvisoryLockKey);  // Admin
        Assert.NotEqual(-156812172L,  LobbyMigrationConstants.AdvisoryLockKey);  // Rankings
        Assert.NotEqual(388956820L,   LobbyMigrationConstants.AdvisoryLockKey);  // Matchmaking
    }
}
```

---

### `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` (test, request-response)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` (lines 1-280)

**Redis multiplexer replacement pattern** (lines 162-166 of analog — used identically in LobbyTestApp):
```csharp
var muxDescriptor = services.FirstOrDefault(
    d => d.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer));
if (muxDescriptor is not null) services.Remove(muxDescriptor);
services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(redis.ConnectionString));
```

**TestModelCustomizer wiring pattern** (lines 173-175 of analog):
```csharp
services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
    dbOpts.UseNpgsql(ConnectionString)
          .ReplaceService<IModelCustomizer, LobbyTestModelCustomizer>());
```

`LobbyTestModelCustomizer` must apply Lobby + Matchmaking + Rankings entity configurations (Lobby queries `lobbies`, `lobby_members`; LobbyService calls `IMatchmakingService` which requires `matchmaking_tickets`; `lobbies.LadderId` FK targets `ladders`).

**MintPlayerJwt pattern** (lines 213-234 of analog — copy verbatim, change issuer/audience strings):
```csharp
public string MintPlayerJwt(Guid playerId)
{
    var creds = new SigningCredentials(new RsaSecurityKey(_signingRsa), SecurityAlgorithms.RsaSha256)
    {
        CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
    };
    var now = DateTime.UtcNow;
    var token = new JwtSecurityToken(
        issuer: Issuer,
        audience: Audience,
        claims: new[]
        {
            new Claim("sub", playerId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, playerId.ToString()),
            new Claim("is_guest", "false"),
            new Claim("provider", "test"),
        },
        notBefore: now.AddMinutes(-1),
        expires: now.AddHours(1),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}
```

**Pipeline configuration** (analog lines 179-191 — add `UseWebSockets()` before routing for SC#2/SC#5):
```csharp
web.Configure(app =>
{
    app.UseWebSockets();   // Required for TestServer WebSocket tests (Pitfall 7)
    app.UseRouting();
    app.UseRateLimiter();
    app.UseGameKitAuth();
    app.UseGameKit();
    app.UseEndpoints(e =>
    {
        e.MapAuth();
        e.MapGameKit();
        e.MapMatchmaking();
        e.MapLobby();      // MapHub<LobbyHub> + lobby REST endpoints
    });
});
```

---

### `tests/GameKit.Lobby.Integration.Tests/IntegrationTestHelpers.cs` (test, CRUD)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs` (lines 1-208)

**CreateFreshDatabase + ApplyMigrations pattern** (lines 27-82 of analog):

```csharp
public static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
{
    // Copy lines 27-51 of analog verbatim, change db name prefix to "gamekit_lobby_"
}

public static async Task ApplyLobbyMigrationsAsync(string cs)
{
    // Apply Core → (Auth) → Rankings → Matchmaking → Lobby in order
    // Copy analog pattern lines 54-82, append Lobby migration step:
    await using (var lobbyCtx = BuildLobbyMigrationContext(cs))
    {
        await MigrationRunner.MigrateWithLockAsync(
            lobbyCtx,
            LobbyMigrationConstants.AdvisoryLockKey);
    }
}
```

---

### `Directory.Packages.props` (config) — two new entries

**Analog:** existing `Directory.Packages.props` — add two `PackageVersion` entries following existing alphabetical order:

```xml
<!-- In Directory.Packages.props, add: -->
<PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.8" />
<PackageVersion Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.8" />
```

`SignalR.Client` is test-only (used in `HubConnectionBuilder` in integration tests). `StackExchangeRedis` backplane is a runtime dep of `GameKit.Lobby`.

---

## Shared Patterns

### GPL Header
**Source:** `src/GameKit.Matchmaking/AssemblyInfo.cs` lines 1-2
**Apply to:** every new `.cs` file in `src/GameKit.Lobby/` and `tests/GameKit.Lobby.Integration.Tests/`
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

### Integer Enum Storage
**Source:** `src/GameKit.Matchmaking/Entities/PartyState.cs` lines 1-29; `src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs` lines 33-36
**Apply to:** `LobbyState.cs`, `LobbyConfiguration.cs`
```csharp
// Entity class: enum property, no HasConversion<string>()
b.Property(x => x.State).IsRequired();  // integer at SQL level
// DO NOT add: .HasConversion<string>()
```

### SERIALIZABLE Transaction + 40001 Retry
**Source:** `src/GameKit.Matchmaking/Services/SerializationFailureRetry.cs` lines 29-59
**Apply to:** `LobbyService.MarkReadyAsync` (all-ready transition path)
```csharp
// Reuse Matchmaking's helper (Lobby has ProjectReference to Matchmaking):
var pipeline = SerializationFailureRetry.Build(_logger, "LobbyMarkReady");
await pipeline.ExecuteAsync(async ct => {
    await using var tx = await _ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    // ... mutate, check, start matchmaking ...
    await tx.CommitAsync(ct);
}, ct);
```

### TryGetPlayerId (HTTP)
**Source:** `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` lines 255-261
**Apply to:** `src/GameKit.Lobby/Http/LobbyEndpoints.cs`
```csharp
private static bool TryGetPlayerId(HttpContext http, out Guid playerId)
{
    playerId = default;
    var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? http.User.FindFirst("sub")?.Value;
    return sub is not null && Guid.TryParse(sub, out playerId);
}
```

### GetPlayerId (Hub)
**Source:** derived from `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` lines 255-261; adapted per RESEARCH.md Pitfall 1
**Apply to:** `src/GameKit.Lobby/Hubs/LobbyHub.cs`
```csharp
// Hub uses Context.User, not HttpContext.User
// NEVER use ICurrentPlayer in hub methods — HttpContext is null in SignalR invocations
private Guid GetPlayerId()
{
    var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? Context.User?.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var id))
        throw new HubException("Player identity not found in JWT.");
    return id;
}
```

### IPostConfigureOptions Registration
**Source:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 130-133
**Apply to:** `LobbyBuilderExtensions.AddLobby()` for both `LobbyJwtBearerPostConfigure` and `LobbyRedisBackplanePostConfigure`
```csharp
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IPostConfigureOptions<T>, TPostConfigure>());
```

### MigrationModelCustomizer ExcludeEntity Helper
**Source:** `src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs` lines 153-160
**Apply to:** `LobbyMigrationModelCustomizer`
```csharp
private static void ExcludeEntity(ModelBuilder modelBuilder, Type type)
{
    var entity = modelBuilder.Model.FindEntityType(type);
    if (entity is null) return;
    var tableName = entity.GetTableName()!;
    var schema = entity.GetSchema();
    modelBuilder.Entity(type).ToTable(tableName, schema, t => t.ExcludeFromMigrations());
}
```

### TestServer IConnectionMultiplexer Replacement
**Source:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` lines 162-166
**Apply to:** `LobbyTestApp.StartAsync`, both AppA and AppB (two-TestServer backplane test)
```csharp
var muxDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
if (muxDescriptor is not null) services.Remove(muxDescriptor);
services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redis.ConnectionString));
```

---

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `tests/GameKit.Lobby.Integration.Tests/BackplaneTests.cs` | test | event-driven | No two-TestServer + SignalR.Client harness exists — first usage of `HubConnectionBuilder` in test suite. RESEARCH.md Pattern 6 is the authoritative pattern. |
| `tests/GameKit.Lobby.Integration.Tests/HubAuthTests.cs` | test | request-response | No WebSocket auth test exists. Planner uses RESEARCH.md Pattern 2 (negotiate endpoint + 401 assertion) with `WebApplicationFactory`-style approach via TestServer. |

---

## Key Warnings for Planner

1. **IPartyService.CreateAsync signature mismatch (A1 resolved):** The actual signature is `Task<Party> CreateAsync(Guid ownerPlayerId, CancellationToken ct = default)` — it creates a party for one owner only, with no member list. `TryStartMatchmakingAsync` in LobbyService must call `CreateAsync(ownerId)` then `JoinAsync(code, memberId)` for each non-owner member, OR bypass IPartyService and insert Party + PartyMember rows directly. The RESEARCH.md Pattern 4 pseudocode's `CreateAsync(ownerId, memberIds)` call does NOT match the real interface.

2. **`AddSignalR().AddStackExchangeRedis()` must be chained** (RESEARCH.md Pitfall 3): `builder.Services.AddSignalR().AddStackExchangeRedis(...)` — `AddStackExchangeRedis` is an extension on `ISignalRServerBuilder`, not on `IServiceCollection`.

3. **`ChannelPrefix` pinned in code** (RESEARCH.md Pitfall 6): `RedisChannel.Literal("GameKit")` — not configurable via options. All deployed Lobby instances must share the same prefix.

4. **OnConnectedAsync must re-add to SignalR groups** (RESEARCH.md Pitfall 2): SignalR group membership is per-connection. Override `OnConnectedAsync` in `LobbyHub` to query `lobby_members` and re-add the new `ConnectionId` to their lobby groups.

5. **LOBBY-04 enforcement:** No `lobby_messages` entity, no persistence of any kind in `SendChatMessageAsync` or `ILobbyMessageHandler`. SC#4 test asserts `SELECT table_name FROM information_schema.tables WHERE table_schema='gamekit' AND table_name LIKE 'lobby_message%'` returns zero rows.

---

## Metadata

**Analog search scope:** `src/GameKit.Matchmaking/`, `src/GameKit.Auth/`, `tests/GameKit.Matchmaking.Integration.Tests/`
**Files scanned:** 27 source files read in full
**Pattern extraction date:** 2026-06-06

---

## PATTERN MAPPING COMPLETE

**Phase:** 11 — GameKit.Lobby (New Package)
**Files classified:** 28
**Analogs found:** 26 / 28

### Coverage
- Files with exact analog: 10
- Files with role-match analog: 16
- Files with no analog: 2 (BackplaneTests, HubAuthTests — first SignalR test usage)

### Key Patterns Identified
1. Entire package skeleton mirrors `GameKit.Matchmaking` exactly — csproj, AssemblyInfo, migration constants, design-time factory, migration model customizer, migration hosted service, model builder extension, builder extensions, application builder extensions
2. Exclusion list is 20 entities (Core 4 + Auth 3 + Admin 1 + Rankings 7 + Matchmaking 5) — Matchmaking's list + 5 new Matchmaking entities
3. `IPostConfigureOptions<JwtBearerOptions>` (chaining pattern from `DiscordBackchannelPostConfigure`) + `IPostConfigureOptions<RedisOptions>` (deferred `IConnectionMultiplexer` resolution) are the two genuinely new post-configure registrations
4. Player-id extraction inside Hub uses `Context.User.FindFirst("sub")` not `ICurrentPlayer` — critical Hub-vs-HTTP context distinction
5. SERIALIZABLE retry reuses Matchmaking's `SerializationFailureRetry.Build()` directly (no Polly re-registration needed)
6. TestServer Redis multiplexer replacement pattern from `MatchmakingTestApp` lines 162-166 is copied verbatim into `LobbyTestApp`, applied to both AppA and AppB for the two-TestServer backplane test
7. `IPartyService.CreateAsync` accepts only `ownerPlayerId` (not a member list) — planner must adapt `TryStartMatchmakingAsync` accordingly

### File Created
`/home/noah/Desktop/projects/gamekit/.planning/phases/11-gamekit-lobby/11-PATTERNS.md`

### Ready for Planning
Pattern mapping complete. Planner can now reference analog patterns in PLAN.md files.
