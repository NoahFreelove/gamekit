// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/parties/join</c>. Case-insensitive lookup against the
/// Postgres <c>citext</c> column — players may type either case.
/// </summary>
/// <param name="Code">Crockford base32 party code (6–8 chars; <c>A–HJKMNP-TV-Z2-9</c>).</param>
public sealed record JoinPartyRequest(string Code);
