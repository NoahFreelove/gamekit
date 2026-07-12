// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Matchmaking.Http.RateLimiting;
using GameKit.Matchmaking.Http.Validators;
using GameKit.Matchmaking.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// HTTP-layer DI registrations for <c>GameKit.Matchmaking</c> (Plan 05-08). Partial-class
/// extension that adds the application services driving the player-facing endpoints +
/// observability port + FluentValidation validators + the matchmaking rate-limit policies.
/// </summary>
public static partial class MatchmakingBuilderExtensions
{
    /// <summary>
    /// Registers the Plan 05-08 HTTP-layer services:
    /// <list type="bullet">
    ///   <item><see cref="IMatchmakingService"/> → <see cref="MatchmakingService"/> (scoped).</item>
    ///   <item><see cref="IBackfillService"/> → <see cref="BackfillService"/> (scoped).</item>
    ///   <item><see cref="IMatchmakingObservability"/> → <see cref="RedisMatchmakingObservability"/> (singleton).</item>
    ///   <item>FluentValidation validators from the <c>GameKit.Matchmaking</c> assembly.</item>
    ///   <item>The matchmaking rate-limit policies (<c>gamekit:mm:enqueue</c> + <c>gamekit:mm:party_join</c>).</item>
    /// </list>
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    internal static IServiceCollection AddHttpServices(this IServiceCollection services)
    {
        services.TryAddScoped<IMatchmakingService, MatchmakingService>();
        services.TryAddScoped<IBackfillService, BackfillService>();
        services.TryAddSingleton<IMatchmakingObservability, RedisMatchmakingObservability>();
        // Admin control surface (Phase 5 UAT-2 D1) — pause/drain flag + audit row.
        // Scoped because IAdminAuditWriter is scoped (writes via the request's DbContext).
        services.TryAddScoped<IMatchmakingControlService, RedisMatchmakingControlService>();

        // FluentValidation 12 — register every validator in this assembly via the canonical
        // AddValidatorsFromAssemblyContaining helper.
        services.AddValidatorsFromAssemblyContaining<JoinPartyRequestValidator>();

        // Matchmaking rate-limit policies (gamekit:mm:enqueue + gamekit:mm:party_join).
        services.AddMatchmakingRateLimits();

        return services;
    }
}
