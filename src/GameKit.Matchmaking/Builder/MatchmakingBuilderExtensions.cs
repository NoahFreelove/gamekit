// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Health;
using GameKit.Matchmaking.Data;
using GameKit.Matchmaking.Health;
using GameKit.Matchmaking.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Fluent-builder extensions that mount <c>GameKit.Matchmaking</c> onto an existing
/// <see cref="IGameKitBuilder"/>. Declared <see langword="partial"/> so concern-specific
/// plan files (05-04 strategy, 05-05 ticker, 05-06 proposals, 05-07 reconciler / analytics,
/// 05-08 endpoints) can add their own extension methods without modifying this file.
/// </summary>
public static partial class MatchmakingBuilderExtensions
{
    /// <summary>
    /// Registers <c>GameKit.Matchmaking</c> services:
    /// <list type="bullet">
    ///   <item>Options + validator: <see cref="GameKitMatchmakingOptions"/> validated by
    ///         <see cref="MatchmakingOptionsValidator"/> (fail-fast at host startup).</item>
    ///   <item><c>MatchmakingModelBuilderExtension</c> via <c>TryAddEnumerable</c> so
    ///         Matchmaking entities land in <c>GameKitDbContext</c> at runtime.</item>
    ///   <item><c>MatchmakingMigrationHostedService</c> — applies
    ///         <c>__ef_migrations_matchmaking</c> at startup under the per-package
    ///         advisory-lock key (<c>388956820L</c>, Plan 05-02).</item>
    ///   <item>The matchmaking builder itself, returned for chained
    ///         <c>.AddLadder(...)</c> calls.</item>
    /// </list>
    /// </summary>
    /// <param name="builder">The existing <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <param name="configure">Callback to populate <see cref="GameKitMatchmakingOptions"/>.</param>
    /// <returns>An <see cref="IGameKitMatchmakingBuilder"/> for further ladder registration.</returns>
    /// <remarks>
    /// <para>
    /// This plan (05-03) wires only the configuration surface. Concrete services
    /// (<c>IMatchmakingStrategy</c>, <c>IPartyService</c>, <c>IMatchmakerTicker</c>,
    /// <c>MatchmakingReconcilerService</c>, <c>MatchmakingAnalyticsDrainService</c>,
    /// <c>MatchmakingRetentionCleanupService</c>, etc.) are registered by their respective
    /// downstream plans (05-04..05-07).
    /// </para>
    /// <para>
    /// The Scrutor assembly-scan for <c>IMatchmakingStrategy</c> implementations is
    /// deferred to Plan 05-04 — the interface symbol lives in that plan and emitting the
    /// scan here would force a compile-time dependency on a type that does not yet exist.
    /// </para>
    /// <para>
    /// <b>OpenTelemetry meter registration (Pitfall §7):</b> the analytics drain service
    /// emits an OTel counter <c>matchmaking.analytics.dropped_events</c> when the bounded
    /// channel is full or Postgres retries are exhausted. The counter is a no-op unless
    /// the host registers <c>AddMeter("GameKit.Matchmaking")</c> in its OpenTelemetry
    /// SDK setup. Operators MUST wire the meter to observe the dropped-event signal during
    /// a Postgres outage.
    /// </para>
    /// </remarks>
    public static IGameKitMatchmakingBuilder AddMatchmaking(
        this IGameKitBuilder builder,
        Action<GameKitMatchmakingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 1. Bind + validate options. ValidateOnStart guarantees IValidateOptions runs at host
        //    startup (mitigates T-05-03-01: misconfigured matcher causes runtime divide-by-zero).
        var optsBuilder = builder.Services.AddOptions<GameKitMatchmakingOptions>();
        if (configure is not null)
            optsBuilder.Configure(configure);
        optsBuilder.ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GameKitMatchmakingOptions>, MatchmakingOptionsValidator>());

        // 2. Matchmaking model extension — contributes the five Matchmaking entities to
        //    GameKitDbContext at runtime (Plan 05-02 defines the configurations).
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, MatchmakingModelBuilderExtension>());

        // 3. Migration runner — applies __ef_migrations_matchmaking at startup under the
        //    Matchmaking advisory-lock key.
        builder.Services.AddHostedService<MatchmakingMigrationHostedService>();

        // 3a. Migration readiness reporter — sixth IMigrationReadinessReporter; consumed by
        //     Core's MigrationAggregateHealthCheck to gate /health/ready (D-05/D-06/HLTH-02).
        builder.Services.AddSingleton<IMigrationReadinessReporter, MatchmakingMigrationReadinessReporter>();

        // 3b. Matchmaking-leader readiness check — Degraded-only; surfaces holder InstanceId +
        //     TTL for HLTH-04. The "redis" connectivity gate is owned solely by Core's
        //     AddGameKitHealthChecks() (D-09, OQ-1 RESOLVED) — Matchmaking registers ONLY this
        //     distinct "matchmaking-leader" check here.
        builder.Services.AddHealthChecks()
            .AddCheck<MatchmakingLeaderHealthCheck>("matchmaking-leader", tags: new[] { "ready" });

        // 4. Register the matchmaking builder as a singleton so downstream services can resolve
        //    IGameKitMatchmakingBuilder + RegisteredLadders directly from DI. Also publish the
        //    accumulated ladder list as a singleton IReadOnlyList<MatchmakingLadderConfig> so
        //    concrete services (Plan 05-04+) can inject the per-ladder config tree without
        //    taking a dep on the builder interface.
        var matchmakingBuilder = new GameKitMatchmakingBuilder(builder.Services);
        builder.Services.AddSingleton<IGameKitMatchmakingBuilder>(matchmakingBuilder);
        builder.Services.AddSingleton<System.Collections.Generic.IReadOnlyList<MatchmakingLadderConfig>>(
            _ => matchmakingBuilder.RegisteredLadders);

        // 5. Strategy + party services + Lua script + analytics channel placeholder
        //    (Plan 05-04 — closes the deferred TODO from Plan 05-03). Implementation lives
        //    in MatchmakingBuilderExtensions.Strategy.cs (partial class).
        //    Wires:
        //      - Scrutor scan for every IMatchmakingStrategy in this assembly
        //      - Singletons for AtomicClaimScript / PartyRatingAggregatorService /
        //        PartyCodeGenerator (stateless helpers)
        //      - Scoped IPartyService → PartyService (requires scoped GameKitDbContext)
        //      - Placeholder bounded Channel<TicketEvent> (capacity 1000, DropNewest);
        //        Plan 05-07's AddBackgroundServices() will services.Replace(...) with the
        //        options-driven instance per CONTEXT D-15 (default 10000).
        builder.Services.AddStrategyServices();

        // 6. Plan 05-06 accept-step proposal services — IProposalService +
        //    IDeclineCooldownService + IDeclineHistoryReader + TeamAssignmentService.
        //    Implementation lives in MatchmakingBuilderExtensions.Accept.cs (partial class).
        builder.Services.AddProposalServices();

        // 7. Plan 05-07 background services — reconciler, analytics drain, retention
        //    cleanup — plus the options-driven bounded TicketEvent channel (rebinds Plan
        //    05-04's placeholder).
        builder.Services.AddBackgroundServices();

        // 6. Plan 05-05 ticker — live match-formation engine (BackgroundService +
        //    PeriodicTimer + Polly-wrapped lease + atomic-claim Lua + proposal-sweeper).
        //    Registered AFTER AddBackgroundServices so the ticker-owned MatchmakerLeaseHelper
        //    can Replace() the Plan 05-07 RedisMatchmakerLease default — see
        //    MatchmakingBuilderExtensions.Ticker.cs §Wave-3 lease unification.
        builder.Services.AddTickerServices();

        // 7. Plan 05-08 HTTP-layer services — IMatchmakingService, IMatchmakingObservability,
        //    FluentValidation validators, rate-limit policies (gamekit:mm:enqueue +
        //    gamekit:mm:party_join). The endpoint mapping itself happens in
        //    MatchmakingApplicationBuilderExtensions.MapMatchmaking.
        builder.Services.AddHttpServices();

        // OBS-04: wire the QueueDepth ObservableGauge Redis reference. Register a short
        // IHostedService that resolves IConnectionMultiplexer from DI after the container
        // is built (avoids eagerly resolving the multiplexer during ConfigureServices) and
        // calls MatchmakingMeter.Init(multiplexer) once at StartAsync. The service is a
        // singleton, starts before the matchmaker ticker, and completes in ~0 ms.
        builder.Services.AddHostedService<MatchmakingMeterInitService>();

        return matchmakingBuilder;
    }
}

/// <summary>
/// Minimal <see cref="IHostedService"/> that calls <see cref="MatchmakingMeter.Init"/> once
/// at host startup so the <c>QueueDepth</c> <see cref="System.Diagnostics.Metrics.ObservableGauge{T}"/>
/// callback has a live Redis reference before the first scrape (OBS-04).
/// </summary>
/// <remarks>
/// Registered by <see cref="MatchmakingBuilderExtensions.AddMatchmaking"/> as a hosted service.
/// The service resolves <see cref="IConnectionMultiplexer"/> lazily from DI (avoids eagerly
/// constructing Redis connections during <c>ConfigureServices</c>) and calls
/// <c>MatchmakingMeter.Init</c> once at <see cref="StartAsync"/>. StopAsync is a no-op.
/// </remarks>
internal sealed class MatchmakingMeterInitService : IHostedService
{
    private readonly IConnectionMultiplexer _multiplexer;

    /// <summary>Constructs the init service.</summary>
    /// <param name="multiplexer">The Redis connection multiplexer.</param>
    public MatchmakingMeterInitService(IConnectionMultiplexer multiplexer)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        _multiplexer = multiplexer;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // OBS-04: wires the QueueDepth ObservableGauge Redis reference.
        MatchmakingMeter.Init(_multiplexer);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
