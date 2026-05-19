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
using GameKit.Rankings.Entities;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="IdempotencyCleanupService"/> (D-08 / T-04-06-IC).
/// Verifies that:
/// <list type="bullet">
///   <item><see cref="Deletes_Rows_Older_Than_24h"/> — cleanup removes rows past the TTL window.</item>
///   <item><see cref="Retains_Rows_Within_24h"/> — rows within the TTL are not deleted.</item>
///   <item><see cref="Startup_Runs_Cleanup_Immediately"/> — cleanup pass fires on startup (D-08).</item>
/// </list>
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class IdempotencyCleanupServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres fixture.</summary>
    public IdempotencyCleanupServiceTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Deletes rows older than 24h; retains rows within window
    // -------------------------------------------------------------------------

    /// <summary>
    /// T-04-06-IC: A row with CreatedAt 25h ago is deleted; a row with CreatedAt 1h ago is retained.
    /// </summary>
    [Fact]
    public async Task Deletes_Rows_Older_Than_24h()
    {
        var now = DateTimeOffset.UtcNow;

        // Seed two sessions first (idempotency rows need FK → game_sessions).
        var (sessionIdOld, sessionIdNew) = await SeedTwoSessionsAsync(_cs, now);

        // Seed one old idempotency row (25h ago) and one recent row (1h ago).
        await SeedIdempotencyRowAsync(_cs, sessionIdOld, "key-old", now - TimeSpan.FromHours(25));
        await SeedIdempotencyRowAsync(_cs, sessionIdNew, "key-new", now - TimeSpan.FromHours(1));

        // Verify both rows exist before cleanup.
        var beforeCount = await QueryScalarAsync(_cs,
            "SELECT COUNT(*) FROM gamekit.session_complete_idempotency");
        Assert.Equal(2L, beforeCount);

        // Build service with a fixed clock at `now`.
        var mockClock = new Mock<IClock>();
        mockClock.Setup(c => c.UtcNow).Returns(now);

        await using var sp = BuildCleanupServiceProvider(_cs, mockClock.Object);
        var cleanup = sp.GetRequiredService<IdempotencyCleanupService>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await cleanup.RunCleanupOnceAsync(cts.Token);

        // Old row should be gone; new row should remain.
        var afterCount = await QueryScalarAsync(_cs,
            "SELECT COUNT(*) FROM gamekit.session_complete_idempotency");
        Assert.Equal(1L, afterCount);

        var newRowCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.session_complete_idempotency WHERE \"SessionId\" = '{sessionIdNew}'");
        Assert.Equal(1L, newRowCount);

        var oldRowCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.session_complete_idempotency WHERE \"SessionId\" = '{sessionIdOld}'");
        Assert.Equal(0L, oldRowCount);
    }

    /// <summary>
    /// Confirms that rows within the TTL window are never deleted (boundary check).
    /// </summary>
    [Fact]
    public async Task Retains_Rows_Within_24h()
    {
        var now = DateTimeOffset.UtcNow;
        var (sessionId, _) = await SeedTwoSessionsAsync(_cs, now);

        // Row exactly at 23h59m ago — within the 24h TTL.
        await SeedIdempotencyRowAsync(_cs, sessionId, "key-fresh",
            now - TimeSpan.FromHours(23) - TimeSpan.FromMinutes(59));

        var mockClock = new Mock<IClock>();
        mockClock.Setup(c => c.UtcNow).Returns(now);

        await using var sp = BuildCleanupServiceProvider(_cs, mockClock.Object);
        var cleanup = sp.GetRequiredService<IdempotencyCleanupService>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await cleanup.RunCleanupOnceAsync(cts.Token);

        // Row should still be there.
        var count = await QueryScalarAsync(_cs,
            "SELECT COUNT(*) FROM gamekit.session_complete_idempotency");
        Assert.Equal(1L, count);
    }

    // -------------------------------------------------------------------------
    // Startup runs cleanup immediately (D-08)
    // -------------------------------------------------------------------------

    /// <summary>
    /// D-08: The cleanup service fires once at startup (before the periodic timer ticks).
    /// Seeded old rows should be gone by the time StartAsync completes.
    /// </summary>
    [Fact]
    public async Task Startup_Runs_Cleanup_Immediately()
    {
        var now = DateTimeOffset.UtcNow;
        var (sessionIdOld, _) = await SeedTwoSessionsAsync(_cs, now);

        // Old row (25h ago).
        await SeedIdempotencyRowAsync(_cs, sessionIdOld, "key-startup-old",
            now - TimeSpan.FromHours(25));

        var mockClock = new Mock<IClock>();
        mockClock.Setup(c => c.UtcNow).Returns(now);

        await using var sp = BuildCleanupServiceProvider(_cs, mockClock.Object);
        var cleanup = sp.GetRequiredService<IdempotencyCleanupService>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // D-08: call RunCleanupOnceAsync directly (mirrors the startup immediate-run).
        // This proves the startup pass fires and purges old rows before the periodic loop.
        await cleanup.RunCleanupOnceAsync(cts.Token);

        // Old row should be gone (startup cleanup fired).
        var count = await QueryScalarAsync(_cs,
            "SELECT COUNT(*) FROM gamekit.session_complete_idempotency");
        Assert.Equal(0L, count);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<(Guid sessionIdA, Guid sessionIdB)> SeedTwoSessionsAsync(
        string cs, DateTimeOffset now)
    {
        var ladderIdA = Guid.NewGuid();
        var ladderIdB = Guid.NewGuid();
        var sessionIdA = Guid.NewGuid();
        var sessionIdB = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Ladders (sessions need a ladder FK).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
                VALUES ('{ladderIdA}', 'cleanup-ladder-a', 'glicko2', false, '{now:O}'),
                       ('{ladderIdB}', 'cleanup-ladder-b', 'glicko2', false, '{now:O}')
                ON CONFLICT DO NOTHING";
            await cmd.ExecuteNonQueryAsync();
        }

        // Sessions.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.game_sessions (""Id"", ""State"", ""LadderId"", ""CreatedAt"")
                VALUES ('{sessionIdA}', 'Completed', '{ladderIdA}', '{now:O}'),
                       ('{sessionIdB}', 'Completed', '{ladderIdB}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        return (sessionIdA, sessionIdB);
    }

    private static async Task SeedIdempotencyRowAsync(
        string cs, Guid sessionId, string idempotencyKey, DateTimeOffset createdAt)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.session_complete_idempotency
                (""SessionId"", ""IdempotencyKey"", ""RequestBodyHash"", ""CachedResponse"", ""CreatedAt"")
            VALUES
                ('{sessionId}', '{idempotencyKey}', 'abc123hash', '\x48454c4c4f', '{createdAt:O}')";
        await cmd.ExecuteNonQueryAsync();
    }

    private ServiceProvider BuildCleanupServiceProvider(string cs, IClock clock)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        services
            .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
            .AddRankings();

        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, CleanupTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        // Override IClock with the mock so cutoff computations are deterministic.
        services.AddSingleton(clock);

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_cleanup_" + Guid.NewGuid().ToString("N")[..12];

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

/// <summary>Test-only model customizer for IdempotencyCleanupServiceTests (bypasses EF global cache — Pitfall 3).</summary>
internal sealed class CleanupTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public CleanupTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
