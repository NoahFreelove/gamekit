// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Http.EndpointFilters;
using GameKit.Matchmaking.Http.RateLimiting;
using GameKit.Matchmaking.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Matchmaking.Http;

/// <summary>
/// Maps the player-facing party HTTP surface (4 routes — MATCH-03 / CONTEXT D-01..D-05).
/// All routes require JWT authorization (the consumer must call <c>UseGameKitAuth</c>
/// upstream).
/// </summary>
public static class PartyEndpoints
{
    /// <summary>
    /// Maps the party endpoints onto the provided route builder.
    /// <list type="bullet">
    ///   <item><c>POST /api/parties</c> — create.</item>
    ///   <item><c>POST /api/parties/join</c> — join by code; rate-limited per IP (T-05-08-04).</item>
    ///   <item><c>GET  /api/parties/{id}</c> — read.</item>
    ///   <item><c>POST /api/parties/{id}/dissolve</c> — dissolve (owner-only).</item>
    /// </list>
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapPartyEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/parties", CreatePartyAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidationEndpointFilter<CreatePartyRequest>>();

        routes.MapPost("/api/parties/join", JoinPartyAsync)
            .RequireAuthorization()
            .RequireRateLimiting(MatchmakingRateLimitRegistrations.PartyJoinPolicy)
            .AddEndpointFilter<ValidationEndpointFilter<JoinPartyRequest>>();

        routes.MapGet("/api/parties/{id:guid}", GetPartyAsync)
            .RequireAuthorization();

        routes.MapPost("/api/parties/{id:guid}/dissolve", DissolvePartyAsync)
            .RequireAuthorization();

        return routes;
    }

    // ---- handlers ----

    private static async Task<IResult> CreatePartyAsync(
        CreatePartyRequest _,
        HttpContext http,
        IPartyService svc,
        GameKitDbContext db,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Forbid();

        try
        {
            var party = await svc.CreateAsync(playerId, ct).ConfigureAwait(false);
            var response = await BuildPartyResponseAsync(db, party, ct).ConfigureAwait(false);
            return Results.Created($"/api/parties/{party.Id}", response);
        }
        catch (PartyConflictException ex)
        {
            return Results.Conflict(new { error = ex.Code, detail = ex.Message });
        }
    }

    private static async Task<IResult> JoinPartyAsync(
        JoinPartyRequest req,
        HttpContext http,
        IPartyService svc,
        GameKitDbContext db,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Forbid();

        try
        {
            var party = await svc.JoinAsync(req.Code, playerId, ct).ConfigureAwait(false);
            var response = await BuildPartyResponseAsync(db, party, ct).ConfigureAwait(false);
            return Results.Ok(response);
        }
        catch (PartyConflictException ex)
        {
            return Results.Conflict(new { error = ex.Code, detail = ex.Message });
        }
        catch (PartyInvalidStateException ex)
        {
            // party_not_found → 404; party_not_open / already_dissolved → 410 Gone.
            return ex.Code == "party_not_found"
                ? Results.NotFound(new { error = ex.Code, detail = ex.Message })
                : Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: StatusCodes.Status410Gone);
        }
    }

    private static async Task<IResult> GetPartyAsync(
        Guid id,
        HttpContext http,
        GameKitDbContext db,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out _))
            return Results.Forbid();

        var party = await db.Set<Party>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

        if (party is null)
            return Results.NotFound(new { error = "party_not_found", partyId = id });

        var response = await BuildPartyResponseAsync(db, party, ct).ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> DissolvePartyAsync(
        Guid id,
        HttpContext http,
        IPartyService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Forbid();

        try
        {
            await svc.DissolveAsync(id, playerId, ct).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (PartyAuthorizationException ex)
        {
            return Results.Json(new { error = ex.Code, detail = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (PartyInvalidStateException ex)
        {
            return ex.Code == "party_not_found"
                ? Results.NotFound(new { error = ex.Code, detail = ex.Message })
                : Results.Conflict(new { error = ex.Code, detail = ex.Message });
        }
    }

    /// <summary>Extracts and parses the player id from the JWT <c>sub</c> / <c>NameIdentifier</c> claim.</summary>
    private static bool TryGetPlayerId(HttpContext http, out Guid playerId)
    {
        playerId = default;
        var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? http.User.FindFirst("sub")?.Value;
        return sub is not null && Guid.TryParse(sub, out playerId);
    }

    private static async Task<PartyResponse> BuildPartyResponseAsync(
        GameKitDbContext db, Party party, CancellationToken ct)
    {
        var members = await db.Set<PartyMember>()
            .AsNoTracking()
            .Where(m => m.PartyId == party.Id)
            .OrderBy(m => m.JoinedAt)
            .Select(m => m.PlayerId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PartyResponse(
            PartyId: party.Id,
            PartyCode: party.PartyCode,
            State: party.State switch
            {
                PartyState.Open => "open",
                PartyState.Queueing => "queueing",
                PartyState.InMatch => "in_match",
                PartyState.Dissolved => "dissolved",
                _ => party.State.ToString().ToLowerInvariant(),
            },
            MemberPlayerIds: members,
            OwnerPlayerId: party.OwnerPlayerId,
            CreatedAt: party.CreatedAt);
    }
}
