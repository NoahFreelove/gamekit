// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Services;

/// <summary>
/// Merges a source player into a target player, re-homing all foreign-key references and
/// tombstoning the source row. This is an irreversible, superadmin-gated operation (AUTH-23/24/25/26).
/// </summary>
/// <remarks>
/// <para>
/// After a successful merge, the source player is soft-deleted (<c>deleted_at</c> set) with
/// <c>merged_into_player_id</c> pointing at the target. The source player id is never returned
/// in the HTTP response layer (SC#5) — callers must capture the target id from the result.
/// </para>
/// <para>
/// The operation is crash-resumable via the <c>account_merges</c> state machine table (SC#1).
/// Calling <c>MergeAsync</c> again with the same source/target pair after a partial crash will
/// resume from the last checkpoint without duplicating audit rows or token revocations.
/// </para>
/// </remarks>
public interface IAccountMergeService
{
    /// <summary>
    /// Merges <paramref name="sourcePlayerId"/> into <paramref name="targetPlayerId"/> using a
    /// SERIALIZABLE transaction, re-pointing all foreign keys, conflict-resolving <c>player_ranks</c>
    /// per ladder, revoking source refresh tokens, and tombstoning the source player row.
    /// </summary>
    /// <param name="sourcePlayerId">
    /// The player to absorb. After the merge, this player will be soft-deleted with
    /// <c>merged_into_player_id = targetPlayerId</c>. This id is never returned in the response.
    /// </param>
    /// <param name="targetPlayerId">The surviving player that inherits all foreign-key references.</param>
    /// <param name="actorId">
    /// The admin user initiating the merge. Recorded in the <c>admin_audit_log</c> row.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="MergeResult"/> carrying the target player id and whether this call completed
    /// a new merge (<see cref="MergeResultKind.Merged"/>) or found an already-complete merge
    /// (<see cref="MergeResultKind.AlreadyMerged"/>).
    /// </returns>
    /// <exception cref="MergeConflictException">
    /// Thrown for precondition failures: <see cref="MergeConflictReason.SelfMerge"/>,
    /// <see cref="MergeConflictReason.SourceAlreadyMerged"/>,
    /// <see cref="MergeConflictReason.TargetBanned"/>, or
    /// <see cref="MergeConflictReason.PlayersInSameParty"/>.
    /// </exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// Thrown when <paramref name="sourcePlayerId"/> or <paramref name="targetPlayerId"/> does not
    /// correspond to an existing player row.
    /// </exception>
    Task<MergeResult> MergeAsync(
        Guid sourcePlayerId,
        Guid targetPlayerId,
        Guid actorId,
        CancellationToken cancellationToken = default);
}
