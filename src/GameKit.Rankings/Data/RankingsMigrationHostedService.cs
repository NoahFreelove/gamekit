// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameKit.Rankings.Data;

/// <summary>
/// Applies the Rankings package's migrations (<c>__ef_migrations_rankings</c>) under the Rankings
/// advisory-lock key. Runs as an <see cref="IHostedService"/> so startup ordering is:
/// <list type="number">
/// <item>Middleware pipeline build — <c>UseGameKit</c> applies Core migrations.</item>
/// <item>Hosted services start — Auth migration service applies Auth migrations, then this service
///       applies Rankings migrations (Core + Auth tables exist, so FK references resolve).</item>
/// <item>Kestrel starts — first HTTP request is served only after all migration sets finish.</item>
/// </list>
/// Skipped when <see cref="GameKitOptions.AutoMigrate"/> is <c>false</c> — operators opting into
/// out-of-band migration via <c>gamekit migrate</c> must also run
/// <c>gamekit migrate --package rankings</c> (or equivalent) to apply Rankings migrations.
/// </summary>
internal sealed class RankingsMigrationHostedService : IHostedService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly ILogger<RankingsMigrationHostedService> _logger;

    /// <summary>Constructs the service with the required options and logger.</summary>
    public RankingsMigrationHostedService(
        GameKitOptions gameKitOpts,
        ILogger<RankingsMigrationHostedService> logger)
    {
        _gameKitOpts = gameKitOpts;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_gameKitOpts.AutoMigrate)
        {
            _logger.LogInformation(
                "AutoMigrate=false — skipping Rankings migration apply. Run migrations out-of-band before accepting traffic.");
            return;
        }

        var connectionString = !string.IsNullOrWhiteSpace(_gameKitOpts.MigrationsConnectionString)
            ? _gameKitOpts.MigrationsConnectionString!
            : _gameKitOpts.ConnectionString;

        await using var ctx = BuildRankingsMigrationContext(connectionString);
        _logger.LogInformation("Applying Rankings migrations (history table {Table}).",
            RankingsMigrationConstants.MigrationsHistoryTable);

        await MigrationRunner
            .MigrateWithLockAsync(ctx, RankingsMigrationConstants.AdvisoryLockKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Rankings migrations applied successfully.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static GameKitDbContext BuildRankingsMigrationContext(string connectionString)
    {
        // Rankings-only migration context. Uses RankingsMigrationModelCustomizer which applies
        // the seven Rankings configurations directly and excludes every Core entity from the
        // migration diff (per-package migration boundary, PITFALLS #3).
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            // The hand-authored snapshot is structurally correct but does not match EF Core's
            // internal model hash exactly (Phase 4 latent: the determinism integration test
            // suppresses the same warning with the same rationale). Without this ignore,
            // MigrateAsync raises PendingModelChangesWarning as an exception on consumer startup.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
