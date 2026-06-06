// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Rankings.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Partial-class extension for <see cref="RankingsBuilderExtensions"/> that wires the
/// rank-decay background service (RANK-15):
/// <list type="bullet">
///   <item><see cref="RankDecayLeaseHelper"/> — singleton Redis distributed-lock helper for decay.</item>
///   <item><see cref="RankDecayBackgroundService"/> — leader-elected decay BackgroundService.</item>
/// </list>
/// </summary>
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Registers the rank-decay lease helper and background service on <paramref name="services"/>.
    /// Called internally by <see cref="AddRankings"/>.
    /// </summary>
    /// <param name="services">The DI container.</param>
    internal static void AddDecayInfrastructure(IServiceCollection services)
    {
        // Decay lease helper: singleton — manages per-instance fencing token across decay runs.
        services.AddSingleton<RankDecayLeaseHelper>();

        // Decay background service: leader-elected; inflates RD for inactive above-threshold players.
        services.AddHostedService<RankDecayBackgroundService>();
    }
}
