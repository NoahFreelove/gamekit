// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Reflection;
using GameKit.Rankings.Algorithms;
using Xunit;

namespace GameKit.Rankings.Tests.Glicko2;

/// <summary>
/// Contract tests for <see cref="IRankingAlgorithm"/> — asserts the batched-only interface
/// shape mandated by RANK-04 / Pitfall §1. A reflection scan confirms there is exactly ONE
/// public method named <c>Apply</c>, and no per-match overload exists.
/// </summary>
public class Glicko2AlgorithmContractTests
{
    /// <summary>
    /// IRankingAlgorithm must declare exactly ONE public instance method named Apply.
    /// Any per-match overload silently corrupts Glicko-2 convergence (Pitfall §1 / RANK-04).
    /// </summary>
    [Fact]
    public void IRankingAlgorithm_Has_Only_Apply_Batch_Method()
    {
        var methods = typeof(IRankingAlgorithm)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName) // exclude property accessors (get_Name)
            .ToList();

        Assert.Single(methods);
        Assert.Equal("Apply", methods[0].Name);

        var parameters = methods[0].GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(RankingState), parameters[0].ParameterType);
        Assert.Equal(typeof(RankingBatch), parameters[1].ParameterType);
        Assert.Equal(typeof(RankingState), methods[0].ReturnType);
    }

    /// <summary>
    /// Glicko2Algorithm.Name must return "glicko2".
    /// </summary>
    [Fact]
    public void Glicko2Algorithm_Reports_Name_Glicko2()
    {
        var algorithm = new Glicko2Algorithm();
        Assert.Equal("glicko2", algorithm.Name);
    }

    /// <summary>
    /// Glicko2Algorithm must use tau=0.5 by default — NOT 0.75 (MaartenStaa upstream default).
    /// Running the Glickman worked example with tau=0.75 produces volatility ~0.06000;
    /// tau=0.5 produces volatility ~0.05999. This test anchors Pitfall §2.
    /// </summary>
    [Fact]
    public void Tau_Is_05_By_Default_Not_075()
    {
        var algorithm = new Glicko2Algorithm(); // default ctor — must use tau 0.5

        // Glickman §3.1 worked example setup
        var playerId = Guid.NewGuid();
        var opp1Id = Guid.NewGuid();
        var opp2Id = Guid.NewGuid();
        var opp3Id = Guid.NewGuid();

        var initialState = new RankingState(new System.Collections.Generic.Dictionary<Guid, PlayerRatingSnapshot>
        {
            [playerId] = new PlayerRatingSnapshot(playerId, 1500.0, 200.0, 0.06),
            [opp1Id]   = new PlayerRatingSnapshot(opp1Id,   1400.0,  30.0, 0.06),
            [opp2Id]   = new PlayerRatingSnapshot(opp2Id,   1550.0, 100.0, 0.06),
            [opp3Id]   = new PlayerRatingSnapshot(opp3Id,   1700.0, 300.0, 0.06),
        });

        var batch = new RankingBatch(new[]
        {
            new MatchOutcome(playerId, opp1Id, MatchResult.Win),
            new MatchOutcome(playerId, opp2Id, MatchResult.Loss),
            new MatchOutcome(playerId, opp3Id, MatchResult.Loss),
        });

        var result = algorithm.Apply(initialState, batch);
        var playerResult = result.Ratings[playerId];

        // tau=0.5 produces volatility ~0.05999; tau=0.75 would produce ~0.06000.
        // The tolerance (0.0001) exactly distinguishes the two paths per Pitfall §2.
        Assert.InRange(playerResult.Volatility, 0.05989, 0.06000 - 0.000001);
    }
}
