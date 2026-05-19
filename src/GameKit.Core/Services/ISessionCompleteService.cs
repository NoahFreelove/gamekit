// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Entities;
using GameKit.Core.Http.Contracts;

namespace GameKit.Core.Services;

/// <summary>
/// Application service that orchestrates the <c>POST /api/sessions/{id}/complete</c> flow (D-07,
/// D-08, D-22, RANK-11). Consumed by <c>SessionEndpoints</c> in <c>GameKit.Core</c>.
/// </summary>
public interface ISessionCompleteService
{
    /// <summary>
    /// Marks a session as completed, writes participant results, runs post-completion handlers,
    /// and stores the idempotency record — all inside a single <c>ReadCommitted</c> transaction.
    /// </summary>
    /// <param name="sessionId">The session to complete.</param>
    /// <param name="idempotencyKey">The client-supplied <c>Idempotency-Key</c> header value.</param>
    /// <param name="req">Request body containing participant outcomes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A discriminated union describing the result of the operation.</returns>
    Task<SessionCompleteResult> CompleteAsync(
        Guid sessionId,
        string idempotencyKey,
        SessionCompleteRequest req,
        CancellationToken ct);
}

/// <summary>
/// Discriminated-union result returned by <see cref="ISessionCompleteService.CompleteAsync"/>.
/// The endpoint maps each case to an appropriate HTTP response.
/// </summary>
public abstract record SessionCompleteResult
{
    private SessionCompleteResult() { }

    /// <summary>
    /// The session was completed successfully on this call. Contains the full response
    /// that the endpoint should return as <c>200 OK</c>.
    /// </summary>
    /// <param name="Response">The completion response.</param>
    public sealed record Completed(SessionCompleteResponse Response) : SessionCompleteResult;

    /// <summary>
    /// The session was already completed by a prior call with the same idempotency key and the same
    /// request body. The cached response from the first call is returned. Map to <c>200 OK</c>.
    /// </summary>
    /// <param name="Response">The cached completion response.</param>
    public sealed record AlreadyCompletedCached(SessionCompleteResponse Response) : SessionCompleteResult;

    /// <summary>
    /// The same <c>Idempotency-Key</c> header was supplied with a different request body.
    /// This is a client error. Map to <c>409 Conflict</c> with problem type
    /// <c>idempotency_key_reused</c>.
    /// </summary>
    public sealed record IdempotencyKeyReused : SessionCompleteResult;

    /// <summary>
    /// No session with the given id exists. Map to <c>404 Not Found</c>.
    /// </summary>
    public sealed record SessionNotFound : SessionCompleteResult;

    /// <summary>
    /// The session exists but is not in the <see cref="GameSessionState.Active"/> state and
    /// therefore cannot be completed via this endpoint. Map to <c>409 Conflict</c> with problem
    /// type <c>invalid_session_state</c>.
    /// </summary>
    /// <param name="CurrentState">The current state of the session.</param>
    public sealed record InvalidState(GameSessionState CurrentState) : SessionCompleteResult;

    /// <summary>
    /// The request references a <c>player_id</c> that is not a participant in this session.
    /// Map to <c>404 Not Found</c>.
    /// </summary>
    /// <param name="PlayerId">The unrecognized player id.</param>
    public sealed record UnknownParticipant(Guid PlayerId) : SessionCompleteResult;

    /// <summary>
    /// A participant that is recorded on the session is missing from the request body.
    /// Map to <c>400 Bad Request</c>.
    /// </summary>
    /// <param name="PlayerId">The missing player id.</param>
    public sealed record MissingParticipant(Guid PlayerId) : SessionCompleteResult;
}
