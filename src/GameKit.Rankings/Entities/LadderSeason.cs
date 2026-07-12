// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Entities;

namespace GameKit.Rankings.Entities;

/// <summary>
/// Tracks the timeline of seasons for a ladder. The "current season" is the row with
/// <c>EndedAt IS NULL</c>. Admin-triggered season end (D-11) closes the current row and opens
/// a new one in the same SERIALIZABLE transaction.
/// </summary>
/// <remarks>
/// <c>EndedByAdminId</c> records the admin actor but is NOT a FK — <see cref="Player"/> may
/// have been deleted by the time an audit consumer reads this row, and a FK would prevent that
/// erasure. The admin user's id is stored for audit purposes only.
/// </remarks>
public sealed class LadderSeason
{
    /// <summary>Row id — UUIDv7 from <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <see cref="Ladder"/> (ON DELETE CASCADE).</summary>
    public Guid LadderId { get; set; }

    /// <summary>
    /// Monotonically increasing season counter per ladder (1, 2, 3 …).
    /// Composite unique constraint <c>(ladder_id, season_number)</c> prevents duplicates.
    /// </summary>
    public int SeasonNumber { get; set; }

    /// <summary>UTC timestamp at which this season was opened.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>UTC timestamp at which this season was closed. Null while the season is current.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Id of the admin user who ended this season. Null while the season is current.
    /// Stored for audit traceability — NOT a FK (admin may be deleted independently).
    /// </summary>
    public Guid? EndedByAdminId { get; set; }
}
