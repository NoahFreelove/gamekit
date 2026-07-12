// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;

namespace GameKit.Auth.Services;

/// <summary>Default audit writer — inserts into <c>admin_audit_log</c> and <c>SaveChangesAsync</c>.</summary>
internal sealed class AuthAuditWriter : IAuthAuditWriter
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    /// <summary>Constructs the writer.</summary>
    /// <param name="ctx">The shared <see cref="GameKitDbContext"/> (scoped lifetime).</param>
    /// <param name="clock">Clock abstraction used for the <c>CreatedAt</c> column.</param>
    /// <param name="ids">Id generator used for the audit row's primary key (UUIDv7).</param>
    public AuthAuditWriter(GameKitDbContext ctx, IClock clock, IIdGenerator ids)
    {
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        string action,
        string targetType,
        Guid? targetId,
        Guid? actorId,
        object? after,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        _ctx.AdminAuditLog.Add(new AdminAuditLog
        {
            Id = _ids.NewId(),
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Before = null,
            After = after is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(after)),
            Reason = reason,
            CreatedAt = _clock.UtcNow,
        });
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
