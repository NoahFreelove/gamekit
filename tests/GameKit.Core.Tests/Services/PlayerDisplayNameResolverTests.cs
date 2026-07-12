// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace GameKit.Core.Tests.Services;

public class PlayerDisplayNameResolverTests
{
    [Fact]
    public async Task ResolveAsync_Null_ReturnsDeletedPlayerDisplayName()
    {
        using var ctx = TestDbContextFactory.Create(nameof(ResolveAsync_Null_ReturnsDeletedPlayerDisplayName));
        var opts = new GameKitOptions { DeletedPlayerDisplayName = "Deleted Player" };
        var cache = new MemoryCache(new MemoryCacheOptions());

        var resolver = new PlayerDisplayNameResolver(ctx, opts, cache);
        var result = await resolver.ResolveAsync(null);

        Assert.Equal("Deleted Player", result);
    }

    [Fact]
    public async Task ResolveAsync_Null_UsesCustomTombstoneName()
    {
        using var ctx = TestDbContextFactory.Create(nameof(ResolveAsync_Null_UsesCustomTombstoneName));
        var opts = new GameKitOptions { DeletedPlayerDisplayName = "Anonymous" };
        var cache = new MemoryCache(new MemoryCacheOptions());

        var resolver = new PlayerDisplayNameResolver(ctx, opts, cache);
        var result = await resolver.ResolveAsync(null);

        Assert.Equal("Anonymous", result);
    }

    [Fact]
    public async Task ResolveAsync_ExistingPlayer_ReturnsDisplayName()
    {
        using var ctx = TestDbContextFactory.Create(nameof(ResolveAsync_ExistingPlayer_ReturnsDisplayName));
        var playerId = Guid.NewGuid();
        ctx.Players.Add(new Player
        {
            Id = playerId,
            DisplayName = "Alice",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();

        var opts = new GameKitOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());

        var resolver = new PlayerDisplayNameResolver(ctx, opts, cache);
        var result = await resolver.ResolveAsync(playerId);

        Assert.Equal("Alice", result);
    }

    [Fact]
    public async Task ResolveAsync_MissingPlayer_ReturnsTombstone()
    {
        using var ctx = TestDbContextFactory.Create(nameof(ResolveAsync_MissingPlayer_ReturnsTombstone));
        var opts = new GameKitOptions { DeletedPlayerDisplayName = "Gone" };
        var cache = new MemoryCache(new MemoryCacheOptions());

        var resolver = new PlayerDisplayNameResolver(ctx, opts, cache);
        var result = await resolver.ResolveAsync(Guid.NewGuid());

        Assert.Equal("Gone", result);
    }

    [Fact]
    public async Task ResolveAsync_CachesResult()
    {
        using var ctx = TestDbContextFactory.Create(nameof(ResolveAsync_CachesResult));
        var playerId = Guid.NewGuid();
        ctx.Players.Add(new Player
        {
            Id = playerId,
            DisplayName = "Bob",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();

        var opts = new GameKitOptions();
        var cache = new MemoryCache(new MemoryCacheOptions());

        var resolver = new PlayerDisplayNameResolver(ctx, opts, cache);

        // First call populates cache
        var first = await resolver.ResolveAsync(playerId);
        Assert.Equal("Bob", first);

        // Remove from DB — cache should still return Bob
        var player = ctx.Players.Find(playerId)!;
        ctx.Players.Remove(player);
        ctx.SaveChanges();

        var second = await resolver.ResolveAsync(playerId);
        Assert.Equal("Bob", second);
    }
}
