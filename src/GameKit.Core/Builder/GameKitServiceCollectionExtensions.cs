// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Data;
using GameKit.Core.RateLimiting;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Builder;

/// <summary>
/// Entry-point extension methods for mounting GameKit into an ASP.NET Core application.
/// </summary>
public static class GameKitServiceCollectionExtensions
{
    /// <summary>
    /// Registers GameKit.Core services and returns an <see cref="IGameKitBuilder"/> sibling packages
    /// extend via their own <c>.AddXxx(...)</c> methods.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configure">Callback to populate <see cref="GameKitOptions"/>. MUST set <see cref="GameKitOptions.ConnectionString"/>.</param>
    /// <returns>A builder for further configuration.</returns>
    /// <exception cref="ArgumentException">Thrown when <see cref="GameKitOptions.ConnectionString"/> is empty after <paramref name="configure"/> runs.</exception>
    public static IGameKitBuilder AddGameKit(
        this IServiceCollection services,
        Action<GameKitOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new GameKitOptions();
        configure(opts);

        if (string.IsNullOrWhiteSpace(opts.ConnectionString))
            throw new ArgumentException(
                $"{nameof(GameKitOptions)}.{nameof(GameKitOptions.ConnectionString)} must be set.",
                nameof(configure));

        services.AddSingleton(opts);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentPlayer, HttpContextCurrentPlayer>();

        // Register the authorization services so endpoints that call .RequireAuthorization()
        // (e.g. GET /api/players, WR-05) have a policy evaluator available. Phase 1 ships
        // no authentication handler, so the default-deny behavior is intentional: the
        // endpoint 401s until Phase 2 (GameKit.Auth) wires a handler.
        services.AddAuthorization();

        services.AddMemoryCache();
        services.AddScoped<IPlayerDisplayNameResolver, PlayerDisplayNameResolver>();

        services.AddScoped<IGdprDeleteService, GdprDeleteService>();

        services.AddSingleton<IGameKitRateLimitPolicies, GameKitRateLimitPolicies>();

        services.AddDbContext<GameKitDbContext>(dbOpts =>
            dbOpts.UseNpgsql(opts.ConnectionString, npg =>
                {
                    npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                    npg.MigrationsHistoryTable(
                        GameKitMigrationConstants.MigrationsHistoryTable,
                        GameKitMigrationConstants.SchemaName);
                })
                .ReplaceService<IModelCustomizer, GameKitModelCustomizer>());

        return new GameKitBuilder(services, opts);
    }
}
