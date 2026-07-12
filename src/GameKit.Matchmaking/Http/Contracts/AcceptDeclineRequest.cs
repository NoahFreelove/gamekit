// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/mm/proposal/{proposalId}/accept</c> and
/// <c>POST /api/mm/proposal/{proposalId}/decline</c>. The caller carries the ticket id of
/// their own party — the proposal service verifies the supplied ticket id belongs to the
/// proposal (T-05-06-01 spoofing guard).
/// </summary>
/// <param name="TicketId">The accepting/declining party's own ticket id.</param>
public sealed record AcceptDeclineRequest(Guid TicketId);
