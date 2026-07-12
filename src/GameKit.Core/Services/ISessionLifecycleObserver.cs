// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;

/// <summary>
/// Cross-package observer port invoked whenever a <c>game_sessions.state</c> transition fires
/// from a session-lifecycle endpoint (<c>POST /api/sessions/{id}/start</c>,
/// <c>/complete</c>, or <c>/abandon</c>). Sibling packages implement this port to react to
/// session lifecycle without coupling Rankings to Presence (D-21).
/// </summary>
/// <remarks>
/// <para>
/// This port is OPTIONAL. If no implementation is registered in DI, the lifecycle endpoints
/// transition session state with no observer side-effects — Core-only and Rankings-only installs
/// remain functional (matches the <see cref="IPostSessionCompleteHandler"/> contract from Phase 4).
/// </para>
/// <para>
/// Implementations run inside the same ambient transaction that updated
/// <c>game_sessions.state</c>. A throw from an observer rolls back the state transition — observers
/// MUST be idempotent and MUST NOT throw under non-fatal conditions (transient Redis errors,
/// optional-side-effect failures, etc.). Mirror the Phase-4 <see cref="IPostSessionCompleteHandler"/>
/// observer contract for behavior and ordering guarantees.
/// </para>
/// <para>
/// This port is a sibling to (not a replacement for) <see cref="IPostSessionCompleteHandler"/>.
/// Phase-4 Rankings registers <see cref="IPostSessionCompleteHandler"/> for rating-update enqueue;
/// Phase-6 Presence registers <see cref="ISessionLifecycleObserver"/> for in-match transitions
/// across all three lifecycle endpoints. Both interfaces coexist (D-21 "kept for backwards
/// compatibility") and may have independent implementations registered in the same container.
/// </para>
/// </remarks>
public interface ISessionLifecycleObserver
{
    /// <summary>
    /// Called after <c>game_sessions.state</c> has transitioned to
    /// <see cref="Entities.GameSessionState.Active"/> by <c>POST /api/sessions/{id}/start</c>.
    /// Runs inside the caller's ambient transaction. Implementers MUST be idempotent.
    /// Presence implementations use this hook to set the in-match marker on each participant
    /// (per <c>IPresenceProvider.PresenceStatus.InMatch</c> XML doc).
    /// </summary>
    /// <param name="sessionId">The session that transitioned to <c>Active</c>.</param>
    /// <param name="participants">
    /// Player ids that joined the session. The order is not guaranteed to be stable across
    /// invocations; observers MUST treat the list as a set.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task OnSessionStartedAsync(
        Guid sessionId,
        IReadOnlyList<Guid> participants,
        CancellationToken ct);

    /// <summary>
    /// Called after <c>game_sessions.state</c> has transitioned to
    /// <see cref="Entities.GameSessionState.Completed"/> by <c>POST /api/sessions/{id}/complete</c>.
    /// Runs inside the caller's ambient transaction. Implementers MUST be idempotent.
    /// Presence implementations use this hook to clear the in-match marker — players fall back
    /// to <c>Online</c> if their heartbeat is fresh or <c>Offline</c> if it has expired.
    /// </summary>
    /// <param name="sessionId">The session that transitioned to <c>Completed</c>.</param>
    /// <param name="participants">Player ids whose in-match marker should be cleared.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnSessionCompletedAsync(
        Guid sessionId,
        IReadOnlyList<Guid> participants,
        CancellationToken ct);

    /// <summary>
    /// Called after <c>game_sessions.state</c> has transitioned to
    /// <see cref="Entities.GameSessionState.Cancelled"/> (or <see cref="Entities.GameSessionState.Abandoned"/>)
    /// by <c>POST /api/sessions/{id}/abandon</c>. Runs inside the caller's ambient transaction.
    /// Implementers MUST be idempotent. Presence implementations use this hook to clear the
    /// in-match marker on each participant (same back-to-online-or-offline behavior as completion).
    /// </summary>
    /// <param name="sessionId">The session that transitioned to <c>Cancelled</c> / <c>Abandoned</c>.</param>
    /// <param name="participants">Player ids whose in-match marker should be cleared.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnSessionAbandonedAsync(
        Guid sessionId,
        IReadOnlyList<Guid> participants,
        CancellationToken ct);
}
