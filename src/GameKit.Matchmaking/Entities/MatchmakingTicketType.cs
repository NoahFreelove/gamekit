// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Entities;

/// <summary>
/// Type of a <see cref="MatchmakingTicket"/>. Stored as <c>integer</c> at the SQL level
/// (Phase 5 mandatory; integer storage convention). <see cref="Normal"/> is the default (0).
/// </summary>
public enum MatchmakingTicketType
{
    /// <summary>Standard player-initiated matchmaking ticket. Score = Unix milliseconds.</summary>
    Normal = 0,

    /// <summary>
    /// Backfill ticket created via <c>POST /api/mm/backfill</c>. Inserted into the Redis
    /// sorted set with score <c>0</c> (Unix epoch) so it sorts before all Normal tickets
    /// and is processed with higher priority by the matcher.
    /// </summary>
    Backfill = 1,
}
