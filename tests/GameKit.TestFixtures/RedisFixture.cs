// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using Testcontainers.Redis;
using Xunit;

namespace GameKit.TestFixtures;

/// <summary>
/// xUnit collection fixture: spins up a Redis 8.6.2 container with the same persistence
/// flags shipped in <c>docker-compose.yml</c> (<c>--appendonly yes --appendfsync everysec
/// --maxmemory-policy noeviction --save ...</c>).
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    /// <summary>Redis connection string (<c>host:port</c>).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _container = new RedisBuilder("redis:8.6.2")
            .WithCommand(
                "redis-server",
                "--appendonly", "yes",
                "--appendfsync", "everysec",
                "--maxmemory-policy", "noeviction",
                "--save", "3600 1 300 100 60 10000")
            .Build();

        await _container.StartAsync();

        ConnectionString = $"{_container.Hostname}:{_container.GetMappedPublicPort(6379)}";
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
