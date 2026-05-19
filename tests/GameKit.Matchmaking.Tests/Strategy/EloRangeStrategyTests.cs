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
/// Unit tests for <see cref="EloRangeMatchmakingStrategy"/> — bracket overlap (symmetric
/// conjunctive form per RESEARCH §Decision 4), oldest-waiter-first picking (Pitfall §6),
/// and MaxPartyRatingSpread defense-in-depth (CONTEXT D-14).
/// </summary>
public sealed class EloRangeStrategyTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 17, 0, 0, 0, TimeSpan.Zero);

    private static MatchmakingLadderConfig DefaultCfg(int? spreadCap = null) => new()
    {
        Name = "main",
        BracketStart = 100,
        BracketEnd = 500,
        BracketRampSeconds = 40,
        MaxPartyRatingSpread = spreadCap,
    };

    private static EloRangeMatchmakingStrategy BuildStrategy(MatchmakingLadderConfig cfg) =>
        new(new[] { cfg }, new PartyRatingAggregatorService(), new FixedClock(Now));

    private static QueuedParty Party(
        double rating,
        DateTimeOffset queuedAt,
        Guid? ticketId = null,
        double rd = 200,
        double[]? memberSpread = null)
    {
        var members = new List<QueuedPartyMember>();
        if (memberSpread is { Length: > 0 })
        {
            foreach (var r in memberSpread)
                members.Add(new QueuedPartyMember(Guid.NewGuid(), r, rd, 0.06));
        }
        else
        {
            members.Add(new QueuedPartyMember(Guid.NewGuid(), rating, rd, 0.06));
        }

        return new QueuedParty(
            TicketId: ticketId ?? Guid.NewGuid(),
            PartyId: null,
            LadderId: Guid.NewGuid(),
            PoolName: "main",
            Members: members,
            AggregateRating: rating,
            QueuedAt: queuedAt);
    }

    [Fact]
    public void Match_Returns_Null_When_Pool_Empty()
    {
        var strategy = BuildStrategy(DefaultCfg());
        var candidate = Party(1500, Now);
        var result = strategy.Match(candidate, Array.Empty<QueuedParty>(), Now);
        Assert.Null(result);
    }

    [Fact]
    public void Match_Returns_Result_When_Brackets_Overlap()
    {
        // candidate: rating 1500, queued 5s ago → bracket = 100 + 400 * 5/40 = 150
        // pool entry: rating 1620, queued 5s ago → bracket = 150
        // diff = 120 ≤ 150 (both) → match.
        var strategy = BuildStrategy(DefaultCfg());
        var candidate = Party(1500, Now.AddSeconds(-5));
        var poolEntry = Party(1620, Now.AddSeconds(-5));
        var result = strategy.Match(candidate, new[] { poolEntry }, Now);
        Assert.NotNull(result);
        Assert.Equal(2, result!.MatchedTickets.Count);
        // Candidate is at index 0 per the IMatchmakingStrategy convention.
        Assert.Equal(candidate.TicketId, result.MatchedTickets[0].TicketId);
        Assert.Equal(poolEntry.TicketId, result.MatchedTickets[1].TicketId);
        // Every player has a team assignment.
        Assert.Equal(2, result.TeamAssignments.Count);
    }

    [Fact]
    public void Match_Returns_Null_When_Brackets_DoNotOverlap_Symmetric()
    {
        // candidate: rating 1000, queued 0s ago → bracket = 100
        // pool entry: rating 1300, queued 40s ago → bracket = 500
        // diff = 300; |diff| ≤ 500 (pool bracket) BUT |diff| > 100 (candidate bracket).
        // Symmetric (conjunctive) rule: BOTH must contain the diff → no match.
        var strategy = BuildStrategy(DefaultCfg());
        var candidate = Party(1000, Now);
        var poolEntry = Party(1300, Now.AddSeconds(-40));
        var result = strategy.Match(candidate, new[] { poolEntry }, Now);
        Assert.Null(result);
    }

    [Fact]
    public void Match_Prefers_Oldest_Waiter_From_Pool()
    {
        // Candidate rating 1500, queued 30s ago → bracket = 100 + 400 * 30/40 = 400.
        // Two pool entries both in bracket; older one MUST be picked.
        var strategy = BuildStrategy(DefaultCfg());
        var candidate = Party(1500, Now.AddSeconds(-30));
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var older = Party(1550, Now.AddSeconds(-60), ticketId: oldId); // queued 60s ago, very old
        var newer = Party(1550, Now.AddSeconds(-5), ticketId: newId);  // queued 5s ago

        // Pool order intentionally REVERSED to verify the strategy re-sorts by QueuedAt.
        var result = strategy.Match(candidate, new[] { newer, older }, Now);
        Assert.NotNull(result);
        Assert.Equal(oldId, result!.MatchedTickets[1].TicketId);
    }

    [Fact]
    public void Match_Respects_MaxPartyRatingSpread_On_Candidate()
    {
        // Candidate has 3 members with rating spread 1000..1600 = 600 (> 500 cap).
        var cfg = DefaultCfg(spreadCap: 500);
        var strategy = BuildStrategy(cfg);

        var candidate = Party(1300, Now.AddSeconds(-10), memberSpread: new[] { 1000.0, 1300.0, 1600.0 });
        var poolEntry = Party(1320, Now.AddSeconds(-10));
        var result = strategy.Match(candidate, new[] { poolEntry }, Now);
        Assert.Null(result);
    }

    [Fact]
    public void Match_Skips_Self_If_Present_In_Pool()
    {
        // Defensive: if a caller bug includes the candidate in the pool, the strategy must
        // not match it against itself.
        var strategy = BuildStrategy(DefaultCfg());
        var candidate = Party(1500, Now.AddSeconds(-5));
        var result = strategy.Match(candidate, new[] { candidate }, Now);
        Assert.Null(result);
    }

    [Fact]
    public void Strategy_Name_Is_EloRange()
    {
        var strategy = BuildStrategy(DefaultCfg());
        Assert.Equal("elo-range", strategy.Name);
    }

    [Fact]
    public void Match_Throws_On_Null_Candidate()
    {
        var strategy = BuildStrategy(DefaultCfg());
        Assert.Throws<ArgumentNullException>(() =>
            strategy.Match(null!, Array.Empty<QueuedParty>(), Now));
    }

    [Fact]
    public void Match_Throws_On_Null_Pool()
    {
        var strategy = BuildStrategy(DefaultCfg());
        var candidate = Party(1500, Now);
        Assert.Throws<ArgumentNullException>(() =>
            strategy.Match(candidate, null!, Now));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
