// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// GDPR pre-delete hook that removes <c>party_members</c> rows where the deleted player is a
/// non-owner member (<c>party_members.PlayerId</c> FK is <c>ON DELETE RESTRICT</c> — SEC-04 GAP 1).
/// </summary>
/// <remarks>
/// <para>
/// Without this hook, deleting a player who is a non-owner member of any party throws a Postgres
/// 23503 FK violation and rolls back the entire GDPR erasure, leaving the player's PII in the
/// database — a GDPR compliance failure.
/// </para>
/// <para>
/// <b>Owned parties are handled by the Postgres cascade chain:</b> <c>parties.OwnerPlayerId</c> is
/// <c>ON DELETE CASCADE</c>, so when the <c>players</c> row is deleted, owned party rows are deleted
/// automatically, which in turn cascades to <c>party_members</c> (also <c>CASCADE</c>) for the
/// members of that party, and sets <c>matchmaking_tickets.PartyId</c> to NULL (<c>SET NULL</c>).
/// This hook only needs to remove the player's non-owner memberships; it must <b>not</b> delete the
/// parties the player owns (those are handled by the cascade).
/// </para>
/// <para>
/// This implementation runs inside the caller's <c>SERIALIZABLE</c> transaction. It <b>MUST NOT</b>
/// open its own transaction or call <c>CommitAsync</c> on the provided context (see
/// <see cref="IGdprDeleteExtension"/> contract).
/// </para>
/// <para>
/// Registered via <c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IGdprDeleteExtension, MatchmakingGdprDeleteExtension&gt;())</c>
/// in <c>AddMatchmaking</c>.
/// </para>
/// </remarks>
internal sealed class MatchmakingGdprDeleteExtension : IGdprDeleteExtension
{
    /// <inheritdoc />
    public async Task DeletePlayerDataAsync(GameKitDbContext ctx, Guid playerId, CancellationToken cancellationToken)
    {
        // SEC-04 GAP 1: party_members.PlayerId = RESTRICT.
        // Remove all party_member rows for this player. This covers non-owner memberships.
        // Owner memberships will be handled by the Postgres CASCADE when parties.OwnerPlayerId
        // is deleted via the players row delete — do NOT delete the owned parties here, as other
        // members of that party need to survive the cascade-driven cleanup correctly.
        await ctx.Set<PartyMember>()
            .Where(pm => pm.PlayerId == playerId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
