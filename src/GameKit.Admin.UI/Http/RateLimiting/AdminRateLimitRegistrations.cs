// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Admin.UI.Http.RateLimiting;

/// <summary>
/// Registers the <c>gamekit:admin:login</c> rate-limit policy (D-18): sliding window,
/// 5 permits / 1-minute / per IP, 6 segments (10-second slide granularity). Mirrors the
/// Auth-package pattern but uses a sliding window instead of fixed, and an IP-only partition
/// key because admin operators do not send <c>X-GameKit-Device</c>.
/// </summary>
public static class AdminRateLimitRegistrations
{
    /// <summary>Policy name — referenced by <c>.RequireRateLimiting(AdminRateLimitPolicies.Login)</c>.</summary>
    public const string AdminLoginPolicy = "gamekit:admin:login";

    /// <summary>
    /// Registers the admin rate-limit policies on the supplied <see cref="IServiceCollection"/>.
    /// The caller MUST have previously called <c>services.AddRateLimiter()</c> so the
    /// <see cref="RateLimiterOptions"/> are configurable.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAdminRateLimits(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<RateLimiterOptions>(opts =>
        {
            opts.AddPolicy(AdminLoginPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }));
        });
        return services;
    }
}
