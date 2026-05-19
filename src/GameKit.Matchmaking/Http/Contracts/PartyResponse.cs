// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Response body for the party endpoints. Mirrors the public-facing shape of the
/// <c>parties</c> + <c>party_members</c> rows.
/// </summary>
/// <param name="PartyId">Party identifier.</param>
/// <param name="PartyCode">Crockford base32 party code (case-insensitive at the DB level — citext).</param>
/// <param name="State">Lower-case state literal: <c>open</c> | <c>queueing</c> | <c>in_match</c> | <c>dissolved</c>.</param>
/// <param name="MemberPlayerIds">Current party members (ordered by join time).</param>
/// <param name="OwnerPlayerId">Owner / creator's player id.</param>
/// <param name="CreatedAt">Party creation UTC timestamp.</param>
public sealed record PartyResponse(
    Guid PartyId,
    string PartyCode,
    string State,
    IReadOnlyList<Guid> MemberPlayerIds,
    Guid OwnerPlayerId,
    DateTimeOffset CreatedAt);
