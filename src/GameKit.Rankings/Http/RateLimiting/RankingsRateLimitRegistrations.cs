// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using GameKit.Core.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

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
    /// <param name="services">The DI container.</param>
    /// <param name="names">Policy-name source from <see cref="IGameKitRateLimitPolicies"/>.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRankingsRateLimits(
        this IServiceCollection services,
        IGameKitRateLimitPolicies names)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(names);

        services.AddRateLimiter(opt =>
        {
            opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opt.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ctx.HttpContext.Response.ContentType = "application/problem+json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://gamekit.dev/errors/rate-limit",
                    title = "Too Many Requests",
                    status = 429,
                }, ct).ConfigureAwait(false);
            };

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
        });

        return services;
    }
}
