// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
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
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Testcontainers integration tests for <see cref="RankingsRatingSource"/> (RANK-17).
/// Proves real <c>player_ranks</c> → <see cref="PlayerRatingValue"/> projection against real Postgres:
/// <list type="bullet">
///   <item><see cref="GetRatings_ReturnsValuesForKnownPlayers"/> — exact Rating/RD/Volatility for seeded rows.</item>
///   <item><see cref="GetRatings_OmitsPlayersWithNoRankRow"/> — absent player is not in the result dict.</item>
///   <item><see cref="GetRatings_ScopesToLadder"/> — a player on ladder B is absent when querying ladder A.</item>
/// </list>
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class RankingsRatingSourceTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres + Redis fixtures.</summary>
    public RankingsRatingSourceTests(PostgresFixture pg, RedisFixture redis)
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
    // Test 1: Returns exact Rating/RD/Volatility for known players
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-17 SC#1: <c>GetRatingsAsync</c> returns a <see cref="PlayerRatingValue"/> with exact
    /// <see cref="PlayerRatingValue.Rating"/>, <see cref="PlayerRatingValue.RatingDeviation"/>,
    /// and <see cref="PlayerRatingValue.Volatility"/> for each seeded player on the ladder.
    /// </summary>
    [Fact]
    public async Task GetRatings_ReturnsValuesForKnownPlayers()
    {
        var now = DateTimeOffset.UtcNow;
        var ladderId = Guid.NewGuid();
        var player1Id = Guid.NewGuid();
        var player2Id = Guid.NewGuid();

        await SeedLadderAsync(_cs, ladderId, "rrs-known-test", now);
        await SeedPlayerRankAsync(_cs, ladderId, player1Id, now, rating: 1650.0, rd: 120.5, volatility: 0.055);
        await SeedPlayerRankAsync(_cs, ladderId, player2Id, now, rating: 1420.0, rd: 200.0, volatility: 0.06);

        await using var sp = BuildRatingSourceServiceProvider(_cs, _redis.ConnectionString, "rrs-known-test");
        await using var scope = sp.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IPlayerRatingProvider>();

        var result = await provider.GetRatingsAsync(
            new List<Guid> { player1Id, player2Id },
            ladderId);

        Assert.Equal(2, result.Count);

        Assert.True(result.ContainsKey(player1Id), "Player 1 must be present");
        Assert.Equal(1650.0, result[player1Id].Rating, precision: 6);
        Assert.Equal(120.5, result[player1Id].RatingDeviation, precision: 6);
        Assert.Equal(0.055, result[player1Id].Volatility, precision: 6);

        Assert.True(result.ContainsKey(player2Id), "Player 2 must be present");
        Assert.Equal(1420.0, result[player2Id].Rating, precision: 6);
        Assert.Equal(200.0, result[player2Id].RatingDeviation, precision: 6);
        Assert.Equal(0.06, result[player2Id].Volatility, precision: 6);
    }

    // -------------------------------------------------------------------------
    // Test 2: Omits players with no rank row
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-17 SC#2: <c>GetRatingsAsync</c> omits a player that has no <c>player_ranks</c> row
    /// on the requested ladder. The key is absent from the dictionary — not a zero entry.
    /// </summary>
    [Fact]
    public async Task GetRatings_OmitsPlayersWithNoRankRow()
    {
        var now = DateTimeOffset.UtcNow;
        var ladderId = Guid.NewGuid();
        var knownPlayerId = Guid.NewGuid();
        var unknownPlayerId = Guid.NewGuid(); // no player_ranks row

        await SeedLadderAsync(_cs, ladderId, "rrs-absent-test", now);
        await SeedPlayerRankAsync(_cs, ladderId, knownPlayerId, now, rating: 1500.0, rd: 350.0, volatility: 0.06);

        await using var sp = BuildRatingSourceServiceProvider(_cs, _redis.ConnectionString, "rrs-absent-test");
        await using var scope = sp.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IPlayerRatingProvider>();

        var result = await provider.GetRatingsAsync(
            new List<Guid> { knownPlayerId, unknownPlayerId },
            ladderId);

        // Known player is present.
        Assert.True(result.ContainsKey(knownPlayerId));
        // Unknown player must be absent — not a zero entry.
        Assert.False(result.ContainsKey(unknownPlayerId),
            "A player with no rank row must be absent from the dictionary (not a zero entry).");
    }

    // -------------------------------------------------------------------------
    // Test 3: Scopes to the requested ladder
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-17 SC#3: <c>GetRatingsAsync</c> scopes correctly to the requested ladder.
    /// A player seeded on ladder B is absent when querying ladder A.
    /// </summary>
    [Fact]
    public async Task GetRatings_ScopesToLadder()
    {
        var now = DateTimeOffset.UtcNow;
        var ladderAId = Guid.NewGuid();
        var ladderBId = Guid.NewGuid();
        var playerOnAId = Guid.NewGuid();
        var playerOnBOnlyId = Guid.NewGuid();

        await SeedLadderAsync(_cs, ladderAId, "rrs-ladder-a-test", now);
        await SeedLadderAsync(_cs, ladderBId, "rrs-ladder-b-test", now);
        await SeedPlayerRankAsync(_cs, ladderAId, playerOnAId, now, rating: 1600.0, rd: 150.0, volatility: 0.06);
        await SeedPlayerRankAsync(_cs, ladderBId, playerOnBOnlyId, now, rating: 1700.0, rd: 100.0, volatility: 0.055);

        await using var sp = BuildRatingSourceServiceProvider(_cs, _redis.ConnectionString, "rrs-ladder-a-test");
        await using var scope = sp.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IPlayerRatingProvider>();

        // Query ladder A with both player ids.
        var result = await provider.GetRatingsAsync(
            new List<Guid> { playerOnAId, playerOnBOnlyId },
            ladderAId);

        // Player on A is present.
        Assert.True(result.ContainsKey(playerOnAId));
        // Player only on B is absent when querying ladder A.
        Assert.False(result.ContainsKey(playerOnBOnlyId),
            "A player ranked only on ladder B must be absent when querying ladder A.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task SeedLadderAsync(string cs, Guid ladderId, string ladderName, DateTimeOffset now)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
            VALUES ('{ladderId}', '{ladderName}', 'glicko2', true, '{now:O}')
            ON CONFLICT DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedPlayerRankAsync(
        string cs,
        Guid ladderId,
        Guid playerId,
        DateTimeOffset now,
        double rating,
        double rd,
        double volatility)
    {
        var rankId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Insert player row first (FK).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES ('{playerId}', 'RrsPlayer-{playerId:N}', '{now:O}')
                ON CONFLICT DO NOTHING";
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert player_ranks row with exact values.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.player_ranks
                    (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"",
                     ""Wins"", ""Losses"", ""Draws"",
                     ""IsInPlacement"", ""PlacementMatchesRemaining"")
                VALUES
                    ('{rankId}', '{playerId}', '{ladderId}',
                     {rating.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                     {rd.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                     {volatility.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                     0, 0, 0, false, 0)";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static ServiceProvider BuildRatingSourceServiceProvider(
        string cs, string redisCs, string ladderName)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        services
            .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
            .AddRankings()
            .WithRatingsFrom<RankingsRatingSource>()
            .AddLadder(ladderName);

        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, RatingSourceTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCs));

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_rrs_" + Guid.NewGuid().ToString("N")[..12];

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

/// <summary>Test-only model customizer for RankingsRatingSourceTests (bypasses EF global cache — Pitfall 3).</summary>
internal sealed class RatingSourceTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public RatingSourceTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
