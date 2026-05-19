// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Builder;
using GameKit.Matchmaking.Data;
using GameKit.Matchmaking.Entities;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Shared helpers for the Plan 05-07 reconciler / retention / drain integration tests —
/// fresh-database creation, full Core+Auth+Admin+Rankings+Matchmaking migration application,
/// and parameterised seed helpers for the four entities the sweeps touch.
/// </summary>
internal static class IntegrationTestHelpers
{
    public static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_mm_recon_" + Guid.NewGuid().ToString("N")[..12];

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

    public static async Task ApplyMatchmakingMigrationsAsync(string cs)
    {
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

        await using (var rankingsCtx = BuildRankingsMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                rankingsCtx,
                GameKit.Rankings.Data.RankingsMigrationConstants.AdvisoryLockKey);
        }

        await using (var matchmakingCtx = BuildMatchmakingMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                matchmakingCtx,
                MatchmakingMigrationConstants.AdvisoryLockKey);
        }
    }

    public static GameKitDbContext BuildMatchmakingContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs)
            .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    public static async Task<Guid> SeedLadderAsync(string cs, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.ladders
            (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"", ""Config"")
            VALUES (@id, @n, 'Glicko2', true, NOW(), '{}'::jsonb)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("n", name);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    public static async Task<Guid> SeedTicketAsync(
        string cs, Guid ladderId, TicketStatus status, DateTimeOffset queuedAt,
        DateTimeOffset? terminalAt = null)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.matchmaking_tickets
            (""Id"", ""PartyId"", ""LadderId"", ""PoolName"", ""Status"", ""QueuedAt"", ""TerminalAt"", ""SessionId"")
            VALUES (@id, NULL, @ladder, 'default', @status, @queued, @terminal, NULL)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("ladder", ladderId);
        cmd.Parameters.AddWithValue("status", (int)status);
        cmd.Parameters.AddWithValue("queued", queuedAt);
        cmd.Parameters.AddWithValue("terminal", (object?)terminalAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    public static async Task<Guid> SeedDeclineHistoryAsync(string cs, Guid playerId, DateTimeOffset declinedAt)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.decline_history
            (""Id"", ""PlayerId"", ""DeclinedAt"", ""ProposalId"")
            VALUES (@id, @p, @when, @prop)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("p", playerId);
        cmd.Parameters.AddWithValue("when", declinedAt);
        cmd.Parameters.AddWithValue("prop", Guid.NewGuid().ToString());
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    public static async Task<Guid> SeedPlayerAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.players
            (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES (@id, @name, NOW(), false)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", "Player_" + id.ToString("N")[..8]);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    public static async Task<Guid> SeedActiveGameSessionAsync(string cs, DateTimeOffset createdAt)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        // GameSession.State is mapped HasConversion<string>() — store the enum name.
        cmd.CommandText = @"INSERT INTO gamekit.game_sessions
            (""Id"", ""State"", ""LadderId"", ""CreatedAt"", ""StartedAt"", ""CompletedAt"", ""Metadata"")
            VALUES (@id, 'Active', NULL, @when, @when, NULL, NULL)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("when", createdAt);
        await cmd.ExecuteNonQueryAsync();
        return id;
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
}
