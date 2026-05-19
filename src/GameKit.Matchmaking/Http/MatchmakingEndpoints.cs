// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.RateLimiting;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Http.EndpointFilters;
using GameKit.Matchmaking.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Http;

/// <summary>
/// Maps the player-facing matchmaking HTTP surface (5 routes — MATCH-01, MATCH-11):
/// <list type="bullet">
///   <item><c>POST   /api/mm/queue</c> — enqueue; rate-limited via <c>gamekit:mm:enqueue</c>.</item>
///   <item><c>GET    /api/mm/queue/{ticketId}/status</c> — long-poll (Pitfall §5 mitigation; handler in Task 2b).</item>
///   <item><c>DELETE /api/mm/queue/{ticketId}</c> — cancel.</item>
///   <item><c>POST   /api/mm/proposal/{proposalId}/accept</c> — accept-step (T-05-06-01).</item>
///   <item><c>POST   /api/mm/proposal/{proposalId}/decline</c> — accept-step.</item>
/// </list>
/// All routes require JWT authorization (consumer's pipeline must run <c>UseGameKitAuth</c>).
/// </summary>
public static class MatchmakingEndpoints
{
    /// <summary>Maps the matchmaking endpoints onto the provided route builder.</summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapMatchmakingEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var names = routes.ServiceProvider.GetRequiredService<IGameKitRateLimitPolicies>();

        routes.MapPost("/api/mm/queue", EnqueueAsync)
            .RequireAuthorization()
            .RequireRateLimiting(names.MmEnqueue)
            .AddEndpointFilter<ValidationEndpointFilter<EnqueueRequest>>();

        routes.MapGet("/api/mm/queue/{ticketId:guid}/status", LongPollStatusAsync)
            .RequireAuthorization();

        routes.MapDelete("/api/mm/queue/{ticketId:guid}", CancelAsync)
            .RequireAuthorization();

        routes.MapPost("/api/mm/proposal/{proposalId:guid}/accept", AcceptAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidationEndpointFilter<AcceptDeclineRequest>>();

        routes.MapPost("/api/mm/proposal/{proposalId:guid}/decline", DeclineAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidationEndpointFilter<AcceptDeclineRequest>>();

        return routes;
    }

    // ---- handlers ----

    private static async Task<IResult> EnqueueAsync(
        EnqueueRequest req,
        HttpContext http,
        IMatchmakingService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Forbid();

        var result = await svc.EnqueueAsync(playerId, req.LadderId, req.PoolName, req.PartyId, ct).ConfigureAwait(false);
        return result.Outcome switch
        {
            EnqueueOutcome.Queued => Results.Ok(new { ticketId = result.TicketId!.Value, status = "queued" }),
            EnqueueOutcome.RejectedDueToCooldown => Results.Json(
                new
                {
                    error = "decline_cooldown_active",
                    retryAfterSeconds = result.RetryAfter is { } ra ? (int)Math.Ceiling(ra.TotalSeconds) : 0,
                    detail = result.Detail,
                },
                statusCode: StatusCodes.Status403Forbidden),
            EnqueueOutcome.RejectedDueToSpread => Results.BadRequest(new
            {
                error = "party_rating_spread_exceeded",
                detail = result.Detail,
            }),
            EnqueueOutcome.AlreadyEnqueued => Results.Conflict(new { error = "ticket_active", detail = result.Detail }),
            EnqueueOutcome.UnknownLadder => Results.BadRequest(new { error = "unknown_ladder", detail = result.Detail }),
            EnqueueOutcome.InvalidParty => Results.BadRequest(new { error = "invalid_party", detail = result.Detail }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Long-poll status endpoint — D-10 contract. Delegates to
    /// <see cref="LongPollStatusHandler.HandleAsync"/> which carries the Pitfall §5
    /// linked-CTS subscription-leak guard.
    /// </summary>
    private static Task<IResult> LongPollStatusAsync(
        Guid ticketId,
        HttpContext http,
        IMatchmakingService svc,
        IConnectionMultiplexer redis,
        IOptions<GameKitMatchmakingOptions> opts,
        GameKitDbContext db,
        CancellationToken ct)
        => LongPollStatusHandler.HandleAsync(http, ticketId, svc, redis, opts, db, ct);

    private static async Task<IResult> CancelAsync(
        Guid ticketId,
        HttpContext http,
        IMatchmakingService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Forbid();

        var result = await svc.CancelAsync(ticketId, playerId, ct).ConfigureAwait(false);
        return result.Outcome switch
        {
            CancelOutcome.Cancelled => Results.NoContent(),
            CancelOutcome.NotFound => Results.NotFound(new { error = "ticket_not_found", ticketId }),
            CancelOutcome.NotAuthorized => Results.Forbid(),
            CancelOutcome.Terminal => Results.Conflict(new { error = "ticket_terminal", ticketId }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> AcceptAsync(
        Guid proposalId,
        AcceptDeclineRequest req,
        HttpContext http,
        IProposalService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Forbid();

        var result = await svc.AcceptAsync(proposalId, req.TicketId, playerId, ct).ConfigureAwait(false);
        return result switch
        {
            AcceptResult.Accepted => Results.Ok(new TicketStatusResponse(Status: "queued", ProposalId: proposalId)),
            AcceptResult.AlreadyAccepted => Results.Ok(new TicketStatusResponse(Status: "queued", ProposalId: proposalId)),
            AcceptResult.AllAccepted => Results.Ok(new TicketStatusResponse(Status: "matched", ProposalId: proposalId)),
            AcceptResult.ProposalNotFound => Results.NotFound(new { error = "proposal_not_found", proposalId }),
            AcceptResult.NotInProposal => Results.Forbid(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> DeclineAsync(
        Guid proposalId,
        AcceptDeclineRequest req,
        HttpContext http,
        IProposalService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Forbid();

        var result = await svc.DeclineAsync(proposalId, req.TicketId, playerId, ct).ConfigureAwait(false);
        return result switch
        {
            DeclineResult.Declined => Results.Ok(new TicketStatusResponse(Status: "cancelled", ProposalId: proposalId)),
            DeclineResult.ProposalNotFound => Results.NotFound(new { error = "proposal_not_found", proposalId }),
            DeclineResult.NotInProposal => Results.Forbid(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>Extracts and parses the player id from the JWT claim.</summary>
    private static bool TryGetPlayerId(HttpContext http, out Guid playerId)
    {
        playerId = default;
        var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? http.User.FindFirst("sub")?.Value;
        return sub is not null && Guid.TryParse(sub, out playerId);
    }
}
