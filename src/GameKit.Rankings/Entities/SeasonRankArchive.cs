// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Rankings.Entities;

/// <summary>
/// Point-in-time snapshot of a player's rank at the close of a season (D-13).
/// Used to answer archived-season leaderboard queries (<c>TopAsync</c> / <c>AroundAsync</c>
/// scoped to a specific <c>SeasonId</c>) without mutating <c>player_ranks</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>PlayerId</c> is NULLABLE for GDPR cascade: when a player exercises hard-delete
/// (erasure request), the FK is set to NULL and the archive row survives as an anonymous
/// data point. This matches the same pattern on <c>SessionParticipant.PlayerId</c>.
/// </para>
/// <para>
/// All three rating columns are <c>double precision</c> (RANK-03 / SC#3), matching
/// <c>player_ranks</c> and the <c>session_participants</c> rating snapshot columns.
/// </para>
/// </remarks>
public sealed class SeasonRankArchive
{
    /// <summary>Row id — UUIDv7 from <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <see cref="Ladder"/>. On DELETE SET NULL is NOT applied here — ladders cannot be deleted while archive rows reference them.</summary>
    public Guid LadderId { get; set; }

    /// <summary>FK → <see cref="LadderSeason"/> — the season this snapshot closes.</summary>
    public Guid SeasonId { get; set; }

    /// <summary>
    /// FK → <c>players.id</c> (ON DELETE SET NULL per GDPR cascade, D-13).
    /// Null after the player exercises hard-delete — the archive row is retained anonymously.
    /// </summary>
    public Guid? PlayerId { get; set; }

    /// <summary>Rating at season close. Stored as <c>double precision</c> (RANK-03).</summary>
    public double Rating { get; set; }

    /// <summary>Rating deviation at season close. Stored as <c>double precision</c> (RANK-03).</summary>
    public double RatingDeviation { get; set; }

    /// <summary>Volatility at season close. Stored as <c>double precision</c> (RANK-03).</summary>
    public double Volatility { get; set; }

    /// <summary>Total wins at season close.</summary>
    public int Wins { get; set; }

    /// <summary>Total losses at season close.</summary>
    public int Losses { get; set; }

    /// <summary>Total draws at season close.</summary>
    public int Draws { get; set; }

    /// <summary>UTC timestamp at which this archive row was written.</summary>
    public DateTimeOffset ArchivedAt { get; set; }
}
