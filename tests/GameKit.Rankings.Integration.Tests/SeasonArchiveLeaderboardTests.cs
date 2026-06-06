// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
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
/// SC#4 anchor tests for seasonal archive + leaderboard (RANK-10 / D-11 / D-12 / D-13 / D-14).
/// Verifies: archive preserves prior-season rankings, three reset policies, audit row, around-me on archived season.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class SeasonArchiveLeaderboardTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with shared Postgres fixture.</summary>
    public SeasonArchiveLeaderboardTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- SC#4: Archive preserves prior-season top-N ----

    /// <summary>
    /// SC#4: After EndSeasonService.EndAsync, season_rank_archive contains exactly the same
    /// top-N rows as the pre-end player_ranks, in the same order.
    /// </summary>
    [Fact]
    public async Task Archive_Preserves_Previous_Season_TopN()
    {
        var ladderId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var playerIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderWithConfigAsync(conn, ladderId, "archive-topn-ladder", SeasonResetPolicy.ArchiveOnly);
            await SeedSeasonAsync(conn, seasonId, ladderId, 1);
            for (var i = 0; i < 10; i++)
            {
                var rating = 2000.0 - i * 100; // 2000, 1900, ..., 1100
                await SeedPlayerAndRankAsync(conn, playerIds[i], $"ArchivePlayer{i}", ladderId, rating);
            }
        }

        // Capture top 5 before end-season.
        var topBefore = new List<(Guid PlayerId, double Rating)>();
        for (var i = 0; i < 5; i++)
            topBefore.Add((playerIds[i], 2000.0 - i * 100));

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEndSeasonService>();

        var actorId = Guid.NewGuid();
        var result = await svc.EndAsync(ladderId, actorId, default);

        // 10 archive rows should exist for the closed season.
        Assert.Equal(10, result.ArchivedRowCount);

        // Verify archive rows via ILeaderboardService.TopAsync with seasonId.
        var leaderboard = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();
        var archived = await leaderboard.TopAsync(ladderId, limit: 5, seasonId: result.ClosedSeasonId);

        Assert.Equal(5, archived.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(topBefore[i].PlayerId, archived[i].PlayerId);
            // Season archive rows always have non-null Rating (completed placement ranks).
            Assert.Equal(topBefore[i].Rating, archived[i].Rating ?? 0.0, precision: 5);
        }
    }

    // ---- D-12: SoftRegress policy ----

    /// <summary>
    /// D-12: SoftRegress applies rating regression toward default (factor 0.5) and RD bump of 50.
    /// </summary>
    [Fact]
    public async Task SoftRegress_Reduces_Rating_Toward_Default()
    {
        var ladderId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        // SoftRegress: default 1500, factor 0.5, rdCeiling 200, rdBump 50
        // Player starts at 2000, RD=100.
        // Expected new rating: 1500 + (2000 - 1500) * 0.5 = 1750
        // Expected new RD: min(200, 100 + 50) = 150

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderWithConfigAsync(conn, ladderId, "softregress-ladder", SeasonResetPolicy.SoftRegress,
                regressionFactor: 0.5, rdCeiling: 200, rdBump: 50);
            await SeedSeasonAsync(conn, seasonId, ladderId, 1);
            await SeedPlayerAndRankWithRdAsync(conn, playerId, "SoftPlayer", ladderId, rating: 2000, rd: 100);
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEndSeasonService>();
        await svc.EndAsync(ladderId, Guid.NewGuid(), default);

        // Assert live player_ranks was mutated.
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var rank = await ctx.Set<PlayerRank>()
            .FirstOrDefaultAsync(r => r.LadderId == ladderId && r.PlayerId == playerId);

        Assert.NotNull(rank);
        Assert.Equal(1750.0, rank.Rating, precision: 5);
        Assert.Equal(150.0, rank.RatingDeviation, precision: 5);
        Assert.Equal(0.06, rank.Volatility, precision: 5); // reset to ladder default
    }

    // ---- D-12: HardReset policy ----

    /// <summary>D-12: HardReset resets rating, RD, and volatility to ladder defaults.</summary>
    [Fact]
    public async Task HardReset_Resets_To_Defaults()
    {
        var ladderId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderWithConfigAsync(conn, ladderId, "hardreset-ladder", SeasonResetPolicy.HardReset);
            await SeedSeasonAsync(conn, seasonId, ladderId, 1);
            await SeedPlayerAndRankAsync(conn, playerId, "HardPlayer", ladderId, rating: 2000);
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEndSeasonService>();
        await svc.EndAsync(ladderId, Guid.NewGuid(), default);

        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var rank = await ctx.Set<PlayerRank>()
            .FirstOrDefaultAsync(r => r.LadderId == ladderId && r.PlayerId == playerId);

        Assert.NotNull(rank);
        Assert.Equal(1500.0, rank.Rating, precision: 5);
        Assert.Equal(350.0, rank.RatingDeviation, precision: 5);
        Assert.Equal(0.06, rank.Volatility, precision: 5);
    }

    // ---- D-12: ArchiveOnly policy ----

    /// <summary>D-12: ArchiveOnly writes archive row but leaves live player_ranks unchanged.</summary>
    [Fact]
    public async Task ArchiveOnly_Leaves_PlayerRanks_Unchanged()
    {
        var ladderId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderWithConfigAsync(conn, ladderId, "archiveonly-ladder", SeasonResetPolicy.ArchiveOnly);
            await SeedSeasonAsync(conn, seasonId, ladderId, 1);
            await SeedPlayerAndRankAsync(conn, playerId, "ArchiveOnlyPlayer", ladderId, rating: 2000);
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEndSeasonService>();
        var result = await svc.EndAsync(ladderId, Guid.NewGuid(), default);

        Assert.Equal(SeasonResetPolicy.ArchiveOnly, result.AppliedPolicy);

        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

        // Archive row should exist.
        var archiveCount = await ctx.Set<SeasonRankArchive>()
            .CountAsync(a => a.LadderId == ladderId && a.SeasonId == result.ClosedSeasonId);
        Assert.Equal(1, archiveCount);

        // Live rank should be unchanged.
        var rank = await ctx.Set<PlayerRank>()
            .FirstOrDefaultAsync(r => r.LadderId == ladderId && r.PlayerId == playerId);
        Assert.NotNull(rank);
        Assert.Equal(2000.0, rank.Rating, precision: 5);
    }

    // ---- T-04-07-AT: Audit row ----

    /// <summary>T-04-07-AT: EndSeasonService.EndAsync writes exactly one admin_audit_log row.</summary>
    [Fact]
    public async Task EndSeason_Writes_Audit_Row()
    {
        var ladderId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderWithConfigAsync(conn, ladderId, "audit-ladder", SeasonResetPolicy.ArchiveOnly);
            await SeedSeasonAsync(conn, seasonId, ladderId, 1);
            await SeedPlayerAndRankAsync(conn, playerId, "AuditPlayer", ladderId, rating: 1600);
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEndSeasonService>();
        await svc.EndAsync(ladderId, actorId, default);

        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var auditRows = await ctx.Set<AdminAuditLog>()
            .Where(a => a.Action == "admin.ladder.end_season" && a.TargetId == ladderId)
            .ToListAsync();

        Assert.Single(auditRows);
        var row = auditRows[0];
        Assert.Equal(actorId, row.ActorId);
        Assert.Equal("ladder", row.TargetType);
        Assert.Equal(ladderId, row.TargetId);
        Assert.NotNull(row.Before);
        Assert.NotNull(row.After);
    }

    // ---- AroundAsync on archived season ----

    /// <summary>AroundAsync with seasonId returns the window centered on a target player from the archive.</summary>
    [Fact]
    public async Task AroundAsync_On_Archived_Season_Works()
    {
        var ladderId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var playerIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedLadderWithConfigAsync(conn, ladderId, "around-archive-ladder", SeasonResetPolicy.ArchiveOnly);
            await SeedSeasonAsync(conn, seasonId, ladderId, 1);
            for (var i = 0; i < 10; i++)
            {
                var rating = 2000.0 - i * 100;
                await SeedPlayerAndRankAsync(conn, playerIds[i], $"ArPlayer{i}", ladderId, rating);
            }
        }

        // End the season — this archives all ranks.
        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEndSeasonService>();
        var result = await svc.EndAsync(ladderId, Guid.NewGuid(), default);

        // Target player is index 5 (rating 1500, rank 6 in the archived season).
        var targetPlayerId = playerIds[5];

        var leaderboard = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();
        var around = await leaderboard.AroundAsync(ladderId, targetPlayerId, window: 2, seasonId: result.ClosedSeasonId);

        // Expect: ranks 4 (1700), 5 (1600), 6 (1500 target), 7 (1400), 8 (1300)
        Assert.Equal(5, around.Count);
        // Season archive rows always have non-null Rating (completed placement ranks).
        Assert.Equal(1700.0, around[0].Rating ?? 0.0, precision: 5);
        Assert.Equal(1600.0, around[1].Rating ?? 0.0, precision: 5);
        Assert.Equal(1500.0, around[2].Rating ?? 0.0, precision: 5);
        Assert.Equal(targetPlayerId, around[2].PlayerId);
        Assert.Equal(1400.0, around[3].Rating ?? 0.0, precision: 5);
        Assert.Equal(1300.0, around[4].Rating ?? 0.0, precision: 5);
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
                .ReplaceService<IModelCustomizer, SeasonArchiveTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_season_" + Guid.NewGuid().ToString("N")[..12];
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

    private static async Task SeedLadderWithConfigAsync(
        NpgsqlConnection conn,
        Guid ladderId,
        string name,
        SeasonResetPolicy policy,
        double regressionFactor = 0.5,
        double rdCeiling = 200,
        double rdBump = 50)
    {
        var now = DateTimeOffset.UtcNow;
        var configJson = JsonSerializer.Serialize(new
        {
            DefaultRating = 1500.0,
            DefaultRd = 350.0,
            DefaultVolatility = 0.06,
            ResetPolicy = policy.ToString(),
            RegressionFactor = regressionFactor,
            RdCeiling = rdCeiling,
            RdBump = rdBump,
        });
        // Escape single quotes in JSON for raw SQL.
        var escapedJson = configJson.Replace("'", "''");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""Config"", ""CreatedAt"")
            VALUES ('{ladderId}', '{name}', 'glicko2', true, '{escapedJson}'::jsonb, '{now:O}')
            ON CONFLICT DO NOTHING";
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
        var rankId = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.player_ranks
                (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"", ""Wins"", ""Losses"", ""Draws"")
            VALUES ('{rankId}', '{playerId}', '{ladderId}',
                {rating.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 50, 0.06, 0, 0, 0)";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedPlayerAndRankWithRdAsync(
        NpgsqlConnection conn, Guid playerId, string displayName, Guid ladderId, double rating, double rd)
    {
        await SeedPlayerAsync(conn, playerId, displayName);
        var rankId = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.player_ranks
                (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"", ""Wins"", ""Losses"", ""Draws"")
            VALUES ('{rankId}', '{playerId}', '{ladderId}',
                {rating.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                {rd.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                0.06, 0, 0, 0)";
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Test-only model customizer for SeasonArchiveLeaderboardTests (Pitfall §3 bypass).</summary>
internal sealed class SeasonArchiveTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public SeasonArchiveTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
