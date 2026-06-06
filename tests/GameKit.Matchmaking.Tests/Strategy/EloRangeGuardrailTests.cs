// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Core.Services;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
using Xunit;

namespace GameKit.Matchmaking.Tests.Strategy;

/// <summary>
/// Unit tests for MATCH-17 guardrails: <see cref="MatchmakingLadderConfig.MaxBracketWidth"/> hard cap
/// and <see cref="MatchmakingLadderConfig.MinPoolDepthBeforeBracketExpansion"/> depth guard in
/// <see cref="EloRangeMatchmakingStrategy"/>. Builder validation (fail-fast at AddLadder time)
/// is also exercised. See PATTERNS.md §EloRangeGuardrailTests for construction patterns.
/// </summary>
public sealed class EloRangeGuardrailTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 17, 0, 0, 0, TimeSpan.Zero);

    private static MatchmakingLadderConfig CfgWithGuardrails(
        int? maxBracketWidth = null,
        int? minPoolDepth = null) => new()
    {
        Name = "main",
        BracketStart = 100,
        BracketEnd = 500,
        BracketRampSeconds = 40,
        MaxBracketWidth = maxBracketWidth,
        MinPoolDepthBeforeBracketExpansion = minPoolDepth,
    };

    private static EloRangeMatchmakingStrategy BuildStrategy(MatchmakingLadderConfig cfg) =>
        new(new[] { cfg }, new PartyRatingAggregatorService(), new FixedClock(Now));

    private static QueuedParty Party(
        double rating,
        DateTimeOffset queuedAt,
        Guid? ticketId = null,
        double rd = 200)
    {
        var members = new List<QueuedPartyMember>
        {
            new QueuedPartyMember(Guid.NewGuid(), rating, rd, 0.06),
        };
        return new QueuedParty(
            TicketId: ticketId ?? Guid.NewGuid(),
            PartyId: null,
            LadderId: Guid.NewGuid(),
            PoolName: "main",
            Members: members,
            AggregateRating: rating,
            QueuedAt: queuedAt);
    }

    // -----------------------------------------------------------------------
    // Bracket cap tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// MaxBracketWidth=300 wins over BracketEnd=500 — bracket never exceeds the cap regardless
    /// of wait time.
    /// </summary>
    [Fact]
    public void Bracket_NeverExceeds_MaxBracketWidth()
    {
        // After 100s in queue, raw = BracketEnd = 500, but MaxBracketWidth = 300 → capped at 300.
        var bracket = EloRangeMatchmakingStrategy.Bracket(CfgWithGuardrails(maxBracketWidth: 300), 100);
        Assert.Equal(300, bracket);
    }

    /// <summary>
    /// When MaxBracketWidth is null, bracket behaves identically to the v1 formula
    /// (Math.Min(raw, BracketEnd)).
    /// </summary>
    [Fact]
    public void Bracket_NullMaxBracketWidth_IsUnchangedFromV1()
    {
        var cfg = CfgWithGuardrails(maxBracketWidth: null);
        // At t=40s, raw = BracketEnd = 500. With no cap, returns 500.
        Assert.Equal(500d, EloRangeMatchmakingStrategy.Bracket(cfg, 40));
        // At t=0s, raw = BracketStart = 100.
        Assert.Equal(100d, EloRangeMatchmakingStrategy.Bracket(cfg, 0));
    }

    /// <summary>
    /// MaxBracketWidth that is larger than BracketEnd has no effect — BracketEnd still wins.
    /// </summary>
    [Fact]
    public void Bracket_MaxBracketWidth_LargerThanBracketEnd_NoEffect()
    {
        var cfg = CfgWithGuardrails(maxBracketWidth: 800); // 800 > BracketEnd=500
        Assert.Equal(500d, EloRangeMatchmakingStrategy.Bracket(cfg, 100));
    }

    // -----------------------------------------------------------------------
    // Pool-depth guard tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// When pool depth is below MinPoolDepthBeforeBracketExpansion the candidate bracket stays
    /// at BracketStart — the strategy should NOT match across a wide rating gap that would
    /// only be reachable after bracket expansion.
    /// </summary>
    /// <remarks>
    /// Uses a candidate-EXCLUSIVE pool (matching production semantics: the ticker strips the
    /// candidate from poolScratch before calling Match). pool.Count is the count of OTHER parties.
    /// </remarks>
    [Fact]
    public void Match_HoldsAtBracketStart_WhenPoolBelowMinDepth()
    {
        // MinPoolDepth = 5, pool has 1 other party (candidate-exclusive) → pool.Count = 1 < 5.
        // Candidate has been waiting 100s → normally bracket = 500.
        // With depth guard, bracket is forced to BracketStart = 100.
        // Pool entry at rating 1700 → diff = 200 > 100 (BracketStart) → no match.
        var cfg = CfgWithGuardrails(minPoolDepth: 5);
        var strategy = BuildStrategy(cfg);

        var candidate = Party(1500, Now.AddSeconds(-100)); // long wait → large bracket normally
        var poolEntry = Party(1700, Now.AddSeconds(-100)); // diff = 200

        // Candidate-exclusive pool: pool.Count = 1 < 5 → guard fires → no match.
        var result = strategy.Match(candidate, new[] { poolEntry }, Now);
        Assert.Null(result);
    }

    /// <summary>
    /// When pool depth meets or exceeds MinPoolDepthBeforeBracketExpansion, bracket expansion
    /// proceeds normally and a valid match is found.
    /// </summary>
    /// <remarks>
    /// Uses a candidate-EXCLUSIVE pool (matching production semantics: the ticker strips the
    /// candidate from poolScratch before calling Match). pool.Count is the count of OTHER parties.
    /// </remarks>
    [Fact]
    public void Match_ExpandsBracket_WhenPoolMeetsMinDepth()
    {
        // MinPoolDepth = 2. Candidate-exclusive pool has 2 others → pool.Count = 2 ≥ 2.
        // Candidate waited 100s → bracket = 500. Pool entry at diff = 200 ≤ 500 → should match.
        var cfg = CfgWithGuardrails(minPoolDepth: 2);
        var strategy = BuildStrategy(cfg);

        var candidate = Party(1500, Now.AddSeconds(-100));
        var other1 = Party(1400, Now.AddSeconds(-100)); // diff = 100, within bracket even at start
        var other2 = Party(1700, Now.AddSeconds(-100)); // diff = 200, only reachable after expansion

        // Candidate-exclusive pool: pool.Count = 2 ≥ MinPoolDepth=2 → guard does NOT fire.
        // The strategy will try to match candidate against the others (oldest-first order).
        var result = strategy.Match(candidate, new[] { other1, other2 }, Now);
        Assert.NotNull(result);
    }

    /// <summary>
    /// When MinPoolDepthBeforeBracketExpansion is null there is no guard — v1 behaviour unchanged.
    /// </summary>
    [Fact]
    public void Match_NoDepthGuard_WhenMinPoolDepthIsNull()
    {
        var cfg = CfgWithGuardrails(minPoolDepth: null);
        var strategy = BuildStrategy(cfg);

        // Candidate waited 100s → bracket = 500; pool entry at diff = 400 ≤ 500 → match.
        var candidate = Party(1500, Now.AddSeconds(-100));
        var poolEntry = Party(1900, Now.AddSeconds(-100)); // diff = 400

        var result = strategy.Match(candidate, new[] { poolEntry }, Now);
        Assert.NotNull(result);
    }

    // -----------------------------------------------------------------------
    // Builder validation tests
    // -----------------------------------------------------------------------

    /// <summary>MaxBracketWidth=0 must throw ArgumentException at AddLadder time.</summary>
    [Fact]
    public void AddLadder_Throws_WhenMaxBracketWidth_IsZero()
    {
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddLadder("test", cfg => cfg.MaxBracketWidth = 0));
        Assert.Contains("MaxBracketWidth", ex.Message);
        Assert.Contains("null", ex.Message);
    }

    /// <summary>MaxBracketWidth=-1 must throw ArgumentException at AddLadder time.</summary>
    [Fact]
    public void AddLadder_Throws_WhenMaxBracketWidth_IsNegative()
    {
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddLadder("test2", cfg => cfg.MaxBracketWidth = -1));
        Assert.Contains("MaxBracketWidth", ex.Message);
    }

    /// <summary>MaxBracketWidth=null must be accepted at AddLadder time.</summary>
    [Fact]
    public void AddLadder_Accepts_NullMaxBracketWidth()
    {
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        // Should not throw.
        builder.AddLadder("test3", cfg => cfg.MaxBracketWidth = null);
        Assert.Single(builder.RegisteredLadders);
    }

    /// <summary>MaxBracketWidth=200 (positive and >= BracketStart=100) must be accepted.</summary>
    [Fact]
    public void AddLadder_Accepts_PositiveMaxBracketWidth_AtLeastBracketStart()
    {
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        // BracketStart defaults to 100; MaxBracketWidth=200 >= 100 → valid.
        builder.AddLadder("test4", cfg => cfg.MaxBracketWidth = 200);
        Assert.Single(builder.RegisteredLadders);
    }

    /// <summary>
    /// MaxBracketWidth below BracketStart must throw ArgumentException at AddLadder time.
    /// A cap below the initial bracket silently undercuts the BracketStart guarantee (WR-01).
    /// </summary>
    [Fact]
    public void AddLadder_Throws_WhenMaxBracketWidth_BelowBracketStart()
    {
        // BracketStart defaults to 100; MaxBracketWidth=50 < 100 → must throw.
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddLadder("test-wr01", cfg => cfg.MaxBracketWidth = 50));
        Assert.Contains("MaxBracketWidth", ex.Message);
        Assert.Contains("BracketStart", ex.Message);
    }

    /// <summary>
    /// MaxBracketWidth equal to BracketStart must be accepted (boundary: cap == start).
    /// </summary>
    [Fact]
    public void AddLadder_Accepts_MaxBracketWidth_EqualToBracketStart()
    {
        // BracketStart defaults to 100; MaxBracketWidth=100 == 100 → valid (bracket stays flat).
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        builder.AddLadder("test-wr01-eq", cfg => { cfg.BracketStart = 100; cfg.MaxBracketWidth = 100; });
        Assert.Single(builder.RegisteredLadders);
    }

    /// <summary>MinPoolDepthBeforeBracketExpansion=0 must throw ArgumentException at AddLadder time.</summary>
    [Fact]
    public void AddLadder_Throws_WhenMinPoolDepth_IsZero()
    {
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddLadder("test5", cfg => cfg.MinPoolDepthBeforeBracketExpansion = 0));
        Assert.Contains("MinPoolDepthBeforeBracketExpansion", ex.Message);
        Assert.Contains("null", ex.Message);
    }

    /// <summary>MinPoolDepthBeforeBracketExpansion=-5 must throw ArgumentException at AddLadder time.</summary>
    [Fact]
    public void AddLadder_Throws_WhenMinPoolDepth_IsNegative()
    {
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        var ex = Assert.Throws<ArgumentException>(() =>
            builder.AddLadder("test6", cfg => cfg.MinPoolDepthBeforeBracketExpansion = -5));
        Assert.Contains("MinPoolDepthBeforeBracketExpansion", ex.Message);
    }

    /// <summary>MinPoolDepthBeforeBracketExpansion=null must be accepted.</summary>
    [Fact]
    public void AddLadder_Accepts_NullMinPoolDepth()
    {
        var builder = new GameKitMatchmakingBuilder(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
        builder.AddLadder("test7", cfg => cfg.MinPoolDepthBeforeBracketExpansion = null);
        Assert.Single(builder.RegisteredLadders);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
