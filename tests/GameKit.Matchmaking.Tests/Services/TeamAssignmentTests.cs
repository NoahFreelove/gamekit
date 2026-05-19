// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using GameKit.Matchmaking.Services;
using GameKit.Matchmaking.Strategy;
using Xunit;

namespace GameKit.Matchmaking.Tests.Services;

/// <summary>
/// Unit tests for <see cref="TeamAssignmentService"/> — v1 random team assignment with
/// party cohesion preserved (CONTEXT §Claude's Discretion).
/// </summary>
/// <remarks>
/// The v1 algorithm: per-party coin flip (all members of a single party land on the same
/// team), then balance is enforced by post-flip swap if both flips landed on the same
/// side. The result is documented in the service XML doc. Tests pin behaviours, not the
/// RNG output — repeated runs are required for the "random" assertions.
/// </remarks>
public sealed class TeamAssignmentTests
{
    [Fact]
    public void OneVsOne_Two_Single_Player_Parties_Assigns_Distinct_Teams()
    {
        var p1 = NewParty(1, "p1");
        var p2 = NewParty(1, "p2");

        var svc = new TeamAssignmentService();
        var assignments = svc.AssignTeams(new[] { p1, p2 });

        var distinctTeams = assignments.Values.Distinct().ToList();
        Assert.Equal(2, distinctTeams.Count);
        Assert.Contains(0, distinctTeams);
        Assert.Contains(1, distinctTeams);
    }

    [Fact]
    public void TwoVsTwo_Assigns_Two_Players_Per_Team_With_Party_Cohesion()
    {
        var pA = NewParty(2, "pa");
        var pB = NewParty(2, "pb");

        var svc = new TeamAssignmentService();
        var assignments = svc.AssignTeams(new[] { pA, pB });

        Assert.Equal(4, assignments.Count);

        // Party cohesion: every member of pA shares a team; every member of pB shares a team.
        var pATeams = pA.Members.Select(m => assignments[m.PlayerId]).Distinct().ToList();
        var pBTeams = pB.Members.Select(m => assignments[m.PlayerId]).Distinct().ToList();
        Assert.Single(pATeams);
        Assert.Single(pBTeams);

        // The two parties are on opposite teams.
        Assert.NotEqual(pATeams[0], pBTeams[0]);

        // Both teams are populated (2 vs 2).
        var teamSizes = assignments.Values.GroupBy(t => t).Select(g => g.Count()).OrderBy(c => c).ToList();
        Assert.Equal(new[] { 2, 2 }, teamSizes);
    }

    [Fact]
    public void ThreeVsThree_Three_OnePlayer_Parties_Per_Side_Balances_Teams()
    {
        // 6 single-player parties split evenly: 3 go to team 0, 3 go to team 1.
        var parties = Enumerable.Range(0, 6).Select(i => NewParty(1, $"p{i}")).ToList();

        var svc = new TeamAssignmentService();
        var assignments = svc.AssignTeams(parties);

        Assert.Equal(6, assignments.Count);
        var team0Count = assignments.Values.Count(t => t == 0);
        var team1Count = assignments.Values.Count(t => t == 1);
        Assert.Equal(3, team0Count);
        Assert.Equal(3, team1Count);
    }

    [Fact]
    public void AssignTeams_Preserves_Party_Cohesion_Across_Many_Runs()
    {
        // The algorithm MUST preserve party cohesion every time — independent of the RNG draw.
        // Run 100 iterations and assert party cohesion every single run.
        var svc = new TeamAssignmentService();
        for (var i = 0; i < 100; i++)
        {
            var pA = NewParty(2, "pa");
            var pB = NewParty(2, "pb");
            var assignments = svc.AssignTeams(new[] { pA, pB });

            var pATeams = pA.Members.Select(m => assignments[m.PlayerId]).Distinct().Count();
            var pBTeams = pB.Members.Select(m => assignments[m.PlayerId]).Distinct().Count();

            Assert.Equal(1, pATeams);
            Assert.Equal(1, pBTeams);
        }
    }

    [Fact]
    public void AssignTeams_OnEmptyInput_ReturnsEmpty()
    {
        var svc = new TeamAssignmentService();
        var assignments = svc.AssignTeams(Array.Empty<QueuedParty>());
        Assert.Empty(assignments);
    }

    private static QueuedParty NewParty(int memberCount, string poolName)
    {
        var members = Enumerable.Range(0, memberCount)
            .Select(_ => new QueuedPartyMember(Guid.NewGuid(), Rating: 1500, RatingDeviation: 350, Volatility: 0.06))
            .ToList();
        return new QueuedParty(
            TicketId: Guid.NewGuid(),
            PartyId: memberCount > 1 ? Guid.NewGuid() : null,
            LadderId: Guid.NewGuid(),
            PoolName: poolName,
            Members: members,
            AggregateRating: 1500,
            QueuedAt: DateTimeOffset.UtcNow);
    }
}
