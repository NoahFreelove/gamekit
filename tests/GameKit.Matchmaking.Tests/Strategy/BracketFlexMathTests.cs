// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
using Xunit;

namespace GameKit.Matchmaking.Tests.Strategy;

/// <summary>
/// Unit tests for the bracket-flex formula (RESEARCH §Decision 4):
/// <c>bracket(t) = min(BracketStart + (BracketEnd − BracketStart) · t / BracketRampSeconds, BracketEnd)</c>.
/// </summary>
public sealed class BracketFlexMathTests
{
    private static MatchmakingLadderConfig DefaultCfg() => new()
    {
        Name = "main",
        BracketStart = 100,
        BracketEnd = 500,
        BracketRampSeconds = 40,
    };

    [Fact]
    public void Bracket_At_T0_Returns_BracketStart()
    {
        var cfg = DefaultCfg();
        var b = EloRangeMatchmakingStrategy.Bracket(cfg, 0);
        Assert.Equal(100, b);
    }

    [Fact]
    public void Bracket_At_T10_Returns_Quarter_Of_Ramp()
    {
        // BracketStart=100, BracketEnd=500, span=400, ramp=40s.
        // At t=10: 100 + 400 * 10/40 = 100 + 100 = 200.
        var cfg = DefaultCfg();
        var b = EloRangeMatchmakingStrategy.Bracket(cfg, 10);
        Assert.Equal(200, b);
    }

    [Fact]
    public void Bracket_At_T20_Returns_Half_Of_Ramp()
    {
        // 100 + 400 * 20/40 = 100 + 200 = 300.
        var cfg = DefaultCfg();
        var b = EloRangeMatchmakingStrategy.Bracket(cfg, 20);
        Assert.Equal(300, b);
    }

    [Fact]
    public void Bracket_At_T40_Returns_BracketEnd()
    {
        // 100 + 400 * 40/40 = 500.
        var cfg = DefaultCfg();
        var b = EloRangeMatchmakingStrategy.Bracket(cfg, 40);
        Assert.Equal(500, b);
    }

    [Fact]
    public void Bracket_At_T60_Stays_Capped_At_BracketEnd()
    {
        // Linear ramp would yield 700 — cap at BracketEnd=500.
        var cfg = DefaultCfg();
        var b = EloRangeMatchmakingStrategy.Bracket(cfg, 60);
        Assert.Equal(500, b);
    }

    [Fact]
    public void Bracket_At_T30_Custom_Curve_200_To_800_Over_60s()
    {
        // BracketStart=200, BracketEnd=800, span=600, ramp=60s.
        // At t=30: 200 + 600 * 30/60 = 200 + 300 = 500.
        var cfg = new MatchmakingLadderConfig
        {
            Name = "custom",
            BracketStart = 200,
            BracketEnd = 800,
            BracketRampSeconds = 60,
        };
        var b = EloRangeMatchmakingStrategy.Bracket(cfg, 30);
        Assert.Equal(500, b);
    }

    [Fact]
    public void Bracket_With_Negative_T_Clamps_To_BracketStart()
    {
        // Defense in depth: a malformed (now < queuedAt) input should not produce
        // a sub-Start bracket. Clamp to BracketStart at t=0.
        var cfg = DefaultCfg();
        var b = EloRangeMatchmakingStrategy.Bracket(cfg, -5);
        Assert.Equal(100, b);
    }

    [Fact]
    public void Bracket_With_Fractional_T_Returns_Fractional_Value()
    {
        // The formula returns a double — the ticker rounds for display only.
        // At t=5: 100 + 400 * 5/40 = 100 + 50 = 150.
        var cfg = DefaultCfg();
        var b = EloRangeMatchmakingStrategy.Bracket(cfg, 5);
        Assert.Equal(150, b);
    }
}
