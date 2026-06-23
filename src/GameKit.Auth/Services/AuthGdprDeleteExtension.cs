// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Services;

/// <summary>
/// GDPR pre-delete hook that removes <c>account_merges</c> rows where the deleted player is the
/// surviving target (<c>account_merges.TargetPlayerId</c> FK is <c>ON DELETE RESTRICT</c> — SEC-04 GAP 2).
/// </summary>
/// <remarks>
/// <para>
/// Without this hook, deleting a player who is the <c>TargetPlayerId</c> of any completed merge
/// throws a Postgres 23503 FK violation and rolls back the entire GDPR erasure, leaving the player's
/// PII in the database — a GDPR compliance failure.
/// </para>
/// <para>
/// This implementation runs inside the caller's <c>SERIALIZABLE</c> transaction. It <b>MUST NOT</b>
/// open its own transaction or call <c>CommitAsync</c> on the provided context (see
/// <see cref="IGdprDeleteExtension"/> contract).
/// </para>
/// <para>
/// The <c>SourcePlayerId</c> column on <c>account_merges</c> is a bare UUID with no FK — source
/// players may have been GDPR-erased earlier without affecting this table. Only
/// <c>TargetPlayerId</c> carries the RESTRICT constraint, so only that predicate is needed.
/// </para>
/// <para>
/// Registered via <c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IGdprDeleteExtension, AuthGdprDeleteExtension&gt;())</c>
/// in <c>AddAuth</c>.
/// </para>
/// </remarks>
internal sealed class AuthGdprDeleteExtension : IGdprDeleteExtension
{
    /// <inheritdoc />
    public async Task DeletePlayerDataAsync(GameKitDbContext ctx, Guid playerId, CancellationToken cancellationToken)
    {
        // SEC-04 GAP 2: account_merges.TargetPlayerId = RESTRICT.
        // Delete merge records where this player is the surviving target — the historical merge
        // record is no longer needed once the target player is erased.
        await ctx.Set<AccountMerge>()
            .Where(am => am.TargetPlayerId == playerId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
