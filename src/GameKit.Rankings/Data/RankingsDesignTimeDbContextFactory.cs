// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Rankings.Data.Configurations;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Rankings.Data;

/// <summary>
/// Design-time factory <c>dotnet ef</c> uses to instantiate <see cref="GameKitDbContext"/>
/// when generating Rankings migrations. Runtime registration happens in plan 04-04 via
/// <c>AddRankings(...)</c>; this factory is invoked only by the EF CLI.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>GameKit.Auth.Data.AuthDesignTimeDbContextFactory</c> verbatim (per
/// PATTERNS.md line 25). The customizer (<see cref="RankingsMigrationModelCustomizer"/>)
/// applies the seven Rankings entity configurations directly and marks every Core entity
/// <c>ExcludeFromMigrations()</c> — the Rankings migration emits ONLY Rankings tables
/// (and the raw-SQL FK to <c>game_sessions.ladder_id</c> added manually in
/// <c>20260515000000_RankingsInitial.cs</c>).
/// </para>
/// <para>
/// Rankings has no <c>ProjectReference</c> to Auth or Admin, so Auth and Admin entity
/// types cannot appear in the model graph — no exclusion needed for those packages.
/// </para>
/// <para>
/// The EF CLI writes a fresh <c>GameKitDbContextModelSnapshot.cs</c> inside the Rankings
/// project's <c>Migrations</c> folder. This is intentional — each package ships its own
/// snapshot (PITFALLS #3).
/// </para>
/// </remarks>
public sealed class RankingsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
{
    /// <inheritdoc />
    public GameKitDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";

        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                // Point migrations-assembly at the Rankings assembly so `dotnet ef migrations add`
                // emits migration sources into src/GameKit.Rankings/Migrations/.
                npg.MigrationsAssembly(typeof(RankingsDesignTimeDbContextFactory).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>();

        return new GameKitDbContext(optionsBuilder.Options);
    }
}

/// <summary>
/// <see cref="IModelCustomizer"/> used whenever a <c>GameKitDbContext</c> is built for
/// <b>Rankings-migration purposes</b> — both at design time by <see cref="RankingsDesignTimeDbContextFactory"/>
/// and at runtime/test time when applying the Rankings migration. It (1) runs the base relational
/// pipeline + Core's <c>OnModelCreating</c>, (2) applies the seven Rankings entity configurations
/// directly (no DI required — avoids the <c>GameKitModelCustomizer</c> DI-resolution gap
/// that EF's internal service provider does not bridge when contexts are built ad-hoc), and
/// (3) excludes every Core entity from migrations so the Rankings migration diff emits
/// only Rankings tables.
/// </summary>
/// <remarks>
/// Pattern follows <c>GameKit.Auth.Data.AuthMigrationModelCustomizer</c> exactly.
/// Rankings excludes the same four Core entities. Auth and Admin entities are not in scope
/// (Rankings has no assembly reference to those packages).
/// </remarks>
public sealed class RankingsMigrationModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer with the EF-internal dependencies tuple.</summary>
    public RankingsMigrationModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // Apply Rankings entity configurations directly — bypass DI because the migration-context
        // factory path does not wire app services into customizer constructor injection.
        modelBuilder.ApplyConfiguration(new LadderConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerRankConfiguration());
        modelBuilder.ApplyConfiguration(new LadderSeasonConfiguration());
        modelBuilder.ApplyConfiguration(new SeasonRankArchiveConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceTokenConfiguration());
        modelBuilder.ApplyConfiguration(new PendingRatingUpdateConfiguration());
        modelBuilder.ApplyConfiguration(new SessionCompleteIdempotencyConfiguration());

        // Exclude every Core entity from the Rankings migration — Core's migration already owns
        // those tables. This preserves the per-package migration boundary (CLAUDE.md, PITFALLS #3).
        var coreEntityTypes = new[]
        {
            typeof(Player),
            typeof(GameSession),
            typeof(SessionParticipant),
            typeof(AdminAuditLog),
        };
        foreach (var type in coreEntityTypes)
        {
            var entity = modelBuilder.Model.FindEntityType(type);
            if (entity is null) continue;
            var tableName = entity.GetTableName()!;
            var schema = entity.GetSchema();
            modelBuilder.Entity(type).ToTable(tableName, schema, t => t.ExcludeFromMigrations());
        }
    }
}
