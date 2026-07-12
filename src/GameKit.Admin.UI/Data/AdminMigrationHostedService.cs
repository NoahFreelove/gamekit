// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameKit.Admin.UI.Data;

/// <summary>
/// Applies the Admin.UI package's migrations (<c>__ef_migrations_admin</c>) under the Admin
/// advisory-lock key. Runs as an <see cref="IHostedService"/> so startup ordering is:
/// <list type="number">
/// <item>Middleware pipeline build — <c>UseGameKit</c> applies Core migrations.</item>
/// <item>Hosted services start — <c>AuthMigrationHostedService</c> applies Auth migrations.</item>
/// <item>Hosted services start — this service applies Admin migrations (Auth tables now exist;
///       no FK references but citext extension is already installed by AuthInitial).</item>
/// <item>Kestrel starts — first HTTP request is served only after all three migration sets finish.</item>
/// </list>
/// Sibling of <see cref="GameKit.Auth.Data.AuthMigrationHostedService"/>. Skipped when
/// <see cref="GameKitOptions.AutoMigrate"/> is <c>false</c> — operators opting into out-of-band
/// migration via <c>gamekit migrate</c> must also run the equivalent Admin migration command.
/// </summary>
internal sealed class AdminMigrationHostedService : IHostedService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly ILogger<AdminMigrationHostedService> _logger;

    public AdminMigrationHostedService(
        GameKitOptions gameKitOpts,
        ILogger<AdminMigrationHostedService> logger)
    {
        _gameKitOpts = gameKitOpts;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_gameKitOpts.AutoMigrate)
        {
            _logger.LogInformation(
                "AutoMigrate=false — skipping Admin migration apply. Run migrations out-of-band before accepting traffic.");
            return;
        }

        var connectionString = !string.IsNullOrWhiteSpace(_gameKitOpts.MigrationsConnectionString)
            ? _gameKitOpts.MigrationsConnectionString!
            : _gameKitOpts.ConnectionString;

        await using var ctx = BuildAdminMigrationContext(connectionString);
        _logger.LogInformation("Applying GameKit.Admin.UI migrations (history table {Table}).",
            AdminMigrationConstants.MigrationsHistoryTable);

        await MigrationRunner
            .MigrateWithLockAsync(ctx, AdminMigrationConstants.AdvisoryLockKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("GameKit.Admin.UI migrations applied successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static GameKitDbContext BuildAdminMigrationContext(string connectionString)
    {
        // Admin-only migration context. Uses AdminMigrationModelCustomizer which applies the Admin
        // configuration directly and excludes every Core + Auth entity from the migration diff
        // (per-package migration boundary, PITFALLS #3).
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AdminMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>();

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
