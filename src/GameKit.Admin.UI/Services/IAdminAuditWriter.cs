// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Writes admin-mutation audit rows into Phase-1 <c>admin_audit_log</c>. Mirrors the Phase-2
/// <c>IAuthAuditWriter</c> shape and adds a <c>before</c> snapshot parameter on
/// <see cref="WriteAsync"/> (mutations ship both before + after payloads per D-17). Scoped
/// lifetime — writes ride the caller's transaction so a surrounding rollback also rolls back
/// the audit row.
/// </summary>
public interface IAdminAuditWriter
{
    /// <summary>Writes one audit row. Null <paramref name="before"/>, <paramref name="after"/>, and <paramref name="reason"/> are all permitted.</summary>
    /// <param name="action">Stable action verb from <see cref="AdminAuditActions"/> (e.g. <c>admin.player.ban</c>).</param>
    /// <param name="targetType">Target entity type (<c>player</c>, <c>admin</c>, etc.).</param>
    /// <param name="targetId">Target entity id, if applicable.</param>
    /// <param name="actorId">Acting admin id (admin_users.id).</param>
    /// <param name="before">Optional pre-mutation snapshot (serialized to JSONB).</param>
    /// <param name="after">Optional post-mutation snapshot (serialized to JSONB).</param>
    /// <param name="reason">Optional free-text or stable-code reason string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(
        string action,
        string targetType,
        Guid? targetId,
        Guid actorId,
        object? before,
        object? after,
        string? reason,
        CancellationToken cancellationToken = default);
}
