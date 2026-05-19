// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Core.Http.Contracts;
using GameKit.Core.Services;
using GameKit.Rankings.Http.RateLimiting;
using GameKit.Rankings.Json;
using GameKit.Rankings.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Partial class extension for <see cref="RankingsBuilderExtensions"/> that wires the
/// session-complete ports and infrastructure (plan 04-05):
/// <list type="bullet">
///   <item><see cref="ICanonicalRequestHasher"/> → <see cref="CanonicalJsonHasher"/> (singleton)</item>
///   <item><see cref="IPostSessionCompleteHandler"/> → <see cref="PendingRatingUpdatesAdapter"/> (scoped)</item>
///   <item><see cref="IIdempotencyStore"/> → <see cref="RankingsIdempotencyStore"/> (scoped)</item>
///   <item><c>IValidator&lt;SessionCompleteRequest&gt;</c> → <c>SessionCompleteRequestValidator</c> (scoped)</item>
///   <item><c>gamekit:sessions:complete</c> rate-limit policy (D-10, 300 req/min/token)</item>
/// </list>
/// </summary>
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Registers the session-complete ports and infrastructure on <paramref name="services"/>.
    /// Called internally by <see cref="AddRankings"/>.
    /// </summary>
    /// <param name="services">The DI container.</param>
    internal static void AddSessionCompleteInfrastructure(IServiceCollection services)
    {
        // Port: ICanonicalRequestHasher → CanonicalJsonHasher (singleton — pure, stateless).
        services.AddSingleton<ICanonicalRequestHasher, CanonicalJsonHasher>();

        // Port: IPostSessionCompleteHandler → PendingRatingUpdatesAdapter (scoped — uses DbContext).
        services.AddScoped<IPostSessionCompleteHandler, PendingRatingUpdatesAdapter>();

        // Port: IIdempotencyStore → RankingsIdempotencyStore (scoped — uses DbContext).
        services.AddScoped<IIdempotencyStore, RankingsIdempotencyStore>();

        // Validator: resolved by ValidationEndpointFilter<SessionCompleteRequest> at runtime.
        services.AddScoped<IValidator<SessionCompleteRequest>, Http.Validators.SessionCompleteRequestValidator>();

        // Rate-limit policy: 300/min/service-token-name (D-10).
        // WR-03: prefer the no-arg overload that resolves IGameKitRateLimitPolicies from DI,
        // rather than constructing a fresh GameKitRateLimitPolicies() that bypasses the
        // registered singleton.
        services.AddRankingsRateLimits();
    }
}
