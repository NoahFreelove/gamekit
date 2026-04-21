// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Default <see cref="IAdminAuditWriter"/> — inserts into <c>admin_audit_log</c> via the shared
/// <see cref="GameKitDbContext"/> and calls <c>SaveChangesAsync</c>. Scoped lifetime, so the write
/// rides the caller's transaction when one is open (Ban/Unban/Create-admin flows open a
/// SERIALIZABLE tx via <c>_ctx.Database.BeginTransactionAsync</c> and commit both the mutation
/// and the audit row atomically).
/// </summary>
public sealed class AdminAuditWriter : IAdminAuditWriter
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    /// <summary>Constructs the writer.</summary>
    /// <param name="ctx">The shared <see cref="GameKitDbContext"/> (scoped lifetime).</param>
    /// <param name="clock">Clock abstraction used for the <c>CreatedAt</c> column.</param>
    /// <param name="ids">Id generator used for the audit row's primary key (UUIDv7).</param>
    public AdminAuditWriter(GameKitDbContext ctx, IClock clock, IIdGenerator ids)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        string action,
        string targetType,
        Guid? targetId,
        Guid actorId,
        object? before,
        object? after,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);
        ArgumentException.ThrowIfNullOrEmpty(targetType);

        _ctx.Set<AdminAuditLog>().Add(new AdminAuditLog
        {
            Id = _ids.NewId(),
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Before = before is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(before)),
            After = after is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(after)),
            Reason = reason,
            CreatedAt = _clock.UtcNow,
        });
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
