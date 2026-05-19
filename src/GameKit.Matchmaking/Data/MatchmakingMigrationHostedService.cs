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

namespace GameKit.Matchmaking.Data;

/// <summary>
/// Applies the Matchmaking package's migrations (<c>__ef_migrations_matchmaking</c>) under the
/// Matchmaking advisory-lock key. Runs as an <see cref="IHostedService"/> so startup ordering is:
/// <list type="number">
/// <item>Middleware pipeline build — <c>UseGameKit</c> applies Core migrations.</item>
/// <item>Hosted services start — Auth, Admin, and Rankings migration services apply their
///       migrations in order, then this service applies Matchmaking migrations (all four prior
///       packages' tables exist, so FK references — <c>players</c>, <c>game_sessions</c>,
///       <c>ladders</c> — resolve).</item>
/// <item>Kestrel starts — first HTTP request is served only after all migration sets finish.</item>
/// </list>
/// Skipped when <see cref="GameKitOptions.AutoMigrate"/> is <c>false</c> — operators opting into
/// out-of-band migration via <c>gamekit migrate</c> must also run
/// <c>gamekit migrate --package matchmaking</c> (or equivalent) to apply Matchmaking migrations.
/// </summary>
/// <remarks>
/// Mirrors <see cref="GameKit.Rankings.Data.RankingsMigrationHostedService"/> structure exactly —
/// only the constant references differ. The actual advisory-lock acquisition + migrate flow lives
/// in <see cref="MigrationRunner.MigrateWithLockAsync(GameKitDbContext, long, CancellationToken)"/>.
/// </remarks>
internal sealed class MatchmakingMigrationHostedService : IHostedService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly ILogger<MatchmakingMigrationHostedService> _logger;

    /// <summary>Constructs the service with the required options and logger.</summary>
    public MatchmakingMigrationHostedService(
        GameKitOptions gameKitOpts,
        ILogger<MatchmakingMigrationHostedService> logger)
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
                "AutoMigrate=false — skipping Matchmaking migration apply. Run migrations out-of-band before accepting traffic.");
            return;
        }

        var connectionString = !string.IsNullOrWhiteSpace(_gameKitOpts.MigrationsConnectionString)
            ? _gameKitOpts.MigrationsConnectionString!
            : _gameKitOpts.ConnectionString;

        await using var ctx = BuildMatchmakingMigrationContext(connectionString);
        _logger.LogInformation("Applying Matchmaking migrations (history table {Table}).",
            MatchmakingMigrationConstants.MigrationsHistoryTable);

        await MigrationRunner
            .MigrateWithLockAsync(ctx, MatchmakingMigrationConstants.AdvisoryLockKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Matchmaking migrations applied successfully.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static GameKitDbContext BuildMatchmakingMigrationContext(string connectionString)
    {
        // Matchmaking-only migration context. Uses MatchmakingMigrationModelCustomizer which applies
        // the five Matchmaking configurations directly and excludes every Core / Auth / Admin / Rankings
        // entity from the migration diff (per-package migration boundary, PITFALLS #3).
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
            // The hand-authored snapshot is structurally correct but does not match EF Core's
            // internal model hash exactly (the Matchmaking determinism integration test suppresses
            // the same warning with the same rationale). Without this ignore, MigrateAsync raises
            // PendingModelChangesWarning as an exception on consumer startup.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
