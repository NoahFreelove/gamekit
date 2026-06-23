// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Builder;
using GameKit.Lobby.Data;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameKit.Platformer3D.Integration.Tests;

/// <summary>
/// Shared helpers for the Phase 21 Platformer3D integration tests —
/// fresh-database creation and full Core + Auth + Rankings + Matchmaking + Lobby
/// migration application. Copied from
/// <see cref="GameKit.Matchmaking.Integration.Tests.IntegrationTestHelpers"/> and
/// extended with Lobby migrations (the Platformer3D demo needs the full five-package chain).
/// </summary>
internal static class PlatformerIntegrationFixture
{
    /// <summary>
    /// Creates a fresh isolated database in the Postgres container provided by
    /// <see cref="PostgresFixture"/>, sets up the <c>citext</c> extension and
    /// <c>gamekit</c> schema, and returns the owner connection string for that database.
    /// </summary>
    public static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_p3d_" + Guid.NewGuid().ToString("N")[..12];

        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var freshCs = builder.ConnectionString;

        await using (var freshConn = new NpgsqlConnection(freshCs))
        {
            await freshConn.OpenAsync();
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync();
        }

        return freshCs;
    }

    /// <summary>
    /// Applies all five migration packages in dependency order:
    /// Core → Auth → Rankings → Matchmaking → Lobby.
    /// This is the full migration chain required by the Platformer3D demo (D-13).
    /// </summary>
    public static async Task ApplyPlatformerMigrationsAsync(string cs)
    {
        // 1. Core migrations (always first — base schema + advisory lock key 1800940027)
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = cs;
            o.MigrationsConnectionString = cs;
            o.AutoMigrate = false;
        });
        await using (var coreSp = coreServices.BuildServiceProvider())
        {
            await using var scope = coreSp.CreateAsyncScope();
            var coreCtx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(coreCtx);
        }

        // 2. Auth migrations (advisory lock key -298890956)
        await using (var authCtx = BuildAuthMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                authCtx,
                AuthMigrationConstants.AdvisoryLockKey);
        }

        // 3. Rankings migrations (advisory lock key -156812172)
        await using (var rankingsCtx = BuildRankingsMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                rankingsCtx,
                GameKit.Rankings.Data.RankingsMigrationConstants.AdvisoryLockKey);
        }

        // 4. Matchmaking migrations (advisory lock key 388956820)
        await using (var matchmakingCtx = BuildMatchmakingMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                matchmakingCtx,
                MatchmakingMigrationConstants.AdvisoryLockKey);
        }

        // 5. Lobby migrations (advisory lock key 12178347) — required for the full Platformer3D demo
        await using (var lobbyCtx = BuildLobbyMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                lobbyCtx,
                LobbyMigrationConstants.AdvisoryLockKey);
        }
    }

    private static GameKitDbContext BuildAuthMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Auth.Data.AuthMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildRankingsMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKit.Rankings.Data.RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKit.Rankings.Data.RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Rankings.Data.RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildMatchmakingMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildLobbyMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(LobbyMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    LobbyMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Lobby.Data.LobbyMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }
}
