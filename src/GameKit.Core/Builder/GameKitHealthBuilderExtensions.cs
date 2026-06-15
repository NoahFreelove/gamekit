// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using GameKit.Core.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace GameKit.Core.Builder;

/// <summary>
/// Builder extensions that register and map GameKit health-check endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Implements D-01 (built-in ASP.NET Core <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>
/// — zero new NuGet pin), D-02 (<c>AddGameKitHealthChecks</c> / <c>MapGameKitHealth</c> surface),
/// D-03 (tag-based live/ready separation), and D-04 (<c>Degraded</c> → 200).
/// </para>
/// <para>
/// See HLTH-01 and HLTH-02 for the requirements these methods satisfy.
/// </para>
/// </remarks>
public static class GameKitHealthBuilderExtensions
{
    /// <summary>
    /// Registers the GameKit health checks (Postgres <c>SELECT 1</c>, conditional Redis
    /// <c>PING</c>, migrations aggregate) and returns an <see cref="IHealthChecksBuilder"/>
    /// so sibling packages (Matchmaking, Presence, Lobby) can register their own checks
    /// additively via the returned builder.
    /// </summary>
    /// <param name="builder">The existing <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <returns>
    /// An <see cref="IHealthChecksBuilder"/> for chaining additional <c>AddCheck&lt;T&gt;</c>
    /// registrations from sibling packages (D-02).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Call-order contract (Pitfall 1):</b> call <c>AddGameKitHealthChecks()</c> AFTER all
    /// sibling <c>Add*</c> extensions that register <see cref="IConnectionMultiplexer"/>
    /// (i.e., after <c>AddMatchmaking()</c>, <c>AddPresence()</c>, <c>AddLobby()</c>) so that
    /// the conditional Redis check guard reliably sees the multiplexer in the service collection.
    /// This matches the call order used in the <c>TicTacToeDuel</c> sample.
    /// </para>
    /// <para>
    /// <b>Redis check ownership (D-09):</b> the <c>"redis"</c> readiness check is registered
    /// here — and ONLY here — for the entire Phase 14 health surface. Sibling packages do NOT
    /// register their own <c>"redis"</c> checks; one registration covers the shared
    /// <see cref="IConnectionMultiplexer"/> connectivity for every Redis-using package.
    /// </para>
    /// <para>
    /// Checks registered:
    /// <list type="bullet">
    ///   <item><description><c>"postgres"</c> — always, tagged <c>"ready"</c> (D-08).</description></item>
    ///   <item><description><c>"redis"</c> — conditional on <see cref="IConnectionMultiplexer"/> in DI, tagged <c>"ready"</c> (D-09).</description></item>
    ///   <item><description><c>"migrations"</c> — always, tagged <c>"ready"</c> (D-06).</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder AddGameKitHealthChecks(
        this IGameKitBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var hcBuilder = builder.Services.AddHealthChecks();

        // D-08: Postgres SELECT 1 — always registered, tagged "ready"
        hcBuilder.AddCheck<PostgresHealthCheck>("postgres", tags: new[] { "ready" });

        // D-09: Redis PING — registered only when IConnectionMultiplexer is already in DI.
        // Core is the SOLE owner of this "redis" check in Phase 14 (no sibling registers one).
        // The consumer must register IConnectionMultiplexer BEFORE calling AddGameKitHealthChecks()
        // for this guard to fire (see call-order contract in the XML doc above).
        if (builder.Services.Any(
                sd => sd.ServiceType == typeof(IConnectionMultiplexer)))
        {
            hcBuilder.AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready" });
        }

        // D-06: Migrations aggregate — always registered, tagged "ready"
        hcBuilder.AddCheck<MigrationAggregateHealthCheck>("migrations", tags: new[] { "ready" });

        // D-05: Register Core's migration reporter as the first enumerable singleton.
        // Sibling packages add their own five reporters via their own Add* builders.
        builder.Services.AddSingleton<IMigrationReadinessReporter, CoreMigrationReadinessReporter>();

        return hcBuilder;
    }

    /// <summary>
    /// Maps <c>GET /health/live</c> (process-only, 200 while alive) and
    /// <c>GET /health/ready</c> (dependency-gated, <c>Degraded</c> → 200, <c>Unhealthy</c> → 503)
    /// on the given <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    /// <param name="routes">The endpoint route builder from the <c>WebApplication</c> pipeline.</param>
    /// <returns>The same <paramref name="routes"/> for continued chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="routes"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Both endpoints are <c>.AllowAnonymous()</c> and excluded from rate limiting — orchestrator
    /// probes must never be throttled or require authentication (D-02/D-03). Call this method
    /// in the flat endpoint pipeline, OUTSIDE any auth or rate-limit group, BEFORE
    /// <c>MapGameKit()</c> (see <c>TicTacToeDuel/Program.cs</c> for the reference call order).
    /// </para>
    /// <para>
    /// <b>Live endpoint (D-03):</b> <c>/health/live</c> runs zero checks (<c>Predicate = _ =&gt; false</c>)
    /// and always returns HTTP 200 as long as the process is alive — even when Postgres or Redis
    /// is unreachable.
    /// </para>
    /// <para>
    /// <b>Ready endpoint (D-03 / D-04):</b> <c>/health/ready</c> runs only checks tagged
    /// <c>"ready"</c>. The HTTP status codes are set explicitly: <c>Healthy</c> → 200,
    /// <c>Degraded</c> → 200 (stays in rotation — D-04), <c>Unhealthy</c> → 503.
    /// </para>
    /// <para>
    /// The custom <see cref="GameKitHealthResponseWriter"/> is used for both endpoints to emit
    /// only <c>{ status, checks: [{name, status, description}] }</c> (D-12 / HLTH-05).
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapGameKitHealth(
        this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // D-03: liveness — no checks execute; 200 whenever the process is alive
        routes.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = GameKitHealthResponseWriter.WriteAsync,
        }).AllowAnonymous();

        // D-03/D-04: readiness — only "ready"-tagged checks; Degraded→200, Unhealthy→503
        routes.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains("ready"),
            ResponseWriter = GameKitHealthResponseWriter.WriteAsync,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,   // D-04: stays in rotation
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).AllowAnonymous();

        return routes;
    }
}
