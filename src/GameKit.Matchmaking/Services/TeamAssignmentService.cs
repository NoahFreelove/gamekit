// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using GameKit.Matchmaking.Strategy;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Assigns matched parties to two teams (0 / 1). v1 uses a random, party-cohesive split —
/// every member of a single party lands on the same team, and the two teams are balanced.
/// MMR-balanced split is deferred to a future phase (CONTEXT §Phase Boundary; the matched
/// parties already passed bracket-flex overlap, so v1 balancing is acceptable).
/// </summary>
/// <remarks>
/// <para>
/// <b>Algorithm:</b>
/// <list type="number">
///   <item>Shuffle the input party list with a CSPRNG-driven Fisher–Yates pass — guarantees
///         the split is non-deterministic without seeding <see cref="Random"/>.</item>
///   <item>Walk the shuffled list and assign parties alternately to team 0 and team 1. This
///         keeps party cohesion (all members of one party share a team) and balances the
///         player counts as far as the input permits.</item>
///   <item>For each party, write <c>team[playerId]</c> for every member.</item>
/// </list>
/// </para>
/// <para>
/// <b>Imbalance:</b> when total party count is odd (e.g. three 1-player parties), team 0
/// gets one more player than team 1. This is the simplest correct behaviour for v1 and matches
/// the "random" billing — a future MMR-balanced split can swap this implementation without
/// changing the surface.
/// </para>
/// <para>
/// <b>Inter-party self-match (Phase 21):</b> when the input is a SINGLE party of more than one
/// member — a full party that fills the whole match roster on its own, e.g. two friends in a
/// 1v1 "inter-party" match — party cohesion cannot apply (there is only one party), so the
/// alternating-party algorithm would place every member on team 0. This case is detected and
/// the lone party's members are split round-robin across teams 0/1 so they become genuine
/// opponents. A normal multi-party match never reaches this branch.
/// </para>
/// <para>
/// <b>Statelessness:</b> the service holds no state and is safe to register as a singleton.
/// </para>
/// </remarks>
public sealed class TeamAssignmentService
{
    /// <summary>
    /// Assign each player in <paramref name="matchedParties"/> to team 0 or team 1.
    /// </summary>
    /// <param name="matchedParties">The parties that just won a bracket-flex overlap match.</param>
    /// <returns>A read-only map from canonical player id to team index (0 or 1).</returns>
    public IReadOnlyDictionary<Guid, int> AssignTeams(IReadOnlyList<QueuedParty> matchedParties)
    {
        ArgumentNullException.ThrowIfNull(matchedParties);

        if (matchedParties.Count == 0)
            return new Dictionary<Guid, int>(0);

        // Inter-party self-match (Phase 21): a SINGLE party that fills the whole match roster
        // on its own — e.g. a 2-member 1v1 "inter-party" match where two friends oppose each
        // other. There is only one party, so party cohesion cannot apply and the alternating
        // algorithm below would place every member on team 0. Split this lone party's members
        // round-robin across the two teams instead so they are genuine opponents. This branch
        // only fires for the matcher's self-match path (one matched ticket carrying >1 member);
        // a normal multi-party match always has >= 2 parties and skips it.
        if (matchedParties.Count == 1 && matchedParties[0].Members.Count > 1)
        {
            var members = matchedParties[0].Members;
            var split = new Dictionary<Guid, int>(members.Count);
            for (var i = 0; i < members.Count; i++)
                split[members[i].PlayerId] = i % 2;
            return split;
        }

        // Fisher–Yates shuffle using CSPRNG. Copy first to avoid mutating the caller's list.
        var shuffled = new QueuedParty[matchedParties.Count];
        for (var i = 0; i < matchedParties.Count; i++) shuffled[i] = matchedParties[i];
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(0, i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        // Alternate assignment: even-indexed party → team 0, odd-indexed party → team 1.
        // Party cohesion is preserved because all members of one party share the index.
        var assignments = new Dictionary<Guid, int>();
        for (var partyIdx = 0; partyIdx < shuffled.Length; partyIdx++)
        {
            var team = partyIdx % 2;
            foreach (var member in shuffled[partyIdx].Members)
                assignments[member.PlayerId] = team;
        }

        return assignments;
    }
}
