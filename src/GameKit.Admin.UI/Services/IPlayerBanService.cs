// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Player ban / unban operations (ADMIN-06). Both paths run inside a SERIALIZABLE
/// transaction: the mutation + the audit-log row commit together (T-03-06-01 mitigation —
/// an exception on the audit write rolls back the mutation).
/// </summary>
public interface IPlayerBanService
{
    /// <summary>
    /// Bans a player. Writes an <c>admin.player.ban</c> audit row with before/after JSON
    /// inside the same SERIALIZABLE transaction as the mutation.
    /// </summary>
    /// <param name="playerId">Target player id.</param>
    /// <param name="actorId">Acting admin id.</param>
    /// <param name="reason">Free-text reason (3-512 chars; validated upstream per D-09).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BanAsync(Guid playerId, Guid actorId, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Unbans a player. Writes an <c>admin.player.unban</c> audit row with before/after JSON
    /// inside the same SERIALIZABLE transaction as the mutation.
    /// </summary>
    /// <param name="playerId">Target player id.</param>
    /// <param name="actorId">Acting admin id.</param>
    /// <param name="reason">Optional free-text reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnbanAsync(Guid playerId, Guid actorId, string? reason, CancellationToken cancellationToken);
}
