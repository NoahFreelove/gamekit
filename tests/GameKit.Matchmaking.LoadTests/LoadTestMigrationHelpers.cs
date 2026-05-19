// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Matchmaking.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameKit.Matchmaking.LoadTests;

/// <summary>
/// Fresh-database creation + cross-package migration application + minimal seed helpers
/// for the SC#3 load-test host. Mirrors
/// <c>tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs</c> but lives
/// inside the LoadTests assembly so this project is self-contained — no <c>internal</c>
/// reach into the integration-tests project.
/// </summary>
/// <remarks>
/// <para>
/// Applies migrations in the order
/// <c>Core → Rankings → Matchmaking</c>. The Auth + Admin migrations are NOT applied here
/// because the load test never enqueues against the Auth/Admin code path (the host mints
/// JWTs locally via its own RSA keypair; the Matchmaking endpoints validate the bearer via
/// the runtime <c>UseGameKitAuth</c> middleware without needing Auth tables present —
/// the JWT validation is signature-only).
/// </para>
/// </remarks>
internal static class LoadTestMigrationHelpers
{
    /// <summary>
    /// Creates a fresh per-host database with the <c>citext</c> extension + <c>gamekit</c>
    /// schema pre-created. Returns the owner-role connection string for the new database.
    /// </summary>
    public static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_mm_load_" + Guid.NewGuid().ToString("N")[..12];

        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync().ConfigureAwait(false);
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var freshCs = builder.ConnectionString;

        await using (var freshConn = new NpgsqlConnection(freshCs))
        {
            await freshConn.OpenAsync().ConfigureAwait(false);
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return freshCs;
    }

    /// <summary>
    /// Applies the Core + Admin + Rankings + Matchmaking migration trains. The order
    /// matters — Matchmaking FK-references <c>ladders</c> + <c>players</c> from
    /// Rankings/Core; the reconciler's orphan-session sweep writes to <c>admin_audit_log</c>
    /// (Admin schema). Auth migrations are NOT applied because the load test mints JWTs
    /// locally and never exercises the Auth code paths that touch <c>player_credentials</c>
    /// / <c>player_identities</c>.
    /// </summary>
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
            await MigrationRunner.MigrateWithLockAsync(coreCtx).ConfigureAwait(false);
        }

        await using (var adminCtx = BuildAdminMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                adminCtx,
                GameKit.Admin.UI.Data.AdminMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }

        await using (var rankingsCtx = BuildRankingsMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                rankingsCtx,
                GameKit.Rankings.Data.RankingsMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }

        await using (var matchmakingCtx = BuildMatchmakingMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                matchmakingCtx,
                MatchmakingMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Seeds a <c>ladders</c> row matching the <see cref="LoadTestFixture.TestLadderName"/>
    /// the load host registers via <c>AddLadder(...)</c>.
    /// </summary>
    public static async Task<Guid> SeedLadderAsync(string cs, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync().ConfigureAwait(false);
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.ladders
            (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"", ""Config"")
            VALUES (@id, @n, 'Glicko2', true, NOW(), '{}'::jsonb)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("n", name);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        return id;
    }

    private static GameKitDbContext BuildAdminMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKit.Admin.UI.Data.AdminMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKit.Admin.UI.Data.AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Admin.UI.Data.AdminMigrationModelCustomizer>()
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
}
