// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Rankings.Http.Contracts;

/// <summary>
/// Wire representation of a single row in a leaderboard response (RANK-08 / D-23).
/// Returned by <c>GET /admin/api/leaderboard</c> and by <see cref="GameKit.Rankings.Services.ILeaderboardService"/> consumers.
/// </summary>
/// <param name="Rank">1-based rank position on this leaderboard page (1 = highest rating).</param>
/// <param name="PlayerId">Player id.</param>
/// <param name="DisplayName">Resolved display name from the live <c>players</c> table. <c>(deleted)</c> when the player was GDPR-erased.</param>
/// <param name="Rating">Current (or archived) Glicko-2 rating.</param>
/// <param name="RatingDeviation">Glicko-2 rating deviation.</param>
/// <param name="Wins">Win count on this ladder.</param>
/// <param name="Losses">Loss count on this ladder.</param>
/// <param name="Draws">Draw count on this ladder.</param>
public sealed record LeaderboardRowDto(
    int Rank,
    Guid PlayerId,
    string DisplayName,
    double Rating,
    double RatingDeviation,
    int Wins,
    int Losses,
    int Draws);
