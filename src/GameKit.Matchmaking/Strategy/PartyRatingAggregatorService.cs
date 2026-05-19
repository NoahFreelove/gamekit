// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;

namespace GameKit.Matchmaking.Strategy;

/// <summary>
/// Pure service that reduces a party's per-member rating snapshots to a single
/// aggregate rating, using the <see cref="PartyRatingAggregator"/> selector configured on
/// the ladder (CONTEXT D-13). Stateless; safe to register as a singleton.
/// </summary>
/// <remarks>
/// <para>
/// Called by the matchmaking enqueue path (Plan 05-08) — the result is cached on the
/// Redis ticket hash (<c>mm:ticket:{id}.aggregateRating</c>) and read back by the
/// strategy via <see cref="QueuedParty.AggregateRating"/>. The matcher does NOT
/// recompute the aggregate per tick (RESEARCH §Decision 5).
/// </para>
/// <para>
/// Algorithms (RESEARCH §Decision 5):
/// <list type="bullet">
/// <item><see cref="PartyRatingAggregator.Mean"/> — <c>members.Average(m =&gt; m.Rating)</c>.</item>
/// <item><see cref="PartyRatingAggregator.Max"/> — <c>members.Max(m =&gt; m.Rating)</c>.</item>
/// <item><see cref="PartyRatingAggregator.GlickoWeighted"/> — <c>Σ rating · (1/RD²) / Σ (1/RD²)</c>. On <c>Σ weights == 0</c> falls back to <see cref="PartyRatingAggregator.Mean"/>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PartyRatingAggregatorService
{
    /// <summary>Compute the aggregate rating for a party of members.</summary>
    /// <param name="mode">The aggregator selector for this ladder (CONTEXT D-13).</param>
    /// <param name="members">Per-member rating snapshots. Must be non-empty.</param>
    /// <returns>The single aggregate rating value the matcher uses for bracket comparison.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="members"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="members"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a known aggregator.</exception>
    public double Compute(PartyRatingAggregator mode, IReadOnlyList<QueuedPartyMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count == 0)
            throw new ArgumentException("Party must have at least one member.", nameof(members));

        return mode switch
        {
            PartyRatingAggregator.Mean => members.Average(m => m.Rating),
            PartyRatingAggregator.Max => members.Max(m => m.Rating),
            PartyRatingAggregator.GlickoWeighted => GlickoWeighted(members),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown PartyRatingAggregator."),
        };
    }

    private static double GlickoWeighted(IReadOnlyList<QueuedPartyMember> members)
    {
        double sumWeightedRating = 0;
        double sumWeights = 0;
        foreach (var m in members)
        {
            // Defensive: a zero RD would cause divide-by-zero. Skip; if all members have
            // zero RD we fall back to arithmetic mean below.
            if (m.RatingDeviation <= 0)
                continue;

            var weight = 1.0 / (m.RatingDeviation * m.RatingDeviation);
            sumWeightedRating += weight * m.Rating;
            sumWeights += weight;
        }

        // Fall back to arithmetic mean if no valid weights (every member had zero/negative RD).
        return sumWeights > 0
            ? sumWeightedRating / sumWeights
            : members.Average(m => m.Rating);
    }
}
