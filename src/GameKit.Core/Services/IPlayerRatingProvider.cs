// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;

/// <summary>
/// Snapshot of a single player's rating on a specific ladder, as issued by <see cref="IPlayerRatingProvider"/>.
/// </summary>
/// <param name="PlayerId">The player the snapshot applies to.</param>
/// <param name="Rating">Current Glicko-2 rating (stored as <c>double precision</c> per RANK-03).</param>
/// <param name="RatingDeviation">Current Glicko-2 rating deviation.</param>
/// <param name="Volatility">Current Glicko-2 volatility.</param>
public sealed record PlayerRatingValue(
    Guid PlayerId,
    double Rating,
    double RatingDeviation,
    double Volatility);

/// <summary>
/// Optional port that provides player ratings to rating-aware services (e.g. matchmaking skill-bracket
/// filtering). Implemented by <c>GameKit.Rankings</c> (Phase 8) which supplies a real
/// <c>RankingsRatingSource</c> backed by Postgres. Core registers <c>NullPlayerRatingProvider</c>
/// as the default via <c>TryAddSingleton</c> so any install that does not include Rankings degrades
/// gracefully to zero-rated (v1) behaviour — matchmaking still functions, it simply ignores ratings.
/// </summary>
public interface IPlayerRatingProvider
{
    /// <summary>
    /// Returns a rating snapshot for each player in <paramref name="playerIds"/> on the given
    /// <paramref name="ladderId"/>. Players with no recorded rank on the ladder are omitted from
    /// the returned dictionary (callers should apply a default when a key is absent).
    /// </summary>
    /// <param name="playerIds">The set of player identifiers to look up. Must not be null.</param>
    /// <param name="ladderId">The ladder whose ratings are queried.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only dictionary keyed by player id. The null-object default always returns an empty
    /// dictionary; <c>GameKit.Rankings</c> returns populated snapshots when installed.
    /// </returns>
    ValueTask<IReadOnlyDictionary<Guid, PlayerRatingValue>> GetRatingsAsync(
        IReadOnlyCollection<Guid> playerIds,
        Guid ladderId,
        CancellationToken ct = default);
}
