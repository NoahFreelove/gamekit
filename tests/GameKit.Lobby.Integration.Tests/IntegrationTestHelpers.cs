// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Lobby.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// Shared helpers for Lobby integration tests — fresh-database creation and full
/// Core+Rankings+Matchmaking+Lobby migration application in dependency order.
/// </summary>
internal static class IntegrationTestHelpers
{
    /// <summary>
    /// Creates a fresh disposable Postgres database for an integration test. The database
    /// is named <c>gamekit_lobby_&lt;12-char-guid&gt;</c> and owned by <c>gamekit_owner</c>.
    /// The <c>citext</c> extension and <c>gamekit</c> schema are pre-created.
    /// </summary>
    public static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_lobby_" + Guid.NewGuid().ToString("N")[..12];

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
    /// Applies all migrations in dependency order (Core → Rankings → Matchmaking → Lobby)
    /// against the given connection string. Each package migration runs under its own
    /// advisory lock via <see cref="MigrationRunner.MigrateWithLockAsync"/>.
    /// </summary>
    /// <remarks>
    /// Lobby's FKs target <c>players</c> (Core) and <c>ladders</c> (Rankings), so Core and
    /// Rankings must be applied first. Matchmaking is applied before Lobby because
    /// <c>GameKit.Lobby</c> has a transitive <c>ProjectReference</c> to
    /// <c>GameKit.Matchmaking</c> and the exclusion list in
    /// <c>LobbyMigrationModelCustomizer</c> lists the five Matchmaking entity types.
    /// </remarks>
    public static async Task ApplyLobbyMigrationsAsync(string cs)
    {
        // Step 1: Core — uses AddGameKit's internal migration runner (MigrationsHistoryTable = __ef_migrations_history)
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

        // Step 2: Rankings — FKs from Lobby.LadderId target gamekit.ladders (Rankings Phase 4)
        await using (var rankingsCtx = BuildRankingsMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                rankingsCtx,
                GameKit.Rankings.Data.RankingsMigrationConstants.AdvisoryLockKey);
        }

        // Step 3: Matchmaking — Lobby has a ProjectReference to Matchmaking; LobbyMigrationModelCustomizer
        // excludes Matchmaking entities by type, so the schema should exist.
        await using (var matchmakingCtx = BuildMatchmakingMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                matchmakingCtx,
                GameKit.Matchmaking.Data.MatchmakingMigrationConstants.AdvisoryLockKey);
        }

        // Step 4: Lobby — creates lobbies + lobby_members under the Lobby advisory lock
        await using (var lobbyCtx = LobbyMigrationHostedService.BuildLobbyMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                lobbyCtx,
                LobbyMigrationConstants.AdvisoryLockKey);
        }
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
                npg.MigrationsAssembly(typeof(GameKit.Matchmaking.Data.MatchmakingMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKit.Matchmaking.Data.MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Matchmaking.Data.MatchmakingMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }
}
