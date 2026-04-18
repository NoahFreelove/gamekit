// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameKit.Auth.Data;

/// <summary>
/// Applies the Auth package's migrations (<c>__ef_migrations_auth</c>) under the Auth
/// advisory-lock key. Runs as an <see cref="IHostedService"/> so startup ordering is:
/// <list type="number">
/// <item>Middleware pipeline build — <c>UseGameKit</c> applies Core migrations.</item>
/// <item>Hosted services start — this service applies Auth migrations (Core tables now exist,
///       so FK references to <c>gamekit.players</c> resolve).</item>
/// <item>Kestrel starts — first HTTP request is served only after both migration sets finish.</item>
/// </list>
/// Skipped when <see cref="GameKitOptions.AutoMigrate"/> is <c>false</c> — operators opting into
/// out-of-band migration via <c>gamekit migrate</c> must also run <c>gamekit migrate --package auth</c>
/// (or equivalent) to apply Auth migrations.
/// </summary>
internal sealed class AuthMigrationHostedService : IHostedService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly ILogger<AuthMigrationHostedService> _logger;

    public AuthMigrationHostedService(
        GameKitOptions gameKitOpts,
        ILogger<AuthMigrationHostedService> logger)
    {
        _gameKitOpts = gameKitOpts;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_gameKitOpts.AutoMigrate)
        {
            _logger.LogInformation(
                "AutoMigrate=false — skipping Auth migration apply. Run migrations out-of-band before accepting traffic.");
            return;
        }

        var connectionString = !string.IsNullOrWhiteSpace(_gameKitOpts.MigrationsConnectionString)
            ? _gameKitOpts.MigrationsConnectionString!
            : _gameKitOpts.ConnectionString;

        await using var ctx = BuildAuthMigrationContext(connectionString);
        _logger.LogInformation("Applying Auth migrations (history table {Table}).",
            AuthMigrationConstants.MigrationsHistoryTable);

        await MigrationRunner
            .MigrateWithLockAsync(ctx, AuthMigrationConstants.AdvisoryLockKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Auth migrations applied successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static GameKitDbContext BuildAuthMigrationContext(string connectionString)
    {
        // Auth-only migration context. Uses AuthMigrationModelCustomizer which applies the three
        // Auth configurations directly and excludes every Core entity from the migration diff
        // (per-package migration boundary, PITFALLS #3).
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>();

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
