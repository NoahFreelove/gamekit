// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GameKit.Core.Services;

/// <summary>
/// Default <see cref="IPlayerDisplayNameResolver"/> with in-memory caching (5-minute sliding expiration)
/// to avoid N+1 lookups when rendering match-history lists.
/// </summary>
internal sealed class PlayerDisplayNameResolver : IPlayerDisplayNameResolver
{
    private readonly GameKitDbContext _ctx;
    private readonly GameKitOptions _opts;
    private readonly IMemoryCache _cache;

    /// <summary>Constructs the resolver.</summary>
    public PlayerDisplayNameResolver(GameKitDbContext ctx, GameKitOptions opts, IMemoryCache cache)
    {
        _ctx = ctx;
        _opts = opts;
        _cache = cache;
    }

    /// <inheritdoc />
    public string Resolve(Guid? playerId)
    {
        if (playerId is null)
            return _opts.DeletedPlayerDisplayName;

        var key = $"player_name:{playerId.Value:N}";
        return _cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(5);
            var name = _ctx.Players
                .AsNoTracking()
                .Where(p => p.Id == playerId.Value)
                .Select(p => p.DisplayName)
                .FirstOrDefault();
            return name ?? _opts.DeletedPlayerDisplayName;
        }) ?? _opts.DeletedPlayerDisplayName;
    }
}
