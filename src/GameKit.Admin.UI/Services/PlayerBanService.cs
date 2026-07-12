// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Default <see cref="IPlayerBanService"/>. Ban/Unban open a SERIALIZABLE transaction,
/// snapshot the player's ban-state fields into the audit row's <c>Before</c> JSON, mutate
/// the tracked entity, and call <c>SaveChangesAsync</c> followed by an audit write — then
/// commit. If either write throws, the transaction rolls both back (T-03-06-01).
/// </summary>
public sealed class PlayerBanService : IPlayerBanService
{
    private readonly GameKitDbContext _ctx;
    private readonly IAdminAuditWriter _audit;
    private readonly IClock _clock;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="audit">Audit writer (same DbContext lifetime — writes ride the tx).</param>
    /// <param name="clock">Clock abstraction used for <c>BannedAt</c>.</param>
    public PlayerBanService(GameKitDbContext ctx, IAdminAuditWriter audit, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        _ctx = ctx;
        _audit = audit;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task BanAsync(
        Guid playerId,
        Guid actorId,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);

        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var player = await _ctx.Set<Player>()
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Player {playerId} not found.");

        // Snapshot BEFORE mutation — captured as an anonymous record, serialized by the audit writer.
        var before = new
        {
            is_banned = player.IsBanned,
            banned_at = player.BannedAt,
            ban_reason = player.BanReason,
        };

        player.IsBanned = true;
        player.BannedAt = _clock.UtcNow;
        player.BanReason = reason;
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            action: AdminAuditActions.PlayerBan,
            targetType: "player",
            targetId: playerId,
            actorId: actorId,
            before: before,
            after: new
            {
                is_banned = player.IsBanned,
                banned_at = player.BannedAt,
                ban_reason = player.BanReason,
            },
            reason: reason,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnbanAsync(
        Guid playerId,
        Guid actorId,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var player = await _ctx.Set<Player>()
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Player {playerId} not found.");

        var before = new
        {
            is_banned = player.IsBanned,
            banned_at = player.BannedAt,
            ban_reason = player.BanReason,
        };

        player.IsBanned = false;
        player.BannedAt = null;
        player.BanReason = null;
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            action: AdminAuditActions.PlayerUnban,
            targetType: "player",
            targetId: playerId,
            actorId: actorId,
            before: before,
            after: new { is_banned = player.IsBanned },
            reason: reason,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
