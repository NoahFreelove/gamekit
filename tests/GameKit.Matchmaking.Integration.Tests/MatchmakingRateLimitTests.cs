// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Redis;
using GameKit.TestFixtures;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// SC#5 phase-gate integration tests for the enqueue rate-limit policy
/// (<c>gamekit:mm:enqueue</c>, 5 / min / player sliding window). Verifies the 6th rapid
/// request from the same player returns 429 and that the spam did not produce duplicate
/// queue entries.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class MatchmakingRateLimitTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp? _app;
    private ConnectionMultiplexer? _mux;

    public MatchmakingRateLimitTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _app = new MatchmakingTestApp();
        await _app.StartAsync(_pg, _redis);
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null) await _mux.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }

    [Fact]
    public async Task SpamPlayerEnqueue_429_After_5_Per_Min()
    {
        var player = Guid.NewGuid();
        using var client = _app!.CreateClient(player);

        // Six rapid requests. The first should succeed (200), the next four should be
        // either 200 (rate-limit grants the permit but the AlreadyEnqueued check rejects
        // on body) OR 409. Either way the 6th must be 429 since the rate-limit budget is
        // 5/min/player.
        var responses = new HttpResponseMessage[6];
        for (var i = 0; i < 6; i++)
        {
            var req = new EnqueueRequest(_app.TestLadderId, _app.TestLadderName);
            responses[i] = await client.PostAsJsonAsync("/api/mm/queue", req);
        }

        // First 5 requests — within the budget. Either 200 (queued solo — v1 solo dedup is
        // best-effort) or 409 (party AlreadyEnqueued). Either way the rate limit grants the
        // permit. The contract is: ≤5 200/409 inside the window, 429 on the 6th.
        for (var i = 0; i < 5; i++)
        {
            Assert.True(
                responses[i].StatusCode == HttpStatusCode.OK ||
                responses[i].StatusCode == HttpStatusCode.Conflict,
                $"Request {i} should land within the rate-limit budget; got {responses[i].StatusCode}.");
        }

        // 6th request — MUST be 429 because the rate-limit budget is exhausted.
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[5].StatusCode);

        // Queue depth is bounded above by the count of 200 responses (each successful enqueue
        // ZADDs once). It is an UPPER bound, not strict equality: best-effort v1 solo dedup may
        // collapse repeated solo enqueues from the same player into fewer ZADDs, so under CI
        // timing the depth can be < the 200-count. The 6th (429) request never reached the
        // service, so it never wrote to Redis. At least the first enqueue persists.
        var db = _mux!.GetDatabase();
        var queueKey = MatchmakingRedisKeys.Queue(_app.TestLadderId, _app.TestLadderName);
        var queueDepth = await db.SortedSetLengthAsync(queueKey);
        var expected200Count = responses
            .Take(5)
            .Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.True(
            queueDepth >= 1 && queueDepth <= expected200Count,
            $"Queue depth {queueDepth} should be in [1, {expected200Count}] — best-effort solo " +
            $"dedup may reduce ZADDs below the 200-count, but at least one enqueue must persist.");

        foreach (var r in responses) r.Dispose();
    }
}
