// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Authentication;

/// <summary>
/// Extension method that registers the <c>GameKitServiceToken</c> authentication scheme and the
/// <c>RequiresServiceToken</c> authorization policy (D-05 / T-04-04-AC).
/// Called from <c>RankingsBuilderExtensions.AddRankings</c>.
/// </summary>
public static class ServiceTokenAuthorizationPolicy
{
    /// <summary>
    /// Adds the <c>GameKitServiceToken</c> scheme to the authentication pipeline (additive —
    /// does NOT replace the default scheme set by <c>AddAuth</c>) and registers the
    /// <c>RequiresServiceToken</c> policy requiring an authenticated user with role
    /// <c>service-account</c> via that scheme only.
    /// </summary>
    /// <param name="services">The DI container.</param>
    public static void AddServiceTokenAuthentication(this IServiceCollection services)
    {
        // Additive — does NOT change the default scheme (player JwtBearer remains default).
        services
            .AddAuthentication()
            .AddScheme<ServiceTokenAuthenticationOptions, ServiceTokenAuthenticationHandler>(
                ServiceTokenAuthenticationDefaults.SchemeName,
                _ => { });

        services.AddAuthorization(o =>
            o.AddPolicy(
                ServiceTokenAuthenticationDefaults.PolicyName,
                p => p
                    .AddAuthenticationSchemes(ServiceTokenAuthenticationDefaults.SchemeName)
                    .RequireAuthenticatedUser()
                    .RequireRole("service-account")));
    }
}
