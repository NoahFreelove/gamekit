// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
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
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// SC#6 anchor: RankAdjustService transactional atomicity tests (RANK-12 / D-19 / D-20).
/// Verifies that the SERIALIZABLE transaction rolls back rating update AND audit log insert
/// together on failure, and that normal operation produces correct results.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class AdminRankAdjustTransactionTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with shared Postgres fixture.</summary>
    public AdminRankAdjustTransactionTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- SC#6: rollback atomicity ----

    /// <summary>
    /// SC#6: When an exception occurs after the player_rank UPDATE but before the SERIALIZABLE
    /// transaction is committed, BOTH the rating change AND the audit log insert must be rolled back.
    /// This is proven by injecting a <see cref="FaultAfterFirstSaveInterceptor"/> that throws on
    /// the second <c>SaveChangesAsync</c> call (the audit row save), then verifying the rating is
    /// unchanged and zero audit rows exist.
    /// </summary>
    [Fact]
    public async Task UpdateAndAudit_RollBack_Together_On_Failure()
    {
        var playerId = Guid.NewGuid();
        var ladderId = Guid.NewGuid();
        const double initialRating = 1500.0;

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedPlayerAsync(conn, playerId, "RollbackPlayer");
            await SeedLadderAsync(conn, ladderId, "rollback-ladder");
            await SeedPlayerRankAsync(conn, playerId, ladderId, initialRating);
        }

        // Build a service provider that injects the fault interceptor to simulate
        // the audit SaveChanges failing.
        var faultInterceptor = new FaultAfterFirstSaveInterceptor();
        await using var sp = BuildServiceProvider(_cs, extraInterceptor: faultInterceptor);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRankAdjustService>();

        // The second SaveChangesAsync (audit insert) throws — transaction must roll back.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AdjustAsync(playerId, ladderId, 2000.0, "manual correction", Guid.NewGuid(), CancellationToken.None));

        // Verify rating was NOT changed (rollback occurred).
        await using var verifyConn = new NpgsqlConnection(_cs);
        await verifyConn.OpenAsync();
        var rating = await QueryRatingAsync(verifyConn, playerId, ladderId);
        Assert.Equal(initialRating, rating, precision: 4);

        // Verify no audit row exists (audit insert was also rolled back).
        var auditCount = await CountAuditRowsAsync(verifyConn, playerId);
        Assert.Equal(0, auditCount);
    }

    // ---- Happy path ----

    /// <summary>
    /// D-19: HappyPath — AdjustAsync updates Rating and writes one audit row with correct before/after.
    /// </summary>
    [Fact]
    public async Task HappyPath_Adjusts_Rating_And_Writes_Audit()
    {
        var playerId = Guid.NewGuid();
        var ladderId = Guid.NewGuid();
        const double initialRating = 1500.0;
        const double newRating = 2000.0;

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedPlayerAsync(conn, playerId, "HappyPathPlayer");
            await SeedLadderAsync(conn, ladderId, "happypath-ladder");
            await SeedPlayerRankAsync(conn, playerId, ladderId, initialRating);
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRankAdjustService>();

        var result = await svc.AdjustAsync(playerId, ladderId, newRating, "manual correction", Guid.NewGuid(), CancellationToken.None);

        // Result shape.
        Assert.Equal(initialRating, result.Before, precision: 4);
        Assert.Equal(newRating, result.After, precision: 4);
        Assert.Equal(newRating - initialRating, result.Delta, precision: 4);

        // Verify rating updated in DB.
        await using var verifyConn = new NpgsqlConnection(_cs);
        await verifyConn.OpenAsync();
        var rating = await QueryRatingAsync(verifyConn, playerId, ladderId);
        Assert.Equal(newRating, rating, precision: 4);

        // Verify exactly one audit row.
        var auditCount = await CountAuditRowsAsync(verifyConn, playerId);
        Assert.Equal(1, auditCount);
    }

    // ---- RANK-07 lazy creation ----

    /// <summary>
    /// RANK-07 carry: When no player_ranks row exists, AdjustAsync lazy-creates one with
    /// the supplied newRating and ladder defaults for RD + Volatility (NOT 0 for RD/volatility).
    /// </summary>
    [Fact]
    public async Task LazyCreate_When_PlayerRank_Missing()
    {
        var playerId = Guid.NewGuid();
        var ladderId = Guid.NewGuid();
        const double newRating = 1800.0;

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedPlayerAsync(conn, playerId, "LazyCreatePlayer");
            await SeedLadderAsync(conn, ladderId, "lazycreate-ladder");
            // Deliberately do NOT seed a player_rank row.
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRankAdjustService>();

        var result = await svc.AdjustAsync(playerId, ladderId, newRating, "initial placement", Guid.NewGuid(), CancellationToken.None);

        // Before is 0 (no prior row).
        Assert.Equal(0.0, result.Before, precision: 4);
        Assert.Equal(newRating, result.After, precision: 4);

        // Verify the new row in DB.
        await using var verifyConn = new NpgsqlConnection(_cs);
        await verifyConn.OpenAsync();
        var rating = await QueryRatingAsync(verifyConn, playerId, ladderId);
        Assert.Equal(newRating, rating, precision: 4);

        // RD and Volatility should be the Glicko-2 defaults (not 0).
        var (rd, vol) = await QueryRdVolatilityAsync(verifyConn, playerId, ladderId);
        Assert.True(rd > 0, $"RatingDeviation should be > 0, was {rd}");
        Assert.True(vol > 0, $"Volatility should be > 0, was {vol}");
    }

    // ---- Out-of-bounds rating ----

    /// <summary>
    /// D-19: newRating below MinRating throws ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public async Task OutOfBoundsRating_Below_Min_Throws()
    {
        var playerId = Guid.NewGuid();
        var ladderId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedPlayerAsync(conn, playerId, "BoundsPlayer");
            await SeedLadderAsync(conn, ladderId, "bounds-ladder");
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRankAdjustService>();

        // Default MinRating is 100 (from GameKitRankingsRankAdjustOptions defaults).
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.AdjustAsync(playerId, ladderId, 50.0, "below min", Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>
    /// D-19: newRating above MaxRating throws ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public async Task OutOfBoundsRating_Above_Max_Throws()
    {
        var playerId = Guid.NewGuid();
        var ladderId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedPlayerAsync(conn, playerId, "AboveMaxPlayer");
            await SeedLadderAsync(conn, ladderId, "abovemax-ladder");
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRankAdjustService>();

        // Default MaxRating is 4000 (from GameKitRankingsRankAdjustOptions defaults).
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.AdjustAsync(playerId, ladderId, 5000.0, "above max", Guid.NewGuid(), CancellationToken.None));
    }

    // ---- Short reason ----

    /// <summary>
    /// D-19: reason shorter than 3 characters throws ArgumentException (enforced by AdjustAsync guard
    /// before DB access — delegates to ArgumentException.ThrowIfNullOrEmpty, short string is caught
    /// by the validator layer; the service itself throws for null/empty only).
    /// The validator (RankAdjustRequestValidator) enforces minimum 3 chars at the HTTP layer.
    /// At the service layer, we verify that empty reason is rejected.
    /// </summary>
    [Fact]
    public async Task EmptyReason_Throws_ArgumentException()
    {
        var playerId = Guid.NewGuid();
        var ladderId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedPlayerAsync(conn, playerId, "ReasonPlayer");
            await SeedLadderAsync(conn, ladderId, "reason-ladder");
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRankAdjustService>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AdjustAsync(playerId, ladderId, 1500.0, string.Empty, Guid.NewGuid(), CancellationToken.None));
    }

    // ---- Missing ladder ----

    /// <summary>
    /// D-19: AdjustAsync throws KeyNotFoundException when the ladder does not exist.
    /// </summary>
    [Fact]
    public async Task MissingLadder_Throws_KeyNotFoundException()
    {
        var playerId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedPlayerAsync(conn, playerId, "LadderlessPlayer");
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRankAdjustService>();

        await Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(
            () => svc.AdjustAsync(playerId, Guid.NewGuid(), 1500.0, "valid reason", Guid.NewGuid(), CancellationToken.None));
    }

    // ---- D-20: RD and Volatility not touched ----

    /// <summary>
    /// D-20: Manual rank-adjust updates Rating + LastMatchAt only; RD and Volatility are preserved.
    /// </summary>
    [Fact]
    public async Task Adjust_Does_Not_Modify_RD_Or_Volatility()
    {
        var playerId = Guid.NewGuid();
        var ladderId = Guid.NewGuid();
        const double initialRating = 1500.0;
        const double customRd = 123.45;
        const double customVol = 0.031;

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync();
            await SeedPlayerAsync(conn, playerId, "RdVolPlayer");
            await SeedLadderAsync(conn, ladderId, "rdvol-ladder");
            await SeedPlayerRankWithCustomRdAsync(conn, playerId, ladderId, initialRating, customRd, customVol);
        }

        await using var sp = BuildServiceProvider(_cs);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRankAdjustService>();

        await svc.AdjustAsync(playerId, ladderId, 1800.0, "precision test", Guid.NewGuid(), CancellationToken.None);

        await using var verifyConn = new NpgsqlConnection(_cs);
        await verifyConn.OpenAsync();
        var (rd, vol) = await QueryRdVolatilityAsync(verifyConn, playerId, ladderId);
        Assert.Equal(customRd, rd, precision: 4);
        Assert.Equal(customVol, vol, precision: 4);
    }

    // ---- Helpers ----

    private static ServiceProvider BuildServiceProvider(string cs, FaultAfterFirstSaveInterceptor? extraInterceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        services
            .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
            .AddRankings();

        services.AddDbContext<GameKitDbContext>((_, opts) =>
        {
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, RankAdjustTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            if (extraInterceptor is not null)
                opts.AddInterceptors(extraInterceptor);
        });

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_rankadjust_" + Guid.NewGuid().ToString("N")[..12];
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
        // 1. Core migrations (includes admin_audit_log table).
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // 2. Rankings migrations (includes player_ranks, ladders tables).
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

    private static async Task<double> QueryRatingAsync(NpgsqlConnection conn, Guid playerId, Guid ladderId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""Rating"" FROM gamekit.player_ranks WHERE ""PlayerId"" = @pid AND ""LadderId"" = @lid";
        cmd.Parameters.AddWithValue("pid", playerId);
        cmd.Parameters.AddWithValue("lid", ladderId);
        var raw = await cmd.ExecuteScalarAsync();
        return raw is double d ? d : 0;
    }

    private static async Task<(double Rd, double Volatility)> QueryRdVolatilityAsync(NpgsqlConnection conn, Guid playerId, Guid ladderId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""RatingDeviation"", ""Volatility"" FROM gamekit.player_ranks WHERE ""PlayerId"" = @pid AND ""LadderId"" = @lid";
        cmd.Parameters.AddWithValue("pid", playerId);
        cmd.Parameters.AddWithValue("lid", ladderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (reader.GetDouble(0), reader.GetDouble(1));
        return (0, 0);
    }

    private static async Task<int> CountAuditRowsAsync(NpgsqlConnection conn, Guid targetId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.admin_audit_log WHERE ""TargetId"" = @tid AND ""Action"" = 'admin.player.rank_adjust'";
        cmd.Parameters.AddWithValue("tid", targetId);
        var raw = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(raw);
    }

    private static async Task SeedPlayerAsync(NpgsqlConnection conn, Guid id, string displayName)
    {
        await using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        cmd.CommandText = $@"
            INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES ('{id}', '{displayName}', '{now:O}', false)
            ON CONFLICT DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedLadderAsync(NpgsqlConnection conn, Guid ladderId, string name)
    {
        await using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        cmd.CommandText = $@"
            INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
            VALUES ('{ladderId}', '{name}', 'glicko2', true, '{now:O}')
            ON CONFLICT DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedPlayerRankAsync(NpgsqlConnection conn, Guid playerId, Guid ladderId, double rating)
    {
        await using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        cmd.CommandText = $@"
            INSERT INTO gamekit.player_ranks (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"", ""Wins"", ""Losses"", ""Draws"", ""LastMatchAt"")
            VALUES ('{Guid.NewGuid()}', '{playerId}', '{ladderId}', {rating}, 200, 0.06, 0, 0, 0, '{now:O}')
            ON CONFLICT DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedPlayerRankWithCustomRdAsync(
        NpgsqlConnection conn, Guid playerId, Guid ladderId, double rating, double rd, double vol)
    {
        await using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        cmd.CommandText = $@"
            INSERT INTO gamekit.player_ranks (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"", ""Wins"", ""Losses"", ""Draws"", ""LastMatchAt"")
            VALUES ('{Guid.NewGuid()}', '{playerId}', '{ladderId}', {rating}, {rd}, {vol}, 0, 0, 0, '{now:O}')
            ON CONFLICT DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// EF model customizer for RankAdjust tests. Includes Core + Rankings entities (AdminAuditLog,
/// player_ranks, ladders) needed by RankAdjustService.
/// </summary>
internal sealed class RankAdjustTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs with EF Core required dependencies.</summary>
    public RankAdjustTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}

/// <summary>
/// SC#6 fault injection: throws <see cref="InvalidOperationException"/> on the second
/// <c>SaveChangesAsync</c> call within a single service scope. This simulates the audit-row
/// write failing after the player_rank update has been staged, verifying that the SERIALIZABLE
/// transaction rolls back both changes atomically.
/// </summary>
internal sealed class FaultAfterFirstSaveInterceptor : SaveChangesInterceptor
{
    private int _callCount;

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var count = Interlocked.Increment(ref _callCount);
        if (count >= 2)
            throw new InvalidOperationException("SC#6 fault injection: simulated audit save failure.");
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
