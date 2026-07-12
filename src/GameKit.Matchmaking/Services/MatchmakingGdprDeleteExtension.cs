// SPDX-License-Identifier: Apache-2.0
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
/// GDPR pre-delete hook that removes ALL <c>party_members</c> rows for the deleted player
/// (<c>party_members.PlayerId</c> FK is <c>ON DELETE RESTRICT</c> — SEC-04 GAP 1).
/// </summary>
/// <remarks>
/// <para>
/// Without this hook, deleting a player who is a member of any party (owner or non-owner) throws
/// a Postgres 23503 FK violation on <c>party_members.PlayerId → players.Id (RESTRICT)</c>,
/// rolling back the entire GDPR erasure and leaving the player's PII in the database — a GDPR
/// compliance failure.
/// </para>
/// <para>
/// <b>What this hook deletes:</b> All <c>party_members</c> rows where <c>PlayerId = playerId</c>,
/// which covers BOTH owner and non-owner memberships. The <c>WHERE PlayerId = playerId</c> predicate
/// does not distinguish owner role — it removes every membership row for this player.
/// </para>
/// <para>
/// <b>Owned parties and the Postgres cascade:</b> <c>parties.OwnerPlayerId</c> is
/// <c>ON DELETE CASCADE</c>, so when the <c>players</c> row is deleted, any party rows owned by
/// this player are also deleted, which in turn cascades to the remaining <c>party_members</c> rows
/// for those parties (also <c>CASCADE</c>) and sets <c>matchmaking_tickets.PartyId</c> to NULL
/// (<c>SET NULL</c>). The owner's own <c>party_members</c> row for those parties has already been
/// deleted by this hook, so the cascade attempt finds zero rows — harmless.
/// This hook must <b>not</b> delete the <c>parties</c> rows themselves (those are handled by the
/// cascade when the <c>players</c> row is deleted).
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
        // SEC-04 GAP 1: party_members.PlayerId → RESTRICT.
        // Remove ALL party_member rows for this player (both owner and non-owner memberships).
        // Owner memberships: this delete pre-empts the Postgres CASCADE on parties.OwnerPlayerId,
        //   which would attempt to cascade into party_members after the player row is deleted.
        //   The cascade finds these rows already gone — harmless.
        // Non-owner memberships: these carry ON DELETE RESTRICT on party_members.PlayerId and MUST
        //   be removed before the player row is deleted to avoid Postgres error 23503.
        // Do NOT delete the parties rows owned by this player — those are handled by the
        // ON DELETE CASCADE on parties.OwnerPlayerId when the players row is deleted.
        await ctx.Set<PartyMember>()
            .Where(pm => pm.PlayerId == playerId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
