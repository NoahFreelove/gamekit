// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Entities;

namespace GameKit.Core.Services;

/// <summary>
/// Application service that orchestrates the <c>POST /api/sessions/{id}/start</c> flow
/// (D-20, PRES-03). Consumed by the session-lifecycle endpoint in <c>GameKit.Rankings</c>
/// (Phase 6 wires the route alongside the existing <c>/complete</c> handler).
/// </summary>
/// <remarks>
/// Mirrors the <see cref="ISessionCompleteService"/> shape — same per-endpoint application
/// service per Phase 4 convention. Starts are naturally idempotent on
/// <c>(session_id, current state)</c>: re-issuing <c>/start</c> on a session already in
/// <see cref="GameSessionState.Active"/> returns a result the endpoint maps to <c>200 OK</c>
/// (the start has already happened). Whether to additionally support <c>Idempotency-Key</c>
/// retry semantics is deferred to the implementation plan (Plan 06-05).
/// </remarks>
public interface ISessionStartService
{
    /// <summary>
    /// Marks a session as started — transitions <c>game_sessions.state</c> from
    /// <see cref="GameSessionState.Pending"/> to <see cref="GameSessionState.Active"/>, invokes
    /// every registered <see cref="ISessionLifecycleObserver.OnSessionStartedAsync"/> inside the
    /// transaction, and commits.
    /// </summary>
    /// <param name="sessionId">The session to start.</param>
    /// <param name="req">Request body. Reserved for future extension (D-20).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A discriminated union describing the result of the operation.</returns>
    Task<SessionStartResult> StartAsync(
        Guid sessionId,
        SessionStartRequest req,
        CancellationToken ct);
}

/// <summary>
/// Request body for <c>POST /api/sessions/{id}/start</c>. Empty in v1 — the operation is
/// fully identified by the route parameter and the bearer's caller identity. Reserved for
/// future per-start metadata (start timestamp override, server-region, etc.).
/// </summary>
public sealed record SessionStartRequest();

/// <summary>
/// Discriminated-union result returned by <see cref="ISessionStartService.StartAsync"/>.
/// The endpoint maps each case to an appropriate HTTP response.
/// </summary>
public abstract record SessionStartResult
{
    private SessionStartResult() { }

    /// <summary>
    /// The session was started successfully on this call. Map to <c>200 OK</c>.
    /// </summary>
    /// <param name="NewState">The session's state after the transition (always <see cref="GameSessionState.Active"/>).</param>
    public sealed record Started(GameSessionState NewState) : SessionStartResult;

    /// <summary>
    /// No session with the given id exists. Map to <c>404 Not Found</c>.
    /// </summary>
    public sealed record SessionNotFound : SessionStartResult;

    /// <summary>
    /// The session exists but is not in the <see cref="GameSessionState.Pending"/> state and
    /// therefore cannot be started via this endpoint. Map to <c>409 Conflict</c> with problem
    /// type <c>invalid_session_state</c>.
    /// </summary>
    /// <param name="CurrentState">The current state of the session.</param>
    public sealed record InvalidState(GameSessionState CurrentState) : SessionStartResult;
}
