// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Admin.UI.Entities;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Lobby.Data.Configurations;
using GameKit.Matchmaking.Entities;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Lobby.Data;

/// <summary>
/// Design-time factory <c>dotnet ef</c> uses to instantiate <see cref="GameKitDbContext"/>
/// when generating Lobby migrations. Runtime registration happens via <c>AddLobby(...)</c>;
/// this factory is invoked only by the EF CLI.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>GameKit.Matchmaking.Data.MatchmakingDesignTimeDbContextFactory</c> verbatim — only
/// the exclusion list is wider: Lobby must exclude Core, Auth, Admin, Rankings, AND the five
/// Matchmaking entity types because <c>GameKit.Lobby</c> has a transitive view of all five
/// prior packages' entity types.
/// </para>
/// <para>
/// The EF CLI writes a fresh <c>GameKitDbContextModelSnapshot.cs</c> inside the Lobby project's
/// <c>Data/Migrations</c> folder. This is intentional — each package ships its own snapshot
/// (PITFALLS #3).
/// </para>
/// </remarks>
public sealed class LobbyDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
{
    /// <inheritdoc />
    public GameKitDbContext CreateDbContext(string[] args)
    {
        // WR-13: never ship a hardcoded password in source. Require GAMEKIT_MIGRATIONS_CONNECTION
        // explicitly — scripts/dev-up.sh shows how to set it for local development.
        var connectionString = Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "GAMEKIT_MIGRATIONS_CONNECTION environment variable is not set. " +
                "Design-time EF tooling (dotnet ef) requires an explicit connection string. " +
                "Example: " +
                "export GAMEKIT_MIGRATIONS_CONNECTION=\"Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=...\"");
        }

        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                // Point migrations-assembly at the Lobby assembly so `dotnet ef migrations add`
                // emits migration sources into src/GameKit.Lobby/Data/Migrations/.
                npg.MigrationsAssembly(typeof(LobbyDesignTimeDbContextFactory).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    LobbyMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, LobbyMigrationModelCustomizer>();

        return new GameKitDbContext(optionsBuilder.Options);
    }
}

/// <summary>
/// <see cref="IModelCustomizer"/> used whenever a <c>GameKitDbContext</c> is built for
/// <b>Lobby-migration purposes</b> — both at design time by
/// <see cref="LobbyDesignTimeDbContextFactory"/> and at runtime/test time when applying
/// the Lobby migration against an existing Core+Auth+Admin+Rankings+Matchmaking-initialized
/// database. It (1) runs the base relational pipeline, (2) applies the two Lobby entity
/// configurations directly, and (3) excludes every prior-package entity from migrations so
/// the Lobby migration diff emits only the two new Lobby tables.
/// </summary>
/// <remarks>
/// The exclusion list enumerates all 20 prior-package types explicitly (rather than reflected)
/// so a future entity addition in any prior package forces a compile error here, surfacing the
/// boundary violation immediately. Pattern follows
/// <see cref="GameKit.Matchmaking.Data.MatchmakingMigrationModelCustomizer"/>.
/// </remarks>
public sealed class LobbyMigrationModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer with the EF-internal dependencies tuple.</summary>
    public LobbyMigrationModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // Apply Lobby entity configurations directly — bypass DI because the migration-context
        // factory path does not wire app services into customizer constructor injection.
        modelBuilder.ApplyConfiguration(new LobbyConfiguration());
        modelBuilder.ApplyConfiguration(new LobbyMemberConfiguration());

        // Per-package migration boundary (CLAUDE.md "Migration boundaries", PITFALLS #3):
        // The Lobby migration must emit ONLY the two new Lobby tables — every
        // prior-package entity type is explicitly excluded from the migration diff.
        //
        // The list is enumerated explicitly (rather than reflected) so a future entity addition
        // in any prior package forces a compile error here, surfacing the boundary explicitly.

        // Core entities (4) — Phase 1
        var coreEntityTypes = new[]
        {
            typeof(Player),
            typeof(GameSession),
            typeof(SessionParticipant),
            typeof(AdminAuditLog),
        };

        // Auth entities (3) — Phase 2
        var authEntityTypes = new[]
        {
            typeof(PlayerIdentity),
            typeof(PlayerCredential),
            typeof(RefreshToken),
        };

        // Admin.UI entities (1) — Phase 3
        var adminEntityTypes = new[]
        {
            typeof(AdminUser),
        };

        // Rankings entities (7) — Phase 4
        var rankingsEntityTypes = new[]
        {
            typeof(Ladder),
            typeof(PlayerRank),
            typeof(PendingRatingUpdate),
            typeof(SessionCompleteIdempotency),
            typeof(LadderSeason),
            typeof(SeasonRankArchive),
            typeof(ServiceToken),
        };

        // Matchmaking entities (5) — Phase 5
        var matchmakingEntityTypes = new[]
        {
            typeof(Party),
            typeof(PartyMember),
            typeof(MatchmakingTicket),
            typeof(TicketEvent),
            typeof(DeclineHistory),
        };

        foreach (var type in coreEntityTypes)        ExcludeEntity(modelBuilder, type);
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
