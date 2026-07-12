// SPDX-License-Identifier: Apache-2.0
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
/// <param name="RegionName">
/// Optional region name for regional pool routing (MATCH-18). When null, routes to the
/// <c>"default"</c> pool (backwards-compatible v1 behaviour). When non-null, must be
/// present in the ladder's <c>AllowedRegions</c> list or the request is rejected with
/// HTTP 400 <c>region_not_allowed</c>. Takes precedence over <see cref="PoolName"/> when
/// both are supplied.
/// </param>
public sealed record EnqueueRequest(
    Guid LadderId,
    string? PoolName = null,
    Guid? PartyId = null,
    string? RegionName = null);
