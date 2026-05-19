// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Matchmaking.Strategy;
using Xunit;

namespace GameKit.Matchmaking.Tests.Strategy;

/// <summary>
/// Unit tests for <see cref="PartyRatingAggregatorService"/> — the Mean / Max /
/// GlickoWeighted switch from RESEARCH §Decision 5.
/// </summary>
public sealed class GlickoWeightedAggregatorTests
{
    private readonly PartyRatingAggregatorService _svc = new();

    private static QueuedPartyMember Member(double rating, double rd = 200, double vol = 0.06) =>
        new(Guid.NewGuid(), rating, rd, vol);

    [Fact]
    public void Mean_Returns_Arithmetic_Mean()
    {
        var members = new[] { Member(1000), Member(1200), Member(1400) };
        var avg = _svc.Compute(PartyRatingAggregator.Mean, members);
        Assert.Equal(1200, avg);
    }

    [Fact]
    public void Max_Returns_Highest_Rating()
    {
        var members = new[] { Member(900), Member(1500), Member(1100) };
        var max = _svc.Compute(PartyRatingAggregator.Max, members);
        Assert.Equal(1500, max);
    }

    [Fact]
    public void GlickoWeighted_Heavily_Weighted_Toward_Low_RD_Member()
    {
        // Member A: rating 1500, RD 50 → weight = 1/2500 = 0.0004
        // Member B: rating 1600, RD 300 → weight = 1/90000 ≈ 1.111e-5
        // sumWR = 0.0004*1500 + 1.111e-5*1600 = 0.6 + 0.01778 ≈ 0.61778
        // sumW  = 0.0004 + 1.111e-5 ≈ 0.000411
        // result ≈ 0.61778 / 0.000411 ≈ 1502.7
        var members = new[] { Member(1500, rd: 50), Member(1600, rd: 300) };
        var weighted = _svc.Compute(PartyRatingAggregator.GlickoWeighted, members);
        Assert.InRange(weighted, 1502.0, 1503.5);
    }

    [Fact]
    public void GlickoWeighted_Falls_Back_To_Mean_When_All_RDs_Are_Zero()
    {
        // Edge case: all RDs zero or negative ⇒ no valid weight; fall back to arithmetic mean.
        var members = new[] { Member(1000, rd: 0), Member(2000, rd: 0) };
        var result = _svc.Compute(PartyRatingAggregator.GlickoWeighted, members);
        Assert.Equal(1500, result);
    }

    [Fact]
    public void GlickoWeighted_Single_Member_Returns_That_Members_Rating()
    {
        var members = new[] { Member(1250, rd: 100) };
        var result = _svc.Compute(PartyRatingAggregator.GlickoWeighted, members);
        Assert.Equal(1250, result, precision: 6);
    }

    [Fact]
    public void Compute_Throws_On_Empty_Member_List()
    {
        Assert.Throws<ArgumentException>(() =>
            _svc.Compute(PartyRatingAggregator.Mean, Array.Empty<QueuedPartyMember>()));
    }

    [Fact]
    public void Compute_Throws_On_Null_Member_List()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _svc.Compute(PartyRatingAggregator.Mean, null!));
    }

    [Fact]
    public void Compute_Throws_On_Unknown_Aggregator()
    {
        var members = new[] { Member(1000) };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _svc.Compute((PartyRatingAggregator)999, members));
    }
}
