// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.TestFixtures;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// OPS-08 + DIST-01: Redis container ships with AOF persistence (<c>appendonly yes</c>,
/// <c>appendfsync everysec</c>) and <c>maxmemory-policy noeviction</c>.
/// </summary>
[Collection("Redis")]
[Trait("Category", "Integration")]
public class RedisPersistenceTests
{
    private readonly RedisFixture _redis;

    public RedisPersistenceTests(RedisFixture redis) => _redis = redis;

    [Fact]
    public async Task Redis_Ships_With_AOF_Every_Second()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(
            $"{_redis.ConnectionString},allowAdmin=true");
        var server = mux.GetServer(mux.GetEndPoints()[0]);

        var appendonly = await server.ConfigGetAsync("appendonly");
        var appendfsync = await server.ConfigGetAsync("appendfsync");
        var maxmemoryPolicy = await server.ConfigGetAsync("maxmemory-policy");

        Assert.Equal("yes", appendonly[0].Value);
        Assert.Equal("everysec", appendfsync[0].Value);
        Assert.Equal("noeviction", maxmemoryPolicy[0].Value);
    }
}
