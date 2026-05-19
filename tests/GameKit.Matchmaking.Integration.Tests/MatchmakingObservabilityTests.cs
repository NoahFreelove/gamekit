// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// SC#6 phase-gate integration tests for <see cref="IMatchmakingObservability"/> +
/// <see cref="RedisMatchmakingObservability"/>. Verifies queue depth comes LIVE from Redis
/// (ZCARD per match), the leader instance id is sourced from the Redis lock key, and
/// stale Postgres rows cannot mask the live depth.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class MatchmakingObservabilityTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;
    private ConnectionMultiplexer? _mux;
    private readonly string _redisKeyPrefix = "obs_" + Guid.NewGuid().ToString("N")[..8] + "_";

    public MatchmakingObservabilityTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync(_cs);

        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);

        // Sanity: clear any stale keys we might use.
        var server = _mux.GetServer(_mux.GetEndPoints()[0]);
        foreach (var k in server.Keys(pattern: "mm:*"))
        {
            await _mux.GetDatabase().KeyDeleteAsync(k);
        }
        await _mux.GetDatabase().KeyDeleteAsync(MatchmakingRedisKeys.MatcherLock);
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null)
        {
            await _mux.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetQueueStats_Returns_LiveZCardPerPool()
    {
        var ladderA = Guid.NewGuid();
        var ladderB = Guid.NewGuid();
        var db = _mux!.GetDatabase();
        var keyA = MatchmakingRedisKeys.Queue(ladderA, "default");
        var keyB = MatchmakingRedisKeys.Queue(ladderB, "default");

        try
        {
            for (var i = 0; i < 5; i++)
            {
                await db.SortedSetAddAsync(keyA, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i);
            }
            for (var i = 0; i < 3; i++)
            {
                await db.SortedSetAddAsync(keyB, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i);
            }

            var sut = new RedisMatchmakingObservability(_mux, new SystemClock(), NullLogger<RedisMatchmakingObservability>.Instance);

            var stats = await sut.GetQueueStatsAsync();

            var poolA = stats.Pools.FirstOrDefault(p => p.LadderId == ladderA);
            var poolB = stats.Pools.FirstOrDefault(p => p.LadderId == ladderB);
            Assert.NotNull(poolA);
            Assert.NotNull(poolB);
            Assert.Equal(5L, poolA!.Depth);
            Assert.Equal(3L, poolB!.Depth);
        }
        finally
        {
            await db.KeyDeleteAsync(keyA);
            await db.KeyDeleteAsync(keyB);
        }
    }

    [Fact]
    public async Task GetQueueStats_LeaderIdentity_Comes_From_LockKey_Not_Postgres()
    {
        var db = _mux!.GetDatabase();
        const string instance = "test-instance-abc";
        await db.StringSetAsync(MatchmakingRedisKeys.MatcherLock, instance, TimeSpan.FromMinutes(1));

        try
        {
            var sut = new RedisMatchmakingObservability(_mux, new SystemClock());
            var stats = await sut.GetQueueStatsAsync();
            Assert.Equal(instance, stats.LeaderInstanceId);
            Assert.Equal(1, stats.ActiveLeaseCount);
        }
        finally
        {
            await db.KeyDeleteAsync(MatchmakingRedisKeys.MatcherLock);
        }
    }

    [Fact]
    public async Task GetQueueStats_NotSourcedFromReconciliationMirrors()
    {
        // Seed a Postgres ladder so the FK on the matchmaking_tickets rows we WILL insert
        // (then delete) is satisfied.
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "obs-not-from-mirrors");

        // Add 5 entries to Redis (the live source of truth).
        var db = _mux!.GetDatabase();
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, "default");

        try
        {
            for (var i = 0; i < 5; i++)
            {
                await db.SortedSetAddAsync(queueKey, Guid.NewGuid().ToString(),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i);
            }

            // Concurrently insert + DELETE matchmaking_tickets rows in Postgres so any
            // implementation that consults the Postgres mirror would observe ZERO rows.
            await using (var pgConn = new NpgsqlConnection(_cs))
            {
                await pgConn.OpenAsync();
                await using var del = pgConn.CreateCommand();
                del.CommandText = "DELETE FROM gamekit.matchmaking_tickets";
                await del.ExecuteNonQueryAsync();
            }

            var sut = new RedisMatchmakingObservability(_mux, new SystemClock());
            var stats = await sut.GetQueueStatsAsync();

            var pool = stats.Pools.FirstOrDefault(p => p.LadderId == ladderId);
            Assert.NotNull(pool);
            // Critical assertion: 5 — not 0 — because Redis is the source of truth.
            Assert.Equal(5L, pool!.Depth);
        }
        finally
        {
            await db.KeyDeleteAsync(queueKey);
        }
    }
}
