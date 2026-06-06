// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Entities;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="ILeaderboardService"/> (RANK-08 / D-23).
/// Covers TopAsync + AroundAsync against live player_ranks and archived season_rank_archive.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class LeaderboardServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with shared Postgres fixture.</summary>
    public LeaderboardServiceTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- TopAsync tests ----

    /// <summary>RANK-08: TopAsync returns rows sorted by Rating DESC, limited to requested count.</summary>
    [Fact]
    public async Task TopAsync_Returns_Sorted_By_Rating_Desc()
    {
        var ladderId = Guid.NewGuid();
        var playerIds = new List<Guid>();
        for (var i = 0; i < 10; i++) playerIds.Add(Guid.NewGuid());

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderAsync(conn, ladderId, "top-n-ladder");
            for (var i = 0; i < 10; i++)
            {
                var rating = 2000.0 - i * 100; // 2000, 1900, ..., 1100
                await SeedPlayerAndRankAsync(conn, playerIds[i], $"Player{i}", ladderId, rating);
            }
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();

        var result = await svc.TopAsync(ladderId, limit: 5);

        Assert.Equal(5, result.Count);
        Assert.Equal(1, result[0].Rank);
        Assert.Equal(2, result[1].Rank);
        Assert.Equal(5, result[4].Rank);
        Assert.Equal(2000.0, result[0].Rating);
        Assert.Equal(1900.0, result[1].Rating);
        Assert.Equal(1800.0, result[2].Rating);
        Assert.Equal(1700.0, result[3].Rating);
        Assert.Equal(1600.0, result[4].Rating);
    }

    /// <summary>
    /// RANK-08: AroundAsync returns window of players centered on the target player
    /// with correct rank assignments.
    /// </summary>
    [Fact]
    public async Task AroundAsync_Returns_Window_Centered_On_Player()
    {
        var ladderId = Guid.NewGuid();
        var playerIds = new List<Guid>();
        for (var i = 0; i < 10; i++) playerIds.Add(Guid.NewGuid());

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderAsync(conn, ladderId, "around-me-ladder");
            for (var i = 0; i < 10; i++)
            {
                var rating = 2000.0 - i * 100; // rank 1=2000, rank 2=1900, ..., rank 10=1100
                await SeedPlayerAndRankAsync(conn, playerIds[i], $"PlayerAround{i}", ladderId, rating);
            }
        }

        // Player at index 5 has rating 1500 (rank 6).
        var targetPlayerId = playerIds[5];

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();

        var result = await svc.AroundAsync(ladderId, targetPlayerId, window: 2);

        // Expect: ranks 4 (1700), 5 (1600), 6 (1500 target), 7 (1400), 8 (1300)
        Assert.Equal(5, result.Count);
        Assert.Equal(4, result[0].Rank);
        Assert.Equal(1700.0, result[0].Rating);
        Assert.Equal(5, result[1].Rank);
        Assert.Equal(1600.0, result[1].Rating);
        Assert.Equal(6, result[2].Rank);
        Assert.Equal(1500.0, result[2].Rating);
        Assert.Equal(targetPlayerId, result[2].PlayerId);
        Assert.Equal(7, result[3].Rank);
        Assert.Equal(1400.0, result[3].Rating);
        Assert.Equal(8, result[4].Rank);
        Assert.Equal(1300.0, result[4].Rating);
    }

    /// <summary>SC#4 precondition: TopAsync with seasonId queries season_rank_archive.</summary>
    [Fact]
    public async Task TopAsync_With_SeasonId_Reads_Archive()
    {
        var ladderId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var playerIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderAsync(conn, ladderId, "archive-top-ladder");
            await SeedSeasonAsync(conn, seasonId, ladderId, 1);
            for (var i = 0; i < 5; i++)
            {
                await SeedPlayerAsync(conn, playerIds[i], $"ArchivePlayer{i}");
                var rating = 1900.0 - i * 100;
                await SeedArchiveRowAsync(conn, ladderId, seasonId, playerIds[i], rating);
            }
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();

        var result = await svc.TopAsync(ladderId, limit: 10, seasonId: seasonId);

        Assert.Equal(5, result.Count);
        // Should be sorted by rating DESC: 1900, 1800, 1700, 1600, 1500
        Assert.Equal(1900.0, result[0].Rating);
        Assert.Equal(1800.0, result[1].Rating);
        Assert.Equal(1700.0, result[2].Rating);
    }

    /// <summary>
    /// AroundAsync for a player with no rank row returns an empty list. Per WR-05, a freshly
    /// registered player who has not completed a ranked match is a normal condition (not a
    /// 500); callers that need a 404 can detect the empty result.
    /// </summary>
    [Fact]
    public async Task Around_NonExistentPlayer_Returns_Empty()
    {
        var ladderId = Guid.NewGuid();
        var nonExistentPlayerId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderAsync(conn, ladderId, "around-notfound-ladder");
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();

        var result = await svc.AroundAsync(ladderId, nonExistentPlayerId, window: 2);

        Assert.Empty(result);
    }

    // ---- Helpers ----

    private static ServiceProvider BuildServiceProvider(string cs)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        services
            .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
            .AddRankings();

        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, LeaderboardTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_leaderboard_" + Guid.NewGuid().ToString("N")[..12];
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

    private static async Task SeedLadderAsync(NpgsqlConnection conn, Guid ladderId, string name)
    {
        var now = DateTimeOffset.UtcNow;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
            VALUES ('{ladderId}', '{name}', 'glicko2', true, '{now:O}')
            ON CONFLICT DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedPlayerAsync(NpgsqlConnection conn, Guid playerId, string displayName)
    {
        var now = DateTimeOffset.UtcNow;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
            VALUES ('{playerId}', '{displayName}', '{now:O}')
            ON CONFLICT DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedPlayerAndRankAsync(
        NpgsqlConnection conn, Guid playerId, string displayName, Guid ladderId, double rating)
    {
        await SeedPlayerAsync(conn, playerId, displayName);
        var now = DateTimeOffset.UtcNow;
        var rankId = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        // IsInPlacement=false / PlacementMatchesRemaining=0: these are established, post-placement
        // ranked players so LeaderboardService surfaces their real Rating (Phase 8 returns null
        // Rating while IsInPlacement=true). The IsInPlacement column was added in Phase 8 migration
        // 20260517000000_RankingsDecayPlacement and defaults to true for rows with zero games.
        cmd.CommandText = $@"
            INSERT INTO gamekit.player_ranks (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"", ""Wins"", ""Losses"", ""Draws"", ""IsInPlacement"", ""PlacementMatchesRemaining"")
            VALUES ('{rankId}', '{playerId}', '{ladderId}', {rating.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 50, 0.06, 0, 0, 0, false, 0)";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedSeasonAsync(NpgsqlConnection conn, Guid seasonId, Guid ladderId, int seasonNumber)
    {
        var now = DateTimeOffset.UtcNow;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.ladder_seasons (""Id"", ""LadderId"", ""SeasonNumber"", ""StartedAt"")
            VALUES ('{seasonId}', '{ladderId}', {seasonNumber}, '{now:O}')";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedArchiveRowAsync(
        NpgsqlConnection conn, Guid ladderId, Guid seasonId, Guid playerId, double rating)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.season_rank_archive
                (""Id"", ""LadderId"", ""SeasonId"", ""PlayerId"", ""Rating"", ""RatingDeviation"", ""Volatility"", ""Wins"", ""Losses"", ""Draws"", ""ArchivedAt"")
            VALUES ('{id}', '{ladderId}', '{seasonId}', '{playerId}', {rating.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 50, 0.06, 0, 0, 0, '{now:O}')";
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Test-only model customizer for LeaderboardServiceTests (Pitfall §3 bypass).</summary>
internal sealed class LeaderboardTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public LeaderboardTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
