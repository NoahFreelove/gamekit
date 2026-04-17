// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using GameKit.Core.Data;
using GameKit.Core.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
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
    public static IApplicationBuilder UseGameKit(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var opts = app.ApplicationServices.GetRequiredService<GameKitOptions>();
        if (opts.AutoMigrate)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            MigrationRunner
                .MigrateWithLockAsync(ctx, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
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
}
