// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Rankings.Algorithms;

namespace Platformer3D.Algorithms;

/// <summary>
/// Custom <see cref="IRankingAlgorithm"/> for the Platformer3D demo ladder (D-09/D-10/D-11).
/// </summary>
/// <remarks>
/// <para>
/// <b>D-09 AMENDMENT — Fixed-delta Elo, not margin-scaled:</b>
/// The original D-09 specified rating updates "scaled by the time margin (bigger gap → bigger swing)".
/// This sub-clause is dropped and replaced with a fixed-delta rule. The forced reason: <c>MatchOutcome</c>
/// in <c>GameKit.Rankings.Algorithms.RankingBatch</c> carries only <c>PlayerId</c>, <c>OpponentId</c>,
/// and <c>Result</c> — there is no <c>Score</c> or margin field. Carrying the completion-time margin
/// into the ranking batch would require adding a field to the <c>GameKit.*</c> package public API,
/// which is explicitly prohibited by SPEC ("Changes to any GameKit.* package public API") and
/// D-15 surface confinement. SPEC (WHAT) outranks CONTEXT D-09 (HOW), so the resolution is forced.
/// </para>
/// <para>
/// The implementation therefore uses <b>fixed-delta Elo</b>: Win = +<see cref="KWin"/>,
/// Loss/Forfeit = −<see cref="KWin"/>, Draw = 0.0, symmetric. The class name retains
/// "TimeMargin" for continuity with the PATTERNS document and plan artifacts; the head-to-head
/// outcome (faster integer-ms time = Win) is decided by the GameServer when it posts
/// <c>SessionResult.Win/Loss/Draw</c> — the time comparison lives there, not here.
/// </para>
/// <para>
/// This still satisfies R6 (verifiable leaderboard change via a custom rule, Name != "glicko2"),
/// D-10 (exact integer-ms tie → draw, symmetric, no asymmetric change), D-11 (batched-only),
/// and D-12 (drives the admin leaderboard).
/// </para>
/// <para>
/// <b>Stateless + thread-safe (IRankingAlgorithm contract):</b> no mutable instance fields.
/// All per-call state is built inside <see cref="Apply"/>. Safe for concurrent invocations.
/// </para>
/// <para>
/// <b>Batched-only (D-11 / RANK-04):</b> accumulates the entire <see cref="RankingBatch"/>
/// into a single per-player delta map, then applies once. Never calls Apply per individual match.
/// Fixed-delta is O(n) over batch outcomes with no convergence/iteration loop (T-21-04).
/// </para>
/// </remarks>
public sealed class TimeMarginRankingAlgorithm : IRankingAlgorithm
{
    /// <summary>Fixed Elo swing per win or loss. Draw produces zero change (D-10).</summary>
    public const double KWin = 30.0;

    /// <summary>Default starting rating for players absent from <see cref="RankingState"/>.</summary>
    public const double DefaultRating = 1500.0;

    /// <summary>Default starting rating deviation for players absent from <see cref="RankingState"/>.</summary>
    public const double DefaultRd = 350.0;

    /// <summary>Default volatility for players absent from <see cref="RankingState"/>.</summary>
    public const double DefaultVolatility = 0.06;

    /// <inheritdoc/>
    public string Name => "time-margin";   // D-09: Name != "glicko2"

    /// <inheritdoc/>
    /// <remarks>
    /// Fixed-delta Elo, batched-only (D-11): accumulates ALL outcomes into a per-player
    /// delta map, then applies once. Reads only <c>MatchOutcome.PlayerId</c>,
    /// <c>.OpponentId</c>, and <c>.Result</c> — never a Score/margin field (none exists;
    /// see class-level D-09 amendment note). Input <paramref name="state"/> is not mutated.
    /// Rating is floored at 0.0.
    /// </remarks>
    public RankingState Apply(RankingState state, RankingBatch batch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(batch);

        // Build one fresh working snapshot per Apply call — no mutable instance fields (RANK-04 / T-21-05).
        // Seed all players from input state first.
        var workingRatings = new Dictionary<Guid, PlayerRatingSnapshot>(state.Ratings.Count);
        foreach (var (id, snap) in state.Ratings)
            workingRatings[id] = snap;

        // Seed any batch player absent from state at defaults.
        foreach (var outcome in batch.Outcomes)
        {
            if (!workingRatings.ContainsKey(outcome.PlayerId))
                workingRatings[outcome.PlayerId] = new PlayerRatingSnapshot(outcome.PlayerId, DefaultRating, DefaultRd, DefaultVolatility);
            if (!workingRatings.ContainsKey(outcome.OpponentId))
                workingRatings[outcome.OpponentId] = new PlayerRatingSnapshot(outcome.OpponentId, DefaultRating, DefaultRd, DefaultVolatility);
        }

        // Accumulate per-player deltas across ALL outcomes (batched-only D-11 / RANK-04).
        // Read ONLY Result — never a Score field (D-09 amendment; no such field in MatchOutcome).
        var deltas = new Dictionary<Guid, double>(workingRatings.Count);

        foreach (var outcome in batch.Outcomes)
        {
            switch (outcome.Result)
            {
                case MatchResult.Win:
                    // Winner +KWin; only accumulate for the perspective player (PlayerId = winner here).
                    deltas.TryGetValue(outcome.PlayerId, out var winnerDelta);
                    deltas[outcome.PlayerId] = winnerDelta + KWin;
                    break;

                case MatchResult.Loss:
                case MatchResult.Forfeit: // Forfeit treated as Loss (mirrors Glicko2Algorithm convention)
                    deltas.TryGetValue(outcome.PlayerId, out var loserDelta);
                    deltas[outcome.PlayerId] = loserDelta - KWin;
                    break;

                case MatchResult.Draw:
                    // D-10: exact-tie = draw = zero delta for this player. No accumulation needed.
                    // Ensure player is tracked in deltas map so they appear in output (no-op add).
                    if (!deltas.ContainsKey(outcome.PlayerId))
                        deltas[outcome.PlayerId] = 0.0;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(outcome.Result), outcome.Result, "Unknown MatchResult value");
            }
        }

        // Apply accumulated deltas once — build new immutable snapshots. Floor rating at 0.0.
        var newRatings = new Dictionary<Guid, PlayerRatingSnapshot>(workingRatings.Count);

        foreach (var (id, snap) in workingRatings)
        {
            deltas.TryGetValue(id, out var delta);
            var newRating = Math.Max(0.0, snap.Rating + delta);
            newRatings[id] = snap with { Rating = newRating };
        }

        return new RankingState(newRatings);
    }
}
