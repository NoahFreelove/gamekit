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
using Microsoft.Extensions.Options;

namespace GameKit.Rankings.Http.RateLimiting;

/// <summary>
/// Registers rate-limit policies used by <c>GameKit.Rankings</c> endpoints.
/// Currently registers the <c>gamekit:sessions:complete</c> fixed-window policy
/// (300 requests/min partitioned by service-token name, D-10).
/// </summary>
public static class RankingsRateLimitRegistrations
{
    /// <summary>Permit limit for the session-complete policy: 300 requests per minute per service-token.</summary>
    public const int SessionsCompletePermitLimit = 300;

    /// <summary>Fixed-window width for the session-complete policy.</summary>
    public static TimeSpan SessionsCompleteWindow => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Registers the <c>gamekit:sessions:complete</c> fixed-window rate-limit policy (D-10).
    /// Partitioned by authenticated service-token name (<c>ClaimTypes.Name</c>); falls back to
    /// remote IP when the claim is absent (mirrors the Auth rate-limit partition precedent).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Purely additive (WR-03):</b> this method does NOT set <c>RejectionStatusCode</c>,
    /// <c>OnRejected</c>, or any other scalar field on <see cref="RateLimiterOptions"/>.
    /// Those fields are last-write-wins across registered configuration delegates — setting
    /// them here would silently overwrite values configured by the host or by other GameKit
    /// packages (e.g. <c>GameKit.Auth.AddAuthRateLimits</c>). The hosting application is
    /// responsible for wiring <c>OnRejected</c> + <c>RejectionStatusCode</c> once at the
    /// composition root; this method only contributes a single
    /// <see cref="RateLimiterOptions.AddPolicy{TPartitionKey}(string, Func{HttpContext, RateLimitPartition{TPartitionKey}})"/> call.
    /// </para>
    /// </remarks>
    /// <param name="services">The DI container.</param>
    /// <param name="names">Policy-name source from <see cref="IGameKitRateLimitPolicies"/>.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRankingsRateLimits(
        this IServiceCollection services,
        IGameKitRateLimitPolicies names)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(names);

        services.AddRateLimiter(opt => ConfigurePolicy(opt, names));

        return services;
    }

    /// <summary>
    /// Registers the <c>gamekit:sessions:complete</c> fixed-window rate-limit policy,
    /// resolving the policy-name source from DI rather than requiring callers to pass it
    /// explicitly. Use this overload when the host has already registered
    /// <see cref="IGameKitRateLimitPolicies"/> (the standard <c>AddGameKit()</c> path).
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRankingsRateLimits(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Defer policy registration: AddRateLimiter accepts an Action<RateLimiterOptions>
        // that runs when IOptions<RateLimiterOptions> is built, so we cannot resolve from
        // the IServiceProvider here. Instead, register a configurator that closes over a
        // late-bound IGameKitRateLimitPolicies fetched at options-build time.
        services.AddOptions<RateLimiterOptions>().Configure<IGameKitRateLimitPolicies>(
            (opt, names) => ConfigurePolicy(opt, names));

        // Still call AddRateLimiter to ensure the middleware is wired (idempotent).
        services.AddRateLimiter(_ => { });

        return services;
    }

    private static void ConfigurePolicy(RateLimiterOptions opt, IGameKitRateLimitPolicies names)
    {
        // Partition key: service-token name from ClaimTypes.Name; fallback to IP (D-10).
        opt.AddPolicy(names.SessionsComplete, httpContext =>
        {
            var tokenName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
            var partitionKey = string.IsNullOrEmpty(tokenName)
                ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                : $"svc:{tokenName}";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partitionKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = SessionsCompletePermitLimit,
                    Window = SessionsCompleteWindow,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    }
}
