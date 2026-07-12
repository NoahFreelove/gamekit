// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;

namespace GameKit.Rankings.Algorithms;

/// <summary>
/// An immutable batch of match outcomes to be processed by <see cref="IRankingAlgorithm.Apply"/>.
/// All outcomes in the batch are treated as belonging to the same rating period.
/// </summary>
/// <remarks>
/// Passing outcomes one at a time instead of as a batch will corrupt Glicko-2 convergence
/// (Pitfall §1 / RANK-04). The entire rating period's results must be accumulated before
/// calling <see cref="IRankingAlgorithm.Apply"/>.
/// </remarks>
/// <param name="Outcomes">The match outcomes for this rating period.</param>
public sealed record RankingBatch(IReadOnlyList<MatchOutcome> Outcomes);

/// <summary>
/// A single match outcome between two players.
/// </summary>
/// <param name="PlayerId">The player whose perspective this outcome describes.</param>
/// <param name="OpponentId">The opponent in the match.</param>
/// <param name="Result">The result from <paramref name="PlayerId"/>'s perspective.</param>
public sealed record MatchOutcome(Guid PlayerId, Guid OpponentId, MatchResult Result);

/// <summary>
/// The result of a match from a specific player's perspective.
/// </summary>
public enum MatchResult
{
    /// <summary>The player won the match.</summary>
    Win,

    /// <summary>The player lost the match.</summary>
    Loss,

    /// <summary>The match ended in a draw.</summary>
    Draw,

    /// <summary>The player forfeited. Treated as a loss for rating purposes.</summary>
    Forfeit,
}
