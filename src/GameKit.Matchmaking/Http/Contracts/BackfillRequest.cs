// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/matchmaking/backfill</c>. Creates a
/// <see cref="Entities.MatchmakingTicketType.Backfill"/> ticket for a player rejoining an
/// in-progress session. The backfill ticket is inserted at score <c>0</c> in the Redis queue
/// so it is processed with higher priority than all normal tickets (MATCH-19 SC#3).
/// Player id is sourced from the JWT <c>NameIdentifier</c> claim.
/// </summary>
/// <param name="LadderId">Ladder identifier; must reference a configured matchmaking ladder.</param>
/// <param name="SessionId">
/// The active <c>game_session</c> being rejoined. The session must exist and be in
/// <c>Active</c> state; otherwise the request is rejected with <c>session_not_found</c>
/// or <c>session_not_active</c> respectively.
/// </param>
/// <param name="RegionName">
/// Optional region name. When null, routes to the <c>"default"</c> pool
/// (backwards-compatible v1 behaviour). When non-null, must be present in the ladder's
/// <c>AllowedRegions</c> list or the request is rejected with HTTP 400
/// <c>region_not_allowed</c>.
/// </param>
public sealed record BackfillRequest(Guid LadderId, Guid SessionId, string? RegionName = null);
