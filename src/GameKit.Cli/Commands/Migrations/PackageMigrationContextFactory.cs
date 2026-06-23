// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Core.Data;
using GameKit.Lobby.Data;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Cli.Commands.Migrations;

/// <summary>
/// Describes a GameKit package's migration context parameters. Used by
/// <see cref="PackageMigrationContextFactory"/> to build per-package <see cref="GameKitDbContext"/>
/// instances for migration operations.
/// </summary>
/// <param name="DisplayName">Human-readable package name (e.g. "Core", "Auth").</param>
/// <param name="CanonicalOrder">1-based order index in the recommended application sequence.</param>
/// <param name="MigrationsAssemblyFullName">Full assembly name where migration classes reside.</param>
/// <param name="MigrationsHistoryTable">Per-package EF migrations history table name.</param>
/// <param name="SchemaName">Postgres schema that owns all GameKit tables (always "gamekit").</param>
/// <param name="AdvisoryLockKey">Postgres advisory lock key for serialized migration application.</param>
/// <param name="CustomizerType">The <see cref="IModelCustomizer"/> type to replace with.</param>
public sealed record PackageDescriptor(
    string DisplayName,
    int CanonicalOrder,
    string MigrationsAssemblyFullName,
    string MigrationsHistoryTable,
    string SchemaName,
    long AdvisoryLockKey,
    Type CustomizerType);

/// <summary>
/// Builds per-package <see cref="GameKitDbContext"/> instances for migration operations
/// (list, apply, dry-run). Mirrors the pattern used in each package's design-time factory
/// and migration hosted service: <c>UseNpgsql(…).ReplaceService&lt;IModelCustomizer, T&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Packages"/> list defines the canonical application order:
/// Core → Auth → Admin → Rankings → Matchmaking → Lobby. Operators must apply packages in
/// this order because later packages declare FK dependencies on tables created by earlier ones.
/// </para>
/// <para>
/// All six <c>*MigrationModelCustomizer</c> classes are <c>public sealed</c> — no
/// <c>InternalsVisibleTo</c> grant is required for this factory to instantiate them.
/// (GameKit.Rankings grants <c>InternalsVisibleTo("gamekit")</c> for its internal types, but
/// <c>RankingsMigrationModelCustomizer</c> itself is public.)
/// </para>
/// </remarks>
public static class PackageMigrationContextFactory
{
    /// <summary>
    /// Ordered list of all 6 GameKit package migration descriptors in canonical application order.
    /// Core → Auth → Admin → Rankings → Matchmaking → Lobby.
    /// </summary>
    public static readonly IReadOnlyList<PackageDescriptor> Packages = new[]
    {
        new PackageDescriptor(
            DisplayName: "Core",
            CanonicalOrder: 1,
            MigrationsAssemblyFullName: typeof(GameKitDbContext).Assembly.FullName!,
            MigrationsHistoryTable: GameKitMigrationConstants.MigrationsHistoryTable,
            SchemaName: GameKitMigrationConstants.SchemaName,
            AdvisoryLockKey: GameKitMigrationConstants.AdvisoryLockKey,
            CustomizerType: typeof(GameKitModelCustomizer)),

        new PackageDescriptor(
            DisplayName: "Auth",
            CanonicalOrder: 2,
            MigrationsAssemblyFullName: typeof(AuthDesignTimeDbContextFactory).Assembly.FullName!,
            MigrationsHistoryTable: AuthMigrationConstants.MigrationsHistoryTable,
            SchemaName: GameKitMigrationConstants.SchemaName,
            AdvisoryLockKey: AuthMigrationConstants.AdvisoryLockKey,
            CustomizerType: typeof(AuthMigrationModelCustomizer)),

        new PackageDescriptor(
            DisplayName: "Admin",
            CanonicalOrder: 3,
            MigrationsAssemblyFullName: typeof(AdminMigrationConstants).Assembly.FullName!,
            MigrationsHistoryTable: AdminMigrationConstants.MigrationsHistoryTable,
            SchemaName: GameKitMigrationConstants.SchemaName,
            AdvisoryLockKey: AdminMigrationConstants.AdvisoryLockKey,
            CustomizerType: typeof(AdminMigrationModelCustomizer)),

        new PackageDescriptor(
            DisplayName: "Rankings",
            CanonicalOrder: 4,
            MigrationsAssemblyFullName: typeof(RankingsDesignTimeDbContextFactory).Assembly.FullName!,
            MigrationsHistoryTable: RankingsMigrationConstants.MigrationsHistoryTable,
            SchemaName: GameKitMigrationConstants.SchemaName,
            AdvisoryLockKey: RankingsMigrationConstants.AdvisoryLockKey,
            CustomizerType: typeof(RankingsMigrationModelCustomizer)),

        new PackageDescriptor(
            DisplayName: "Matchmaking",
            CanonicalOrder: 5,
            MigrationsAssemblyFullName: typeof(MatchmakingDesignTimeDbContextFactory).Assembly.FullName!,
            MigrationsHistoryTable: MatchmakingMigrationConstants.MigrationsHistoryTable,
            SchemaName: GameKitMigrationConstants.SchemaName,
            AdvisoryLockKey: MatchmakingMigrationConstants.AdvisoryLockKey,
            CustomizerType: typeof(MatchmakingMigrationModelCustomizer)),

        new PackageDescriptor(
            DisplayName: "Lobby",
            CanonicalOrder: 6,
            MigrationsAssemblyFullName: typeof(LobbyDesignTimeDbContextFactory).Assembly.FullName!,
            MigrationsHistoryTable: LobbyMigrationConstants.MigrationsHistoryTable,
            SchemaName: GameKitMigrationConstants.SchemaName,
            AdvisoryLockKey: LobbyMigrationConstants.AdvisoryLockKey,
            CustomizerType: typeof(LobbyMigrationModelCustomizer)),
    };

    /// <summary>
    /// Builds a <see cref="GameKitDbContext"/> configured for <paramref name="package"/>'s
    /// migration assembly, history table, and schema. The customizer replaces the default
    /// <see cref="IModelCustomizer"/> so EF emits only this package's tables in the migration diff.
    /// </summary>
    /// <param name="package">The package descriptor from <see cref="Packages"/>.</param>
    /// <param name="connectionString">Resolved Postgres connection string (gamekit_owner role recommended).</param>
    /// <returns>A configured <see cref="GameKitDbContext"/> ready for migration introspection.</returns>
    public static GameKitDbContext BuildContext(PackageDescriptor package, string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(package.MigrationsAssemblyFullName);
                npg.MigrationsHistoryTable(package.MigrationsHistoryTable, package.SchemaName);
            });

        // ReplaceService<TService, TImplementation>() is a generic instance method on DbContextOptionsBuilder.
        // We need to call it with a runtime-determined customizer type, so use reflection to invoke
        // the two-type-argument overload: ReplaceService<IModelCustomizer, <CustomizerType>>().
        // Signature: DbContextOptionsBuilder ReplaceService<TService, TImplementation>()  (no parameters).
        var replaceServiceMethod = typeof(DbContextOptionsBuilder)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
                m.Name == nameof(optionsBuilder.ReplaceService) &&
                m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 2 &&
                m.GetParameters().Length == 0);

        if (replaceServiceMethod is null)
            throw new InvalidOperationException(
                "Could not locate DbContextOptionsBuilder.ReplaceService<TService, TImplementation>(). " +
                "EF Core API may have changed — update PackageMigrationContextFactory.");

        var boundMethod = replaceServiceMethod.MakeGenericMethod(typeof(IModelCustomizer), package.CustomizerType);
        boundMethod.Invoke(optionsBuilder, null);

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
