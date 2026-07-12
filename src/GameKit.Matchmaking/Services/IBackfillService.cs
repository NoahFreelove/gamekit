// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Application service driving the backfill flow (MATCH-19 SC#3). Creates
/// <see cref="Entities.MatchmakingTicketType.Backfill"/> tickets at Redis score <c>0</c>
/// so they are processed before all normal tickets by the existing ticker's ZRANGEBYSCORE
/// Ascending ordering — no ticker code change required.
/// </summary>
public interface IBackfillService
{
    /// <summary>
    /// Create a backfill ticket for <paramref name="playerId"/> targeting the specified
    /// active session. The ticket is inserted into the Redis queue at score <c>0</c>
    /// (higher priority than all normal tickets whose scores are Unix milliseconds).
    /// </summary>
    /// <param name="playerId">Canonical player id extracted from the JWT.</param>
    /// <param name="ladderId">Ladder to queue against — must be registered via <c>AddLadder</c>.</param>
    /// <param name="sessionId">
    /// The active game session being rejoined. Must exist and be in <c>Active</c> state.
    /// </param>
    /// <param name="regionName">
    /// Optional region name. When null, routes to the <c>"default"</c> pool. When non-null,
    /// must be present in the ladder's <c>AllowedRegions</c> list.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="BackfillResult"/> describing the outcome.</returns>
    Task<BackfillResult> BackfillAsync(
        Guid playerId,
        Guid ladderId,
        Guid sessionId,
        string? regionName,
        CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="IBackfillService.BackfillAsync"/>.</summary>
public enum BackfillOutcome
{
    /// <summary>Backfill ticket was queued at Redis score 0.</summary>
    Queued = 0,

    /// <summary>The supplied ladder id is not registered.</summary>
    UnknownLadder = 1,

    /// <summary>The target session does not exist.</summary>
    SessionNotFound = 2,

    /// <summary>The target session is not in <c>Active</c> state.</summary>
    SessionNotActive = 3,

    /// <summary>The supplied region name is not in the ladder's <c>AllowedRegions</c> list (MATCH-19).</summary>
    InvalidRegion = 4,

    /// <summary>
    /// A non-terminal ticket for the same player/ladder/pool already exists.
    /// The caller should wait for the existing ticket to reach a terminal state before retrying.
    /// </summary>
    AlreadyEnqueued = 5,
}

/// <summary>Structured result of <see cref="IBackfillService.BackfillAsync"/>.</summary>
/// <param name="Outcome">High-level outcome — drives the HTTP status code.</param>
/// <param name="TicketId">Populated on <see cref="BackfillOutcome.Queued"/>; <see langword="null"/> otherwise.</param>
/// <param name="Detail">Optional free-text detail for the client.</param>
public sealed record BackfillResult(
    BackfillOutcome Outcome,
    Guid? TicketId = null,
    string? Detail = null);
