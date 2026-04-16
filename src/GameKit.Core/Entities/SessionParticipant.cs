// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Core.Entities;

/// <summary>
/// Per-player record within a <see cref="GameSession"/>. Captures team assignment, result, score,
/// and a snapshot of the rating change that occurred at session completion.
/// </summary>
/// <remarks>
/// <see cref="PlayerId"/> is NULLable by design: when a player exercises GDPR erasure (hard delete),
/// the FK is set to NULL rather than cascade-deleting historical session records. Opponent sessions
/// remain intact; the deleted player's display name renders as the configured deleted-player
/// tombstone (e.g. "Deleted Player") via <c>IPlayerDisplayNameResolver</c>.
/// </remarks>
public sealed class SessionParticipant
{
    /// <summary>Participant row id — UUIDv7.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="GameSession"/>. Cascade-deleted with the session.</summary>
    public Guid SessionId { get; set; }

    /// <summary>FK to the participating <see cref="Player"/>. NULL after GDPR erasure of that player.</summary>
    public Guid? PlayerId { get; set; }

    /// <summary>Team number (0-indexed). Games that are free-for-all should use a unique team per participant.</summary>
    public int Team { get; set; }

    /// <summary>Outcome for this participant, or null while the session is non-terminal.</summary>
    public SessionResult? Result { get; set; }

    /// <summary>Game-reported score. Semantics are game-specific.</summary>
    public int? Score { get; set; }

    /// <summary>Rating snapshot at session start. Populated at completion by <c>GameKit.Rankings</c> (Phase 4).</summary>
    public double? RatingBefore { get; set; }

    /// <summary>Rating snapshot at session end. Populated at completion by <c>GameKit.Rankings</c> (Phase 4).</summary>
    public double? RatingAfter { get; set; }

    /// <summary>Rating delta (<see cref="RatingAfter"/> - <see cref="RatingBefore"/>). Denormalized for leaderboard speed.</summary>
    public double? RatingDelta { get; set; }
}
