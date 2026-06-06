// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Integration tests for regional pool routing (MATCH-18). Covers success criteria SC#1 and SC#2.
/// Wave 2 — Plan 09-02 turns these tests green.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class RegionalPoolTests : IAsyncLifetime
{
    private const string TestRegion = "us-east";

    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp? _app;
    private ConnectionMultiplexer? _mux;

    /// <summary>Constructs the test with injected fixtures.</summary>
    public RegionalPoolTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Configure the default ladder with AllowedRegions = ["us-east"] so SC#1 / SC#2 assertions work.
        _app = new MatchmakingTestApp(configureLadder: cfg =>
        {
            cfg.AllowedRegions = new[] { TestRegion };
        });
        await _app.StartAsync(_pg, _redis);
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_mux is not null) await _mux.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>
    /// SC#1: Enqueue with a RegionName not in AllowedRegions returns HTTP 400 region_not_allowed.
    /// Wave 2 — Plan 09-02.
    /// </summary>
    [Fact]
    public async Task SC1_Enqueue_MismatchedRegionName_Returns400()
    {
        var player = Guid.NewGuid();
        using var client = _app!.CreateClient(player);

        // "eu-west" is not in AllowedRegions=["us-east"] → should be rejected.
        var resp = await client.PostAsJsonAsync("/api/mm/queue",
            new EnqueueRequest(_app.TestLadderId, RegionName: "eu-west"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("region_not_allowed", body.GetProperty("error").GetString());
    }

    /// <summary>
    /// SC#1: Enqueue with null RegionName routes to the default pool (backwards-compat).
    /// Wave 2 — Plan 09-02.
    /// </summary>
    [Fact]
    public async Task SC1_NullRegion_RoutesToDefaultPool()
    {
        var player = Guid.NewGuid();
        using var client = _app!.CreateClient(player);

        // RegionName=null → pool "default" (backwards-compatible v1 behaviour).
        var resp = await client.PostAsJsonAsync("/api/mm/queue",
            new EnqueueRequest(_app.TestLadderId));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ticketId = body.GetProperty("ticketId").GetGuid();

        // Ticket must be in the default pool, not a regional key.
        var db = _mux!.GetDatabase();
        var defaultScore = await db.SortedSetScoreAsync(
            MatchmakingRedisKeys.Queue(_app.TestLadderId, "default"),
            ticketId.ToString());
        var regionalScore = await db.SortedSetScoreAsync(
            MatchmakingRedisKeys.Queue(_app.TestLadderId, TestRegion),
            ticketId.ToString());

        Assert.NotNull(defaultScore);    // ticket IS in the default pool
        Assert.Null(regionalScore);      // ticket is NOT in the us-east pool
    }

    /// <summary>
    /// SC#2: Enqueue with RegionName="us-east" writes to the us-east pool key, not the default pool key.
    /// Wave 2 — Plan 09-02.
    /// </summary>
    [Fact]
    public async Task SC2_RegionalKey_IsDistinctFromDefaultKey()
    {
        var player = Guid.NewGuid();
        using var client = _app!.CreateClient(player);

        // RegionName="us-east" is in AllowedRegions → ticket lands in the us-east pool.
        var resp = await client.PostAsJsonAsync("/api/mm/queue",
            new EnqueueRequest(_app.TestLadderId, RegionName: TestRegion));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ticketId = body.GetProperty("ticketId").GetGuid();

        var db = _mux!.GetDatabase();
        var regionalScore = await db.SortedSetScoreAsync(
            MatchmakingRedisKeys.Queue(_app.TestLadderId, TestRegion),
            ticketId.ToString());
        var defaultScore = await db.SortedSetScoreAsync(
            MatchmakingRedisKeys.Queue(_app.TestLadderId, "default"),
            ticketId.ToString());

        Assert.NotNull(regionalScore);   // ticket IS in us-east pool (SC#2)
        Assert.Null(defaultScore);       // ticket is NOT in default pool (keys are distinct)
    }

    /// <summary>
    /// SC#2: Ticker glob picks up both regional pool keys and the default pool key on each tick.
    /// Wave 2 — Plan 09-02.
    /// </summary>
    [Fact]
    public async Task SC2_TickerGlob_PicksUpBothRegionalAndDefaultKeys()
    {
        // Build an isolated ServiceProvider (no BackgroundService loop) so we drive a single
        // deterministic tick via RunOnceAsync — mirrors MatchmakingLeaderElectionTests pattern.
        // AllowedRegions=["us-east"] so GetPoolNamesForLadder yields ["default","us-east"].
        var ladderName = "regional-tick-test";
        await using var sp = BuildTickerServiceProvider(
            _redis.ConnectionString, ladderName, lockTtlSeconds: 30,
            allowedRegions: new[] { TestRegion });

        var muxOpts = ConfigurationOptions.Parse(_redis.ConnectionString);
        muxOpts.AllowAdmin = true;
        var mux = await ConnectionMultiplexer.ConnectAsync(muxOpts);
        var db = mux.GetDatabase();
        var server = mux.GetServer(mux.GetEndPoints()[0]);

        // Clean slate.
        await server.FlushDatabaseAsync();
        await db.KeyDeleteAsync(MatchmakingRedisKeys.MatcherLock);

        var ladderId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Seed 2 tickets in the "default" pool.
        var d1 = Guid.NewGuid(); var d2 = Guid.NewGuid();
        await SeedTicketAsync(db, d1, ladderId, "default", now, aggregateRating: 1500);
        await SeedTicketAsync(db, d2, ladderId, "default", now + 1, aggregateRating: 1500);
        await db.SortedSetAddAsync(MatchmakingRedisKeys.Queue(ladderId, "default"), d1.ToString(), now);
        await db.SortedSetAddAsync(MatchmakingRedisKeys.Queue(ladderId, "default"), d2.ToString(), now + 1);

        // Seed 2 tickets in the "us-east" pool.
        var r1 = Guid.NewGuid(); var r2 = Guid.NewGuid();
        await SeedTicketAsync(db, r1, ladderId, TestRegion, now + 2, aggregateRating: 1500);
        await SeedTicketAsync(db, r2, ladderId, TestRegion, now + 3, aggregateRating: 1500);
        await db.SortedSetAddAsync(MatchmakingRedisKeys.Queue(ladderId, TestRegion), r1.ToString(), now + 2);
        await db.SortedSetAddAsync(MatchmakingRedisKeys.Queue(ladderId, TestRegion), r2.ToString(), now + 3);

        // Assert both queues are non-empty before the tick.
        Assert.Equal(2, await db.SortedSetLengthAsync(MatchmakingRedisKeys.Queue(ladderId, "default")));
        Assert.Equal(2, await db.SortedSetLengthAsync(MatchmakingRedisKeys.Queue(ladderId, TestRegion)));

        // Drive a single deterministic tick.
        var ticker = sp.GetRequiredService<IMatchmakerTicker>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await ticker.RunOnceAsync(cts.Token);

        // The ticker must have scanned both pools — result is Matched (both produced a match)
        // or at minimum both queues are drained (the ticker processed them).
        Assert.True(
            result == MatcherTickResult.Matched || result == MatcherTickResult.NoMatch,
            $"Unexpected ticker result: {result}");

        // Both pool queues must be empty — the ticker glob enumerated both "default" and
        // "us-east" pool keys and drained them in a single tick (SC#2 key assertion).
        var defaultDepth = await db.SortedSetLengthAsync(MatchmakingRedisKeys.Queue(ladderId, "default"));
        var regionalDepth = await db.SortedSetLengthAsync(MatchmakingRedisKeys.Queue(ladderId, TestRegion));

        Assert.Equal(0, defaultDepth);    // default pool was scanned and drained
        Assert.Equal(0, regionalDepth);   // us-east pool was scanned and drained

        await mux.DisposeAsync();
    }

    // ---- helpers ----

    private ServiceProvider BuildTickerServiceProvider(
        string redisCs, string ladderName, int lockTtlSeconds,
        string[]? allowedRegions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        services
            .AddGameKit(o =>
            {
                o.ConnectionString = _pg.OwnerConnectionString;
                o.AutoMigrate = false;
            })
            .AddMatchmaking(o =>
            {
                o.Ticker.LockTtlSeconds = lockTtlSeconds;
                o.Ticker.TickIntervalMs = 500;
            })
            .AddLadder(ladderName, cfg =>
            {
                if (allowedRegions is { Length: > 0 })
                    cfg.AllowedRegions = allowedRegions;
            });

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCs));

        return services.BuildServiceProvider();
    }

    private static async Task SeedTicketAsync(
        IDatabase db, Guid ticketId, Guid ladderId, string poolName,
        long queuedAtMs, double aggregateRating)
    {
        await db.HashSetAsync(
            MatchmakingRedisKeys.Ticket(ticketId),
            new[]
            {
                new HashEntry("ladderId", ladderId.ToString()),
                new HashEntry("poolName", poolName),
                new HashEntry("queuedAt", queuedAtMs.ToString(CultureInfo.InvariantCulture)),
                new HashEntry("aggregateRating", aggregateRating.ToString(CultureInfo.InvariantCulture)),
                new HashEntry("status", "Queued"),
                new HashEntry("members", "[{\"PlayerId\":\"" + Guid.NewGuid() + "\",\"Rating\":1500.0,\"RatingDeviation\":200.0,\"Volatility\":0.06}]"),
            });
    }
}
