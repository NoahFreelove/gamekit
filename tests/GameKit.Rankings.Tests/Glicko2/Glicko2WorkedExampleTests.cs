// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameKit.Rankings.Algorithms;
using Xunit;

namespace GameKit.Rankings.Tests.Glicko2;

/// <summary>
/// RANK-05 regression test: validates <see cref="Glicko2Algorithm"/> against the exact
/// worked example from Glickman's 2012 paper (§3.1).
///
/// Expected outputs (from glicko.net PDF, rounded to 2–4 decimals):
///   rating    ≈ 1464.05  (tolerance ±0.5)
///   rd        ≈  151.52  (tolerance ±0.5)
///   volatility ≈ 0.05999  (tolerance ±0.0001)
/// </summary>
public class Glicko2WorkedExampleTests
{
    [Fact]
    public void Glickman_Worked_Example_Matches_Within_Tolerance()
    {
        // Load the fixture that plan 04-01 committed
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Glicko2", "Fixtures", "Glickman_Worked_Example.json");

        Assert.True(File.Exists(fixturePath), $"Fixture not found: {fixturePath}");

        var fixture = JsonSerializer.Deserialize<WorkedExampleFixture>(
            File.ReadAllText(fixturePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        // Build initial RankingState — one player vs three opponents
        var playerId = Guid.NewGuid();
        var oppIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var ratings = new Dictionary<Guid, PlayerRatingSnapshot>
        {
            [playerId] = new PlayerRatingSnapshot(
                playerId,
                fixture.Player.Rating,
                fixture.Player.RatingDeviation,
                fixture.Player.Volatility),
        };

        for (int i = 0; i < fixture.Opponents.Length; i++)
        {
            var opp = fixture.Opponents[i];
            ratings[oppIds[i]] = new PlayerRatingSnapshot(
                oppIds[i], opp.Rating, opp.RatingDeviation, opp.Volatility);
        }

        var initialState = new RankingState(ratings);

        // Build RankingBatch from fixture outcomes
        var outcomes = new List<MatchOutcome>();
        foreach (var outcome in fixture.Outcomes)
        {
            var result = outcome.Result.ToLowerInvariant() switch
            {
                "win"     => MatchResult.Win,
                "loss"    => MatchResult.Loss,
                "draw"    => MatchResult.Draw,
                "forfeit" => MatchResult.Forfeit,
                _         => throw new InvalidOperationException($"Unknown result: {outcome.Result}")
            };
            outcomes.Add(new MatchOutcome(playerId, oppIds[outcome.OpponentIndex], result));
        }

        var batch = new RankingBatch(outcomes);

        // Apply the algorithm — default ctor uses tau=0.5 per Pitfall §2
        var algorithm = new Glicko2Algorithm();
        var resultState = algorithm.Apply(initialState, batch);

        var playerResult = resultState.Ratings[playerId];

        // Assert within Glickman's documented tolerances
        Assert.InRange(playerResult.Rating,
            fixture.Expected.Rating - fixture.Expected.Tolerances.Rating,
            fixture.Expected.Rating + fixture.Expected.Tolerances.Rating);

        Assert.InRange(playerResult.RatingDeviation,
            fixture.Expected.RatingDeviation - fixture.Expected.Tolerances.RatingDeviation,
            fixture.Expected.RatingDeviation + fixture.Expected.Tolerances.RatingDeviation);

        Assert.InRange(playerResult.Volatility,
            fixture.Expected.Volatility - fixture.Expected.Tolerances.Volatility,
            fixture.Expected.Volatility + fixture.Expected.Tolerances.Volatility);
    }

    // Fixture deserialization model
    private sealed class WorkedExampleFixture
    {
        public PlayerFixture Player { get; set; } = null!;
        public PlayerFixture[] Opponents { get; set; } = [];
        public OutcomeFixture[] Outcomes { get; set; } = [];
        public ExpectedFixture Expected { get; set; } = null!;
    }

    private sealed class PlayerFixture
    {
        public double Rating { get; set; }
        public double RatingDeviation { get; set; }
        public double Volatility { get; set; }
    }

    private sealed class OutcomeFixture
    {
        public int OpponentIndex { get; set; }
        public string Result { get; set; } = string.Empty;
    }

    private sealed class ExpectedFixture
    {
        public double Rating { get; set; }
        public double RatingDeviation { get; set; }
        public double Volatility { get; set; }
        public TolerancesFixture Tolerances { get; set; } = null!;
    }

    private sealed class TolerancesFixture
    {
        public double Rating { get; set; }
        public double RatingDeviation { get; set; }
        public double Volatility { get; set; }
    }
}
