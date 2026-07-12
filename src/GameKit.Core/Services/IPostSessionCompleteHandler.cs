// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Entities;

namespace GameKit.Core.Services;

/// <summary>
/// Port interface called by <see cref="ISessionCompleteService"/> after a session is marked
/// completed (D-22). Implementations may enqueue rating-update work, write audit rows, or
/// perform any other post-completion side effect.
/// </summary>
/// <remarks>
/// <para>
/// This port is OPTIONAL. If no implementation is registered in DI,
/// <see cref="ISessionCompleteService.CompleteAsync"/> operates in degraded mode: the session is
/// marked completed, participant results are recorded, but no post-completion handlers run
/// (Open Q6 — Core-only install completes sessions without rating updates).
/// </para>
/// <para>
/// Implementations run inside the same ambient transaction that updated
/// <c>game_sessions.state</c>. Implementations MUST be idempotent — the contract does not
/// guarantee exactly-once delivery if the caller retries (e.g. under a database failure after
/// the UPDATE commits but before SaveChanges on the handler's side).
/// </para>
/// </remarks>
public interface IPostSessionCompleteHandler
{
    /// <summary>
    /// Called after <c>game_sessions.state</c> has been set to <c>Completed</c> and participant
    /// results have been written. Runs inside the caller's ambient transaction.
    /// </summary>
    /// <param name="sessionId">The completed session's id.</param>
    /// <param name="participants">
    /// Snapshot of every participant, including the ladder id and result, as recorded in the
    /// session-complete request.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task OnCompletedAsync(
        Guid sessionId,
        IReadOnlyList<SessionParticipantSnapshot> participants,
        CancellationToken ct);
}

/// <summary>
/// Immutable snapshot of a session participant's data at completion time.
/// Passed to <see cref="IPostSessionCompleteHandler.OnCompletedAsync"/>.
/// </summary>
/// <param name="PlayerId">The participant's player id.</param>
/// <param name="LadderId">
/// The ladder this session is scored against. <see langword="null"/> for unranked sessions.
/// </param>
/// <param name="Result">Outcome for this participant.</param>
/// <param name="Score">Optional game-reported score.</param>
public sealed record SessionParticipantSnapshot(
    Guid PlayerId,
    Guid? LadderId,
    SessionResult Result,
    int? Score);
