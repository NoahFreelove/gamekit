// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Rankings.Algorithms;
using GameKit.Rankings.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Partial class extension for <see cref="RankingsBuilderExtensions"/> that wires the
/// rankings ticker and idempotency cleanup services (plan 04-06):
/// <list type="bullet">
///   <item><see cref="RankingsTickerLeaseHelper"/> — singleton Redis distributed-lock helper.</item>
///   <item><see cref="IRankingsTicker"/> / <see cref="RankingsTickerService"/> — singleton ticker + BackgroundService.</item>
///   <item><see cref="Glicko2Algorithm"/> as the default <see cref="IRankingAlgorithm"/> singleton.</item>
///   <item><see cref="IdempotencyCleanupService"/> — nightly cleanup BackgroundService.</item>
/// </list>
/// </summary>
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Registers the rankings ticker + idempotency cleanup services on <paramref name="services"/>.
    /// Called internally by <see cref="AddRankings"/>.
    /// </summary>
    /// <param name="services">The DI container.</param>
    internal static void AddTickerInfrastructure(IServiceCollection services)
    {
        // Default IRankingAlgorithm: Glicko-2 with τ=0.5 and σ₀=0.06 (Glickman's defaults).
        // Registered as singleton — stateless, reused across ticks.
        services.AddSingleton<IRankingAlgorithm, Glicko2Algorithm>();

        // Lease helper: singleton — manages per-instance fencing token across ticks.
        services.AddSingleton<RankingsTickerLeaseHelper>();

        // Ticker: registered as both IRankingsTicker (for test injection) and as a hosted
        // service (for the hosting pipeline). The same singleton instance is returned for both.
        services.AddSingleton<RankingsTickerService>();
        services.AddSingleton<IRankingsTicker>(sp => sp.GetRequiredService<RankingsTickerService>());
        services.AddHostedService(sp => sp.GetRequiredService<RankingsTickerService>());

        // Idempotency cleanup: nightly cleanup of session_complete_idempotency rows (D-08).
        services.AddSingleton<IdempotencyCleanupService>();
        services.AddHostedService(sp => sp.GetRequiredService<IdempotencyCleanupService>());
    }
}
