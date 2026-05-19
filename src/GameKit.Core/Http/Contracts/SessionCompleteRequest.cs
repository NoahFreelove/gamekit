// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Core.Entities;

namespace GameKit.Core.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/sessions/{id}/complete</c> (D-09).
/// Carries the outcome for every participant who took part in the session.
/// </summary>
/// <param name="Participants">
/// One entry per participant. Must not be empty; must contain at most 32 participants (validated by
/// <c>SessionCompleteRequestValidator</c> in <c>GameKit.Rankings</c>).
/// </param>
public sealed record SessionCompleteRequest(
    IReadOnlyList<SessionCompleteParticipant> Participants);

/// <summary>
/// Per-participant outcome data for <c>POST /api/sessions/{id}/complete</c> (D-09).
/// </summary>
/// <param name="PlayerId">
/// The player's id. Must not be <see cref="Guid.Empty"/>.
/// </param>
/// <param name="Team">
/// Zero-indexed team number. Free-for-all games should assign a unique team per participant.
/// Must be ≥ 0.
/// </param>
/// <param name="Result">
/// Outcome for this participant. Must be a defined <see cref="SessionResult"/> enum value.
/// </param>
/// <param name="Score">
/// Optional game-reported score. Semantics are game-specific. When provided, must be ≥ 0.
/// </param>
public sealed record SessionCompleteParticipant(
    Guid PlayerId,
    int Team,
    SessionResult Result,
    int? Score);
