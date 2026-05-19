// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Rankings.Glicko2;

namespace GameKit.Rankings.Algorithms;

/// <summary>
/// Default <see cref="IRankingAlgorithm"/> implementation wrapping the vendored
/// MaartenStaa/glicko2-csharp engine (BSD-3-Clause, see THIRD-PARTY-NOTICES.md).
/// </summary>
/// <remarks>
/// <b>tau = 0.5 (Pitfall §2):</b> Glickman's own worked example uses τ = 0.5.
/// The upstream <c>RatingCalculator</c> defaults to τ = 0.75 — that value produces
/// volatility ≈ 0.06000 on the worked example, whereas τ = 0.5 produces 0.05999.
/// This constructor always passes τ = 0.5 explicitly so the regression tests match
/// Glickman's published numerics.
/// <para/>
/// <b>Overriding tau:</b> use the <c>tau</c> constructor parameter. Plan 04-04 wires
/// <c>GameKitRankingsOptions.Glicko2.Tau</c> to this parameter so operators can tune the
/// system constant without recompiling.
/// <para/>
/// <b>Forfeit treatment:</b> <see cref="MatchResult.Forfeit"/> is treated as a loss for the
/// forfeiting player. This is equivalent to <c>MatchResult.Loss</c> for Glicko-2 purposes —
/// the algorithm does not distinguish forfeits from losses.
/// </remarks>
public sealed class Glicko2Algorithm : IRankingAlgorithm
{
    private readonly double _tau;
    private readonly double _initVolatility;

    /// <summary>
    /// Constructs a <see cref="Glicko2Algorithm"/> with Glickman's recommended defaults.
    /// </summary>
    /// <param name="tau">
    /// System constant τ constraining volatility change over time. Glickman recommends
    /// a value between 0.3 and 1.2; smaller values prevent large rating swings.
    /// Default: 0.5 (Glickman's worked example value). Do not use the upstream default
    /// of 0.75 — it produces different numerics from the published example (Pitfall §2).
    /// </param>
    /// <param name="initVolatility">
    /// Default initial volatility for players with no prior rating. Default: 0.06
    /// (Glickman's worked example value).
    /// </param>
    // Default values: tau: 0.5 (Glickman's example); initVolatility: 0.06 (Glickman's example)
    public Glicko2Algorithm(double tau = 0.5, double initVolatility = 0.06)
    {
        _tau = tau;
        _initVolatility = initVolatility;
    }

    /// <inheritdoc/>
    public string Name => "glicko2";

    /// <inheritdoc/>
    public RankingState Apply(RankingState state, RankingBatch batch)
    {
        // Build one RatingCalculator per Apply call — it is stateful per period.
        var calc = new RatingCalculator(initVolatility: _initVolatility, tau: _tau);

        // Map GameKit player IDs to vendored Rating wrappers
        var ratingMap = new Dictionary<Guid, Rating>();

        foreach (var (id, snapshot) in state.Ratings)
        {
            ratingMap[id] = new Rating(calc, snapshot.Rating, snapshot.RatingDeviation, snapshot.Volatility);
        }

        // Ensure any player referenced in the batch but absent from state gets a default Rating
        foreach (var outcome in batch.Outcomes)
        {
            if (!ratingMap.ContainsKey(outcome.PlayerId))
                ratingMap[outcome.PlayerId] = new Rating(calc);

            if (!ratingMap.ContainsKey(outcome.OpponentId))
                ratingMap[outcome.OpponentId] = new Rating(calc);
        }

        // Accumulate all outcomes into a single RatingPeriodResults — batched-only (RANK-04)
        var results = new RatingPeriodResults();

        foreach (var outcome in batch.Outcomes)
        {
            var player   = ratingMap[outcome.PlayerId];
            var opponent = ratingMap[outcome.OpponentId];

            switch (outcome.Result)
            {
                case MatchResult.Win:
                    results.AddResult(winner: player, loser: opponent);
                    break;

                case MatchResult.Loss:
                case MatchResult.Forfeit: // Forfeit is treated as a loss (documented above)
                    results.AddResult(winner: opponent, loser: player);
                    break;

                case MatchResult.Draw:
                    results.AddDraw(player, opponent);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(outcome.Result), outcome.Result, "Unknown MatchResult value");
            }
        }

        // Run the Glicko-2 algorithm for this rating period
        calc.UpdateRatings(results);

        // Build the new immutable RankingState from updated Rating wrappers
        var newRatings = new Dictionary<Guid, PlayerRatingSnapshot>(ratingMap.Count);

        foreach (var (id, rating) in ratingMap)
        {
            newRatings[id] = new PlayerRatingSnapshot(
                PlayerId:        id,
                Rating:          rating.GetRating(),
                RatingDeviation: rating.GetRatingDeviation(),
                Volatility:      rating.GetVolatility());
        }

        return new RankingState(newRatings);
    }
}
