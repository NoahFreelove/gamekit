// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
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
/// Integration tests proving RANK-16 atomic placement decrement in
/// <see cref="PendingRatingUpdatesAdapter.OnCompletedAsync"/>:
/// <list type="bullet">
///   <item><see cref="PlacementDecrement_DecrementsByOne_WhenMoreRemaining"/> — N→N-1, IsInPlacement unchanged.</item>
///   <item><see cref="PlacementDecrement_FlipsIsInPlacement_WhenReachesZero"/> — at 1 remaining → reaches 0, flips false.</item>
///   <item><see cref="PlacementDecrement_DoesNotTouch_NonPlacementPlayer"/> — WHERE guard excludes non-placement rows.</item>
///   <item><see cref="PlacementDecrement_RaceGuard_NeverDropsBelowZero"/> — two sequential decrements from N=1 cannot underflow.</item>
/// </list>
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class PlacementMatchTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres + Redis fixtures.</summary>
    public PlacementMatchTests(PostgresFixture pg, RedisFixture redis)
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
    // Test 1: N > 1 → N-1, still in placement
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-16: A placement player with PlacementMatchesRemaining=5 after one session-complete
    /// has PlacementMatchesRemaining=4 and IsInPlacement=true.
    /// </summary>
    [Fact]
    public async Task PlacementDecrement_DecrementsByOne_WhenMoreRemaining()
    {
        var (ladderId, sessionId, playerId) = await SeedPlacementPlayerAsync(
            _cs, "placement-decrement-test", placementRemaining: 5, isInPlacement: true);

        await InvokeOnCompletedAsync(_cs, _redis.ConnectionString, sessionId, playerId, ladderId, "placement-decrement-test");

        var (remaining, isInPlacement) = await QueryPlacementStateAsync(_cs, playerId, ladderId);
        Assert.Equal(4, remaining);
        Assert.True(isInPlacement);
    }

    // -------------------------------------------------------------------------
    // Test 2: N=1 → 0, flips IsInPlacement to false
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-16: A placement player with PlacementMatchesRemaining=1 after one session-complete
    /// has PlacementMatchesRemaining=0 and IsInPlacement=false.
    /// </summary>
    [Fact]
    public async Task PlacementDecrement_FlipsIsInPlacement_WhenReachesZero()
    {
        var (ladderId, sessionId, playerId) = await SeedPlacementPlayerAsync(
            _cs, "placement-flip-test", placementRemaining: 1, isInPlacement: true);

        await InvokeOnCompletedAsync(_cs, _redis.ConnectionString, sessionId, playerId, ladderId, "placement-flip-test");

        var (remaining, isInPlacement) = await QueryPlacementStateAsync(_cs, playerId, ladderId);
        Assert.Equal(0, remaining);
        Assert.False(isInPlacement);
    }

    // -------------------------------------------------------------------------
    // Test 3: Non-placement player is untouched
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-16: A non-placement player (IsInPlacement=false) is not touched by the
    /// session-complete decrement (WHERE guard PlacementMatchesRemaining > 0 excludes them).
    /// </summary>
    [Fact]
    public async Task PlacementDecrement_DoesNotTouch_NonPlacementPlayer()
    {
        var (ladderId, sessionId, playerId) = await SeedPlacementPlayerAsync(
            _cs, "placement-noop-test", placementRemaining: 0, isInPlacement: false);

        await InvokeOnCompletedAsync(_cs, _redis.ConnectionString, sessionId, playerId, ladderId, "placement-noop-test");

        var (remaining, isInPlacement) = await QueryPlacementStateAsync(_cs, playerId, ladderId);
        Assert.Equal(0, remaining);
        Assert.False(isInPlacement);
    }

    // -------------------------------------------------------------------------
    // Test 4: Race guard — two sequential decrements from N=1 → exactly 0
    // -------------------------------------------------------------------------

    /// <summary>
    /// RANK-16: Two sequential decrements on a player with PlacementMatchesRemaining=1
    /// result in exactly 0 and IsInPlacement=false. The WHERE > 0 race guard prevents
    /// a second decrement from underflowing below 0.
    /// </summary>
    [Fact]
    public async Task PlacementDecrement_RaceGuard_NeverDropsBelowZero()
    {
        var (ladderId, sessionId, playerId) = await SeedPlacementPlayerAsync(
            _cs, "placement-race-test", placementRemaining: 1, isInPlacement: true);

        // First decrement: 1 → 0, flips to false.
        await InvokeOnCompletedAsync(_cs, _redis.ConnectionString, sessionId, playerId, ladderId, "placement-race-test");

        // Seed a second session to trigger another decrement attempt.
        var session2Id = Guid.NewGuid();
        await SeedSessionForPlayerAsync(_cs, session2Id, playerId, ladderId, "placement-race-test");

        // Second decrement: WHERE guard prevents going below 0.
        await InvokeOnCompletedAsync(_cs, _redis.ConnectionString, session2Id, playerId, ladderId, "placement-race-test");

        var (remaining, isInPlacement) = await QueryPlacementStateAsync(_cs, playerId, ladderId);
        Assert.Equal(0, remaining);
        Assert.False(isInPlacement);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<(Guid ladderId, Guid sessionId, Guid playerId)> SeedPlacementPlayerAsync(
        string cs, string ladderName, int placementRemaining, bool isInPlacement)
    {
        var now = DateTimeOffset.UtcNow;
        var ladderId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Player.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES ('{playerId}', 'PlacementPlayer-{playerId:N}', '{now:O}')";
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

        // Re-read actual ladder id.
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

        // Session participant.
        var spId = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.session_participants (""Id"", ""SessionId"", ""PlayerId"", ""Team"")
                VALUES ('{spId}', '{sessionId}', '{playerId}', 0)";
            await cmd.ExecuteNonQueryAsync();
        }

        // Player rank row with controlled placement state.
        var rankId = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.player_ranks
                    (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"",
                     ""Wins"", ""Losses"", ""Draws"",
                     ""IsInPlacement"", ""PlacementMatchesRemaining"")
                VALUES
                    ('{rankId}', '{playerId}', '{ladderId}', 1500, 350, 0.06,
                     0, 0, 0,
                     {(isInPlacement ? "true" : "false")}, {placementRemaining})";
            await cmd.ExecuteNonQueryAsync();
        }

        return (ladderId, sessionId, playerId);
    }

    private static async Task SeedSessionForPlayerAsync(
        string cs, Guid sessionId, Guid playerId, Guid ladderId, string ladderName)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.game_sessions (""Id"", ""State"", ""LadderId"", ""CreatedAt"", ""StartedAt"")
                VALUES ('{sessionId}', 'Active', '{ladderId}', '{now:O}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        var spId = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.session_participants (""Id"", ""SessionId"", ""PlayerId"", ""Team"")
                VALUES ('{spId}', '{sessionId}', '{playerId}', 0)";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task InvokeOnCompletedAsync(
        string cs, string redisCs, Guid sessionId, Guid playerId, Guid ladderId, string ladderName)
    {
        await using var sp = BuildAdapterServiceProvider(cs, redisCs, ladderName);

        await using var scope = sp.CreateAsyncScope();
        var adapter = scope.ServiceProvider.GetRequiredService<PendingRatingUpdatesAdapter>();

        var participants = new[]
        {
            new SessionParticipantSnapshot(playerId, ladderId, SessionResult.Win, null),
        };

        // Simulate the caller's ambient ReadCommitted transaction
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        await using var tx = await ctx.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted);

        await adapter.OnCompletedAsync(sessionId, participants, CancellationToken.None);
        await tx.CommitAsync();
    }

    private static async Task<(int PlacementMatchesRemaining, bool IsInPlacement)>
        QueryPlacementStateAsync(string cs, Guid playerId, Guid ladderId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT ""PlacementMatchesRemaining"", ""IsInPlacement""
            FROM gamekit.player_ranks
            WHERE ""PlayerId"" = '{playerId}' AND ""LadderId"" = '{ladderId}'";
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var remaining = reader.GetInt32(0);
        var isInPlacement = reader.GetBoolean(1);
        return (remaining, isInPlacement);
    }

    private static ServiceProvider BuildAdapterServiceProvider(
        string cs, string redisCs, string ladderName)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        services
            .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
            .AddRankings()
            .AddLadder(ladderName);

        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, PlacementTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCs));

        // Also register the concrete type so tests can resolve PendingRatingUpdatesAdapter directly.
        services.AddScoped<PendingRatingUpdatesAdapter>();

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_placement_" + Guid.NewGuid().ToString("N")[..12];

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

/// <summary>Test-only model customizer for PlacementMatchTests (bypasses EF global cache — Pitfall 3).</summary>
internal sealed class PlacementTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public PlacementTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
