// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
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
    public async ValueTask<string> ResolveAsync(Guid? playerId, CancellationToken cancellationToken = default)
    {
        if (playerId is null)
            return _opts.DeletedPlayerDisplayName;

        var key = $"player_name:{playerId.Value:N}";
        if (_cache.TryGetValue(key, out string? cached) && cached is not null)
            return cached;

        var name = await _ctx.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId.Value)
            .Select(p => p.DisplayName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = name ?? _opts.DeletedPlayerDisplayName;
        _cache.Set(key, result, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) });
        return result;
    }
}
