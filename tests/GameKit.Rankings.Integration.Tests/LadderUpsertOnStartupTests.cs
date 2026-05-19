// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Builder;
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
/// Integration tests for <c>StartupLadderUpserter</c> (RANK-09 / D-21).
/// Verifies that <c>AddRankings().AddLadder("name")</c> causes the ladder row to be
/// created on first host start and that calling <c>StartAsync</c> a second time does
/// not produce duplicate rows.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class LadderUpsertOnStartupTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres fixture.</summary>
    public LadderUpsertOnStartupTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// RANK-09: <c>StartAsync</c> inserts the registered ladder row on first boot,
    /// then calling <c>StartAsync</c> again does not insert a duplicate (idempotent).
    /// </summary>
    [Fact]
    public async Task AddLadder_Inserts_Row_Idempotently()
    {
        // Act — call StartAsync twice (simulates two application restarts on the same DB).
        await RunStartupLadderUpserterAsync(_cs, "test-ladder");
        await RunStartupLadderUpserterAsync(_cs, "test-ladder");

        // Assert — exactly one row with the expected defaults.
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM gamekit.ladders WHERE \"Name\" = 'test-ladder'";
        var count = (long)(await countCmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);

        await using var dataCmd = conn.CreateCommand();
        dataCmd.CommandText = "SELECT \"Algorithm\", \"IsActive\" FROM gamekit.ladders WHERE \"Name\" = 'test-ladder'";
        await using var reader = await dataCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("glicko2", reader.GetString(0));
        Assert.True(reader.GetBoolean(1)); // IsActive defaults to true
    }

    /// <summary>
    /// Verifies that <c>AddLadder</c> throws <see cref="ArgumentException"/> when the same
    /// name is registered twice at build time (duplicate-registration guard).
    /// </summary>
    [Fact]
    public void AddLadder_DuplicateName_Throws_ArgumentException()
    {
        var services = new ServiceCollection();

        // AddGameKit requires a ConnectionString — use a dummy for this pure DI test.
        var gkBuilder = services.AddGameKit(o => { o.ConnectionString = _cs; o.AutoMigrate = false; });

        Assert.Throws<ArgumentException>(() =>
        {
            var builder = gkBuilder.AddRankings(o => { });
            builder.AddLadder("dup-ladder");
            builder.AddLadder("dup-ladder"); // second registration must throw
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task RunStartupLadderUpserterAsync(string cs, string ladderName)
    {
        var services = new ServiceCollection();
        services
            .AddGameKit(o =>
            {
                o.ConnectionString = cs;
                o.MigrationsConnectionString = cs;
                o.AutoMigrate = false;
            })
            .AddRankings(o => { })
            .AddLadder(ladderName);

        // Add minimal logging so hosted services don't throw on ILogger resolution.
        services.AddLogging();

        // Override DbContext registration to use RankingsCliModelCustomizer — this bypasses
        // EF Core's global model cache which may have been populated by a prior Core-only
        // DbContext in the same test-runner process (Pitfall 3 from 04-02-SUMMARY).
        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
            dbOpts
                .UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, LadderUpserterTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();

        var upserter = scope.ServiceProvider.GetRequiredService<Services.StartupLadderUpserter>();
        await upserter.StartAsync(default);
    }

    /// <summary>
    /// Test-only model customizer that applies all Rankings entity configurations via
    /// <see cref="RankingsModelBuilderExtension"/>, bypassing the global EF Core model cache
    /// (Pitfall 3 — mirrors AdminCliModelCustomizer pattern).
    /// </summary>
    internal sealed class LadderUpserterTestModelCustomizer : RelationalModelCustomizer
    {
        /// <summary>Constructs the customizer.</summary>
        public LadderUpserterTestModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies) { }

        /// <inheritdoc />
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            // Apply all Rankings entity configurations directly using the internal extension.
            new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
        }
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_ladder_" + Guid.NewGuid().ToString("N")[..12];

        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var cs = builder.ConnectionString;

        await using (var freshConn = new NpgsqlConnection(cs))
        {
            await freshConn.OpenAsync();
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync();
        }

        return cs;
    }

    private static async Task ApplyMigrationsAsync(string cs)
    {
        // Core
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // Rankings
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
