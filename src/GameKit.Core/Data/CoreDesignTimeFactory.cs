// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameKit.Core.Data;

/// <summary>
/// Design-time factory <c>dotnet ef</c> uses to instantiate <see cref="GameKitDbContext"/>
/// while generating migrations. Runtime uses <c>AddGameKit(...)</c> in Plan 05 — this factory
/// is invoked only by the EF CLI tooling (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>).
/// </summary>
/// <remarks>
/// At design time, sibling <see cref="IModelBuilderExtension"/> implementations are not available
/// (they live in sibling packages with their own design-time factories). This factory therefore
/// passes an empty extension list to <see cref="GameKitModelCustomizer"/>, producing a Core-only
/// model snapshot — exactly the isolation per-package migrations require.
/// </remarks>
public sealed class CoreDesignTimeFactory : IDesignTimeDbContextFactory<GameKitDbContext>
{
    /// <inheritdoc />
    public GameKitDbContext CreateDbContext(string[] args)
    {
        // WR-13: require GAMEKIT_MIGRATIONS_CONNECTION explicitly — no hardcoded dev password.
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
                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKitMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer, GameKitModelCustomizer>();

        // Core-only snapshot at design time — sibling extensions are absent by construction.
        // (GameKitModelCustomizer's DI-injected IEnumerable<IModelBuilderExtension> resolves to empty
        // because the design-time service provider is the one EF builds internally.)
        return new GameKitDbContext(optionsBuilder.Options);
    }
}
