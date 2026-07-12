// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Data;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Integration tests for <c>RankingsTickerService</c> leader election (T-04-06-DD / D-03).
/// Proves that:
/// <list type="bullet">
///   <item><see cref="Two_Tickers_Only_One_Drains_Per_Tick"/> — only one replica drains when two race simultaneously.</item>
///   <item><see cref="Lock_Released_Allows_Subsequent_Tick"/> — after the winner releases the lock, a subsequent tick can acquire it.</item>
/// </list>
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class RankingsTickerLeaderElectionTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres + Redis fixtures.</summary>
    public RankingsTickerLeaderElectionTests(PostgresFixture pg, RedisFixture redis)
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
    // Two replicas race — only one drains
    // -------------------------------------------------------------------------

    /// <summary>
    /// T-04-06-DD: Two ticker instances pointing at the same Postgres + Redis compete for the
    /// distributed lock. Exactly one must return <see cref="TickResult.Drained"/> and the other
    /// must return <see cref="TickResult.LockNotAcquired"/>. The <c>pending_rating_updates</c>
    /// rows must be applied exactly once (no double-apply).
    /// </summary>
    [Fact]
    public async Task Two_Tickers_Only_One_Drains_Per_Tick()
    {
        var ladderName = "leader-election-test";

        // Seed ladder + session + two pending_rating_updates rows.
        var (ladderId, sessionId, p1Id, p2Id) = await SeedLadderAndPendingUpdatesAsync(_cs, ladderName);

        // Build two separate service providers — simulates two app replicas.
        await using var sp1 = BuildTickerServiceProvider(_cs, _redis.ConnectionString, ladderName, suffix: "1");
        await using var sp2 = BuildTickerServiceProvider(_cs, _redis.ConnectionString, ladderName, suffix: "2");

        await sp1.GetRequiredService<StartupLadderUpserter>().StartAsync(default);
        await sp2.GetRequiredService<StartupLadderUpserter>().StartAsync(default);

        var ticker1 = sp1.GetRequiredService<IRankingsTicker>();
        var ticker2 = sp2.GetRequiredService<IRankingsTicker>();

        // Flush any stale keys from prior runs.
        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        var db = mux.GetDatabase();
        await db.KeyDeleteAsync("gamekit:rankings:ticker:lease");

        // Run both concurrently.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var t1 = ticker1.RunOnceAsync(cts.Token);
        var t2 = ticker2.RunOnceAsync(cts.Token);
        var results = await Task.WhenAll(t1, t2);

        // Exactly one must drain; the other must not acquire the lock.
        var drainCount = Array.FindAll(results, r => r == TickResult.Drained).Length;
        var lockNotAcquiredCount = Array.FindAll(results, r => r == TickResult.LockNotAcquired).Length;

        Assert.Equal(1, drainCount);
        Assert.Equal(1, lockNotAcquiredCount);

        // pending_rating_updates rows must be applied exactly once.
        var appliedCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.pending_rating_updates WHERE \"SessionId\" = '{sessionId}' AND \"AppliedAt\" IS NOT NULL");
        Assert.Equal(2L, appliedCount); // 2 rows, each applied exactly once

        // No double-apply: total rows still 2 (drain does not insert more rows).
        var totalCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.pending_rating_updates WHERE \"SessionId\" = '{sessionId}'");
        Assert.Equal(2L, totalCount);
    }

    // -------------------------------------------------------------------------
    // Lock released → subsequent tick succeeds
    // -------------------------------------------------------------------------

    /// <summary>
    /// Proves that after the winner releases the lock, a subsequent tick from the same
    /// (or different) instance can acquire it and proceed. The second tick finds no ladders
    /// due (already drained) and returns <see cref="TickResult.NoLaddersDue"/>.
    /// </summary>
    [Fact]
    public async Task Lock_Released_Allows_Subsequent_Tick()
    {
        var ladderName = "lock-release-test";

        await SeedLadderAndPendingUpdatesAsync(_cs, ladderName);

        await using var sp = BuildTickerServiceProvider(_cs, _redis.ConnectionString, ladderName, suffix: "single");
        await sp.GetRequiredService<StartupLadderUpserter>().StartAsync(default);

        var ticker = sp.GetRequiredService<IRankingsTicker>();

        // Flush any stale key.
        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        await mux.GetDatabase().KeyDeleteAsync("gamekit:rankings:ticker:lease");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // First tick: drains.
        var result1 = await ticker.RunOnceAsync(cts.Token);
        Assert.Equal(TickResult.Drained, result1);

        // Second tick: lock should be released; no ladders due (just drained) → NoLaddersDue.
        var result2 = await ticker.RunOnceAsync(cts.Token);
        Assert.Equal(TickResult.NoLaddersDue, result2);
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
                VALUES ('{p1Id}', 'P1', '{now:O}'), ('{p2Id}', 'P2', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Ladder — LastDrainedAt = NULL so it is immediately eligible.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
                VALUES ('{ladderId}', '{ladderName}', 'glicko2', true, '{now:O}')
                ON CONFLICT DO NOTHING";
            await cmd.ExecuteNonQueryAsync();
        }

        // Verify the ladder got its id (may already exist from StartupLadderUpserter).
        object? existingId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"Id\" FROM gamekit.ladders WHERE \"Name\" = '{ladderName}'";
            existingId = await cmd.ExecuteScalarAsync();
        }
        if (existingId is Guid existingGuid && existingGuid != ladderId)
            ladderId = existingGuid;

        // Game session (Active state stored as text per HasConversion<string>()).
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

        // Pending rating updates (unapplied).
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

        // Override DbContext to include Rankings entities (Pitfall 3 bypass).
        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, TickerTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        // Provide the Redis multiplexer (the ticker and lease helper need it).
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCs));

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_ticker_" + Guid.NewGuid().ToString("N")[..12];

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
}

/// <summary>Test-only model customizer that includes Rankings entities (bypasses EF global cache — Pitfall 3).</summary>
internal sealed class TickerTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public TickerTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
