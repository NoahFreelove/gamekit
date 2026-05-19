// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Http.Contracts;
using GameKit.Core.Http.EndpointFilters;
using GameKit.Core.RateLimiting;
using GameKit.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Core.Http;

/// <summary>
/// Registers the session-management endpoint group (<c>/api/sessions</c>) in <c>GameKit.Core</c>.
/// Currently exposes <c>POST /api/sessions/{id}/complete</c> (D-07, D-08, D-22, RANK-11).
/// </summary>
public static class SessionEndpoints
{
    /// <summary>
    /// Maps the <c>/api/sessions</c> endpoint group onto <paramref name="routes"/>.
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <param name="policies">Rate-limit policy names (from <see cref="IGameKitRateLimitPolicies"/>).</param>
    /// <returns>The route group builder for further composition.</returns>
    public static RouteGroupBuilder MapSessions(
        this IEndpointRouteBuilder routes,
        IGameKitRateLimitPolicies policies)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(policies);

        var group = routes.MapGroup("/api/sessions").WithTags("GameKit.Core");

        group.MapPost("/{id}/complete", CompleteSessionAsync)
            // Validates the Idempotency-Key header (D-08 / T-04-05-MK).
            .AddEndpointFilter<IdempotencyKeyEndpointFilter>()
            // Validates the request body via IValidator<SessionCompleteRequest> (D-09).
            .AddEndpointFilter<ValidationEndpointFilter<SessionCompleteRequest>>()
            // Rate-limit: 300 requests/min/service-token (D-10).
            .RequireRateLimiting(policies.SessionsComplete)
            // Auth: only service-account tokens (GameKitServiceToken scheme).
            // Policy name referenced as a string literal so Core has zero compile-time dep on
            // GameKit.Rankings (D-22 invariant).
            // The literal "RequiresServiceToken" matches ServiceTokenAuthenticationDefaults.PolicyName
            // in src/GameKit.Rankings/Authentication/ServiceTokenAuthenticationDefaults.cs.
            .RequireAuthorization("RequiresServiceToken");

        return group;
    }

    private static async Task<IResult> CompleteSessionAsync(
        Guid id,
        SessionCompleteRequest req,
        ISessionCompleteService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // IdempotencyKeyEndpointFilter validated the header and stored the value in Items.
        var idempotencyKey = httpContext.Items[IdempotencyKeyEndpointFilter.ItemsKey] as string
            ?? httpContext.Request.Headers[IdempotencyKeyEndpointFilter.HeaderName].ToString();

        var result = await service.CompleteAsync(id, idempotencyKey, req, ct);

        return result switch
        {
            SessionCompleteResult.Completed c => Results.Ok(c.Response),
            SessionCompleteResult.AlreadyCompletedCached acc => Results.Ok(acc.Response),
            SessionCompleteResult.IdempotencyKeyReused => Results.Conflict(new
            {
                type = "https://gamekit.dev/errors/idempotency-key-reused",
                title = "Idempotency Key Reused",
                detail = "The supplied Idempotency-Key was used for a different request body.",
                status = 409,
                error = "idempotency_key_reused",
            }),
            SessionCompleteResult.SessionNotFound => Results.NotFound(new
            {
                type = "https://gamekit.dev/errors/session-not-found",
                title = "Session Not Found",
                detail = $"No session with id '{id}' was found.",
                status = 404,
            }),
            SessionCompleteResult.InvalidState s => Results.Conflict(new
            {
                type = "https://gamekit.dev/errors/invalid-session-state",
                title = "Invalid Session State",
                detail = $"Session is in state '{s.CurrentState}' and cannot be completed.",
                status = 409,
                error = "invalid_session_state",
                currentState = s.CurrentState.ToString(),
            }),
            SessionCompleteResult.UnknownParticipant u => Results.NotFound(new
            {
                type = "https://gamekit.dev/errors/unknown-participant",
                title = "Unknown Participant",
                detail = $"Player '{u.PlayerId}' is not a participant in this session.",
                status = 404,
            }),
            SessionCompleteResult.MissingParticipant m => Results.BadRequest(new
            {
                type = "https://gamekit.dev/errors/missing-participant",
                title = "Missing Participant",
                detail = $"Player '{m.PlayerId}' is recorded on the session but was not included in the request.",
                status = 400,
                error = "missing_participant",
            }),
            _ => Results.StatusCode(500),
        };
    }
}
