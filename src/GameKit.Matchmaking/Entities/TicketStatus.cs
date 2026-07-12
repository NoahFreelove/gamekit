// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Entities;

/// <summary>
/// Lifecycle / terminal status of a <see cref="MatchmakingTicket"/>. Stored as <c>integer</c>
/// at the SQL level (Phase 5 mandatory; CONTEXT.md §Established Patterns).
/// </summary>
/// <remarks>
/// Values pinned per CONTEXT.md D-18 + RESEARCH §Decision 14 — eight values modelling both
/// in-flight and terminal states. <see cref="TicketEventType"/> mirrors the same numeric
/// layout so a <see cref="TicketEvent.EventType"/> equal to a <see cref="TicketStatus"/>
/// signals the transition that produced the event.
/// </remarks>
public enum TicketStatus
{
    /// <summary>Ticket is enqueued in Redis and visible to the matcher.</summary>
    Queued = 0,

    /// <summary>Ticket has been included in a match proposal and is awaiting accept-step responses (D-06).</summary>
    Proposed = 1,

    /// <summary>Player accepted the proposal within the accept window (D-07, 10s).</summary>
    Accepted = 2,

    /// <summary>Player explicitly declined the proposal — triggers escalating cooldown (D-08).</summary>
    Declined = 3,

    /// <summary>Accept window elapsed without a response — also triggers escalating cooldown (D-08).</summary>
    TimedOut = 4,

    /// <summary>Proposal succeeded for all participants and the <c>game_session</c> was created.</summary>
    Matched = 5,

    /// <summary>Ticket was cancelled by the player, by party dissolution, or by mid-queue disconnect (D-04).</summary>
    Cancelled = 6,

    /// <summary>Reconciler marked the ticket abandoned (MATCH-06; was non-terminal in Postgres but absent from Redis).</summary>
    Expired = 7,
}
