// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Entities;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameKit.Admin.UI.Authentication;

/// <summary>
/// Startup gate (SP-11 / D-04 / D-05) — asserts at least one <c>admin_users.role = 'superadmin'</c>
/// row exists when the host is in <see cref="Environments.Production"/>. Fails fast with a loud
/// <see cref="InvalidOperationException"/> (message points operators at <c>dotnet gamekit admin
/// create</c>) when the assertion fails; in Development/Staging logs a warning and lets startup
/// continue so operators can exercise the UI during build-out (D-05).
/// </summary>
/// <remarks>
/// Runs inside <see cref="IHost.StartAsync"/>, AFTER <c>AdminMigrationHostedService</c> (hosted
/// services start in registration order) so <c>admin_users</c> exists when the query runs.
/// Kestrel has not yet accepted traffic. No retry — if Postgres is unreachable at boot, the
/// exception propagates and the host does not start (T-03-06-05).
/// </remarks>
public sealed class SuperadminGateHostedService : IHostedService
{
    private readonly IHostEnvironment _env;
    private readonly IServiceProvider _sp;
    private readonly ILogger<SuperadminGateHostedService> _logger;

    /// <summary>Constructs the gate.</summary>
    /// <param name="env">Host environment (Production triggers throw-on-missing-superadmin).</param>
    /// <param name="sp">Root service provider (used to create a scope for <see cref="GameKitDbContext"/>).</param>
    /// <param name="logger">Logger.</param>
    public SuperadminGateHostedService(
        IHostEnvironment env,
        IServiceProvider sp,
        ILogger<SuperadminGateHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(logger);
        _env = env;
        _sp = sp;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var hasSuper = await ctx.Set<AdminUser>()
            .AsNoTracking()
            .AnyAsync(u => u.Role == AdminRoles.Superadmin, cancellationToken)
            .ConfigureAwait(false);
        if (hasSuper) return;

        if (_env.IsProduction())
        {
            throw new InvalidOperationException(
                "GameKit.Admin.UI is mounted in Production but no superadmin exists in admin_users. " +
                "Bootstrap the first admin by running: `dotnet gamekit admin create`. " +
                "The first admin created is automatically promoted to superadmin.");
        }

        _logger.LogWarning(
            "GameKit.Admin.UI: no superadmin exists in admin_users. Bootstrap one with " +
            "`dotnet gamekit admin create`. The admin UI will render a placeholder until then.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
