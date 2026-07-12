// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Admin.UI.Data;

/// <summary>
/// Design-time factory <c>dotnet ef</c> uses to instantiate <see cref="GameKitDbContext"/>
/// when generating Admin migrations. Runtime registration happens in plan 03-03 via
/// <c>AddGameKitAdmin(...)</c>; this factory is invoked only by the EF CLI.
/// </summary>
/// <remarks>
/// Mirrors <see cref="GameKit.Auth.Data.AuthDesignTimeDbContextFactory"/>. The customizer
/// (<see cref="AdminMigrationModelCustomizer"/>) applies the Admin entity configuration directly
/// and marks every Core + Auth entity <c>ExcludeFromMigrations()</c> — the Admin migration emits
/// ONLY the <c>admin_users</c> table and leaves Core/Auth tables untouched. Per the per-package
/// migration boundary (PITFALLS #3), Admin must only add new tables.
/// <para>
/// The EF CLI writes a fresh <c>GameKitDbContextModelSnapshot.cs</c> inside the Admin project's
/// <c>Migrations</c> folder. This is intentional — each package ships its own snapshot.
/// </para>
/// </remarks>
public sealed class AdminDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
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
                // Point migrations-assembly at the Admin assembly so `dotnet ef migrations add`
                // emits migration sources into src/GameKit.Admin.UI/Migrations/.
                npg.MigrationsAssembly(typeof(AdminDesignTimeDbContextFactory).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>();

        // No UseApplicationServiceProvider — the migration path intentionally has no service
        // provider per FOLLOW-UP-02-03-01 resolution closed in plan 02-08.
        return new GameKitDbContext(optionsBuilder.Options);
    }
}
