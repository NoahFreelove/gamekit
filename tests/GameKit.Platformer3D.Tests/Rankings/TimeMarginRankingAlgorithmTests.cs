// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Xunit;

namespace GameKit.Platformer3D.Tests.Rankings;

/// <summary>
/// Unit tests for <c>TimeMarginRankingAlgorithm</c> — the custom
/// <see cref="GameKit.Rankings.Algorithms.IRankingAlgorithm"/> for the
/// Platformer3D demo ladder (D-09/D-10/D-11).
/// </summary>
/// <remarks>
/// Wave 1 scaffold: all tests are marked Skip until Plan 21-02 writes
/// <c>TimeMarginRankingAlgorithm</c> and fills in the assertions.
/// The scaffold exists now so every later task has an automated verify target
/// (Nyquist sampling continuity).
/// </remarks>
public sealed class TimeMarginRankingAlgorithmTests
{
    /// <summary>
    /// Win/loss delta: faster player wins; winner's rating increases, loser's decreases.
    /// Larger time margin produces a larger rating swing (D-09).
    /// </summary>
    [Fact(Skip = "Implemented in 21-02")]
    public void TimeMarginRankingAlgorithm_WinLossDelta()
    {
        // Implemented in 21-02: assert winner gains, loser loses, margin scales swing
    }

    /// <summary>
    /// Draw edge: exact integer-ms tie produces no rating change for either player (D-10).
    /// </summary>
    [Fact(Skip = "Implemented in 21-02")]
    public void TimeMarginRankingAlgorithm_DrawEdge()
    {
        // Implemented in 21-02: assert exact-ms tie = draw = no rating delta
    }
}
