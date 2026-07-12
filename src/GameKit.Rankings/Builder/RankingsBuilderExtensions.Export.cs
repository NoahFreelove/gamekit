// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Rankings.Http.Contracts;
using GameKit.Rankings.Http.Validators;
using GameKit.Rankings.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Partial class extension for <see cref="RankingsBuilderExtensions"/> that wires the
/// GDPR export and rank-adjust services (plan 04-08):
/// <list type="bullet">
///   <item><see cref="IGdprExportService"/> → <see cref="GdprExportService"/> (scoped)</item>
///   <item><see cref="IRankAdjustService"/> → <see cref="RankAdjustService"/> (scoped)</item>
///   <item><c>IValidator&lt;RankAdjustRequest&gt;</c> → <c>RankAdjustRequestValidator</c> (scoped)</item>
/// </list>
/// </summary>
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Registers GDPR export + rank-adjust services on <paramref name="services"/>.
    /// Called internally by <see cref="AddRankings"/>.
    /// </summary>
    /// <param name="services">The DI container.</param>
    internal static void AddExportInfrastructure(IServiceCollection services)
    {
        // GDPR export service — scoped (uses GameKitDbContext).
        services.AddScoped<IGdprExportService, GdprExportService>();

        // Rank-adjust service — scoped (SERIALIZABLE tx via GameKitDbContext).
        services.AddScoped<IRankAdjustService, RankAdjustService>();

        // Validator for RankAdjustRequest — scoped (resolved by ValidationEndpointFilter<RankAdjustRequest>).
        services.AddScoped<IValidator<RankAdjustRequest>, RankAdjustRequestValidator>();
    }
}
