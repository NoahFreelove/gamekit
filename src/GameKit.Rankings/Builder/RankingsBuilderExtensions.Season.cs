// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Rankings.Http.Contracts;
using GameKit.Rankings.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Partial class extension for <see cref="RankingsBuilderExtensions"/> that wires the
/// season-management and leaderboard services (plan 04-07):
/// <list type="bullet">
///   <item><see cref="ILeaderboardService"/> → <see cref="LeaderboardService"/> (scoped)</item>
///   <item><see cref="IEndSeasonService"/> → <see cref="EndSeasonService"/> (scoped)</item>
///   <item><c>IValidator&lt;EndSeasonRequest&gt;</c> → <c>EndSeasonRequestValidator</c> (scoped)</item>
/// </list>
/// </summary>
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Registers the season-management + leaderboard services on <paramref name="services"/>.
    /// Called internally by <see cref="AddRankings"/>.
    /// </summary>
    /// <param name="services">The DI container.</param>
    internal static void AddSeasonInfrastructure(IServiceCollection services)
    {
        // Leaderboard service — scoped (uses GameKitDbContext).
        services.AddScoped<ILeaderboardService, LeaderboardService>();

        // Season-end service — scoped (SERIALIZABLE tx via GameKitDbContext + IAdminAuditWriter).
        services.AddScoped<IEndSeasonService, EndSeasonService>();

        // Validator — scoped (resolved by ValidationEndpointFilter<EndSeasonRequest>).
        services.AddScoped<IValidator<EndSeasonRequest>, Http.Validators.EndSeasonRequestValidator>();
    }
}
