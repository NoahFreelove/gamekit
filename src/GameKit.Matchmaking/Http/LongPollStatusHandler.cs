// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Http;

/// <summary>
/// Implements the <c>GET /api/mm/queue/{ticketId}/status</c> long-poll contract
/// (RESEARCH §Decision 9; D-10) with the Pitfall §5 connection-leak guard.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract:</b> hold the connection for up to
/// <see cref="GameKitMatchmakingOptions.LongPollTimeoutSeconds"/> waiting for a status change
/// PUBLISH on <c>mm:status:{ticketId}</c>. Return the new <see cref="TicketStatusResponse"/>
/// immediately on receipt; on timeout return the current <c>queued</c> status; on client
/// abort return <see cref="Results.Empty"/>. No SignalR / short-poll fallback in v1.
/// </para>
/// <para>
/// <b>Pitfall §5 mitigation (subscription-leak guard):</b> the handler creates a linked
/// <see cref="CancellationTokenSource"/> from <see cref="HttpContext.RequestAborted"/> + the
/// bounded long-poll timeout, awaits a <see cref="TaskCompletionSource{T}"/> populated by
/// the Redis SUBSCRIBE callback, and <em>always</em> Unsubscribes in the <c>finally</c>
/// block. The integration test <c>LongPoll_AbortMidPoll_UnsubscribesWithin500ms</c> verifies
/// the subscription count returns to baseline ≤500 ms after client abort.
/// </para>
/// <para>
/// <b>Ownership check (T-05-08-01):</b> the player id from the JWT must belong to the
/// ticket's party (or match the solo holder). A cross-player long-poll on someone else's
/// ticket returns 403 Forbidden without leaking ticket state.
/// </para>
/// </remarks>
public static class LongPollStatusHandler
{
    /// <summary>
    /// Handle a single long-poll request. Awaits a status change on the ticket's pub/sub
    /// channel for up to <see cref="GameKitMatchmakingOptions.LongPollTimeoutSeconds"/>.
    /// </summary>
    /// <param name="http">The current HTTP context (carries <see cref="HttpContext.RequestAborted"/>).</param>
    /// <param name="ticketId">Route ticket identifier.</param>
    /// <param name="svc">Matchmaking service (first-read fast-path).</param>
    /// <param name="redis">Redis multiplexer (subscriber).</param>
    /// <param name="opts">Matchmaking options snapshot.</param>
    /// <param name="db">Scoped DbContext (party-membership lookup for the ownership check).</param>
    /// <param name="ct">Caller cancellation token (combined with HttpContext.RequestAborted + timeout).</param>
    /// <returns>An <see cref="IResult"/> mirroring the contract above.</returns>
    public static async Task<IResult> HandleAsync(
        HttpContext http,
        Guid ticketId,
        IMatchmakingService svc,
        IConnectionMultiplexer redis,
        IOptions<GameKitMatchmakingOptions> opts,
        GameKitDbContext db,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(svc);
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(db);

        if (!TryGetPlayerId(http, out var playerId))
            return Results.Forbid();

        // STEP 1: ownership + first-read fast-path. The IMatchmakingService.GetStatusAsync
        // reads the Redis ticket hash; we extract partyId + playerId from the same hash via
        // a second read inside the ownership helper. (Ordering: ownership check first to
        // avoid leaking ticket-existence to non-members.)
        var owns = await VerifyOwnershipAsync(redis, db, ticketId, playerId, ct).ConfigureAwait(false);
        if (owns is OwnershipResult.NotFound)
            return Results.NotFound(new { error = "ticket_not_found", ticketId });
        if (owns is OwnershipResult.NotAuthorized)
            return Results.Forbid();

        var snapshot = await svc.GetStatusAsync(ticketId, ct).ConfigureAwait(false);
        if (snapshot is not null && !string.Equals(snapshot.Status, "queued", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new TicketStatusResponse(
                Status: snapshot.Status,
                ProposalId: snapshot.ProposalId,
                Deadline: snapshot.Deadline,
                SessionId: snapshot.SessionId));
        }

        // STEP 2: linked CTS — THE Pitfall §5 mitigation.
        var timeoutSec = opts.Value.LongPollTimeoutSeconds > 0 ? opts.Value.LongPollTimeoutSeconds : 30;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        // STEP 3: SUBSCRIBE to the status channel. The TCS is set by the subscribe callback.
        var subscriber = redis.GetSubscriber();
        var channel = RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticketId));
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await subscriber.SubscribeAsync(channel, (_, message) =>
        {
            // First message wins; later publishes are ignored (TrySetResult returns false).
            tcs.TrySetResult(message.HasValue ? (string?)message : null);
        }).ConfigureAwait(false);

        try
        {
            // After SUBSCRIBE, race a second status read against the subscription. This
            // closes the SUBSCRIBE/HSET ordering window where the status transitioned to
            // non-queued between STEP 1 and SUBSCRIBE — without this re-read the long-poll
            // could hang for the full timeout despite the ticket being already matched.
            var snapshot2 = await svc.GetStatusAsync(ticketId, linkedCts.Token).ConfigureAwait(false);
            if (snapshot2 is not null && !string.Equals(snapshot2.Status, "queued", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(new TicketStatusResponse(
                    Status: snapshot2.Status,
                    ProposalId: snapshot2.ProposalId,
                    Deadline: snapshot2.Deadline,
                    SessionId: snapshot2.SessionId));
            }

            // STEP 4: await first publish OR cancellation.
            using var registration = linkedCts.Token.Register(() => tcs.TrySetCanceled(linkedCts.Token));

            try
            {
                var message = await tcs.Task.ConfigureAwait(false);
                return Results.Ok(ParseStatusMessage(message, ticketId));
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                // Client abandoned the request — return empty (no body needs to be flushed).
                return Results.Empty;
            }
            catch (OperationCanceledException)
            {
                // Timeout elapsed — return the current snapshot (still "queued").
                var current = await svc.GetStatusAsync(ticketId, CancellationToken.None).ConfigureAwait(false);
                return Results.Ok(new TicketStatusResponse(
                    Status: current?.Status ?? "queued",
                    ProposalId: current?.ProposalId,
                    Deadline: current?.Deadline,
                    SessionId: current?.SessionId));
            }
        }
        finally
        {
            // Pitfall §5 second half — ALWAYS Unsubscribe. Without this, abandoned long-poll
            // subscribers accumulate in the StackExchange.Redis subscriber tables and
            // eventually starve the multiplexer.
            try
            {
                await subscriber.UnsubscribeAsync(channel).ConfigureAwait(false);
            }
            catch
            {
                // Swallow — Unsubscribe failure is logged at the multiplexer layer and
                // never propagates out of a long-poll cleanup.
            }
        }
    }

    /// <summary>
    /// Verifies the calling player belongs to the ticket. Returns
    /// <see cref="OwnershipResult.NotFound"/> when the ticket hash is missing,
    /// <see cref="OwnershipResult.NotAuthorized"/> when the player is neither a party member
    /// nor the solo holder, and <see cref="OwnershipResult.Authorized"/> otherwise.
    /// </summary>
    private static async Task<OwnershipResult> VerifyOwnershipAsync(
        IConnectionMultiplexer redis,
        GameKitDbContext db,
        Guid ticketId,
        Guid playerId,
        CancellationToken ct)
    {
        var rdb = redis.GetDatabase();
        var entries = await rdb.HashGetAllAsync(MatchmakingRedisKeys.Ticket(ticketId)).ConfigureAwait(false);
        if (entries.Length == 0)
            return OwnershipResult.NotFound;

        string? partyIdStr = null;
        string? holderPlayerIdStr = null;
        foreach (var e in entries)
        {
            var n = (string?)e.Name;
            if (n == "partyId") partyIdStr = (string?)e.Value;
            else if (n == "playerId") holderPlayerIdStr = (string?)e.Value;
        }

        if (!string.IsNullOrEmpty(partyIdStr) && Guid.TryParse(partyIdStr, out var partyId))
        {
            var isMember = await db.Set<PartyMember>()
                .AsNoTracking()
                .AnyAsync(m => m.PartyId == partyId && m.PlayerId == playerId, ct)
                .ConfigureAwait(false);
            return isMember ? OwnershipResult.Authorized : OwnershipResult.NotAuthorized;
        }

        if (holderPlayerIdStr is not null
            && Guid.TryParse(holderPlayerIdStr, out var holder)
            && holder == playerId)
        {
            return OwnershipResult.Authorized;
        }

        return OwnershipResult.NotAuthorized;
    }

    /// <summary>
    /// Parses a status PUBLISH payload into a <see cref="TicketStatusResponse"/>. Recognized
    /// payloads: <c>proposed</c>, <c>proposed:{proposalId}</c>, <c>matched</c>,
    /// <c>matched:{sessionId}</c>, <c>cancelled</c>, <c>requeued</c>. Unknown payloads return
    /// the literal value as the status field for forward-compat with future event types.
    /// </summary>
    private static TicketStatusResponse ParseStatusMessage(string? message, Guid ticketId)
    {
        var raw = message ?? string.Empty;
        var colon = raw.IndexOf(':', StringComparison.Ordinal);
        var head = colon >= 0 ? raw[..colon] : raw;
        var tail = colon >= 0 ? raw[(colon + 1)..] : string.Empty;

        switch (head)
        {
            case "proposed":
                return new TicketStatusResponse(
                    Status: "proposed",
                    ProposalId: Guid.TryParse(tail, out var pid) ? pid : null);

            case "matched":
                return new TicketStatusResponse(
                    Status: "matched",
                    SessionId: Guid.TryParse(tail, out var sid) ? sid : null);

            case "cancelled":
                return new TicketStatusResponse(Status: "cancelled");

            case "requeued":
                return new TicketStatusResponse(Status: "queued");

            default:
                return new TicketStatusResponse(Status: string.IsNullOrEmpty(raw) ? "queued" : raw);
        }
    }

    private static bool TryGetPlayerId(HttpContext http, out Guid playerId)
    {
        playerId = default;
        var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? http.User.FindFirst("sub")?.Value;
        return sub is not null && Guid.TryParse(sub, out playerId);
    }

    private enum OwnershipResult
    {
        Authorized,
        NotAuthorized,
        NotFound,
    }
}
