// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Entities;

/// <summary>
/// Lifecycle state of a <see cref="Party"/>. Stored as <c>integer</c> at the SQL level
/// (Phase 5 mandatory pattern — Phase 4 was bitten by <c>HasConversion&lt;string&gt;()</c>
/// emitting integer-cast SQL seeds, see CONTEXT.md §Established Patterns).
/// </summary>
/// <remarks>
/// Values pinned per CONTEXT.md §Phase Boundary: the matcher uses <c>State == Queueing</c>
/// to gate re-enqueue on accept-failure (D-09) and <c>State == Dissolved</c> to release
/// the single-active-party constraint (application-enforced; see RESEARCH §Decision 12).
/// </remarks>
public enum PartyState
{
    /// <summary>Party exists, members can join via party code, no ticket queued.</summary>
    Open = 0,

    /// <summary>Party has an active <see cref="MatchmakingTicket"/> in the Redis queue.</summary>
    Queueing = 1,

    /// <summary>Party's ticket matched and a <c>game_sessions</c> row exists; members are in-game.</summary>
    InMatch = 2,

    /// <summary>Party terminated. Members are unbound; party code is freed; row retained for audit.</summary>
    Dissolved = 3,
}
