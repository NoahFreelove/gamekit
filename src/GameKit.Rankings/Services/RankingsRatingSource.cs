// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Rankings.Services;

/// <summary>
/// <see cref="IPlayerRatingProvider"/> implementation backed by <c>player_ranks</c> (RANK-17).
/// Returns a rating snapshot for each requested player on the given ladder, projecting
/// <see cref="PlayerRank.Rating"/>, <see cref="PlayerRank.RatingDeviation"/>, and
/// <see cref="PlayerRank.Volatility"/> into <see cref="PlayerRatingValue"/>. Players with no
/// rank row on the requested ladder are omitted from the result (callers apply defaults for absent
/// keys — matches the <see cref="IPlayerRatingProvider"/> contract).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime: Scoped</b> — reads the scoped <see cref="GameKitDbContext"/>. Deviates from the
/// Phase 8 CONTEXT.md wording of "Singleton+AddSingleton"; the deviation is intentional and
/// documented in Phase 8 RESEARCH §RANK-17 (Assumption A2). Using Singleton + IServiceScopeFactory
/// would create a second Postgres connection per call and break ambient-transaction semantics for
/// future features. <c>MatchmakingService.EnqueueAsync</c> is itself Scoped, so
/// <see cref="RankingsRatingSource"/> shares the same <see cref="GameKitDbContext"/> instance.
/// </para>
/// <para>
/// Wire up via <c>.WithRatingsFrom&lt;RankingsRatingSource&gt;()</c> after <c>.AddRankings()</c>.
/// Omitting the call leaves Core's <c>NullPlayerRatingProvider</c> active (zero-rating fallback).
/// </para>
/// </remarks>
public sealed class RankingsRatingSource : IPlayerRatingProvider
{
    private readonly GameKitDbContext _ctx;

    /// <summary>Constructs the rating source.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/> shared with the request scope.</param>
    public RankingsRatingSource(GameKitDbContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Returns a rating snapshot for each player in <paramref name="playerIds"/> on the given
    /// <paramref name="ladderId"/>. Executes a single batched <c>SELECT</c> with
    /// <c>AsNoTracking()</c>. Players with no rank row are naturally omitted from the result.
    /// </summary>
    /// <param name="playerIds">The set of player identifiers to look up. Must not be null.</param>
    /// <param name="ladderId">The ladder whose ratings are queried.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only dictionary keyed by player id. Returns an empty dictionary when
    /// <paramref name="playerIds"/> is empty (no query issued).
    /// </returns>
    public async ValueTask<IReadOnlyDictionary<Guid, PlayerRatingValue>> GetRatingsAsync(
        IReadOnlyCollection<Guid> playerIds,
        Guid ladderId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        if (playerIds.Count == 0)
            return new Dictionary<Guid, PlayerRatingValue>();

        var ranks = await _ctx.Set<PlayerRank>()
            .AsNoTracking()
            .Where(r => r.LadderId == ladderId && playerIds.Contains(r.PlayerId))
            .Select(r => new { r.PlayerId, r.Rating, r.RatingDeviation, r.Volatility })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return ranks.ToDictionary(
            r => r.PlayerId,
            r => new PlayerRatingValue(r.PlayerId, r.Rating, r.RatingDeviation, r.Volatility));
    }
}
