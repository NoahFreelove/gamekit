// SPDX-License-Identifier: GPL-3.0-or-later
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
/// Integration tests for PATTERNS warning #6 — the heartbeat Lua script MUST NOT
/// downgrade an existing <c>in_match</c> value to <c>online</c>. Tests seed the
/// in-match marker directly via the shared <see cref="IConnectionMultiplexer"/>
/// (in production this happens via <c>PresenceSessionObserver.OnSessionStartedAsync</c>
/// when <c>POST /api/sessions/{id}/start</c> fires — wired in Plan 06-05). For
/// THIS plan we exercise only the precedence rule.
/// </summary>
[Collection("Presence")]
[Trait("Category", "Integration")]
public sealed class InMatchPrecedenceTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public InMatchPrecedenceTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact]
    public async Task InMatch_NotDowngradedByHeartbeat()
    {
        await using var app = new PresenceTestApp();
        await app.StartAsync(_pg, _redis);

        var playerId = Guid.NewGuid();
        var key = PresenceRedisKeys.Player(playerId);
        var db = app.Multiplexer.GetDatabase();

        // Seed in_match marker with the production-shaped TTL (game-server authoritative).
        await db.StringSetAsync(key, PresenceValues.InMatch, expiry: TimeSpan.FromSeconds(30));

        // Player fires their heartbeat — Lua precedence MUST refuse to overwrite in_match.
        using var client = app.CreateClient(playerId);
        var resp = await client.PostAsync("/api/presence/heartbeat", content: null);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var value = await db.StringGetAsync(key);
        Assert.Equal(PresenceValues.InMatch, (string?)value);

        // TTL has been refreshed by the heartbeat (PEXPIRE arm of the Lua script).
        var ttl = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 28, 30);
    }

    [Fact]
    public async Task Online_OverwrittenByHeartbeat()
    {
        await using var app = new PresenceTestApp();
        await app.StartAsync(_pg, _redis);

        var playerId = Guid.NewGuid();
        var key = PresenceRedisKeys.Player(playerId);
        var db = app.Multiplexer.GetDatabase();

        // Seed an online marker with a short TTL so we can prove the heartbeat refreshed it.
        await db.StringSetAsync(key, PresenceValues.Online, expiry: TimeSpan.FromSeconds(5));

        using var client = app.CreateClient(playerId);
        var resp = await client.PostAsync("/api/presence/heartbeat", content: null);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var value = await db.StringGetAsync(key);
        Assert.Equal(PresenceValues.Online, (string?)value);

        // TTL bumped back to the default ~30s (the SET 'online' PX ARGV[1] arm).
        var ttl = await db.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 28, 30);
    }
}
