// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Services;

/// <summary>Scoped default implementation of <see cref="IIsGuestResolver"/>; shares the request-scoped <see cref="GameKitDbContext"/>.</summary>
internal sealed class IsGuestResolver : IIsGuestResolver
{
    private readonly GameKitDbContext _ctx;

    /// <summary>Constructs the resolver with the request-scoped context.</summary>
    /// <param name="ctx">The shared <see cref="GameKitDbContext"/>.</param>
    public IsGuestResolver(GameKitDbContext ctx) => _ctx = ctx;

    /// <inheritdoc />
    public async Task<bool> IsGuestAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var hasIdentity = await _ctx.Set<PlayerIdentity>()
            .AnyAsync(i => i.PlayerId == playerId, cancellationToken)
            .ConfigureAwait(false);
        if (hasIdentity) return false;

        var hasCredential = await _ctx.Set<PlayerCredential>()
            .AnyAsync(c => c.PlayerId == playerId, cancellationToken)
            .ConfigureAwait(false);
        return !hasCredential;
    }
}
