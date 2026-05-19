// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/mm/queue</c>. Player id is sourced from the JWT
/// <c>NameIdentifier</c> claim.
/// </summary>
/// <param name="LadderId">Ladder identifier; must reference a configured matchmaking ladder.</param>
/// <param name="PoolName">Optional pool name within the ladder (defaults to <c>"default"</c>).</param>
/// <param name="PartyId">
/// Optional party id. When non-null, every member of the party shares the ticket (CONTEXT D-04).
/// When null, the enqueue is solo.
/// </param>
public sealed record EnqueueRequest(Guid LadderId, string? PoolName = null, Guid? PartyId = null);
