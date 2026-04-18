// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Globalization;
using System.Threading.RateLimiting;
using GameKit.Core.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Auth.Http.RateLimiting;

/// <summary>
/// Registers three fixed-window rate-limit policies (login / refresh / register) under the
/// Phase-1 <see cref="IGameKitRateLimitPolicies"/> names. Partition key = IP + X-GameKit-Device
/// composite (RESEARCH §8.7) to defend against fingerprint-spray DoS while still allowing a
/// single-NAT device to burst. Rejected requests carry <c>Retry-After</c> and a
/// problem+json body.
/// </summary>
public static class AuthRateLimitRegistrations
{
    /// <summary>Canonical login throttle: 10 requests per minute per (IP, fingerprint) tuple.</summary>
    public const int LoginPermitLimit = 10;

    /// <summary>Canonical refresh throttle: 60 requests per minute per (IP, fingerprint) tuple.</summary>
    public const int RefreshPermitLimit = 60;

    /// <summary>Canonical register throttle: 5 requests per minute per (IP, fingerprint) tuple.</summary>
    public const int RegisterPermitLimit = 5;

    /// <summary>Fixed-window width for all three Auth policies.</summary>
    public static TimeSpan Window => TimeSpan.FromMinutes(1);

    /// <summary>
    /// Adds the three Auth rate-limit policies to <paramref name="services"/>. Idempotent under
    /// repeat-invocation — <c>AddRateLimiter</c> merges subsequent calls into the same options
    /// instance (see <c>Microsoft.AspNetCore.Builder.RateLimiterServiceCollectionExtensions</c>).
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="names">Phase-1 policy-name source.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAuthRateLimits(
        this IServiceCollection services, IGameKitRateLimitPolicies names)
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

            AddPolicy(opt, names.AuthLogin,    permit: LoginPermitLimit,    window: Window);
            AddPolicy(opt, names.AuthRefresh,  permit: RefreshPermitLimit,  window: Window);
            AddPolicy(opt, names.AuthRegister, permit: RegisterPermitLimit, window: Window);
        });

        return services;
    }

    private static void AddPolicy(RateLimiterOptions opt, string name, int permit, TimeSpan window)
    {
        opt.AddPolicy(name, httpContext =>
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var fp = httpContext.Request.Headers["X-GameKit-Device"].ToString();
            var partitionKey = string.IsNullOrEmpty(fp) ? ip : $"{ip}:{fp}";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: partitionKey,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permit,
                    Window = window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    }
}
