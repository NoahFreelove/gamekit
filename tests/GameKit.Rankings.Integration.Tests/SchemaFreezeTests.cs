// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// SC#5 schema-freeze proof: asserts that after applying the Rankings migrations, the
/// <c>gamekit.player_ranks</c> table has the frozen set of decay + placement columns, the
/// <c>idx_player_ranks_decay_candidates</c> partial index exists, and the migration was
/// applied under the existing <c>__ef_migrations_rankings</c> history table (no new lock key).
///
/// Runs against a real Testcontainers Postgres instance (RANK-15 / RANK-16).
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class SchemaFreezeTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with shared Postgres fixture.</summary>
    public SchemaFreezeTests(PostgresFixture pg)
    {
        _pg = pg;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // SC#5: frozen player_ranks columns
    // -------------------------------------------------------------------------

    /// <summary>
    /// SC#5 RANK-15: after applying migrations, player_ranks has LastDecayAt, PlacementMatchesRemaining, IsInPlacement.
    /// </summary>
    [Fact]
    public async Task PlayerRanks_Has_LastDecayAt_Column()
    {
        var exists = await ColumnExistsAsync(_cs, "player_ranks", "LastDecayAt");
        Assert.True(exists, "player_ranks must have LastDecayAt column after migration (RANK-15)");
    }

    /// <summary>SC#5 RANK-16: player_ranks has PlacementMatchesRemaining column.</summary>
    [Fact]
    public async Task PlayerRanks_Has_PlacementMatchesRemaining_Column()
    {
        var exists = await ColumnExistsAsync(_cs, "player_ranks", "PlacementMatchesRemaining");
        Assert.True(exists, "player_ranks must have PlacementMatchesRemaining column after migration (RANK-16)");
    }

    /// <summary>SC#5 RANK-16: player_ranks has IsInPlacement column.</summary>
    [Fact]
    public async Task PlayerRanks_Has_IsInPlacement_Column()
    {
        var exists = await ColumnExistsAsync(_cs, "player_ranks", "IsInPlacement");
        Assert.True(exists, "player_ranks must have IsInPlacement column after migration (RANK-16)");
    }

    // -------------------------------------------------------------------------
    // Decay index
    // -------------------------------------------------------------------------

    /// <summary>RANK-15: decay candidate partial index exists on player_ranks.</summary>
    [Fact]
    public async Task DecayIndex_Exists()
    {
        var exists = await IndexExistsAsync(_cs, "idx_player_ranks_decay_candidates");
        Assert.True(exists, "idx_player_ranks_decay_candidates index must exist after migration (RANK-15)");
    }

    // -------------------------------------------------------------------------
    // Migration history table — reused advisory lock wiring
    // -------------------------------------------------------------------------

    /// <summary>
    /// Migration applied under the existing __ef_migrations_rankings history table
    /// (proves the advisory lock / history wiring carried the new migration).
    /// </summary>
    [Fact]
    public async Task RankingsMigration_Applied_Under_Correct_History_Table()
    {
        var count = await QueryScalarAsync(_cs,
            "SELECT COUNT(*) FROM gamekit.__ef_migrations_rankings WHERE \"MigrationId\" = '20260517000000_RankingsDecayPlacement'");
        Assert.Equal(1L, count);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_schema_freeze_" + Guid.NewGuid().ToString("N")[..8];

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

    private static async Task ApplyMigrationsAsync(string cs)
    {
        // Step 1: Apply Core migrations.
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // Step 2: Apply Rankings migrations (including 20260517000000_RankingsDecayPlacement).
        var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var rankingsCtx = new GameKitDbContext(rankingsOpts);
        await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);
    }

    private static async Task<bool> ColumnExistsAsync(string cs, string tableName, string columnName)
    {
        var count = await QueryScalarAsync(cs,
            $"SELECT COUNT(*) FROM information_schema.columns " +
            $"WHERE table_schema = 'gamekit' AND table_name = '{tableName}' AND column_name = '{columnName}'");
        return count > 0;
    }

    private static async Task<bool> IndexExistsAsync(string cs, string indexName)
    {
        var count = await QueryScalarAsync(cs,
            $"SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'gamekit' AND indexname = '{indexName}'");
        return count > 0;
    }

    private static async Task<long> QueryScalarAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : Convert.ToInt64(result);
    }
}
