// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Strategy;

/// <summary>
/// Per-ladder party-rating aggregator selector (CONTEXT D-13). Determines how a party's
/// constituent member ratings are reduced to a single value the matchmaker uses for bracket
/// comparison.
/// </summary>
/// <remarks>
/// <para>
/// Stored as <c>integer</c> (Phase 5 mandatory pattern — no <c>HasConversion&lt;string&gt;()</c>
/// per CONTEXT §Established Patterns). Default is <see cref="Mean"/>.
/// </para>
/// <para>
/// Algorithm details for each aggregator are documented in RESEARCH §Decision 5; the actual
/// implementation lands in Plan 05-04's <c>EloRangeMatchmakingStrategy</c>.
/// </para>
/// </remarks>
public enum PartyRatingAggregator
{
    /// <summary>Simple arithmetic mean of all party members' current rating (default).</summary>
    Mean = 0,

    /// <summary>
    /// Highest rating among party members. Pairs the party against the strongest member —
    /// used to discourage smurfing in low-bracket lobbies.
    /// </summary>
    Max = 1,

    /// <summary>
    /// RD-aware weighted mean (<c>Σ rating · (1/RD²) / Σ (1/RD²)</c>). Down-weights members
    /// with high rating-deviation (new / inactive players); see RESEARCH §Decision 5.
    /// </summary>
    GlickoWeighted = 2,
}
