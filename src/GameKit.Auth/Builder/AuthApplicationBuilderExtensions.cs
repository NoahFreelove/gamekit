// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using GameKit.Auth.Data;
using GameKit.Auth.Http;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Auth.Builder;

/// <summary>
/// Extension methods that mount GameKit.Auth middleware + endpoints. <c>UseGameKitAuth()</c>
/// MUST be called BEFORE <c>UseGameKit()</c> so <c>UseAuthentication</c> runs ahead of Core's
/// <c>UseAuthorization</c> (RESEARCH §8.12 #6 middleware ordering fix).
/// </summary>
public static class AuthApplicationBuilderExtensions
{
    /// <summary>
    /// Inserts <c>UseAuthentication()</c> into the pipeline. Call immediately before
    /// <c>UseGameKit()</c> — the ordering is strict:
    /// <c>UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit (→ authorize) → map*</c>.
    /// Auth migrations (<c>__ef_migrations_auth</c>) are applied by a hosted service registered
    /// via <c>AddAuth(...)</c>; they run AFTER Core's <c>UseGameKit</c> migration so FK references
    /// to <c>gamekit.players</c> resolve.
    /// </summary>
    public static IApplicationBuilder UseGameKitAuth(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseAuthentication();
        return app;
    }

    /// <summary>
    /// Maps the <c>/auth/*</c> endpoint group: <c>/login/{provider}</c>, <c>/refresh</c>,
    /// <c>/register</c>, <c>/logout</c>, <c>/logout/all</c>, <c>/me</c>, <c>/challenge/{provider}</c>,
    /// <c>/callback/{provider}</c>, <c>/link/{provider}</c>. Rate-limit policies (<c>gamekit:auth:login</c>,
    /// <c>gamekit:auth:refresh</c>, <c>gamekit:auth:register</c>) and FluentValidation endpoint
    /// filters are applied per-endpoint by <see cref="AuthEndpoints.MapAuthEndpoints"/>.
    /// </summary>
    /// <remarks>
    /// Consumer middleware order is strict — see <see cref="UseGameKitAuth"/>:
    /// <c>UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit → MapGameKit + MapAuth</c>.
    /// Deviating causes authenticated endpoints (<c>/auth/me</c>, <c>/auth/link</c>, etc.) to 401
    /// even with a valid bearer token (RESEARCH §8.12 #6).
    /// </remarks>
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var policies = routes.ServiceProvider.GetRequiredService<IGameKitRateLimitPolicies>();
        AuthEndpoints.MapAuthEndpoints(routes, policies);
        return routes;
    }
}
