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

/// <summary>
/// Design-time factory <c>dotnet ef</c> uses to instantiate <see cref="GameKitDbContext"/>
/// when generating Auth migrations. Runtime registration happens in plan 02-03 via
/// <c>AddAuth(...)</c>; this factory is invoked only by the EF CLI.
/// </summary>
/// <remarks>
/// <para>
/// The shared <see cref="GameKitDbContext"/>.<c>OnModelCreating</c> always applies Core entity
/// configurations (via <c>ApplyConfigurationsFromAssembly(typeof(GameKitDbContext).Assembly)</c>).
/// At design time we cannot rely on the DI-wired runtime <see cref="GameKitModelCustomizer"/>
/// to pick up Auth's <see cref="IModelBuilderExtension"/> — EF's internal service-provider
/// construction path does not flow application services into customizer constructor injection
/// when the context is built via an <see cref="IDesignTimeDbContextFactory{TContext}"/> that
/// calls <c>new GameKitDbContext(options)</c> directly. Instead we replace the customizer with
/// <see cref="AuthMigrationModelCustomizer"/>, which applies the three Auth configurations
/// directly and marks every Core entity <c>ExcludeFromMigrations()</c>.
/// </para>
/// <para>
/// This keeps the per-package migration pattern (PITFALLS #3) — the Auth migration emits ONLY
/// Auth tables and leaves Core tables untouched. Core's migration already owns those tables; per
/// CLAUDE.md migration-boundaries rule, Auth must only add new tables or FK references.
/// </para>
/// <para>
/// The EF CLI writes a fresh <c>GameKitDbContextModelSnapshot.cs</c> inside the Auth project's
/// <c>Migrations</c> folder. This is intentional — each package ships its own snapshot (PITFALLS #3).
/// </para>
/// </remarks>
public sealed class AuthDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
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
                // Point migrations-assembly at the Auth assembly so `dotnet ef migrations add`
                // emits migration sources into src/GameKit.Auth/Migrations/.
                npg.MigrationsAssembly(typeof(AuthDesignTimeDbContextFactory).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>();

        return new GameKitDbContext(optionsBuilder.Options);
    }
}

/// <summary>
/// <see cref="IModelCustomizer"/> used whenever a <see cref="GameKitDbContext"/> is built for
/// <b>Auth-migration purposes</b> — both at design time by <see cref="AuthDesignTimeDbContextFactory"/>
/// and at runtime/test time when applying the Auth migration against an existing Core-initialized
/// database. It (1) runs the base relational pipeline + Core's <c>OnModelCreating</c>, (2) applies
/// the three Auth entity configurations directly (no DI required — avoids the
/// <see cref="GameKitModelCustomizer"/> DI-resolution gap that EF's internal service provider does
/// not bridge when contexts are built ad-hoc), and (3) excludes every Core entity from migrations
/// so the Auth migration diff emits only Auth tables.
/// </summary>
/// <remarks>
/// <b>Not</b> the runtime query customizer — runtime application code uses
/// <see cref="GameKitModelCustomizer"/> (DI-driven, supplies both Core and Auth entities to the
/// query model). This customizer is used only where EF's migration pipeline needs an isolated
/// Auth-only schema view that matches the Auth migration snapshot.
/// </remarks>
public sealed class AuthMigrationModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer with the EF-internal dependencies tuple.</summary>
    public AuthMigrationModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        // Apply Auth entity configurations directly — bypass DI because the migration-context
        // factory path does not wire app services into customizer constructor injection.
        modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());

        // Exclude every Core entity from the Auth migration — Core's migration already owns those
        // tables. This preserves the per-package migration boundary (CLAUDE.md, PITFALLS #3).
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
