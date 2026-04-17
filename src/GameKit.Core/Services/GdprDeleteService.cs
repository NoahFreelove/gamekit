// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Core.Services;

/// <summary>Default <see cref="IGdprDeleteService"/> — hard delete with SERIALIZABLE transaction + audit log.</summary>
internal sealed class GdprDeleteService : IGdprDeleteService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    /// <summary>Constructs the service.</summary>
    public GdprDeleteService(GameKitDbContext ctx, IClock clock, IIdGenerator ids)
    {
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
    }

    /// <inheritdoc />
    public async Task DeletePlayerAsync(Guid playerId, Guid? actorId, string reason, CancellationToken cancellationToken = default)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);

        // Snapshot the player state BEFORE deletion for the audit row.
        var snapshot = await _ctx.Players
            .AsNoTracking()
            .Where(p => p.Id == playerId)
            .Select(p => new { p.Id, p.DisplayName, p.CreatedAt, p.IsBanned })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
            throw new PlayerNotFoundException(playerId);

        // JsonDocument rents from ArrayPool. The tracker retains a reference for as long as the
        // DbContext (scoped to the request/caller lifetime), so the pooled arrays are released
        // when the context is disposed. We do NOT dispose inline — downstream readers of the
        // AdminAuditLog entity (e.g. admin UI listing recent actions) dereference the
        // JsonDocument after SaveChangesAsync returns.
        //
        // Under sustained bulk-erasure load, prefer invoking DeletePlayerAsync under a short-lived
        // DbContext scope (the default ASP.NET Core scoped lifetime already provides this) so the
        // pool turnover tracks request lifetime.
        var before = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));

        _ctx.AdminAuditLog.Add(new AdminAuditLog
        {
            Id = _ids.NewId(),
            ActorId = actorId,
            Action = "gdpr.delete",
            TargetType = "player",
            TargetId = playerId,
            Before = before,
            After = null,
            Reason = reason,
            CreatedAt = _clock.UtcNow,
        });
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var deleted = await _ctx.Players
            .Where(p => p.Id == playerId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deleted != 1)
        {
            // Race: someone else deleted between snapshot read and ExecuteDelete. Treat as success
            // (target erasure already achieved) but keep the audit row — operators want the record.
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
