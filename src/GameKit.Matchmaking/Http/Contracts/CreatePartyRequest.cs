// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/parties</c>. Player id is sourced from the JWT
/// <c>NameIdentifier</c> claim — the body carries no payload.
/// </summary>
public sealed record CreatePartyRequest();
