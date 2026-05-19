// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Entities;

/// <summary>
/// Lifecycle event type for a <see cref="TicketEvent"/>. Stored as <c>integer</c> at the SQL level
/// (Phase 5 mandatory; CONTEXT.md §Established Patterns). Values intentionally mirror
/// <see cref="TicketStatus"/> so an event type and the resulting ticket status share the same
/// integer code (CONTEXT.md D-18).
/// </summary>
public enum TicketEventType
{
    /// <summary>Emitted when a ticket is enqueued (records the initial <c>QueuedAt</c> timestamp).</summary>
    Queued = 0,

    /// <summary>Emitted when the ticket is included in a match proposal (records <c>proposalId</c> in the payload).</summary>
    Proposed = 1,

    /// <summary>Emitted when the owning player accepts the proposal within the accept window.</summary>
    Accepted = 2,

    /// <summary>Emitted when the owning player explicitly declines.</summary>
    Declined = 3,

    /// <summary>Emitted when the accept window elapses without a response.</summary>
    TimedOut = 4,

    /// <summary>Emitted when the proposal succeeds and a <c>game_session</c> is created (records <c>sessionId</c> in the payload).</summary>
    Matched = 5,

    /// <summary>Emitted when the ticket is cancelled.</summary>
    Cancelled = 6,

    /// <summary>Emitted when the reconciler marks the ticket abandoned.</summary>
    Expired = 7,
}
