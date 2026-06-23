// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Rankings.Algorithms;
using Platformer3D.Algorithms;
using Xunit;

namespace GameKit.Platformer3D.Tests.Rankings;

/// <summary>
/// Unit tests for <c>TimeMarginRankingAlgorithm</c> — the custom
/// <see cref="GameKit.Rankings.Algorithms.IRankingAlgorithm"/> for the
/// Platformer3D demo ladder (D-09 amended/D-10/D-11).
/// </summary>
public sealed class TimeMarginRankingAlgorithmTests
{
    private const double KWin = 30.0;

    private static PlayerRatingSnapshot MakeSnapshot(Guid playerId, double rating = 1500.0) =>
        new(playerId, rating, 350.0, 0.06);

    private static RankingState MakeState(params (Guid id, double rating)[] players)
    {
        var dict = new Dictionary<Guid, PlayerRatingSnapshot>();
        foreach (var (id, r) in players)
            dict[id] = MakeSnapshot(id, r);
        return new RankingState(dict);
    }

    /// <summary>
    /// The algorithm Name must not be "glicko2" (R6/D-09).
    /// </summary>
    [Fact]
    public void Name_IsTimeMargin_NotGlicko2()
    {
        var algo = new TimeMarginRankingAlgorithm();
        Assert.Equal("time-margin", algo.Name);
        Assert.NotEqual("glicko2", algo.Name);
    }

    /// <summary>
    /// Win/loss delta: winner Rating += KWin, loser Rating -= KWin. Symmetric.
    /// </summary>
    [Fact]
    public void WinLossDelta_WinnerGains_LoserLoses()
    {
        var algo = new TimeMarginRankingAlgorithm();
        var winnerId = Guid.NewGuid();
        var loserId = Guid.NewGuid();

        var state = MakeState((winnerId, 1500.0), (loserId, 1500.0));
        var batch = new RankingBatch(new[]
        {
            new MatchOutcome(winnerId, loserId, MatchResult.Win),
            new MatchOutcome(loserId, winnerId, MatchResult.Loss),
        });

        var result = algo.Apply(state, batch);

        var newWinner = result.Ratings[winnerId];
        var newLoser = result.Ratings[loserId];

        Assert.Equal(1500.0 + KWin, newWinner.Rating, precision: 10);
        Assert.Equal(1500.0 - KWin, newLoser.Rating, precision: 10);
    }

    /// <summary>
    /// Draw edge (D-10): exact-tie produces zero rating change for both players, symmetrically.
    /// </summary>
    [Fact]
    public void DrawEdge_ExactTie_ZeroRatingChange()
    {
        var algo = new TimeMarginRankingAlgorithm();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();

        var state = MakeState((playerA, 1600.0), (playerB, 1400.0));
        var batch = new RankingBatch(new[]
        {
            new MatchOutcome(playerA, playerB, MatchResult.Draw),
            new MatchOutcome(playerB, playerA, MatchResult.Draw),
        });

        var result = algo.Apply(state, batch);

        // Exact zero change — both ratings must be identical to input
        Assert.Equal(1600.0, result.Ratings[playerA].Rating, precision: 10);
        Assert.Equal(1400.0, result.Ratings[playerB].Rating, precision: 10);
    }

    /// <summary>
    /// Forfeit treated as a Loss: forfeiting player loses KWin points.
    /// </summary>
    [Fact]
    public void Forfeit_TreatedAsLoss()
    {
        var algo = new TimeMarginRankingAlgorithm();
        var winner = Guid.NewGuid();
        var forfeiter = Guid.NewGuid();

        var state = MakeState((winner, 1500.0), (forfeiter, 1500.0));
        var batch = new RankingBatch(new[]
        {
            new MatchOutcome(forfeiter, winner, MatchResult.Forfeit),
        });

        var result = algo.Apply(state, batch);

        Assert.Equal(1500.0 - KWin, result.Ratings[forfeiter].Rating, precision: 10);
        // Winner not in batch — rating unchanged
        Assert.Equal(1500.0, result.Ratings[winner].Rating, precision: 10);
    }

    /// <summary>
    /// Batched accumulation (D-11): multi-outcome batch applied once accumulates correctly.
    /// A player with two wins in one batch gains 2*KWin (not just KWin from one Apply call).
    /// </summary>
    [Fact]
    public void BatchedAccumulation_MultipleOutcomesSamePlayer()
    {
        var algo = new TimeMarginRankingAlgorithm();
        var player = Guid.NewGuid();
        var opponent1 = Guid.NewGuid();
        var opponent2 = Guid.NewGuid();

        var state = MakeState((player, 1500.0), (opponent1, 1500.0), (opponent2, 1500.0));
        // Two wins in one batch
        var batch = new RankingBatch(new[]
        {
            new MatchOutcome(player, opponent1, MatchResult.Win),
            new MatchOutcome(opponent1, player, MatchResult.Loss),
            new MatchOutcome(player, opponent2, MatchResult.Win),
            new MatchOutcome(opponent2, player, MatchResult.Loss),
        });

        var result = algo.Apply(state, batch);

        // Two wins → +2*KWin accumulated in one Apply call
        Assert.Equal(1500.0 + 2 * KWin, result.Ratings[player].Rating, precision: 10);
    }

    /// <summary>
    /// Input state is not mutated — original snapshots retain their values.
    /// </summary>
    [Fact]
    public void Apply_DoesNotMutate_InputState()
    {
        var algo = new TimeMarginRankingAlgorithm();
        var playerId = Guid.NewGuid();
        var opponentId = Guid.NewGuid();

        var state = MakeState((playerId, 1500.0), (opponentId, 1500.0));
        var originalRating = state.Ratings[playerId].Rating;

        var batch = new RankingBatch(new[]
        {
            new MatchOutcome(playerId, opponentId, MatchResult.Win),
        });

        _ = algo.Apply(state, batch);

        // Input state must be unchanged
        Assert.Equal(originalRating, state.Ratings[playerId].Rating);
    }

    /// <summary>
    /// Players absent from input state are seeded at DefaultRating before delta.
    /// </summary>
    [Fact]
    public void UnknownPlayer_SeededAtDefault_ThenDeltaApplied()
    {
        var algo = new TimeMarginRankingAlgorithm();
        var newPlayer = Guid.NewGuid();
        var knownPlayer = Guid.NewGuid();

        // newPlayer not in state
        var state = MakeState((knownPlayer, 1500.0));
        var batch = new RankingBatch(new[]
        {
            new MatchOutcome(newPlayer, knownPlayer, MatchResult.Win),
        });

        var result = algo.Apply(state, batch);

        // Seeded at 1500 (default) then +KWin
        Assert.Equal(1500.0 + KWin, result.Ratings[newPlayer].Rating, precision: 10);
    }

    /// <summary>
    /// Rating is floored at 0.0 — negative ratings cannot occur.
    /// </summary>
    [Fact]
    public void Rating_FlooredAtZero()
    {
        var algo = new TimeMarginRankingAlgorithm();
        var player = Guid.NewGuid();
        var opponent = Guid.NewGuid();

        // Start very low rating
        var state = MakeState((player, 10.0), (opponent, 1500.0));
        // Batch of losses that would push below 0
        var outcomes = new List<MatchOutcome>();
        for (var i = 0; i < 5; i++)
        {
            outcomes.Add(new MatchOutcome(player, opponent, MatchResult.Loss));
        }
        var batch = new RankingBatch(outcomes);

        var result = algo.Apply(state, batch);

        // 10 - 5*30 = -140, floored to 0
        Assert.Equal(0.0, result.Ratings[player].Rating, precision: 10);
    }
}
