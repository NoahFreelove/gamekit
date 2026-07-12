// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;

/// <summary>
/// GDPR / right-to-erasure service. Hard-deletes the player row and relies on Postgres FK actions
/// (<c>ON DELETE SET NULL</c> on session_participants / rating_history / matchmaking_tickets;
/// <c>ON DELETE CASCADE</c> on player_identities / player_credentials when those arrive in Phase 2)
/// to fan out the erasure.
/// </summary>
public interface IGdprDeleteService
{
    /// <summary>
    /// Deletes the player and cascades per the FK design (D-10). Writes an <c>admin_audit_log</c> entry
    /// capturing actor + reason BEFORE the delete so the audit row survives.
    /// </summary>
    /// <exception cref="PlayerNotFoundException">Thrown when no player with the given id exists.</exception>
    Task DeletePlayerAsync(Guid playerId, Guid? actorId, string reason, CancellationToken cancellationToken = default);
}
