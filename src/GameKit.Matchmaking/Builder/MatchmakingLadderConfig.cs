// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Matchmaking.Strategy;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Build-time matchmaking configuration for a single named ladder. Registered via
/// <c>services.AddGameKit().AddMatchmaking().AddLadder("name", cfg =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Name"/> is the JOIN KEY against the Rankings-owned ladder of the same name.
/// A consumer who calls <c>AddRankings().AddLadder("main", ...)</c> followed by
/// <c>AddMatchmaking().AddLadder("main", ...)</c> configures both surfaces for the same
/// logical ladder; case-insensitive dedup at both builder layers enforces the convention.
/// </para>
/// <para>
/// Defaults sourced from CONTEXT D-11 (bracket curve), D-12 (per-ladder configurable
/// curve), D-13 (party-rating aggregator), and D-14 (max-party-rating-spread cap).
/// </para>
/// </remarks>
public sealed class MatchmakingLadderConfig
{
    /// <summary>
    /// Ladder name — case-insensitive JOIN KEY against the Rankings-owned ladder. Must be
    /// non-empty and unique within a single matchmaking builder.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Starting bracket half-width in rating points. Default <c>100</c> per CONTEXT D-11
    /// (linear ramp 100 → 500 over 40s).
    /// </summary>
    public int BracketStart { get; set; } = 100;

    /// <summary>
    /// Cap bracket half-width in rating points. Default <c>500</c> per CONTEXT D-11.
    /// Must be &gt;= <see cref="BracketStart"/>.
    /// </summary>
    public int BracketEnd { get; set; } = 500;

    /// <summary>
    /// Seconds over which the bracket ramps linearly from
    /// <see cref="BracketStart"/> to <see cref="BracketEnd"/>. Default <c>40</c> seconds
    /// per CONTEXT D-11. Must be &gt; 0.
    /// </summary>
    public int BracketRampSeconds { get; set; } = 40;

    /// <summary>
    /// Strategy for aggregating party-member ratings into a single party rating. Default
    /// <see cref="PartyRatingAggregator.Mean"/> per CONTEXT D-13.
    /// </summary>
    public PartyRatingAggregator PartyRatingAggregator { get; set; } = PartyRatingAggregator.Mean;

    /// <summary>
    /// Optional cap on within-party rating spread (<c>max - min</c>). Parties exceeding this
    /// cap are rejected at enqueue with HTTP 400 <c>PartyRatingSpreadExceeded</c>.
    /// Default <c>null</c> (no cap) per CONTEXT D-14. When set, must be &gt; 0.
    /// </summary>
    public int? MaxPartyRatingSpread { get; set; }

    /// <summary>
    /// Hard cap on bracket half-width in rating points (MATCH-17). Bracket-widening NEVER exceeds
    /// this value regardless of wait time, preventing high-RD new players from being matched against
    /// top-rated players on sparse pools. Default <c>null</c> (no cap — maintains v1 behaviour).
    /// When set, must be &gt; 0 and &gt;= <see cref="BracketStart"/>. A value below
    /// <see cref="BracketStart"/> would silently undercut the initial bracket guarantee — the
    /// builder rejects this configuration with an <see cref="ArgumentException"/> at
    /// <c>AddLadder</c> time.
    /// </summary>
    public int? MaxBracketWidth { get; set; }

    /// <summary>
    /// Minimum number of tickets in the pool before bracket expansion begins (MATCH-17).
    /// When the pool has fewer than this many candidates, the bracket stays at
    /// <see cref="BracketStart"/> regardless of wait time. Default <c>null</c> (no guard).
    /// Set to <c>2 * expected_party_size</c> as a starting recommendation. When set, must be &gt; 0.
    /// </summary>
    public int? MinPoolDepthBeforeBracketExpansion { get; set; }
}
