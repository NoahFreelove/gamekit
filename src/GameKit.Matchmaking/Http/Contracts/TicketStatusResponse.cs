// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Response body for <c>GET /api/mm/queue/{ticketId}/status</c> and the accept / decline
/// endpoints. Carries the current state of the ticket plus any state-specific identifiers.
/// </summary>
/// <param name="Status">Lower-case status literal: <c>queued</c> | <c>proposed</c> | <c>matched</c> | <c>cancelled</c>.</param>
/// <param name="ProposalId">Populated when <see cref="Status"/> is <c>proposed</c>.</param>
/// <param name="Deadline">Proposal accept-window deadline; populated when <see cref="Status"/> is <c>proposed</c>.</param>
/// <param name="SessionId">Game session id; populated when <see cref="Status"/> is <c>matched</c>.</param>
public sealed record TicketStatusResponse(
    string Status,
    Guid? ProposalId = null,
    DateTimeOffset? Deadline = null,
    Guid? SessionId = null);
