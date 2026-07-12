// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Admin.UI.Http.RateLimiting;

/// <summary>
/// Registers the admin rate-limit policies (D-18): sliding-window, 5 permits / 1-minute / per IP,
/// 6 segments (10-second slide granularity). Mirrors the Auth-package pattern but uses an IP-only
/// partition key because admin operators do not send <c>X-GameKit-Device</c>.
/// </summary>
public static class AdminRateLimitRegistrations
{
    /// <summary>Policy name for the admin login endpoint — referenced by <c>.RequireRateLimiting(AdminLoginPolicy)</c>.</summary>
    public const string AdminLoginPolicy = "gamekit:admin:login";

    /// <summary>
    /// Policy name for the account-merge endpoint (T-10-04-04). Destructive op: 5 requests per minute
    /// per IP. Conservative limit — a superadmin should not need to initiate multiple merges per minute
    /// from the same IP address.
    /// </summary>
    public const string AdminMergePolicy = "gamekit:admin:merge";

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

            // Account-merge rate limit (T-10-04-04): 5 per minute per IP.
            // Mirrors AdminLoginPolicy pattern — same per-IP partition key, same sliding-window shape.
            opts.AddPolicy(AdminMergePolicy, httpContext =>
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
