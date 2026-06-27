// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
using Platformer3D.Strategy;
using Xunit;

namespace GameKit.Platformer3D.Tests.Strategy;

/// <summary>
/// Unit tests for <c>BestTimeMatchmakingStrategy</c> — the custom
/// <see cref="GameKit.Matchmaking.Strategy.IMatchmakingStrategy"/> for the
/// Platformer3D demo ladder (D-06/D-07/D-08).
/// </summary>
public sealed class BestTimeMatchmakingStrategyTests
{
    private static readonly DateTimeOffset BaseNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Makes a single-member QueuedParty at the given aggregate rating and queue time.</summary>
    private static QueuedParty MakeParty(
        double aggregateRating,
        double ratingDeviation = 50.0,
        double secondsInQueue = 0.0,
        string poolName = "platformer")
    {
        var queuedAt = BaseNow - TimeSpan.FromSeconds(secondsInQueue);
        return new QueuedParty(
            TicketId: Guid.NewGuid(),
            PartyId: null,
            LadderId: Guid.NewGuid(),
            PoolName: poolName,
            Members: new[]
            {
                new QueuedPartyMember(Guid.NewGuid(), aggregateRating, ratingDeviation, 0.06),
            },
            AggregateRating: aggregateRating,
            QueuedAt: queuedAt);
    }

    /// <summary>Makes a cold-start party (RatingDeviation >= 300).</summary>
    private static QueuedParty MakeColdStartParty(double aggregateRating = 1500.0, double secondsInQueue = 0.0)
        => MakeParty(aggregateRating, ratingDeviation: 350.0, secondsInQueue: secondsInQueue);

    /// <summary>
    /// Makes a multi-member party (an "inter-party" group queued as one ticket). Used to
    /// exercise the Phase 21 full-party self-match path.
    /// </summary>
    private static QueuedParty MakeFullParty(
        int memberCount = BestTimeMatchmakingStrategy.MatchPlayerCount,
        double aggregateRating = 1500.0,
        double ratingDeviation = 50.0,
        double secondsInQueue = 0.0,
        string poolName = "platformer")
    {
        var queuedAt = BaseNow - TimeSpan.FromSeconds(secondsInQueue);
        var members = new List<QueuedPartyMember>(memberCount);
        for (var i = 0; i < memberCount; i++)
            members.Add(new QueuedPartyMember(Guid.NewGuid(), aggregateRating, ratingDeviation, 0.06));
        return new QueuedParty(
            TicketId: Guid.NewGuid(),
            PartyId: Guid.NewGuid(),
            LadderId: Guid.NewGuid(),
            PoolName: poolName,
            Members: members,
            AggregateRating: aggregateRating,
            QueuedAt: queuedAt);
    }

    private static IReadOnlyList<MatchmakingLadderConfig> MakeSingleLadder() =>
        new[]
        {
            new MatchmakingLadderConfig { Name = "platformer" },
        };

    // ─── R5 / D-07: Name discriminator ───────────────────────────────────────

    /// <summary>
    /// DI resolution check: strategy Name is "best-time", not "elo-range" (R5/D-07).
    /// </summary>
    [Fact]
    public void BestTimeMatchmakingStrategyResolutionTests()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        Assert.Equal("best-time", strategy.Name);
        Assert.NotEqual("elo-range", strategy.Name);
        // Must be the custom type, not the built-in EloRange
        Assert.IsType<BestTimeMatchmakingStrategy>(strategy);
    }

    // ─── Match logic ──────────────────────────────────────────────────────────

    /// <summary>
    /// In-window match: two parties with |AggregateRating| difference within the initial
    /// window (5000ms ≡ 5000 rating points for t=0) form a match.
    /// The strategy uses AggregateRating for proximity comparison, and the window starts at
    /// BestTimeMatchmakingStrategy.InitialBracketMs (5000).
    /// </summary>
    [Fact]
    public void BestTimeMatchmakingStrategyMatchTests()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var candidate = MakeParty(aggregateRating: 1500.0, secondsInQueue: 0.0);
        var close = MakeParty(aggregateRating: 1502.0, secondsInQueue: 0.0); // diff=2 << 5000

        var result = strategy.Match(candidate, new[] { close }, BaseNow);

        Assert.NotNull(result);
    }

    /// <summary>
    /// Out-of-window match: two parties whose |AggregateRating| difference exceeds the
    /// current bracket (initial = 5000ms, and AggregateRating difference here is huge)
    /// return null — no match this tick.
    /// </summary>
    [Fact]
    public void OutOfWindow_ReturnsNull()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        // Difference = 100_000, initial bracket = 5_000
        var candidate = MakeParty(aggregateRating: 1_000.0, secondsInQueue: 0.0);
        var farAway = MakeParty(aggregateRating: 101_000.0, secondsInQueue: 0.0);

        var result = strategy.Match(candidate, new[] { farAway }, BaseNow);

        Assert.Null(result);
    }

    /// <summary>
    /// Queue-time widening (D-06): a pair that does not match at t=0 matches after
    /// the ramp window elapses. InitialBracketMs=5000, MaxBracketMs=30000, RampSeconds=60.
    /// After 60s, the window is MaxBracketMs=30000.
    /// </summary>
    [Fact]
    public void QueueTimeWidening_EventuallyMatches()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        // diff = 20_000 — out of initial 5_000 bracket but within max 30_000 bracket
        var candidate = MakeParty(aggregateRating: 1_000.0, secondsInQueue: 0.0);
        // Pool entry has also been waiting 70s (> ramp) → full max bracket of 30_000
        var poolEntry = MakeParty(aggregateRating: 21_000.0, secondsInQueue: 70.0);

        // At t=0 for candidate → window = 5_000 → diff 20_000 > window → no match
        var resultT0 = strategy.Match(candidate, new[] { poolEntry }, BaseNow);
        Assert.Null(resultT0);

        // At 70s for candidate → window = 30_000 → diff 20_000 < window → match
        var candidateLongWait = MakeParty(aggregateRating: 1_000.0, secondsInQueue: 70.0);
        var resultT70 = strategy.Match(candidateLongWait, new[] { poolEntry }, BaseNow);
        Assert.NotNull(resultT70);
    }

    /// <summary>
    /// Cold-start (D-08): a party whose members all have RatingDeviation >= 300 uses the
    /// neutral 60_000ms bracket and matches anyone (even a very different AggregateRating),
    /// including a non-cold-start opponent within neutral range.
    /// </summary>
    [Fact]
    public void ColdStart_NeutralBracket_MatchesAnyone()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        // Cold-start party (RD=350 >= 300)
        var coldCandidate = MakeColdStartParty(aggregateRating: 1500.0, secondsInQueue: 0.0);
        // Non-cold-start opponent with a huge AggregateRating difference — within 60_000 neutral window
        var opponent = MakeParty(aggregateRating: 50_000.0, ratingDeviation: 50.0, secondsInQueue: 0.0);

        var result = strategy.Match(coldCandidate, new[] { opponent }, BaseNow);

        Assert.NotNull(result);
    }

    /// <summary>
    /// Cold-start symmetric: if the pool entry is also cold-start, they still match
    /// via the symmetric conjunctive overlap of both neutral brackets.
    /// </summary>
    [Fact]
    public void ColdStart_BothCold_StillMatch()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var coldCandidate = MakeColdStartParty(aggregateRating: 1500.0);
        var coldOpponent = MakeColdStartParty(aggregateRating: 2000.0);

        var result = strategy.Match(coldCandidate, new[] { coldOpponent }, BaseNow);

        Assert.NotNull(result);
    }

    /// <summary>
    /// A ticket never matches itself (same TicketId must be skipped).
    /// </summary>
    [Fact]
    public void SelfMatch_IsNotAllowed()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var candidate = MakeParty(aggregateRating: 1500.0, secondsInQueue: 70.0);
        // Pool contains only the candidate itself
        var pool = new[] { candidate };

        var result = strategy.Match(candidate, pool, BaseNow);

        Assert.Null(result);
    }

    /// <summary>
    /// Symmetric conjunctive overlap: both candidate and pool entry brackets must
    /// contain the difference. If only the candidate's bracket is wide enough (due to
    /// long wait) but the pool entry is still on initial bracket and the difference
    /// exceeds the pool entry's bracket → no match.
    /// </summary>
    [Fact]
    public void SymmetricConjunctive_BothBracketsMustFit()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        // candidate waited 70s → bracket = max 30_000
        var candidate = MakeParty(aggregateRating: 1_000.0, secondsInQueue: 70.0);
        // poolEntry just joined at t=0 → bracket = initial 5_000; diff=20_000 > 5_000
        var freshPoolEntry = MakeParty(aggregateRating: 21_000.0, secondsInQueue: 0.0);

        // candidate bracket: 30_000 >= 20_000 ✓
        // poolEntry bracket: 5_000 < 20_000 ✗ → no match (conjunctive)
        var result = strategy.Match(candidate, new[] { freshPoolEntry }, BaseNow);

        Assert.Null(result);
    }

    /// <summary>
    /// Empty pool returns null (no candidates to match).
    /// </summary>
    [Fact]
    public void EmptyPool_ReturnsNull()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var candidate = MakeParty(1500.0);

        var result = strategy.Match(candidate, Array.Empty<QueuedParty>(), BaseNow);

        Assert.Null(result);
    }

    /// <summary>
    /// Statelessness: repeated identical Match calls return equivalent results
    /// (no mutable instance fields).
    /// </summary>
    [Fact]
    public void Stateless_RepeatedCalls_EquivalentResults()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var candidate = MakeParty(1500.0, secondsInQueue: 0.0);
        var close = MakeParty(1501.0, secondsInQueue: 0.0);
        var pool = new[] { close };

        var result1 = strategy.Match(candidate, pool, BaseNow);
        var result2 = strategy.Match(candidate, pool, BaseNow);

        // Both must produce a non-null match (or both null — but here close is in window)
        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }

    /// <summary>
    /// Match result contains both matched tickets.
    /// </summary>
    [Fact]
    public void MatchResult_ContainsBothTickets()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var candidate = MakeParty(1500.0);
        var poolEntry = MakeParty(1501.0);

        var result = strategy.Match(candidate, new[] { poolEntry }, BaseNow);

        Assert.NotNull(result);
        Assert.Contains(candidate, result!.MatchedTickets);
        Assert.Contains(poolEntry, result.MatchedTickets);
    }

    // ─── Phase 21: inter-party 1v1 (full-party self-match) ────────────────────

    /// <summary>
    /// A full 2-member party (two friends queued together) forms an inter-party 1v1 from its
    /// OWN members with an empty pool — a single matched ticket, members split across teams 0/1.
    /// </summary>
    [Fact]
    public void FullTwoMemberParty_SelfMatches_OnEmptyPool()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var party = MakeFullParty(); // 2 members

        var result = strategy.Match(party, Array.Empty<QueuedParty>(), BaseNow);

        Assert.NotNull(result);
        Assert.Single(result!.MatchedTickets);
        Assert.Same(party, result.MatchedTickets[0]);
        // The two members land on opposing teams.
        var teams = result.TeamAssignments.Values.Distinct().OrderBy(t => t).ToList();
        Assert.Equal(new[] { 0, 1 }, teams);
    }

    /// <summary>
    /// A full party self-matches immediately even at t=0 and regardless of pool contents — it
    /// already holds the whole roster, so it never waits for or pairs with strangers.
    /// </summary>
    [Fact]
    public void FullTwoMemberParty_SelfMatches_IgnoringPool()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var party = MakeFullParty(secondsInQueue: 0.0);
        var unrelatedSolo = MakeParty(1500.0);

        var result = strategy.Match(party, new[] { unrelatedSolo }, BaseNow);

        Assert.NotNull(result);
        Assert.Single(result!.MatchedTickets);
        Assert.Same(party, result.MatchedTickets[0]);
    }

    /// <summary>
    /// Roster-fit guard: a single queued player (partial party) must NOT pair with a full
    /// 2-member party — that would overflow the 1v1 roster into a lop-sided 1v2. Returns null.
    /// </summary>
    [Fact]
    public void SoloCandidate_DoesNotPairWithFullParty()
    {
        var strategy = new BestTimeMatchmakingStrategy(MakeSingleLadder());
        var solo = MakeParty(1500.0);            // 1 member
        var fullParty = MakeFullParty(aggregateRating: 1500.0); // 2 members, same rating

        var result = strategy.Match(solo, new[] { fullParty }, BaseNow);

        Assert.Null(result);
    }
}
