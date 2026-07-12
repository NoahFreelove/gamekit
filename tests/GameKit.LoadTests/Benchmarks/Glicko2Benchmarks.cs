// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using GameKit.Rankings.Algorithms;

namespace GameKit.LoadTests.Benchmarks;

/// <summary>
/// Benchmarks <see cref="Glicko2Algorithm.Apply"/> at three batch sizes (2 / 10 / 100 outcomes)
/// to characterise O(n) scaling behaviour. The algorithm is pure CPU — no I/O — so results
/// reflect JIT-optimised steady-state throughput.
/// </summary>
/// <remarks>
/// A 200-player <see cref="RankingState"/> is built once in <see cref="Setup"/>.
/// Three <see cref="RankingBatch"/> objects (sizes 2, 10, 100) are pre-built so each
/// benchmark iteration pays only the <c>Apply</c> cost.
/// Each <c>Apply</c> call creates a new <c>RatingCalculator</c> internally (one per rating period),
/// which is the correct Glicko-2 batching contract — do NOT call <c>Apply</c> per-outcome.
/// </remarks>
[MemoryDiagnoser]
public class Glicko2Benchmarks
{
    private Glicko2Algorithm _algo = null!;
    private RankingState _state = null!;
    private RankingBatch _batch2 = null!;
    private RankingBatch _batch10 = null!;
    private RankingBatch _batch100 = null!;

    /// <summary>
    /// Builds a 200-player <see cref="RankingState"/> and three <see cref="RankingBatch"/>
    /// instances at sizes 2, 10, and 100 <see cref="MatchOutcome"/> entries. The player IDs
    /// are stable across setup calls so outcome references are valid.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        // tau=0.5 and initVolatility=0.06 are Glickman's worked-example values (constructor defaults).
        // Using the defaults ensures the algorithm matches the regression fixture in GameKit.Rankings.Tests.
        _algo = new Glicko2Algorithm(tau: 0.5, initVolatility: 0.06);
        _state = BuildState(200);
        _batch2 = BuildBatch(_state, 2);
        _batch10 = BuildBatch(_state, 10);
        _batch100 = BuildBatch(_state, 100);
    }

    /// <summary>Measures <see cref="Glicko2Algorithm.Apply"/> over a 2-outcome batch.</summary>
    /// <returns>Updated <see cref="RankingState"/> (discarded after each iteration).</returns>
    [Benchmark]
    public RankingState Apply_2() => _algo.Apply(_state, _batch2);

    /// <summary>Measures <see cref="Glicko2Algorithm.Apply"/> over a 10-outcome batch.</summary>
    /// <returns>Updated <see cref="RankingState"/> (discarded after each iteration).</returns>
    [Benchmark]
    public RankingState Apply_10() => _algo.Apply(_state, _batch10);

    /// <summary>Measures <see cref="Glicko2Algorithm.Apply"/> over a 100-outcome batch.</summary>
    /// <returns>Updated <see cref="RankingState"/> (discarded after each iteration).</returns>
    [Benchmark]
    public RankingState Apply_100() => _algo.Apply(_state, _batch100);

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="RankingState"/> with <paramref name="count"/> players at Glicko-2
    /// default initial values (rating=1500, RD=350, volatility=0.06).
    /// </summary>
    private static RankingState BuildState(int count)
    {
        var ratings = new Dictionary<Guid, PlayerRatingSnapshot>(count);
        for (int i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            ratings[id] = new PlayerRatingSnapshot(
                PlayerId:        id,
                Rating:          1500.0,
                RatingDeviation: 350.0,
                Volatility:      0.06);
        }
        return new RankingState(ratings);
    }

    /// <summary>
    /// Builds a <see cref="RankingBatch"/> with <paramref name="outcomeCount"/> outcomes,
    /// sampling pairs from the <paramref name="state"/>'s player list.
    /// Outcomes alternate Win/Loss/Draw to exercise all three branches of the algorithm.
    /// </summary>
    private static RankingBatch BuildBatch(RankingState state, int outcomeCount)
    {
        var playerIds = new List<Guid>(state.Ratings.Keys);
        var outcomes = new List<MatchOutcome>(outcomeCount);
        var results = new[] { MatchResult.Win, MatchResult.Loss, MatchResult.Draw };

        for (int i = 0; i < outcomeCount; i++)
        {
            // Pair players in a round-robin pattern; wrap if outcomeCount > playerIds.Count / 2.
            int a = (i * 2) % playerIds.Count;
            int b = (i * 2 + 1) % playerIds.Count;
            if (a == b) b = (b + 1) % playerIds.Count;

            outcomes.Add(new MatchOutcome(
                PlayerId:   playerIds[a],
                OpponentId: playerIds[b],
                Result:     results[i % results.Length]));
        }

        return new RankingBatch(outcomes);
    }
}
