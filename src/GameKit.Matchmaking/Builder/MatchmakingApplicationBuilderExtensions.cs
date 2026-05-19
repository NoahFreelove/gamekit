// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Matchmaking.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Extension methods that mount <c>GameKit.Matchmaking</c> middleware + endpoints into the
/// ASP.NET Core pipeline. Mirrors <c>RankingsApplicationBuilderExtensions</c>.
/// </summary>
public static class MatchmakingApplicationBuilderExtensions
{
    /// <summary>
    /// Placeholder extension for future Matchmaking middleware (e.g. observability tags,
    /// per-request matcher correlation IDs). Currently a no-op.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="app"/> is null.</exception>
    public static IApplicationBuilder UseGameKitMatchmaking(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }

    /// <summary>
    /// Maps all Matchmaking HTTP endpoints. Forward-compatible stub — endpoint registration
    /// lands in Plan 05-08 (<c>POST /api/parties</c>, <c>POST /api/parties/join</c>,
    /// <c>POST /api/mm/queue</c>, <c>GET /api/mm/queue/{ticketId}/status</c>,
    /// <c>POST /api/mm/proposal/{id}/accept</c>, <c>POST /api/mm/proposal/{id}/decline</c>,
    /// and admin pause/drain verbs).
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="routes"/> is null.</exception>
    /// <remarks>
    /// This method intentionally maps zero endpoints today so that downstream consumers
    /// (<c>TicTacToeDuel</c>, Plan 05-09) can wire <c>app.MapMatchmaking()</c> from Plan 05-03
    /// onward without breaking changes when 05-08 lands.
    /// </remarks>
    public static IEndpointRouteBuilder MapMatchmaking(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Plan 05-08: 4 party routes + 5 matchmaking routes.
        routes.MapPartyEndpoints();
        routes.MapMatchmakingEndpoints();

        // Plan 05-08 Task 4 wires admin pause/drain verbs separately via
        // MatchmakingAdminEndpoints.MapMatchmakingAdmin — the consumer's pipeline calls it
        // alongside MapGameKitAdmin (it is intentionally NOT included here because admin
        // endpoints live under /admin/api/* and use a different auth scheme).
        return routes;
    }
}
