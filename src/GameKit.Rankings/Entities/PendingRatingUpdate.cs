// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Entities;

namespace GameKit.Rankings.Entities;

/// <summary>
/// Lightweight queue row enqueued by the session-complete handler and drained by
/// <c>RankingsTickerService</c> in per-ladder batches (D-22).
/// </summary>
/// <remarks>
/// <para>
/// Uses a denormalized column shape (session_id, player_id, ladder_id, result, score)
/// rather than a reference to <c>session_participants.id</c>, so the ticker can drain
/// in bulk without <c>JOIN</c>s (Open Q2 recommendation). Rows are retained after a
/// successful drain (audit trail) and cleaned up by <c>IdempotencyCleanupService</c>
/// after the configured retention period (default 30 days).
/// </para>
/// <para>
/// <c>PlayerId</c> is NULLABLE (Pitfall §12): when a player is GDPR-erased, the FK
/// is set to NULL (<c>ON DELETE SET NULL</c>). A NULL <c>PlayerId</c> row is skipped
/// by the ticker to avoid applying a ghost rating update.
/// </para>
/// </remarks>
public sealed class PendingRatingUpdate
{
    /// <summary>Row id — UUIDv7 from <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <see cref="GameSession"/> (ON DELETE CASCADE).</summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// FK → <c>players.id</c> (ON DELETE SET NULL — Pitfall §12).
    /// NULLABLE: set to NULL when the player is GDPR-erased.
    /// The ticker skips rows where this is NULL.
    /// </summary>
    public Guid? PlayerId { get; set; }

    /// <summary>FK → <see cref="Ladder"/> (ON DELETE RESTRICT).</summary>
    public Guid LadderId { get; set; }

    /// <summary>
    /// Session result for this participant. Denormalized from
    /// <c>session_participants.result</c> at enqueue time for join-free batch drains.
    /// Values: <c>"win"</c>, <c>"loss"</c>, <c>"draw"</c>, <c>"forfeit"</c>.
    /// </summary>
    public required string Result { get; set; }

    /// <summary>Participant score (game-specific semantics). Null when not reported.</summary>
    public int? Score { get; set; }

    /// <summary>UTC timestamp at which the session-complete handler enqueued this row.</summary>
    public DateTimeOffset EnqueuedAt { get; set; }

    /// <summary>UTC timestamp at which the ticker leased this row for the current batch. Null until claimed.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>UTC timestamp at which the algorithm successfully applied the rating update. Null until applied.</summary>
    public DateTimeOffset? AppliedAt { get; set; }
}
