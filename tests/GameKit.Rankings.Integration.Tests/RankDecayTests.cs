// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="RankDecayBackgroundService"/> (RANK-15).
/// Proves:
/// <list type="bullet">
///   <item><see cref="Decay_InflatesRD_LeavesRatingConstant_StampsLastDecayAt"/> — scale-correct RD inflation, rating constant, LastDecayAt stamped.</item>
///   <item><see cref="Decay_SkipsBelowThreshold_NeverPlayed_AndPlacement"/> — below-threshold, never-played, and placement players are excluded.</item>
///   <item><see cref="Decay_UsesDedicatedLockKey_NotTickerKey"/> — decay acquires gamekit:rankings:decay:lease, not the ticker key.</item>
/// </list>
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class RankDecayTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres + Redis fixtures.</summary>
    public RankDecayTests(PostgresFixture pg, RedisFixture redis)
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
    // Test 1: RD inflates, Rating constant, LastDecayAt stamped
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-15 SC#1: An inactive above-threshold non-placement player's RD inflates via
    /// the scale-correct Glicko-2 inactivity step. Rating is byte-identical to the seeded
    /// value. LastDecayAt is non-null after the run.
    /// </summary>
    [Fact]
    public async Task Decay_InflatesRD_LeavesRatingConstant_StampsLastDecayAt()
    {
        var now = DateTimeOffset.UtcNow;
        var ladderId = await SeedLadderAsync(_cs, "decay-rd-test", now);

        // Decay candidate: Rating=1800 (>1500), IsInPlacement=false, LastMatchAt = 35 days ago.
        const double seededRd = 200.0;
        const double seededVolatility = 0.06;
        const double seededRating = 1800.0;
        var candidateId = await SeedPlayerRankAsync(_cs, ladderId, now,
            rating: seededRating,
            rd: seededRd,
            volatility: seededVolatility,
            isInPlacement: false,
            lastMatchAt: now.AddDays(-35));

        var mockClock = new Mock<IClock>();
        mockClock.Setup(c => c.UtcNow).Returns(now);

        // Flush any stale decay lease key.
        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        await mux.GetDatabase().KeyDeleteAsync("gamekit:rankings:decay:lease");

        await using var sp = BuildDecayServiceProvider(_cs, _redis.ConnectionString, "decay-rd-test", mockClock.Object);
        var decay = sp.GetRequiredService<RankDecayBackgroundService>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await decay.RunOnceAsync(cts.Token);

        // Query the rank row back.
        var (actualRating, actualRd, actualLastDecayAt) = await QueryPlayerRankAsync(_cs, candidateId);

        // Rating MUST be unchanged (byte-identical to seeded value).
        Assert.Equal(seededRating, actualRating);

        // RD must inflate: phi' = sqrt((RD/173.7178)^2 + vol^2) * 173.7178 > RD.
        const double Multiplier = 173.7178;
        var expectedPhiG2 = seededRd / Multiplier;
        var expectedPhiPrimeG2 = Math.Sqrt(expectedPhiG2 * expectedPhiG2 + seededVolatility * seededVolatility);
        var expectedRdPrime = expectedPhiPrimeG2 * Multiplier;

        Assert.True(actualRd > seededRd,
            $"Expected RD to inflate: actualRd={actualRd:F6} should be > seededRd={seededRd}");
        Assert.Equal(expectedRdPrime, actualRd, precision: 6);

        // LastDecayAt must be stamped.
        Assert.NotNull(actualLastDecayAt);
    }

    // -------------------------------------------------------------------------
    // Test 2: Skips below-threshold, never-played, placement rows
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-15 SC#2: Players below the rating threshold, players with LastMatchAt = null,
    /// and players in placement are not decayed.
    /// </summary>
    [Fact]
    public async Task Decay_SkipsBelowThreshold_NeverPlayed_AndPlacement()
    {
        var now = DateTimeOffset.UtcNow;
        var ladderId = await SeedLadderAsync(_cs, "decay-skip-test", now);

        // Row 1: below threshold (Rating <= 1500).
        var belowThresholdId = await SeedPlayerRankAsync(_cs, ladderId, now,
            rating: 1400.0,
            rd: 200.0,
            volatility: 0.06,
            isInPlacement: false,
            lastMatchAt: now.AddDays(-35));

        // Row 2: never played (LastMatchAt = null).
        var neverPlayedId = await SeedPlayerRankAsync(_cs, ladderId, now,
            rating: 1800.0,
            rd: 200.0,
            volatility: 0.06,
            isInPlacement: false,
            lastMatchAt: null);

        // Row 3: in placement.
        var inPlacementId = await SeedPlayerRankAsync(_cs, ladderId, now,
            rating: 1800.0,
            rd: 200.0,
            volatility: 0.06,
            isInPlacement: true,
            lastMatchAt: now.AddDays(-35));

        var mockClock = new Mock<IClock>();
        mockClock.Setup(c => c.UtcNow).Returns(now);

        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        await mux.GetDatabase().KeyDeleteAsync("gamekit:rankings:decay:lease");

        await using var sp = BuildDecayServiceProvider(_cs, _redis.ConnectionString, "decay-skip-test", mockClock.Object);
        var decay = sp.GetRequiredService<RankDecayBackgroundService>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await decay.RunOnceAsync(cts.Token);

        // All three rows must have unchanged RD (200.0) and null LastDecayAt.
        var (_, belowRd, belowLastDecay) = await QueryPlayerRankAsync(_cs, belowThresholdId);
        Assert.Equal(200.0, belowRd);
        Assert.Null(belowLastDecay);

        var (_, neverRd, neverLastDecay) = await QueryPlayerRankAsync(_cs, neverPlayedId);
        Assert.Equal(200.0, neverRd);
        Assert.Null(neverLastDecay);

        var (_, placementRd, placementLastDecay) = await QueryPlayerRankAsync(_cs, inPlacementId);
        Assert.Equal(200.0, placementRd);
        Assert.Null(placementLastDecay);
    }

    // -------------------------------------------------------------------------
    // Test 3: Dedicated lock key; ticker key does NOT block decay
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-15 SC#3: The decay runner acquires <c>gamekit:rankings:decay:lease</c>.
    /// When that key is held by another connection, the decay run finds itself not-leader and
    /// skips work. When the ticker key (<c>gamekit:rankings:ticker:lease</c>) is held instead,
    /// the decay run is NOT blocked — proving non-collision between the two services.
    /// </summary>
    [Fact]
    public async Task Decay_UsesDedicatedLockKey_NotTickerKey()
    {
        var now = DateTimeOffset.UtcNow;
        var ladderId = await SeedLadderAsync(_cs, "decay-lock-test", now);

        // Seed a decay candidate so we can detect if decay ran (RD would inflate).
        const double seededRd = 200.0;
        var candidateId = await SeedPlayerRankAsync(_cs, ladderId, now,
            rating: 1800.0,
            rd: seededRd,
            volatility: 0.06,
            isInPlacement: false,
            lastMatchAt: now.AddDays(-35));

        var mockClock = new Mock<IClock>();
        mockClock.Setup(c => c.UtcNow).Returns(now);

        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        var db = mux.GetDatabase();

        // --- Part A: Pre-take the decay key → decay run must be a no-op ---
        await db.KeyDeleteAsync("gamekit:rankings:decay:lease");
        await db.KeyDeleteAsync("gamekit:rankings:ticker:lease");

        // Manually acquire the decay lease so the decay service cannot become leader.
        var blockerToken = $"test-blocker:{Guid.NewGuid()}";
        var blocked = await db.LockTakeAsync(
            "gamekit:rankings:decay:lease", blockerToken, TimeSpan.FromSeconds(30));
        Assert.True(blocked, "Test setup: expected to acquire decay lease for blocking.");

        await using var sp1 = BuildDecayServiceProvider(_cs, _redis.ConnectionString, "decay-lock-test", mockClock.Object);
        var decay1 = sp1.GetRequiredService<RankDecayBackgroundService>();

        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await decay1.RunOnceAsync(cts1.Token); // Should find lock held → skip

        // The candidate must NOT have been decayed (lock blocked decay).
        var (_, rdAfterBlocked, lastDecayAfterBlocked) = await QueryPlayerRankAsync(_cs, candidateId);
        Assert.Equal(seededRd, rdAfterBlocked);
        Assert.Null(lastDecayAfterBlocked);

        // Release the blocker.
        await db.LockReleaseAsync("gamekit:rankings:decay:lease", blockerToken);

        // --- Part B: Pre-take the TICKER key → decay must NOT be blocked ---
        // Hold the ticker lease exclusively.
        var tickerBlocker = $"test-ticker-blocker:{Guid.NewGuid()}";
        var tickerBlocked = await db.LockTakeAsync(
            "gamekit:rankings:ticker:lease", tickerBlocker, TimeSpan.FromSeconds(30));
        Assert.True(tickerBlocked, "Test setup: expected to acquire ticker lease.");

        await using var sp2 = BuildDecayServiceProvider(_cs, _redis.ConnectionString, "decay-lock-test", mockClock.Object);
        var decay2 = sp2.GetRequiredService<RankDecayBackgroundService>();

        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await decay2.RunOnceAsync(cts2.Token); // Ticker key held → decay still proceeds

        // The candidate MUST have been decayed now (ticker key does not block decay).
        var (_, rdAfterUnblocked, lastDecayAfterUnblocked) = await QueryPlayerRankAsync(_cs, candidateId);
        Assert.True(rdAfterUnblocked > seededRd,
            $"Expected RD to inflate when only ticker key is held. actualRd={rdAfterUnblocked:F6}");
        Assert.NotNull(lastDecayAfterUnblocked);

        // Clean up.
        await db.LockReleaseAsync("gamekit:rankings:ticker:lease", tickerBlocker);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<Guid> SeedLadderAsync(string cs, string ladderName, DateTimeOffset now)
    {
        var ladderId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
                VALUES ('{ladderId}', '{ladderName}', 'glicko2', true, '{now:O}')
                ON CONFLICT DO NOTHING";
            await cmd.ExecuteNonQueryAsync();
        }

        // Re-read actual id in case of conflict.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"Id\" FROM gamekit.ladders WHERE \"Name\" = '{ladderName}'";
            var result = await cmd.ExecuteScalarAsync();
            if (result is Guid existingId)
                return existingId;
        }

        return ladderId;
    }

    private static async Task<Guid> SeedPlayerRankAsync(
        string cs,
        Guid ladderId,
        DateTimeOffset now,
        double rating,
        double rd,
        double volatility,
        bool isInPlacement,
        DateTimeOffset? lastMatchAt)
    {
        var playerId = Guid.NewGuid();
        var rankId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Insert a player row first (FK constraint).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES ('{playerId}', 'DecayTestPlayer-{playerId:N}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        var lastMatchAtSql = lastMatchAt.HasValue
            ? $"'{lastMatchAt.Value:O}'"
            : "NULL";

        var placementRemaining = isInPlacement ? 10 : 0;

        // Insert the player_ranks row directly — bypass the ticker so we control Rating/RD exactly.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.player_ranks
                    (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"",
                     ""Wins"", ""Losses"", ""Draws"", ""LastMatchAt"", ""LastDecayAt"",
                     ""IsInPlacement"", ""PlacementMatchesRemaining"")
                VALUES
                    ('{rankId}', '{playerId}', '{ladderId}', {rating}, {rd}, {volatility},
                     0, 0, 0, {lastMatchAtSql}, NULL,
                     {(isInPlacement ? "true" : "false")}, {placementRemaining})";
            await cmd.ExecuteNonQueryAsync();
        }

        return rankId;
    }

    private static async Task<(double Rating, double RatingDeviation, DateTimeOffset? LastDecayAt)>
        QueryPlayerRankAsync(string cs, Guid rankId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT ""Rating"", ""RatingDeviation"", ""LastDecayAt""
            FROM gamekit.player_ranks
            WHERE ""Id"" = '{rankId}'";
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var rating = reader.GetDouble(0);
        var rd = reader.GetDouble(1);
        DateTimeOffset? lastDecayAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
        return (rating, rd, lastDecayAt);
    }

    private static ServiceProvider BuildDecayServiceProvider(
        string cs, string redisCs, string ladderName, IClock clock)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        services
            .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
            .AddRankings()
            .AddLadder(ladderName);

        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, DecayTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        // Provide the Redis multiplexer (decay lease helper needs it).
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCs));

        // Override IClock so decay cutoff is deterministic.
        services.AddSingleton(clock);

        // Register RankDecayBackgroundService as a resolvable singleton for direct test invocation.
        services.AddSingleton<RankDecayBackgroundService>();

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_decay_" + Guid.NewGuid().ToString("N")[..12];

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
}

/// <summary>Test-only model customizer for RankDecayTests (bypasses EF global cache — Pitfall 3).</summary>
internal sealed class DecayTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public DecayTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
