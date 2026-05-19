// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Rankings.Http.Contracts;

namespace GameKit.Rankings.Services;

/// <summary>
/// Provides leaderboard query access for live and archived seasons on a ladder (RANK-08 / D-23).
/// </summary>
/// <remarks>
/// <para>
/// Two query modes are supported:
/// <list type="bullet">
///   <item><see cref="TopAsync"/> — top-N players by rating, optionally scoped to an archived season.</item>
///   <item><see cref="AroundAsync"/> — window of players above and below a given player, optionally archived.</item>
/// </list>
/// </para>
/// <para>
/// When <c>seasonId</c> is <see langword="null"/>, the live <c>player_ranks</c> table is queried.
/// When <c>seasonId</c> is non-null, the <c>season_rank_archive</c> table for that specific season
/// is queried instead (SC#4).
/// </para>
/// </remarks>
public interface ILeaderboardService
{
    /// <summary>
    /// Returns the top-N players on the specified ladder sorted by rating descending.
    /// </summary>
    /// <param name="ladderId">Ladder to query.</param>
    /// <param name="limit">Maximum rows to return. Clamped to [1, 500]. Defaults to 100.</param>
    /// <param name="seasonId">
    /// When non-null, queries <c>season_rank_archive</c> for the specified season instead of the
    /// live <c>player_ranks</c> table. Pass <see langword="null"/> for the current live leaderboard.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of <see cref="LeaderboardRowDto"/> (rank 1 = highest rating).</returns>
    Task<IReadOnlyList<LeaderboardRowDto>> TopAsync(
        Guid ladderId,
        int limit = 100,
        Guid? seasonId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the players within <paramref name="window"/> positions above and below the target player,
    /// centered on that player.
    /// </summary>
    /// <param name="ladderId">Ladder to query.</param>
    /// <param name="playerId">The target player whose rank is the center of the window.</param>
    /// <param name="window">Rows above and below the target. Clamped to [1, 50]. Defaults to 5.</param>
    /// <param name="seasonId">
    /// When non-null, queries <c>season_rank_archive</c> for the specified season.
    /// Pass <see langword="null"/> for the current live leaderboard.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// List of <see cref="LeaderboardRowDto"/> sorted by rating descending, centered on the target player.
    /// When the target player has no rank row in the specified context (e.g. a freshly registered
    /// player who has not completed a ranked match), an empty list is returned (WR-05). Callers
    /// needing a 404 semantic can detect the empty result and map it themselves.
    /// </returns>
    Task<IReadOnlyList<LeaderboardRowDto>> AroundAsync(
        Guid ladderId,
        Guid playerId,
        int window = 5,
        Guid? seasonId = null,
        CancellationToken ct = default);
}
