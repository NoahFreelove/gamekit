// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Lobby.Http.Contracts;
using GameKit.Lobby.Http.EndpointFilters;
using GameKit.Lobby.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Lobby.Http;

/// <summary>
/// Maps the Lobby REST endpoints:
/// <list type="bullet">
///   <item><c>POST   /api/lobbies</c> — create a lobby (LOBBY-02).</item>
///   <item><c>GET    /api/lobbies/{lobbyId}</c> — retrieve a lobby by id.</item>
///   <item><c>POST   /api/lobbies/{lobbyId}/join</c> — join a lobby (LOBBY-02 / CR-01).</item>
///   <item><c>DELETE /api/lobbies/{lobbyId}/members/{playerId}</c> — remove a member (owner-or-self).</item>
/// </list>
/// All routes require JWT authorization. Domain exceptions are mapped to appropriate HTTP status
/// codes per the GameKit convention (WR-02).
/// </summary>
public static class LobbyEndpoints
{
    /// <summary>Maps the Lobby REST endpoints onto the provided route builder.</summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapLobbyEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/lobbies", CreateLobbyAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidationEndpointFilter<CreateLobbyRequest>>();

        routes.MapGet("/api/lobbies/{lobbyId:guid}", GetLobbyAsync)
            .RequireAuthorization();

        routes.MapPost("/api/lobbies/{lobbyId:guid}/join", JoinLobbyAsync)
            .RequireAuthorization()
            .AddEndpointFilter<ValidationEndpointFilter<JoinLobbyRequest>>();

        routes.MapDelete("/api/lobbies/{lobbyId:guid}/members/{targetPlayerId:guid}", RemoveMemberAsync)
            .RequireAuthorization();

        return routes;
    }

    // ---- handlers ----

    private static async Task<IResult> CreateLobbyAsync(
        CreateLobbyRequest req,
        HttpContext http,
        ILobbyService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Unauthorized();

        var lobby = await svc.CreateLobbyAsync(
            playerId,
            req.MaxMembers,
            req.LadderId,
            req.RegionName,
            ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            lobbyId = lobby.Id,
            state = lobby.State.ToString(),
            maxMembers = lobby.MaxMembers,
            regionName = lobby.RegionName,
            ladderId = lobby.LadderId,
            createdAt = lobby.CreatedAt,
        });
    }

    private static async Task<IResult> GetLobbyAsync(
        Guid lobbyId,
        HttpContext http,
        ILobbyService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out _))
            return Results.Unauthorized();

        var lobby = await svc.GetLobbyAsync(lobbyId, ct).ConfigureAwait(false);
        if (lobby is null)
            return Results.NotFound(new { error = "lobby_not_found" });

        return Results.Ok(new
        {
            lobbyId = lobby.Id,
            ownerId = lobby.OwnerId,
            ladderId = lobby.LadderId,
            state = lobby.State.ToString(),
            maxMembers = lobby.MaxMembers,
            regionName = lobby.RegionName,
            memberCount = lobby.Members.Count,
            createdAt = lobby.CreatedAt,
            updatedAt = lobby.UpdatedAt,
        });
    }

    private static async Task<IResult> JoinLobbyAsync(
        Guid lobbyId,
        HttpContext http,
        ILobbyService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
            return Results.Unauthorized();

        try
        {
            var lobby = await svc.JoinLobbyAsync(lobbyId, playerId, ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                lobbyId = lobby.Id,
                state = lobby.State.ToString(),
                maxMembers = lobby.MaxMembers,
                memberCount = lobby.Members.Count,
            });
        }
        catch (LobbyNotFoundException)      { return Results.NotFound(new { error = "lobby_not_found" }); }
        catch (LobbyFullException ex)       { return Results.Conflict(new { error = "lobby_full", maxMembers = ex.MaxMembers }); }
        catch (AlreadyMemberException)      { return Results.Conflict(new { error = "already_member" }); }
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid lobbyId,
        Guid targetPlayerId,
        HttpContext http,
        ILobbyService svc,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var actorId))
            return Results.Unauthorized();

        try
        {
            await svc.RemoveMemberAsync(lobbyId, actorId, targetPlayerId, ct).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (LobbyNotFoundException)         { return Results.NotFound(new { error = "lobby_not_found" }); }
        catch (LobbyAuthorizationException)    { return Results.Forbid(); }
        catch (NotAMemberException)            { return Results.NotFound(new { error = "member_not_found" }); }
    }

    // ---- helpers ----

    /// <summary>Extracts and parses the player id from the JWT claim (HTTP context).</summary>
    private static bool TryGetPlayerId(HttpContext http, out Guid playerId)
    {
        playerId = default;
        var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? http.User.FindFirst("sub")?.Value;
        return sub is not null && Guid.TryParse(sub, out playerId);
    }
}
