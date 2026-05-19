// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Channels;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.Matchmaking.Strategy;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Strategy-related DI registrations split out of <c>MatchmakingBuilderExtensions</c>
/// per the partial-class pattern Plan 05-03 left behind. Wires:
/// <list type="bullet">
///   <item>Scrutor scan for every <see cref="IMatchmakingStrategy"/> implementation in this assembly.</item>
///   <item>Concrete singletons for <see cref="AtomicClaimScript"/>, <see cref="PartyCodeGenerator"/>, <see cref="PartyRatingAggregatorService"/>.</item>
///   <item>Scoped <see cref="IPartyService"/> → <see cref="PartyService"/> registration.</item>
///   <item>A <b>placeholder</b> bounded <see cref="Channel{T}"/> for <see cref="TicketEvent"/> + derived writer/reader singletons. Plan 05-07 will <c>services.Replace(...)</c> these with options-driven capacity.</item>
/// </list>
/// </summary>
/// <remarks>
/// Plan 05-03 deferred the Scrutor scan + strategy/services/script registrations to Plan
/// 05-04 because the <see cref="IMatchmakingStrategy"/> symbol did not exist at 05-03
/// compile time. The TODO comment in <c>MatchmakingBuilderExtensions.AddMatchmaking</c>
/// is satisfied by this file's <see cref="AddStrategyServices"/> call wired below.
/// </remarks>
public static partial class MatchmakingBuilderExtensions
{
    /// <summary>
    /// Registers the Matchmaking strategy + party services + Redis script executor +
    /// placeholder analytics channel. Idempotent: subsequent calls are no-ops via the
    /// Scrutor dedup contract and <c>TryAdd*</c>-style add-once semantics.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Channel placeholder:</b> the bounded <c>Channel&lt;TicketEvent&gt;</c> here is
    /// intentionally a <b>placeholder</b> with capacity 1000 + <c>DropNewest</c>. Plan
    /// 05-07's <c>AddBackgroundServices()</c> calls <c>services.Replace(...)</c> to bind
    /// the production singleton from <c>GameKitMatchmakingOptions.Analytics.ChannelCapacity</c>
    /// (default 10000 per CONTEXT D-15). The placeholder exists so Wave 3 plans 05-05
    /// (ticker writes events) and 05-06 (proposal accept/decline writes events) resolve
    /// <see cref="ChannelWriter{T}"/> from DI cleanly without depending on 05-07 having
    /// shipped first.
    /// </para>
    /// <para>
    /// <b>Scrutor scope:</b> the assembly scan is rooted at <see cref="EloRangeMatchmakingStrategy"/>
    /// — it picks up every <see cref="IMatchmakingStrategy"/> implementation in
    /// <c>GameKit.Matchmaking</c>. Consumers who ship their own strategy in a separate
    /// assembly register it via <c>services.AddSingleton&lt;IMatchmakingStrategy, MyStrategy&gt;()</c>
    /// BEFORE calling <c>AddMatchmaking()</c>; the Scrutor scan dedups by service+impl
    /// pair so no double-registration occurs.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddStrategyServices(this IServiceCollection services)
    {
        // 1. Scrutor scan for IMatchmakingStrategy implementations. Picks up
        //    EloRangeMatchmakingStrategy in this plan; future operator-authored strategies
        //    in the same assembly (or a consumer assembly registered before AddMatchmaking)
        //    are also discovered.
        //
        //    publicOnly:false — EloRangeMatchmakingStrategy is `public sealed`, but future
        //    internal implementations should also be picked up.
        services.Scan(scan => scan
            .FromAssemblyOf<EloRangeMatchmakingStrategy>()
            .AddClasses(c => c.AssignableTo<IMatchmakingStrategy>(), publicOnly: false)
            .AsImplementedInterfaces()
            .WithSingletonLifetime());

        // 2. Concrete singletons for stateless helpers.
        services.AddSingleton<PartyRatingAggregatorService>();
        services.AddSingleton<AtomicClaimScript>();
        services.AddSingleton<IPartyCodeGenerator, PartyCodeGenerator>();

        // 3. Scoped IPartyService (requires scoped GameKitDbContext).
        services.AddScoped<IPartyService, PartyService>();

        // 4. PLACEHOLDER analytics channel (Plan 05-07 will Replace() with options-driven
        //    instance — capacity 10000 default per CONTEXT D-15). We register the bounded
        //    channel as a singleton + derived writer/reader singletons so 05-05 (ticker)
        //    and 05-06 (proposal service) can take a dep on ChannelWriter<TicketEvent>
        //    from DI without an inter-plan compile-order dependency on 05-07.
        services.AddSingleton<Channel<TicketEvent>>(_ =>
            Channel.CreateBounded<TicketEvent>(new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropNewest,
                SingleReader = true,
                SingleWriter = false,
            }));
        services.AddSingleton<ChannelWriter<TicketEvent>>(sp =>
            sp.GetRequiredService<Channel<TicketEvent>>().Writer);
        services.AddSingleton<ChannelReader<TicketEvent>>(sp =>
            sp.GetRequiredService<Channel<TicketEvent>>().Reader);

        return services;
    }
}
