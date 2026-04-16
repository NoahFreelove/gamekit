// SPDX-License-Identifier: GPL-3.0-or-later
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
    public void Resolve_Null_ReturnsDeletedPlayerDisplayName()
    {
        using var ctx = TestDbContextFactory.Create(nameof(Resolve_Null_ReturnsDeletedPlayerDisplayName));
        var opts = new GameKitOptions { DeletedPlayerDisplayName = "Deleted Player" };
        var cache = new MemoryCache(new MemoryCacheOptions());

        var resolver = new PlayerDisplayNameResolver(ctx, opts, cache);
        var result = resolver.Resolve(null);

        Assert.Equal("Deleted Player", result);
    }

    [Fact]
    public void Resolve_Null_UsesCustomTombstoneName()
    {
        using var ctx = TestDbContextFactory.Create(nameof(Resolve_Null_UsesCustomTombstoneName));
        var opts = new GameKitOptions { DeletedPlayerDisplayName = "Anonymous" };
        var cache = new MemoryCache(new MemoryCacheOptions());

        var resolver = new PlayerDisplayNameResolver(ctx, opts, cache);
        var result = resolver.Resolve(null);

        Assert.Equal("Anonymous", result);
    }

    [Fact]
    public void Resolve_ExistingPlayer_ReturnsDisplayName()
    {
        using var ctx = TestDbContextFactory.Create(nameof(Resolve_ExistingPlayer_ReturnsDisplayName));
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
        var result = resolver.Resolve(playerId);

        Assert.Equal("Alice", result);
    }

    [Fact]
    public void Resolve_MissingPlayer_ReturnsTombstone()
    {
        using var ctx = TestDbContextFactory.Create(nameof(Resolve_MissingPlayer_ReturnsTombstone));
        var opts = new GameKitOptions { DeletedPlayerDisplayName = "Gone" };
        var cache = new MemoryCache(new MemoryCacheOptions());

        var resolver = new PlayerDisplayNameResolver(ctx, opts, cache);
        var result = resolver.Resolve(Guid.NewGuid());

        Assert.Equal("Gone", result);
    }

    [Fact]
    public void Resolve_CachesResult()
    {
        using var ctx = TestDbContextFactory.Create(nameof(Resolve_CachesResult));
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
        var first = resolver.Resolve(playerId);
        Assert.Equal("Bob", first);

        // Remove from DB — cache should still return Bob
        var player = ctx.Players.Find(playerId)!;
        ctx.Players.Remove(player);
        ctx.SaveChanges();

        var second = resolver.Resolve(playerId);
        Assert.Equal("Bob", second);
    }
}
