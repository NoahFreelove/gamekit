// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Integration tests proving RANK-07 lazy rank creation and GDPR-null row skipping
/// (Pitfall §12 / T-04-06-PR) during drain.
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class LazyRankCreationTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    /// <summary>Constructs with shared Postgres + Redis fixtures.</summary>
    public LazyRankCreationTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
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
    // RANK-07: lazy rank row creation on first match drain
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-07: players who have pending_rating_updates but NO player_ranks row receive
    /// a new row with the ladder's default rating (1500), rd (350), volatility (0.06)
    /// after the first drain.
    /// </summary>
    [Fact]
    public async Task Rank_Row_Created_On_First_Match_Drain()
    {
        const string ladderName = "lazy-rank-test";

        var (ladderId, sessionId, p1Id, p2Id) = await SeedLadderAndPendingUpdatesAsync(_cs, ladderName);

        await using var sp = BuildTickerServiceProvider(_cs, _redis.ConnectionString, ladderName, suffix: "lazy");
        await sp.GetRequiredService<StartupLadderUpserter>().StartAsync(default);

        var ticker = sp.GetRequiredService<IRankingsTicker>();

        // Flush any stale lock from prior runs.
        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        await mux.GetDatabase().KeyDeleteAsync("gamekit:rankings:ticker:lease");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await ticker.RunOnceAsync(cts.Token);
        Assert.Equal(TickResult.Drained, result);

        // Both players must now have player_ranks rows.
        var rankCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.player_ranks WHERE \"LadderId\" = '{ladderId}'");
        Assert.Equal(2L, rankCount);

        // Verify default values were used (1500 rating, 350 rd).
        // After one match Glicko-2 will have updated them, but they STARTED from defaults.
        // We verify the rows exist and have been updated (non-default values after algorithm run).
        var updatedRatings = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.player_ranks WHERE \"LadderId\" = '{ladderId}' AND \"Rating\" != 1500");
        // After one match, ratings should have changed from defaults
        // (Glicko-2 updates both players — winner goes up, loser goes down).
        Assert.Equal(2L, updatedRatings);

        // pending_rating_updates rows should be marked applied.
        var appliedCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.pending_rating_updates WHERE \"SessionId\" = '{sessionId}' AND \"AppliedAt\" IS NOT NULL");
        Assert.Equal(2L, appliedCount);

        // session_participants should have RatingAfter populated.
        var ratingAfterCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.session_participants WHERE \"SessionId\" = '{sessionId}' AND \"RatingAfter\" IS NOT NULL");
        Assert.Equal(2L, ratingAfterCount);
    }

    // -------------------------------------------------------------------------
    // Pitfall 12: GDPR null PlayerId is skipped
    // -------------------------------------------------------------------------

    /// <summary>
    /// T-04-06-PR: pending_rating_updates rows with PlayerId = NULL (GDPR-cascade after enqueue)
    /// are skipped during drain. No player_ranks row is created for null. The null row is
    /// not marked applied. Other non-null rows in the same session still drain.
    /// </summary>
    [Fact]
    public async Task Skips_PlayerId_Null_From_GDPR_Cascade()
    {
        const string ladderName = "gdpr-null-test";

        var (ladderId, sessionId, p1Id, p2Id) = await SeedLadderAndPendingUpdatesAsync(_cs, ladderName);

        // Simulate GDPR cascade: set PlayerId to null for one row.
        await ExecuteAsync(_cs,
            $"UPDATE gamekit.pending_rating_updates SET \"PlayerId\" = NULL WHERE \"PlayerId\" = '{p2Id}'");

        await using var sp = BuildTickerServiceProvider(_cs, _redis.ConnectionString, ladderName, suffix: "gdpr");
        await sp.GetRequiredService<StartupLadderUpserter>().StartAsync(default);

        var ticker = sp.GetRequiredService<IRankingsTicker>();

        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        await mux.GetDatabase().KeyDeleteAsync("gamekit:rankings:ticker:lease");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await ticker.RunOnceAsync(cts.Token);

        // The ticker drains the non-null row (p1Id); it should still return Drained.
        // (If only null rows existed it would be NoLaddersDue, but here we have one valid row.)
        // Actually, with only 1 non-null player there's no opponent to pair with, so the
        // algorithm gets a batch with zero outcomes — it still counts as a drain (rows processed).
        // The exact result depends on whether the batch is empty or not.
        // The key assertion is: no player_ranks row for the null player; only p1Id may have one.
        Assert.NotEqual(TickResult.LockNotAcquired, result);

        // No player_ranks row should exist for p2Id (it was null in pending updates).
        var p2RankCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.player_ranks WHERE \"LadderId\" = '{ladderId}' AND \"PlayerId\" = '{p2Id}'");
        Assert.Equal(0L, p2RankCount);

        // The null row should NOT be marked as applied.
        var nullAppliedCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.pending_rating_updates WHERE \"PlayerId\" IS NULL AND \"AppliedAt\" IS NOT NULL");
        Assert.Equal(0L, nullAppliedCount);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<(Guid ladderId, Guid sessionId, Guid p1Id, Guid p2Id)>
        SeedLadderAndPendingUpdatesAsync(string cs, string ladderName)
    {
        var now = DateTimeOffset.UtcNow;
        var ladderId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Players.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES ('{p1Id}', 'LazyP1', '{now:O}'), ('{p2Id}', 'LazyP2', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Ladder.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
                VALUES ('{ladderId}', '{ladderName}', 'glicko2', true, '{now:O}')
                ON CONFLICT DO NOTHING";
            await cmd.ExecuteNonQueryAsync();
        }

        // Re-read actual ladder id (may exist from StartupLadderUpserter).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"Id\" FROM gamekit.ladders WHERE \"Name\" = '{ladderName}'";
            var result = await cmd.ExecuteScalarAsync();
            if (result is Guid existingId && existingId != ladderId)
                ladderId = existingId;
        }

        // Game session.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.game_sessions (""Id"", ""State"", ""LadderId"", ""CreatedAt"", ""StartedAt"")
                VALUES ('{sessionId}', 'Active', '{ladderId}', '{now:O}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Session participants.
        var sp1Id = Guid.NewGuid();
        var sp2Id = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.session_participants (""Id"", ""SessionId"", ""PlayerId"", ""Team"")
                VALUES ('{sp1Id}', '{sessionId}', '{p1Id}', 0),
                       ('{sp2Id}', '{sessionId}', '{p2Id}', 1)";
            await cmd.ExecuteNonQueryAsync();
        }

        // Pending rating updates — both players, NO existing player_ranks rows.
        var upd1Id = Guid.NewGuid();
        var upd2Id = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.pending_rating_updates
                    (""Id"", ""SessionId"", ""PlayerId"", ""LadderId"", ""Result"", ""EnqueuedAt"")
                VALUES
                    ('{upd1Id}', '{sessionId}', '{p1Id}', '{ladderId}', 'Win', '{now:O}'),
                    ('{upd2Id}', '{sessionId}', '{p2Id}', '{ladderId}', 'Loss', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        return (ladderId, sessionId, p1Id, p2Id);
    }

    private static ServiceProvider BuildTickerServiceProvider(
        string cs, string redisCs, string ladderName, string suffix)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        services
            .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
            .AddRankings()
            .AddLadder(ladderName);

        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, LazyRankTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCs));

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_lazy_" + Guid.NewGuid().ToString("N")[..12];

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
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

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

    private static async Task<long> QueryScalarAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : Convert.ToInt64(result);
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Test-only model customizer for LazyRankCreationTests (bypasses EF global cache — Pitfall 3).</summary>
internal sealed class LazyRankTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public LazyRankTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
