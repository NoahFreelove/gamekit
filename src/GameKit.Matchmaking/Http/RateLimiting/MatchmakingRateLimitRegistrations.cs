// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Threading.RateLimiting;
using GameKit.Core.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Matchmaking.Http.RateLimiting;

/// <summary>
/// Registers rate-limit policies used by <c>GameKit.Matchmaking</c> endpoints (MATCH-11):
/// <list type="bullet">
///   <item><c>gamekit:mm:enqueue</c> — sliding window, 5 requests/min/player (RESEARCH §Decision 10).</item>
///   <item><c>gamekit:mm:party_join</c> — sliding window, 5 requests/min/IP (T-05-08-04 — anti-enumeration).</item>
/// </list>
/// </summary>
public static class MatchmakingRateLimitRegistrations
{
    /// <summary>Permit limit for the player-enqueue policy (5 req / 1 min / partition).</summary>
    public const int EnqueuePermitLimit = 5;

    /// <summary>Sliding window for the player-enqueue policy.</summary>
    public static TimeSpan EnqueueWindow => TimeSpan.FromMinutes(1);

    /// <summary>Number of segments for the player-enqueue sliding window.</summary>
    public const int EnqueueSegments = 6;

    /// <summary>Canonical policy name for the per-IP party-join rate limit (T-05-08-04).</summary>
    public const string PartyJoinPolicy = "gamekit:mm:party_join";

    /// <summary>Permit limit for the party-join policy (5 req / 1 min / IP).</summary>
    public const int PartyJoinPermitLimit = 5;

    /// <summary>Sliding window for the party-join policy.</summary>
    public static TimeSpan PartyJoinWindow => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Registers the matchmaking rate-limit policies (player enqueue + party join).
    /// Purely additive (WR-03 / Rankings precedent) — does not set
    /// <c>RejectionStatusCode</c> or <c>OnRejected</c>.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="names">Policy-name source from <see cref="IGameKitRateLimitPolicies"/>.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddMatchmakingRateLimits(
        this IServiceCollection services,
        IGameKitRateLimitPolicies names)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(names);

        services.AddRateLimiter(opt => ConfigurePolicies(opt, names));

        return services;
    }

    /// <summary>
    /// Registers the matchmaking rate-limit policies, resolving
    /// <see cref="IGameKitRateLimitPolicies"/> from DI at options-build time. Use this
    /// overload when the host has already registered the names provider via
    /// <c>AddGameKit()</c>.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddMatchmakingRateLimits(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<RateLimiterOptions>().Configure<IGameKitRateLimitPolicies>(
            (opt, names) => ConfigurePolicies(opt, names));

        // Ensure the middleware is wired (idempotent).
        services.AddRateLimiter(_ => { });
        return services;
    }

    private static void ConfigurePolicies(RateLimiterOptions opt, IGameKitRateLimitPolicies names)
    {
        // Player-enqueue policy — partition by NameIdentifier (canonical PlayerId), fall back
        // to RemoteIp when the claim is absent (e.g. anonymous-rejected callers — but the
        // endpoint requires authorization so the fallback is purely defensive).
        opt.AddPolicy(names.MmEnqueue, httpContext =>
        {
            var playerId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var partitionKey = string.IsNullOrEmpty(playerId)
                ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                : $"player:{playerId}";

            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: partitionKey,
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = EnqueuePermitLimit,
                    Window = EnqueueWindow,
                    SegmentsPerWindow = EnqueueSegments,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });

        // Per-IP party-join rate limit (T-05-08-04 — anti-enumeration). The party-code
        // namespace is 32^6 ≈ 1B for a 6-char code, so 5/min/IP makes brute force a
        // multi-decade exercise. Partition strictly by RemoteIp (the JWT claim is irrelevant —
        // an attacker could rotate JWT identities; the IP is the genuinely-limiting handle).
        opt.AddPolicy(PartyJoinPolicy, httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"ip:{ip}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = PartyJoinPermitLimit,
                    Window = PartyJoinWindow,
                    SegmentsPerWindow = EnqueueSegments,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    }
}
