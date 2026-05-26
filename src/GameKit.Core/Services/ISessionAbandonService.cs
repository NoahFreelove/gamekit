// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Entities;

namespace GameKit.Core.Services;

/// <summary>
/// Application service that orchestrates the <c>POST /api/sessions/{id}/abandon</c> flow
/// (D-20, PRES-03). Consumed by the session-lifecycle endpoint in <c>GameKit.Rankings</c>
/// (Phase 6 wires the route alongside the existing <c>/complete</c> handler).
/// </summary>
/// <remarks>
/// Mirrors the <see cref="ISessionCompleteService"/> shape — same per-endpoint application
/// service per Phase 4 convention. The handler transitions <c>game_sessions.state</c> from
/// <see cref="GameSessionState.Active"/> to <see cref="GameSessionState.Cancelled"/> (or
/// <see cref="GameSessionState.Abandoned"/> per Plan 06-05 policy choice) and fires
/// <see cref="ISessionLifecycleObserver.OnSessionAbandonedAsync"/>. Repeated calls on an
/// already-terminal session return <see cref="SessionAbandonResult.InvalidState"/>.
/// </remarks>
public interface ISessionAbandonService
{
    /// <summary>
    /// Marks a session as abandoned — transitions <c>game_sessions.state</c> away from
    /// <see cref="GameSessionState.Active"/>, invokes every registered
    /// <see cref="ISessionLifecycleObserver.OnSessionAbandonedAsync"/> inside the transaction,
    /// and commits.
    /// </summary>
    /// <param name="sessionId">The session to abandon.</param>
    /// <param name="req">Request body. Reserved for future extension (D-20).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A discriminated union describing the result of the operation.</returns>
    Task<SessionAbandonResult> AbandonAsync(
        Guid sessionId,
        SessionAbandonRequest req,
        CancellationToken ct);
}

/// <summary>
/// Request body for <c>POST /api/sessions/{id}/abandon</c>. Empty in v1 — the operation is
/// fully identified by the route parameter and the bearer's caller identity. Reserved for
/// future per-abandon metadata (reason code, abandoning-player attribution, etc.).
/// </summary>
public sealed record SessionAbandonRequest();

/// <summary>
/// Discriminated-union result returned by <see cref="ISessionAbandonService.AbandonAsync"/>.
/// The endpoint maps each case to an appropriate HTTP response.
/// </summary>
public abstract record SessionAbandonResult
{
    private SessionAbandonResult() { }

    /// <summary>
    /// The session was abandoned successfully on this call. Map to <c>200 OK</c>.
    /// </summary>
    /// <param name="NewState">The session's state after the transition.</param>
    public sealed record Abandoned(GameSessionState NewState) : SessionAbandonResult;

    /// <summary>
    /// No session with the given id exists. Map to <c>404 Not Found</c>.
    /// </summary>
    public sealed record SessionNotFound : SessionAbandonResult;

    /// <summary>
    /// The session exists but is not in the <see cref="GameSessionState.Active"/> state and
    /// therefore cannot be abandoned via this endpoint. Map to <c>409 Conflict</c> with problem
    /// type <c>invalid_session_state</c>.
    /// </summary>
    /// <param name="CurrentState">The current state of the session.</param>
    public sealed record InvalidState(GameSessionState CurrentState) : SessionAbandonResult;
}
