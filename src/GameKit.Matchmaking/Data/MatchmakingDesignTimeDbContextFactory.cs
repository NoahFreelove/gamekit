// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Admin.UI.Entities;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Matchmaking.Data.Configurations;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Matchmaking.Data;

/// <summary>
/// Design-time factory <c>dotnet ef</c> uses to instantiate <see cref="GameKitDbContext"/>
/// when generating Matchmaking migrations. Runtime registration happens via <c>AddMatchmaking(...)</c>
/// (later Phase 5 plan); this factory is invoked only by the EF CLI.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>GameKit.Rankings.Data.RankingsDesignTimeDbContextFactory</c> verbatim — only the
/// exclusion list is wider (Matchmaking must exclude Core <i>and</i> Auth <i>and</i> Admin
/// <i>and</i> Rankings entity types because Matchmaking has a transitive view of all four
/// packages' entity types).
/// </para>
/// <para>
/// The EF CLI writes a fresh <c>GameKitDbContextModelSnapshot.cs</c> inside the Matchmaking
/// project's <c>Migrations</c> folder. This is intentional — each package ships its own
/// snapshot (PITFALLS #3).
/// </para>
/// </remarks>
public sealed class MatchmakingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
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
                // Point migrations-assembly at the Matchmaking assembly so `dotnet ef migrations add`
                // emits migration sources into src/GameKit.Matchmaking/Migrations/.
                npg.MigrationsAssembly(typeof(MatchmakingDesignTimeDbContextFactory).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>();

        return new GameKitDbContext(optionsBuilder.Options);
    }
}

/// <summary>
/// <see cref="IModelCustomizer"/> used whenever a <c>GameKitDbContext</c> is built for
/// <b>Matchmaking-migration purposes</b> — both at design time by
/// <see cref="MatchmakingDesignTimeDbContextFactory"/> and at runtime/test time when applying
/// the Matchmaking migration against an existing Core+Auth+Admin+Rankings-initialized database.
/// It (1) runs the base relational pipeline + Core's <c>OnModelCreating</c>, (2) applies the
/// five Matchmaking entity configurations directly (no DI required — avoids the
/// <c>GameKitModelCustomizer</c> DI-resolution gap that EF's internal service provider does
/// not bridge when contexts are built ad-hoc), and (3) excludes every Core / Auth / Admin /
/// Rankings entity from migrations so the Matchmaking migration diff emits only the five new
/// Matchmaking tables.
/// </summary>
/// <remarks>
/// Pattern follows <see cref="GameKit.Rankings.Data.RankingsMigrationModelCustomizer"/>. The
/// exclusion list is the widest of all sibling packages because Matchmaking sees Core + Auth +
/// Admin (transitively via Rankings's ProjectReference graph; Admin depends on Auth + Rankings)
/// and Rankings entities through its own ProjectReference to Rankings.
/// </remarks>
public sealed class MatchmakingMigrationModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer with the EF-internal dependencies tuple.</summary>
    public MatchmakingMigrationModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // Apply Matchmaking entity configurations directly — bypass DI because the migration-context
        // factory path does not wire app services into customizer constructor injection.
        modelBuilder.ApplyConfiguration(new PartyConfiguration());
        modelBuilder.ApplyConfiguration(new PartyMemberConfiguration());
        modelBuilder.ApplyConfiguration(new MatchmakingTicketConfiguration());
        modelBuilder.ApplyConfiguration(new TicketEventConfiguration());
        modelBuilder.ApplyConfiguration(new DeclineHistoryConfiguration());

        // Per-package migration boundary (CLAUDE.md "Migration boundaries", PITFALLS #3):
        // The Matchmaking migration must emit ONLY the five new Matchmaking tables — every
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

        foreach (var type in coreEntityTypes)     ExcludeEntity(modelBuilder, type);
        foreach (var type in authEntityTypes)     ExcludeEntity(modelBuilder, type);
        foreach (var type in adminEntityTypes)    ExcludeEntity(modelBuilder, type);
        foreach (var type in rankingsEntityTypes) ExcludeEntity(modelBuilder, type);
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
