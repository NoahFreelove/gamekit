// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Health;
using GameKit.Rankings.Authentication;
using GameKit.Rankings.Data;
using GameKit.Rankings.Health;
using GameKit.Rankings.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Fluent-builder extensions that mount <c>GameKit.Rankings</c> onto an existing
/// <see cref="IGameKitBuilder"/>. Declared <see langword="partial"/> so concern-specific
/// plan files (04-05 session-complete, 04-06 ticker, 04-07 season, 04-08 GDPR) can add
/// their own extension methods without modifying this file.
/// </summary>
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Registers <c>GameKit.Rankings</c> services:
    /// <list type="bullet">
    ///   <item>Options singleton via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.</item>
    ///   <item><see cref="RankingsModelBuilderExtension"/> via <c>TryAddEnumerable</c> so Rankings entities
    ///         land in <c>GameKitDbContext</c> at runtime.</item>
    ///   <item><see cref="RankingsMigrationHostedService"/> — applies <c>__ef_migrations_rankings</c> at startup.</item>
    ///   <item><see cref="StartupLadderUpserter"/> — idempotently upserts registered ladders at startup (D-21).</item>
    ///   <item><c>IServiceTokenService</c> → <c>ServiceTokenService</c> (scoped, D-06).</item>
    ///   <item>The <c>GameKitServiceToken</c> authentication scheme + <c>RequiresServiceToken</c> authorization
    ///         policy (D-05).</item>
    /// </list>
    /// </summary>
    /// <param name="builder">The existing <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <param name="configure">Callback to populate <see cref="GameKitRankingsOptions"/>.</param>
    /// <returns>An <see cref="IGameKitRankingsBuilder"/> for further ladder registration.</returns>
    public static IGameKitRankingsBuilder AddRankings(
        this IGameKitBuilder builder,
        Action<GameKitRankingsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is not null)
            builder.Services.Configure(configure);

        // 1. Rankings model extension — contributes the seven Rankings entities to GameKitDbContext.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, RankingsModelBuilderExtension>());

        // 2. Migration runner — applies __ef_migrations_rankings at startup.
        builder.Services.AddHostedService<RankingsMigrationHostedService>();
        // 2a. Rankings migration readiness reporter — reports whether __ef_migrations_rankings
        //     migrations are all applied. Registered as an enumerable singleton so the Core
        //     aggregate "migrations" health check discovers all six IMigrationReadinessReporter
        //     implementations.
        builder.Services.AddSingleton<IMigrationReadinessReporter, RankingsMigrationReadinessReporter>();

        // 3. Startup ladder upserter — idempotently upserts registered ladders (D-21 / RANK-09).
        //    Registered both as IHostedService (for the hosting pipeline) and as the concrete
        //    type (so integration tests can resolve it directly to call StartAsync in isolation).
        builder.Services.AddSingleton<StartupLadderUpserter>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<StartupLadderUpserter>());

        // 4. Service token service — mints / revokes / lists / looks up service tokens (D-06).
        builder.Services.AddScoped<IServiceTokenService, ServiceTokenService>();

        // 5. Authentication scheme + authorization policy for service-account tokens (D-05).
        builder.Services.AddServiceTokenAuthentication();

        // 6. Session-complete infrastructure: ICanonicalRequestHasher, validator, rate-limit policy (plan 04-05).
        AddSessionCompleteInfrastructure(builder.Services);

        // 7. Ticker infrastructure: Glicko2Algorithm, RankingsTickerLeaseHelper, RankingsTickerService,
        //    IdempotencyCleanupService (plan 04-06).
        AddTickerInfrastructure(builder.Services);

        // 8. Decay infrastructure: RankDecayLeaseHelper + RankDecayBackgroundService (RANK-15).
        AddDecayInfrastructure(builder.Services);

        // 9. Season infrastructure: ILeaderboardService, IEndSeasonService, EndSeasonRequestValidator (plan 04-07).
        AddSeasonInfrastructure(builder.Services);

        // 10. Export infrastructure: IGdprExportService, IRankAdjustService, RankAdjustRequestValidator (plan 04-08).
        AddExportInfrastructure(builder.Services);

        // 11. Build and register the rankings builder as a singleton so StartupLadderUpserter can
        //    resolve IGameKitRankingsBuilder from DI to read RegisteredLadders.
        var rankingsBuilder = new GameKitRankingsBuilder(builder.Services);
        builder.Services.AddSingleton<IGameKitRankingsBuilder>(rankingsBuilder);

        return rankingsBuilder;
    }
}
