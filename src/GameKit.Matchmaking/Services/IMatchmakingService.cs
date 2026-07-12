// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Application service driving the player-facing matchmaking flow — enqueue, cancel, status
/// lookup. Wires the HTTP endpoints in Plan 05-08 (<c>POST /api/mm/queue</c>,
/// <c>DELETE /api/mm/queue/{ticketId}</c>, <c>GET /api/mm/queue/{ticketId}/status</c>) against
/// the Redis live queue + Postgres analytics mirror.
/// </summary>
/// <remarks>
/// <para>
/// Redis is the source of truth (MATCH-04 / CONTEXT D-03). Postgres ticket rows are written
/// asynchronously via the bounded <c>Channel&lt;TicketEvent&gt;</c> drained by
/// <see cref="MatchmakingAnalyticsDrainService"/> (Plan 05-07).
/// </para>
/// <para>
/// Threat model (T-05-08-01 / T-05-08-02): <see cref="CancelAsync"/> verifies the cancelling
/// player belongs to the ticket's party (or is the solo holder) — a cross-player cancel
/// returns <see cref="CancelOutcome.NotAuthorized"/>.
/// </para>
/// </remarks>
public interface IMatchmakingService
{
    /// <summary>
    /// Enqueue a new matchmaking ticket on behalf of <paramref name="playerId"/>.
    /// </summary>
    /// <param name="playerId">Canonical player id extracted from the JWT.</param>
    /// <param name="ladderId">Ladder to queue against — must be registered via <c>AddLadder</c>.</param>
    /// <param name="poolName">
    /// Pool name within the ladder. Defaults to <c>"default"</c>. Multiple pools per ladder
    /// support region affinity / game-mode segmentation in v2; v1 ships the single default pool.
    /// </param>
    /// <param name="partyId">
    /// Optional party id. When non-null, every member of the party shares the ticket
    /// (CONTEXT D-04). When null, the enqueue is solo (single-player ticket).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="EnqueueResult"/> describing the outcome.</returns>
    Task<EnqueueResult> EnqueueAsync(
        Guid playerId,
        Guid ladderId,
        string? poolName,
        Guid? partyId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel the ticket identified by <paramref name="ticketId"/> on behalf of
    /// <paramref name="playerId"/>. Verifies ownership before mutating Redis state.
    /// </summary>
    /// <param name="ticketId">Ticket identifier.</param>
    /// <param name="playerId">Canonical player id (must belong to the ticket's party or be the solo holder).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="CancelResult"/> describing the outcome.</returns>
    Task<CancelResult> CancelAsync(Guid ticketId, Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Read the current status of the ticket from Redis. Used by the long-poll handler's
    /// first-read fast-path — when the status is non-Queued the handler returns immediately
    /// without subscribing to <c>mm:status:{ticketId}</c>.
    /// </summary>
    /// <param name="ticketId">Ticket identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="TicketStatusSnapshot"/>, or <see langword="null"/> when the ticket is unknown to Redis.</returns>
    Task<TicketStatusSnapshot?> GetStatusAsync(Guid ticketId, CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="IMatchmakingService.EnqueueAsync"/>.</summary>
public enum EnqueueOutcome
{
    /// <summary>Ticket was queued; <see cref="EnqueueResult.TicketId"/> is populated.</summary>
    Queued = 0,

    /// <summary>The player is in cooldown — <see cref="EnqueueResult.RetryAfter"/> carries the wait.</summary>
    RejectedDueToCooldown = 1,

    /// <summary>The party's intra-spread exceeds the configured cap (CONTEXT D-14).</summary>
    RejectedDueToSpread = 2,

    /// <summary>The party already has a non-terminal ticket (solo or shared).</summary>
    AlreadyEnqueued = 3,

    /// <summary>The supplied ladder id is not registered.</summary>
    UnknownLadder = 4,

    /// <summary>The supplied party id is not in <see cref="Entities.PartyState.Open"/> or the player is not a member.</summary>
    InvalidParty = 5,

    /// <summary>An admin has paused the requested ladder's queue via the admin UI / control service. New enqueues are rejected until the pause flag is cleared.</summary>
    RejectedDueToQueuePaused = 6,

    /// <summary>An admin has marked the requested ladder for drain — existing tickets continue to match, but new enqueues are rejected.</summary>
    RejectedDueToQueueDraining = 7,

    /// <summary>The supplied region name is not in the ladder's <c>AllowedRegions</c> list (MATCH-18).</summary>
    InvalidRegion = 8,
}

/// <summary>Structured result of <see cref="IMatchmakingService.EnqueueAsync"/>.</summary>
/// <param name="Outcome">High-level outcome — drives the HTTP status code.</param>
/// <param name="TicketId">Populated on <see cref="EnqueueOutcome.Queued"/>; <see langword="null"/> otherwise.</param>
/// <param name="RetryAfter">Populated on <see cref="EnqueueOutcome.RejectedDueToCooldown"/>.</param>
/// <param name="Detail">Optional free-text detail for the client (logged + surfaced in problem+json responses).</param>
public sealed record EnqueueResult(
    EnqueueOutcome Outcome,
    Guid? TicketId = null,
    TimeSpan? RetryAfter = null,
    string? Detail = null);

/// <summary>Outcome of <see cref="IMatchmakingService.CancelAsync"/>.</summary>
public enum CancelOutcome
{
    /// <summary>The ticket was cancelled.</summary>
    Cancelled = 0,

    /// <summary>The ticket does not exist (already cancelled, or never created).</summary>
    NotFound = 1,

    /// <summary>The caller is not authorized to cancel this ticket (T-05-08-01).</summary>
    NotAuthorized = 2,

    /// <summary>The ticket is in a terminal state and cannot be cancelled.</summary>
    Terminal = 3,
}

/// <summary>Structured result of <see cref="IMatchmakingService.CancelAsync"/>.</summary>
/// <param name="Outcome">High-level outcome — drives the HTTP status code.</param>
public sealed record CancelResult(CancelOutcome Outcome);

/// <summary>Snapshot of the current ticket status as recorded in Redis.</summary>
/// <param name="Status">Lower-case status literal — one of <c>queued</c>, <c>proposed</c>, <c>matched</c>, <c>cancelled</c>.</param>
/// <param name="ProposalId">Populated when <see cref="Status"/> is <c>proposed</c>.</param>
/// <param name="Deadline">Proposal accept-window deadline (UTC), populated when <see cref="Status"/> is <c>proposed</c>.</param>
/// <param name="SessionId">Game session id, populated when <see cref="Status"/> is <c>matched</c>.</param>
public sealed record TicketStatusSnapshot(
    string Status,
    Guid? ProposalId = null,
    DateTimeOffset? Deadline = null,
    Guid? SessionId = null);
