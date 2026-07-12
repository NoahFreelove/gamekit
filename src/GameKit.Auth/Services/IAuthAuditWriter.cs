// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Services;

/// <summary>
/// Writes into Phase-1 <c>admin_audit_log</c>. Auth records 10 action types (RESEARCH §8.10).
/// Saves immediately (does NOT require the caller to commit a surrounding tx — audit rows are
/// written inside the same scope, so if the surrounding tx rolls back the audit row rolls back too).
/// </summary>
public interface IAuthAuditWriter
{
    /// <summary>Writes one row. Null <paramref name="after"/> and <paramref name="reason"/> are allowed.</summary>
    /// <param name="action">Stable action verb (e.g. <c>auth.login.success</c>, <c>auth.refresh.rotated</c>).</param>
    /// <param name="targetType">Target entity type (e.g. <c>player</c>, <c>refresh_token</c>).</param>
    /// <param name="targetId">Target entity id, if applicable.</param>
    /// <param name="actorId">Acting player id (null for server-initiated actions).</param>
    /// <param name="after">Optional post-action snapshot (serialized to JSONB); null for purely observational rows.</param>
    /// <param name="reason">Optional free-text or stable-code reason string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(
        string action,
        string targetType,
        Guid? targetId,
        Guid? actorId,
        object? after,
        string? reason,
        CancellationToken cancellationToken = default);
}
