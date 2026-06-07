// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Lobby.Http;
using GameKit.Lobby.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Lobby.Builder;

/// <summary>
/// Extension methods that mount <c>GameKit.Lobby</c> middleware and endpoints into the
/// ASP.NET Core pipeline.
/// </summary>
public static class LobbyApplicationBuilderExtensions
{
    /// <summary>
    /// Placeholder extension for future Lobby-specific middleware (currently a no-op).
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="app"/> is <see langword="null"/>.</exception>
    public static IApplicationBuilder UseGameKitLobby(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }

    /// <summary>
    /// Maps the Lobby SignalR hub at <c>/hubs/lobby</c> and the REST endpoints
    /// (<c>POST /api/lobbies</c>, <c>GET /api/lobbies/{lobbyId}</c>,
    /// <c>DELETE /api/lobbies/{lobbyId}/members/{playerId}</c>).
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="routes"/> is <see langword="null"/>.</exception>
    public static IEndpointRouteBuilder MapLobby(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // SignalR hub — [Authorize]-gated, JWT extracted from query string via
        // LobbyJwtBearerPostConfigure for WebSocket upgrades (SC#2).
        routes.MapHub<LobbyHub>("/hubs/lobby");

        // REST endpoints: POST /api/lobbies, GET /api/lobbies/{id}, DELETE members.
        routes.MapLobbyEndpoints();

        return routes;
    }
}
