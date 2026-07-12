// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Presence.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Presence.Builder;

/// <summary>
/// Extension methods that mount <c>GameKit.Presence</c> middleware + endpoints into the
/// ASP.NET Core pipeline. Mirrors <c>MatchmakingApplicationBuilderExtensions</c>.
/// </summary>
public static class PresenceApplicationBuilderExtensions
{
    /// <summary>
    /// Placeholder extension for future Presence middleware (e.g. observability tags,
    /// per-request heartbeat correlation IDs). Currently a no-op — Phase 6 Plan 06-04
    /// stops at the endpoint-mapping surface; consumers may still call this for forward
    /// compatibility.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="app"/> is null.</exception>
    public static IApplicationBuilder UsePresence(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }

    /// <summary>
    /// Maps all Presence HTTP endpoints. In Plan 06-04 the only endpoint mapped is
    /// <c>POST /api/presence/heartbeat</c> (JWT-Bearer required, no rate limit per
    /// CONTEXT D-05). The mapping is wrapped by <see cref="PresenceEndpoints.MapPresenceEndpoints"/>
    /// so additional Presence routes (e.g. an admin top-N endpoint) can be added in
    /// later plans without modifying this builder.
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="routes"/> is null.</exception>
    public static IEndpointRouteBuilder MapPresence(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        routes.MapPresenceEndpoints();
        return routes;
    }
}
