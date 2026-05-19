// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Matchmaking.Tests.Builder;

/// <summary>
/// Asserts the per-ladder defaults pinned by CONTEXT D-11/D-12/D-13/D-14 and the case
/// insensitive duplicate-name + invalid-range guards enforced by
/// <see cref="GameKitMatchmakingBuilder.AddLadder(string, Action{MatchmakingLadderConfig}?)"/>.
/// </summary>
public sealed class LadderConfigDefaultsTests
{
    [Fact]
    public void BracketStart_DefaultsTo_100()
    {
        var cfg = new MatchmakingLadderConfig { Name = "main" };
        Assert.Equal(100, cfg.BracketStart);
    }

    [Fact]
    public void BracketEnd_DefaultsTo_500()
    {
        var cfg = new MatchmakingLadderConfig { Name = "main" };
        Assert.Equal(500, cfg.BracketEnd);
    }

    [Fact]
    public void BracketRampSeconds_DefaultsTo_40()
    {
        var cfg = new MatchmakingLadderConfig { Name = "main" };
        Assert.Equal(40, cfg.BracketRampSeconds);
    }

    [Fact]
    public void PartyRatingAggregator_DefaultsTo_Mean()
    {
        var cfg = new MatchmakingLadderConfig { Name = "main" };
        Assert.Equal(PartyRatingAggregator.Mean, cfg.PartyRatingAggregator);
    }

    [Fact]
    public void MaxPartyRatingSpread_DefaultsTo_Null()
    {
        var cfg = new MatchmakingLadderConfig { Name = "main" };
        Assert.Null(cfg.MaxPartyRatingSpread);
    }

    [Fact]
    public void AddLadder_Rejects_Duplicate_Name_Case_Insensitively()
    {
        var builder = new GameKitMatchmakingBuilder(new ServiceCollection());
        builder.AddLadder("Main");
        // Same name, different casing — must still throw because case-insensitive dedup
        // mirrors the citext uniqueness enforced on the Rankings ladder name column.
        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddLadder("MAIN"));
        Assert.Contains("already registered", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddLadder_Rejects_BracketEnd_Below_BracketStart()
    {
        var builder = new GameKitMatchmakingBuilder(new ServiceCollection());
        var ex = Assert.Throws<ArgumentException>(() => builder.AddLadder("main", c =>
        {
            c.BracketStart = 500;
            c.BracketEnd = 100; // invalid — end must be >= start
        }));
        Assert.Contains("BracketEnd", ex.Message);
    }

    [Fact]
    public void AddLadder_Rejects_BracketRampSeconds_NonPositive()
    {
        var builder = new GameKitMatchmakingBuilder(new ServiceCollection());
        var ex = Assert.Throws<ArgumentException>(() => builder.AddLadder("main", c => c.BracketRampSeconds = 0));
        Assert.Contains("BracketRampSeconds", ex.Message);
    }

    [Fact]
    public void AddLadder_Rejects_MaxPartyRatingSpread_NonPositive()
    {
        var builder = new GameKitMatchmakingBuilder(new ServiceCollection());
        var ex = Assert.Throws<ArgumentException>(() => builder.AddLadder("main", c => c.MaxPartyRatingSpread = 0));
        Assert.Contains("MaxPartyRatingSpread", ex.Message);
    }

    [Fact]
    public void AddLadder_Accepts_Null_MaxPartyRatingSpread_As_Default()
    {
        var builder = new GameKitMatchmakingBuilder(new ServiceCollection());
        builder.AddLadder("main"); // no configure callback — defaults are honored
        var ladder = Assert.Single(builder.RegisteredLadders);
        Assert.Null(ladder.MaxPartyRatingSpread);
        Assert.Equal("main", ladder.Name);
    }

    [Fact]
    public void AddLadder_Allows_Two_Distinct_Names()
    {
        var builder = new GameKitMatchmakingBuilder(new ServiceCollection());
        builder.AddLadder("main");
        builder.AddLadder("tournament");
        Assert.Equal(2, builder.RegisteredLadders.Count);
    }

    [Fact]
    public void AddLadder_Rejects_Null_Or_Empty_Name()
    {
        var builder = new GameKitMatchmakingBuilder(new ServiceCollection());
        Assert.Throws<ArgumentException>(() => builder.AddLadder(""));
        Assert.Throws<ArgumentException>(() => builder.AddLadder("   "));
    }
}
