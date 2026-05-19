// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;

namespace GameKit.Rankings.Algorithms;

/// <summary>
/// Immutable snapshot of every player's Glicko-2 rating before or after a rating period update.
/// <see cref="IRankingAlgorithm.Apply"/> consumes a <see cref="RankingState"/> and returns a
/// new <see cref="RankingState"/> — it never mutates its input.
/// </summary>
/// <param name="Ratings">
/// Per-player rating snapshots keyed by player ID. Players absent from this dictionary are
/// treated as unrated and receive the algorithm's configured defaults on their first appearance.
/// </param>
public sealed record RankingState(IReadOnlyDictionary<Guid, PlayerRatingSnapshot> Ratings);

/// <summary>
/// Immutable Glicko-2 rating snapshot for a single player.
/// </summary>
/// <param name="PlayerId">The player's unique identifier.</param>
/// <param name="Rating">Glicko-2 rating on the Glicko scale (default 1500).</param>
/// <param name="RatingDeviation">Rating deviation on the Glicko scale (default 350).</param>
/// <param name="Volatility">
/// Volatility — measures rating consistency (Glickman's example default is 0.06).
/// Values are algorithm-specific; do not compare across different algorithms.
/// </param>
public sealed record PlayerRatingSnapshot(
    Guid PlayerId,
    double Rating,
    double RatingDeviation,
    double Volatility);
