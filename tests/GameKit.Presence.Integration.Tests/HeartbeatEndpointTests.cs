// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using GameKit.Presence;
using GameKit.TestFixtures;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Presence.Integration.Tests;

/// <summary>
/// Integration tests for <c>POST /api/presence/heartbeat</c> — covers the JWT-required
/// authorization gate (D-02) and the round-trip from HTTP to Redis (the heartbeat writes
/// the player presence key with the configured 30-second TTL per CONTEXT D-01).
/// </summary>
[Collection("Presence")]
[Trait("Category", "Integration")]
public sealed class HeartbeatEndpointTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public HeartbeatEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact]
    public async Task Heartbeat_Anonymous_Returns401()
    {
        await using var app = new PresenceTestApp();
        await app.StartAsync(_pg, _redis);

        // No Authorization header — bare client.
        var resp = await app.Client.PostAsync("/api/presence/heartbeat", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_AuthenticatedPlayer_Returns204AndWritesKey()
    {
        await using var app = new PresenceTestApp();
        await app.StartAsync(_pg, _redis);

        var playerId = Guid.NewGuid();
        using var client = app.CreateClient(playerId);

        var resp = await client.PostAsync("/api/presence/heartbeat", content: null);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        // Probe Redis directly via the shared multiplexer.
        var db = app.Multiplexer.GetDatabase();
        var key = PresenceRedisKeys.Player(playerId);
        var value = await db.StringGetAsync(key);
        Assert.Equal(PresenceValues.Online, (string?)value);

        var ttl = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        // Default TtlSeconds = 30 (CONTEXT D-01). Allow 2 s skew for round-trip + scheduler.
        Assert.InRange(ttl!.Value.TotalSeconds, 28, 30);
    }
}
