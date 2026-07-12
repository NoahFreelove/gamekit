// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Providers;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Services;

/// <summary>
/// Shared ban-check used by every <see cref="IOAuthProvider"/> and by the refresh rotation path (D-03).
/// Returns <c>null</c> when the player is not banned; returns an <see cref="OAuthResult"/> failure with
/// error code <c>"banned:&lt;reasonHash&gt;"</c> (first 8 bytes of SHA-256(BanReason) in lowercase hex, 16 chars)
/// when the player is banned. The reason hash is opaque to the player but lets an admin cross-reference
/// the audit-log entry containing the full reason.
/// </summary>
/// <remarks>
/// <para>
/// Design rationale (RESEARCH §Integration with Phase 2 / D-03): ban enforcement happens at the
/// two auth checkpoints — login (per-provider after upsert) and refresh (per-family in the
/// rotation path). Per-request middleware was rejected in CONTEXT D-03 because the DB round-trip
/// per authenticated request is too expensive; the low-frequency login/refresh paths are the
/// cheapest place to enforce with the correct semantics. Access tokens self-expire within the
/// configured lifetime (default 15 min), bounding the residual-access window after a ban.
/// </para>
/// <para>
/// The first 8 bytes (16 hex chars) of SHA-256(BanReason) is deliberately irreversible — players
/// receive only an opaque handle. Admins can reproduce the same hash from the audit-log row's
/// reason field to correlate a user complaint with a specific ban.
/// </para>
/// </remarks>
internal static class BannedCheckHelper
{
    /// <summary>
    /// Looks up the player; returns a failure <see cref="OAuthResult"/> when banned, null otherwise.
    /// Uses <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}(IQueryable{TEntity})"/>
    /// to avoid polluting the scoped context's change tracker.
    /// </summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="playerId">Target player id (must already exist — callers invoke after upsert).</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<OAuthResult?> CheckAsync(
        GameKitDbContext ctx, Guid playerId, CancellationToken ct)
    {
        var player = await ctx.Set<Player>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId, ct).ConfigureAwait(false);
        if (player is null || !player.IsBanned) return null;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(player.BanReason ?? string.Empty));
        var hex = Convert.ToHexString(digest, 0, 8).ToLowerInvariant(); // 16 chars
        return OAuthResult.Fail($"banned:{hex}");
    }
}
