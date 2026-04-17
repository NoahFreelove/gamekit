// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using GameKit.Core.Data;
using GameKit.Core.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Builder;

/// <summary>Extension methods mounted on <see cref="IApplicationBuilder"/> and <see cref="IEndpointRouteBuilder"/>.</summary>
public static class GameKitApplicationBuilderExtensions
{
    /// <summary>
    /// Applies pending migrations when <see cref="GameKitOptions.AutoMigrate"/> is <c>true</c> (default,
    /// per D-07). Operators running multiple replicas should set <c>AutoMigrate = false</c> and apply
    /// migrations out-of-band via <c>gamekit migrate</c> to avoid contention on the advisory lock.
    /// </summary>
    /// <remarks>
    /// When <see cref="GameKitOptions.MigrationsConnectionString"/> is set, migrations run under those
    /// (elevated) credentials via a one-off DbContext, while runtime traffic continues to use the
    /// app <see cref="GameKitOptions.ConnectionString"/>. This supports the three-role Postgres
    /// isolation model (owner applies DDL, app performs DML) required by OPS-08.
    /// </remarks>
    public static IApplicationBuilder UseGameKit(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var opts = app.ApplicationServices.GetRequiredService<GameKitOptions>();
        if (opts.AutoMigrate)
        {
            using var scope = app.ApplicationServices.CreateScope();
            GameKitDbContext? migrationCtx = null;
            try
            {
                migrationCtx = !string.IsNullOrWhiteSpace(opts.MigrationsConnectionString)
                    ? BuildMigrationContext(opts.MigrationsConnectionString, scope.ServiceProvider)
                    : scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

                MigrationRunner
                    .MigrateWithLockAsync(migrationCtx, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            finally
            {
                // Only dispose the one-off migration context — the DI-resolved one is owned by the scope.
                if (!string.IsNullOrWhiteSpace(opts.MigrationsConnectionString))
                    migrationCtx?.Dispose();
            }
        }

        // Register authorization middleware so endpoints carrying .RequireAuthorization()
        // metadata (WR-05) are evaluated. Phase 1 has no authentication handler; the default
        // policy denies — the endpoint returns 401 until Phase 2 wires GameKit.Auth.
        app.UseAuthorization();

        return app;
    }

    /// <summary>Maps GameKit.Core endpoints (<c>GET /api/players</c>). Sibling packages add their own maps.</summary>
    public static IEndpointRouteBuilder MapGameKit(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        routes.MapPlayers();
        return routes;
    }

    private static GameKitDbContext BuildMigrationContext(string connectionString, IServiceProvider appServices)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKitMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKitModelCustomizer>()
            // Wires the app DI into EF so GameKitModelCustomizer can resolve
            // IEnumerable<IModelBuilderExtension> from sibling packages.
            .UseApplicationServiceProvider(appServices);

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
