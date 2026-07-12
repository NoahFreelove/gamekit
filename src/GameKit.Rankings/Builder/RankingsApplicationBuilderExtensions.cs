// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Rankings.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Extension methods that mount <c>GameKit.Rankings</c> middleware + endpoints into the
/// ASP.NET Core pipeline.
/// </summary>
public static class RankingsApplicationBuilderExtensions
{
    /// <summary>
    /// Placeholder extension for future Rankings middleware. Currently a no-op — rankings
    /// endpoints are mapped via <c>MapRankings()</c> on the endpoint route builder.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseGameKitRankings(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }

    /// <summary>
    /// Maps all player-facing and admin Rankings endpoints.
    /// Call this in the endpoint-mapping section of the application pipeline after
    /// <c>UseGameKitAuth</c> and <c>UseAuthorization</c> have been registered.
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapRankings(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        routes.MapRankingsPlayer();
        routes.MapRankingsAdmin();
        return routes;
    }
}
