// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Xunit;

namespace GameKit.Platformer3D.Tests.Strategy;

/// <summary>
/// Unit tests for <c>BestTimeMatchmakingStrategy</c> — the custom
/// <see cref="GameKit.Matchmaking.Strategy.IMatchmakingStrategy"/> for the
/// Platformer3D demo ladder (D-06/D-07).
/// </summary>
/// <remarks>
/// Wave 1 scaffold: all tests are marked Skip until Plan 21-02 writes
/// <c>BestTimeMatchmakingStrategy</c> and fills in the assertions.
/// The scaffold exists now so every later task has an automated verify target
/// (Nyquist sampling continuity).
/// </remarks>
public sealed class BestTimeMatchmakingStrategyTests
{
    /// <summary>
    /// DI resolution check: <c>GetRequiredService&lt;IMatchmakingStrategy&gt;()</c>
    /// must return an instance that is a <c>BestTimeMatchmakingStrategy</c>
    /// (R5 — custom strategy resolves, not EloRange).
    /// </summary>
    [Fact(Skip = "Implemented in 21-02")]
    public void BestTimeMatchmakingStrategyResolutionTests()
    {
        // Implemented in 21-02: assert IMatchmakingStrategy resolves as BestTimeMatchmakingStrategy
    }

    /// <summary>
    /// Match logic: two players with close best-times form a match within the
    /// initial window; a player with a far-off time does not match until ramp widens.
    /// </summary>
    [Fact(Skip = "Implemented in 21-02")]
    public void BestTimeMatchmakingStrategyMatchTests()
    {
        // Implemented in 21-02: assert match forms for close best-times, not for far-off times
    }
}
