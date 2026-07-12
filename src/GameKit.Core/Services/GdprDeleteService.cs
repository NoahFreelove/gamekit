// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
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
    private readonly IEnumerable<IGdprDeleteExtension> _extensions;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">The shared database context.</param>
    /// <param name="clock">UTC clock.</param>
    /// <param name="ids">UUIDv7 generator.</param>
    /// <param name="extensions">
    /// Zero or more package-registered pre-delete hooks (SEC-04 Option A). Resolved as
    /// <c>IEnumerable&lt;IGdprDeleteExtension&gt;</c> — empty when no sibling packages are
    /// installed, preserving Core-standalone behavior.
    /// </param>
    public GdprDeleteService(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator ids,
        IEnumerable<IGdprDeleteExtension> extensions)
    {
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
        _extensions = extensions;
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

        // SEC-04 Option A: invoke package-registered pre-delete hooks BEFORE the players delete
        // so that RESTRICT-FK rows (party_members.PlayerId, account_merges.TargetPlayerId) are
        // removed while we still own the SERIALIZABLE transaction. Each extension MUST NOT open
        // or commit its own transaction (contract documented on IGdprDeleteExtension).
        foreach (var ext in _extensions)
        {
            await ext.DeletePlayerDataAsync(_ctx, playerId, cancellationToken).ConfigureAwait(false);
        }

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
