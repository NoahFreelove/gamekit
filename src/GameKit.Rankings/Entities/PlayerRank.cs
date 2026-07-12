// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Entities;

namespace GameKit.Rankings.Entities;

/// <summary>
/// Live ranking row for a single player on a single ladder. Lazily created on first match (RANK-07).
/// Updated in batch by <c>RankingsTickerService</c> using the configured <c>IRankingAlgorithm</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Rating</c>, <c>RatingDeviation</c>, and <c>Volatility</c> are stored as
/// <c>double precision</c> (NOT <c>numeric</c>) per RANK-03 / SC#3. EF Core 10 maps <c>double</c>
/// CLR to <c>double precision</c> natively; the configuration additionally calls
/// <c>.HasColumnType("double precision")</c> to make intent explicit and guarantee the schema
/// introspection test passes.
/// </para>
/// <para>
/// The composite unique constraint <c>(player_id, ladder_id)</c> ensures at most one live rank
/// per player per ladder. The index <c>idx_player_ranks_ladder_rating</c> on
/// <c>(ladder_id, rating DESC)</c> backs the leaderboard hot-path (RANK-08 / D-23).
/// </para>
/// </remarks>
public sealed class PlayerRank
{
    /// <summary>Row id — UUIDv7 from <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <see cref="Player"/> (ON DELETE CASCADE). Not nullable — a rank without a player is orphaned.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>FK → <see cref="Ladder"/> (ON DELETE RESTRICT). Cannot delete a ladder that has active ranks.</summary>
    public Guid LadderId { get; set; }

    /// <summary>Current Glicko-2 rating. Stored as <c>double precision</c> (RANK-03).</summary>
    public double Rating { get; set; }

    /// <summary>Current Glicko-2 rating deviation. Stored as <c>double precision</c> (RANK-03).</summary>
    public double RatingDeviation { get; set; }

    /// <summary>Current Glicko-2 volatility. Stored as <c>double precision</c> (RANK-03).</summary>
    public double Volatility { get; set; }

    /// <summary>Total wins on this ladder (incremented per batch drain, not per session-complete).</summary>
    public int Wins { get; set; }

    /// <summary>Total losses on this ladder.</summary>
    public int Losses { get; set; }

    /// <summary>Total draws on this ladder.</summary>
    public int Draws { get; set; }

    /// <summary>UTC timestamp of the player's most recent match on this ladder. Null until first match.</summary>
    public DateTimeOffset? LastMatchAt { get; set; }

    /// <summary>UTC timestamp of the last decay run applied to this rank. Null = never decayed (RANK-15).</summary>
    public DateTimeOffset? LastDecayAt { get; set; }

    /// <summary>Placement matches remaining before visible rank is revealed. 0 = placement complete (RANK-16).</summary>
    public int PlacementMatchesRemaining { get; set; }

    /// <summary>True while the player is still completing placement matches (RANK-16). False once all placement matches are done.</summary>
    public bool IsInPlacement { get; set; }
}
