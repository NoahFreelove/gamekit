// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using GameKit.Core.Entities;

namespace GameKit.Core.Http.Contracts;

/// <summary>
/// Response body for a successful <c>POST /api/sessions/{id}/complete</c> call (D-07 / D-08).
/// Returned both for the first completion and for subsequent cached retries with the same
/// <c>Idempotency-Key</c> header value.
/// </summary>
/// <param name="SessionId">The completed session's id.</param>
/// <param name="State">
/// The new lifecycle state of the session. For a successful completion this is always
/// <see cref="GameSessionState.Completed"/>.
/// </param>
/// <param name="Participants">Per-participant result snapshots.</param>
/// <param name="CompletedAt">UTC timestamp at which the state-conditional UPDATE committed.</param>
public sealed record SessionCompleteResponse(
    Guid SessionId,
    GameSessionState State,
    IReadOnlyList<SessionCompleteParticipantResult> Participants,
    DateTimeOffset CompletedAt);

/// <summary>
/// Per-participant result snapshot included in <see cref="SessionCompleteResponse"/>.
/// </summary>
/// <param name="PlayerId">The participant's player id.</param>
/// <param name="Result">Recorded outcome.</param>
/// <param name="RatingBefore">
/// Rating snapshot captured at session-complete time by the Rankings adapter.
/// <see langword="null"/> until the adapter runs (Core-only installs always return
/// <see langword="null"/>).
/// </param>
/// <param name="RatingAfter">
/// Rating after the batch rating-period update (populated on the next ticker tick).
/// <see langword="null"/> in the immediate response.
/// </param>
/// <param name="RatingDelta">
/// Difference between <see cref="RatingAfter"/> and <see cref="RatingBefore"/>.
/// <see langword="null"/> until the ticker applies the batch.
/// </param>
public sealed record SessionCompleteParticipantResult(
    Guid PlayerId,
    SessionResult Result,
    double? RatingBefore,
    double? RatingAfter,
    double? RatingDelta);
