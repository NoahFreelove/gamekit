// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
using Xunit.Abstractions;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// SC#1 anchor: 1000-match two-population convergence test (RANK-06).
/// Proves the full RankingsTickerService + Glicko2Algorithm pipeline produces correctly
/// converging ratings for two populations with known true skill values.
/// </summary>
/// <remarks>
/// SC#1 specification:
/// <list type="bullet">
///   <item>Two 50-player populations: "strong" (true skill 1700) and "weak" (true skill 1300).</item>
///   <item>All 100 players start at Glicko-2 defaults: rating 1500, rd 350, volatility 0.06.</item>
///   <item>1000 paired matches; outcomes are probabilistic based on true-skill delta.</item>
///   <item>100 rating periods (10 matches per period) using a clock that advances past the period.</item>
///   <item>After 1000 matches: mean strong rating within ±50 of 1700; mean weak within ±50 of 1300.</item>
/// </list>
/// Random seed 42 is pinned for determinism.
/// </remarks>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class Glicko2ConvergenceTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private readonly ITestOutputHelper _output;
    private string _cs = string.Empty;

    /// <summary>Constructs with shared Postgres + Redis fixtures.</summary>
    public Glicko2ConvergenceTests(PostgresFixture pg, RedisFixture redis, ITestOutputHelper output)
    {
        _pg = pg;
        _redis = redis;
        _output = output;
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
    // SC#1 anchor: 1000-match convergence
    // -------------------------------------------------------------------------

    /// <summary>
    /// SC#1: After 1000 simulated matches across 100 rating periods, the mean Glicko-2
    /// rating of the "strong" population (true skill 1700) converges to within ±50 of 1700,
    /// and the mean of the "weak" population (true skill 1300) converges to within ±50 of 1300.
    /// </summary>
    [Fact]
    public async Task Two_Populations_Converge_Within_Tolerance()
    {
        var sw = Stopwatch.StartNew();
        const int totalMatches = 1000;
        const int matchesPerPeriod = 10;
        const int periods = totalMatches / matchesPerPeriod; // 100
        const int strongCount = 50;
        const int weakCount = 50;
        const double strongTrueSkill = 1700.0;
        const double weakTrueSkill = 1300.0;
        const double convergenceTolerance = 50.0; // Glickman's documented tolerance

        const string ladderName = "convergence-test-ladder";
        var rng = new Random(42); // deterministic seed per SC#1

        // Seed 100 players: 50 strong, 50 weak.
        var (strongIds, weakIds, ladderId) = await SeedPlayersAndLadderAsync(_cs, ladderName,
            strongCount, weakCount);

        var allPlayers = strongIds
            .Select(id => (Id: id, TrueSkill: strongTrueSkill))
            .Concat(weakIds.Select(id => (Id: id, TrueSkill: weakTrueSkill)))
            .ToList();

        // Build the ticker service provider.
        // Use a StepClock that we manually advance between periods.
        var stepClock = new StepClock(DateTimeOffset.UtcNow);

        await using var sp = BuildTickerServiceProvider(_cs, _redis.ConnectionString, ladderName, stepClock);
        await sp.GetRequiredService<StartupLadderUpserter>().StartAsync(default);

        var ticker = sp.GetRequiredService<IRankingsTicker>();

        // Flush any stale lock.
        var mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        await mux.GetDatabase().KeyDeleteAsync("gamekit:rankings:ticker:lease");

        // Simulate 100 rating periods of 10 matches each.
        for (var period = 0; period < periods; period++)
        {
            // Generate 10 matches this period.
            for (var m = 0; m < matchesPerPeriod; m++)
            {
                // Pick two random players (no self-match).
                int idxA, idxB;
                do
                {
                    idxA = rng.Next(allPlayers.Count);
                    idxB = rng.Next(allPlayers.Count);
                } while (idxA == idxB);

                var playerA = allPlayers[idxA];
                var playerB = allPlayers[idxB];

                // Determine outcome: P(A wins) = Elo expected-score formula.
                var pAWins = 1.0 / (1.0 + Math.Pow(10.0, (playerB.TrueSkill - playerA.TrueSkill) / 400.0));
                var aWins = rng.NextDouble() < pAWins;

                // Insert a session + session_participants + pending_rating_updates.
                await InsertMatchAsync(_cs, ladderId, playerA.Id, playerB.Id,
                    aWins ? "Win" : "Loss",
                    aWins ? "Loss" : "Win",
                    stepClock.UtcNow);
            }

            // Advance the clock past the rating period (RatingPeriod = 0 → already past it).
            stepClock.Advance(TimeSpan.FromHours(2)); // advance past the 1-hour default period

            // Run one ticker drain.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await ticker.RunOnceAsync(cts.Token);

            // Each period should drain successfully (or NoLaddersDue if the clock didn't advance enough,
            // but we advance by 2h so a 1h period is always elapsed).
            Assert.True(result == TickResult.Drained || result == TickResult.NoLaddersDue,
                $"Period {period}: unexpected TickResult {result}");
        }

        sw.Stop();
        _output.WriteLine($"SC#1 simulation completed in {sw.ElapsedMilliseconds:N0}ms " +
                          $"({periods} periods × {matchesPerPeriod} matches).");

        // Read final ratings for all players.
        var finalRatings = await ReadPlayerRatingsAsync(_cs, ladderId, allPlayers.Select(p => p.Id).ToList());

        // Compute statistics.
        var strongRatings = strongIds
            .Where(id => finalRatings.ContainsKey(id))
            .Select(id => finalRatings[id])
            .ToList();

        var weakRatings = weakIds
            .Where(id => finalRatings.ContainsKey(id))
            .Select(id => finalRatings[id])
            .ToList();

        if (strongRatings.Count == 0 || weakRatings.Count == 0)
        {
            Assert.Fail("No player_ranks rows found — the ticker never drained successfully.");
        }

        var strongMean = strongRatings.Average();
        var weakMean = weakRatings.Average();
        var strongStdDev = StdDev(strongRatings);
        var weakStdDev = StdDev(weakRatings);

        _output.WriteLine($"Strong population: mean={strongMean:F1} (true={strongTrueSkill}), " +
                          $"std={strongStdDev:F1}, n={strongRatings.Count}");
        _output.WriteLine($"Weak population:   mean={weakMean:F1} (true={weakTrueSkill}), " +
                          $"std={weakStdDev:F1}, n={weakRatings.Count}");

        // SC#1 convergence assertions.
        Assert.True(
            Math.Abs(strongMean - strongTrueSkill) <= convergenceTolerance,
            $"SC#1 FAIL: strong mean {strongMean:F1} is not within {convergenceTolerance} of true skill {strongTrueSkill}. " +
            $"Delta: {Math.Abs(strongMean - strongTrueSkill):F1}");

        Assert.True(
            Math.Abs(weakMean - weakTrueSkill) <= convergenceTolerance,
            $"SC#1 FAIL: weak mean {weakMean:F1} is not within {convergenceTolerance} of true skill {weakTrueSkill}. " +
            $"Delta: {Math.Abs(weakMean - weakTrueSkill):F1}");

        // Sanity: standard deviation should be bounded (ratings not exploding).
        Assert.True(strongStdDev < 200,
            $"SC#1 WARN: strong population std dev {strongStdDev:F1} is unexpectedly high (>200).");
        Assert.True(weakStdDev < 200,
            $"SC#1 WARN: weak population std dev {weakStdDev:F1} is unexpectedly high (>200).");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<(List<Guid> strongIds, List<Guid> weakIds, Guid ladderId)>
        SeedPlayersAndLadderAsync(string cs, string ladderName, int strongCount, int weakCount)
    {
        var now = DateTimeOffset.UtcNow;
        var strongIds = Enumerable.Range(0, strongCount).Select(_ => Guid.NewGuid()).ToList();
        var weakIds = Enumerable.Range(0, weakCount).Select(_ => Guid.NewGuid()).ToList();
        var ladderId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Insert players in bulk.
        var playerValues = string.Join(",\n",
            strongIds.Select((id, i) => $"('{id}', 'Strong{i:D2}', '{now:O}')")
            .Concat(weakIds.Select((id, i) => $"('{id}', 'Weak{i:D2}', '{now:O}')")));

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES {playerValues}";
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert ladder with RatingPeriodSeconds = 3600 (1 hour).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.ladders
                    (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"",
                     ""Config"")
                VALUES ('{ladderId}', '{ladderName}', 'glicko2', true, '{now:O}',
                        '{{""DefaultRating"":1500,""DefaultRd"":350,""DefaultVolatility"":0.06,""RatingPeriodSeconds"":3600}}')
                ON CONFLICT DO NOTHING";
            await cmd.ExecuteNonQueryAsync();
        }

        // Re-read actual ladder id (may be overwritten by StartupLadderUpserter).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"Id\" FROM gamekit.ladders WHERE \"Name\" = '{ladderName}'";
            var result = await cmd.ExecuteScalarAsync();
            if (result is Guid existingId)
                ladderId = existingId;
        }

        return (strongIds, weakIds, ladderId);
    }

    private static async Task InsertMatchAsync(
        string cs, Guid ladderId, Guid playerAId, Guid playerBId,
        string resultA, string resultB, DateTimeOffset now)
    {
        var sessionId = Guid.NewGuid();
        var sp1Id = Guid.NewGuid();
        var sp2Id = Guid.NewGuid();
        var upd1Id = Guid.NewGuid();
        var upd2Id = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.game_sessions (""Id"", ""State"", ""LadderId"", ""CreatedAt"", ""StartedAt"")
                VALUES ('{sessionId}', 'Completed', '{ladderId}', '{now:O}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.session_participants (""Id"", ""SessionId"", ""PlayerId"", ""Team"")
                VALUES ('{sp1Id}', '{sessionId}', '{playerAId}', 0),
                       ('{sp2Id}', '{sessionId}', '{playerBId}', 1)";
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.pending_rating_updates
                    (""Id"", ""SessionId"", ""PlayerId"", ""LadderId"", ""Result"", ""EnqueuedAt"")
                VALUES
                    ('{upd1Id}', '{sessionId}', '{playerAId}', '{ladderId}', '{resultA}', '{now:O}'),
                    ('{upd2Id}', '{sessionId}', '{playerBId}', '{ladderId}', '{resultB}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<Dictionary<Guid, double>> ReadPlayerRatingsAsync(
        string cs, Guid ladderId, List<Guid> playerIds)
    {
        var ratings = new Dictionary<Guid, double>();
        var idList = string.Join(",", playerIds.Select(id => $"'{id}'"));

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT ""PlayerId"", ""Rating""
            FROM gamekit.player_ranks
            WHERE ""LadderId"" = '{ladderId}'
            AND ""PlayerId"" IN ({idList})";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var playerId = reader.GetGuid(0);
            var rating = reader.GetDouble(1);
            ratings[playerId] = rating;
        }

        return ratings;
    }

    private static double StdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        var sumSq = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }

    private static ServiceProvider BuildTickerServiceProvider(
        string cs, string redisCs, string ladderName, StepClock clock)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        services
            .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
            .AddRankings()
            .AddLadder(ladderName);

        services.AddDbContext<GameKitDbContext>((_, opts) =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, ConvergenceTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCs));

        // Override IClock with the StepClock so we can advance time between periods.
        services.AddSingleton<GameKit.Core.Services.IClock>(clock);

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_conv_" + Guid.NewGuid().ToString("N")[..12];

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

/// <summary>Adjustable clock for convergence test — allows advancing simulated time between rating periods.</summary>
internal sealed class StepClock : GameKit.Core.Services.IClock
{
    private DateTimeOffset _current;

    /// <summary>Initializes the clock at the given starting time.</summary>
    public StepClock(DateTimeOffset start) => _current = start;

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _current;

    /// <summary>Advances the simulated clock by <paramref name="delta"/>.</summary>
    public void Advance(TimeSpan delta) => _current += delta;
}

/// <summary>Test-only model customizer for Glicko2ConvergenceTests (bypasses EF global cache — Pitfall 3).</summary>
internal sealed class ConvergenceTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public ConvergenceTestModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
