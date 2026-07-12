// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Channels;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Partial-class extension methods registering the three Plan 05-07 background services —
/// <see cref="MatchmakingReconcilerService"/>, <see cref="MatchmakingAnalyticsDrainService"/>,
/// <see cref="MatchmakingRetentionCleanupService"/> — plus the bounded
/// <see cref="Channel{T}"/> of <see cref="TicketEvent"/> that the matchmaker producer writes
/// into.
/// </summary>
/// <remarks>
/// <para>
/// <b>Channel rebinding (Wave-3 ordering):</b> Plan 05-04 ships a placeholder
/// <c>Channel&lt;TicketEvent&gt;</c> (capacity 1000, DropNewest) via
/// <c>MatchmakingBuilderExtensions.Strategy.cs</c> so Plans 05-05/05-06 can resolve
/// <c>ChannelWriter&lt;TicketEvent&gt;</c> from DI without depending on Plan 05-07. This
/// plan then <c>services.Replace(...)</c>s the placeholder with an options-driven
/// instance whose capacity equals
/// <see cref="GameKitMatchmakingAnalyticsOptions.ChannelCapacity"/> (10000 default per
/// D-15). The derived <see cref="ChannelWriter{T}"/> + <see cref="ChannelReader{T}"/>
/// singletons are also rebound.
/// </para>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> OpenTelemetry instruments are no-ops
/// unless the host registers <c>AddMeter("GameKit.Matchmaking")</c> in its OpenTelemetry
/// SDK setup. Without this registration, <c>matchmaking.analytics.dropped_events</c>
/// (incremented by <see cref="MatchmakingAnalyticsDrainService"/>) is discarded silently
/// during a Postgres outage. Operators MUST wire the meter to observe this signal.
/// </para>
/// </remarks>
public static partial class MatchmakingBuilderExtensions
{
    /// <summary>
    /// Registers the Plan 05-07 background services + rebinds the bounded TicketEvent
    /// channel from the Plan 05-04 placeholder to the options-driven instance.
    /// </summary>
    /// <param name="services">The service collection being configured by
    /// <c>AddMatchmaking</c>.</param>
    /// <remarks>
    /// Idempotent: uses <c>services.Replace(...)</c> for the channel + reader/writer
    /// singletons so a prior Plan 05-04 placeholder is swapped cleanly. The three
    /// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> registrations use
    /// <c>TryAddEnumerable</c>-equivalent <c>AddHostedService</c> patterns — calling this
    /// method twice would register duplicate hosted services, which is why it is invoked
    /// exactly once from <c>AddMatchmaking</c>.
    /// </remarks>
    internal static void AddBackgroundServices(this IServiceCollection services)
    {
        // 1. Bounded Channel<TicketEvent> — options-driven capacity.
        services.Replace(ServiceDescriptor.Singleton<Channel<TicketEvent>>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<GameKitMatchmakingOptions>>().Value;
            return Channel.CreateBounded<TicketEvent>(new BoundedChannelOptions(opts.Analytics.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropNewest,
                SingleReader = true,
                SingleWriter = false,
            });
        }));

        // 2. ChannelWriter<TicketEvent> + ChannelReader<TicketEvent> derived singletons.
        services.Replace(ServiceDescriptor.Singleton<ChannelWriter<TicketEvent>>(sp =>
            sp.GetRequiredService<Channel<TicketEvent>>().Writer));
        services.Replace(ServiceDescriptor.Singleton<ChannelReader<TicketEvent>>(sp =>
            sp.GetRequiredService<Channel<TicketEvent>>().Reader));

        // 3. Leader-gate helper — IMatchmakerLease.
        //    Plan 05-05 may also register a concrete MatchmakerLeaseHelper; both
        //    implementations honour the same MatcherLock Redis key, so the merge of
        //    Wave 2 / Wave 3 resolves cleanly via DI — the later registration wins.
        services.TryAddSingleton<IMatchmakerLease, RedisMatchmakerLease>();

        // 4. Background services.
        services.AddHostedService<MatchmakingAnalyticsDrainService>();
        services.AddHostedService<MatchmakingReconcilerService>();
        services.AddHostedService<MatchmakingRetentionCleanupService>();

        // 5. Expose testable contracts. Each AddSingleton resolves the same instance the
        //    hosted-service registration uses so the integration tests can inject the
        //    drain / reconciler directly without spinning up the host.
        services.TryAddSingleton<MatchmakingAnalyticsDrainService>();
        services.TryAddSingleton<MatchmakingReconcilerService>();
        services.TryAddSingleton<MatchmakingRetentionCleanupService>();
        services.TryAddSingleton<IMatchmakingAnalyticsDrain>(sp =>
            sp.GetRequiredService<MatchmakingAnalyticsDrainService>());
        services.TryAddSingleton<IMatchmakingReconciler>(sp =>
            sp.GetRequiredService<MatchmakingReconcilerService>());
    }
}
