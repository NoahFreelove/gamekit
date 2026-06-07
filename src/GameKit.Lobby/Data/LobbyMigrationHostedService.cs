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

namespace GameKit.Lobby.Data;

/// <summary>
/// Applies the Lobby package's migrations (<c>__ef_migrations_lobby</c>) under the
/// Lobby advisory-lock key. Runs as an <see cref="IHostedService"/> so startup ordering is:
/// <list type="number">
/// <item>Middleware pipeline build — <c>UseGameKit</c> applies Core migrations.</item>
/// <item>Hosted services start — Auth, Admin, Rankings, and Matchmaking migration services
///       apply their migrations in order, then this service applies Lobby migrations (all five
///       prior packages' tables exist, so FK references — <c>players</c>, <c>ladders</c> —
///       resolve).</item>
/// <item>Kestrel starts — first HTTP request is served only after all migration sets finish.</item>
/// </list>
/// Skipped when <see cref="GameKitOptions.AutoMigrate"/> is <c>false</c> — operators opting
/// into out-of-band migration via <c>gamekit migrate</c> must also run
/// <c>gamekit migrate --package lobby</c> (or equivalent) to apply Lobby migrations.
/// </summary>
/// <remarks>
/// Mirrors <see cref="GameKit.Matchmaking.Data.MatchmakingMigrationHostedService"/> structure
/// exactly — only the constant references differ. The actual advisory-lock acquisition + migrate
/// flow lives in
/// <see cref="MigrationRunner.MigrateWithLockAsync(GameKitDbContext, long, CancellationToken)"/>.
/// </remarks>
internal sealed class LobbyMigrationHostedService : IHostedService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly ILogger<LobbyMigrationHostedService> _logger;

    /// <summary>Constructs the service with the required options and logger.</summary>
    public LobbyMigrationHostedService(
        GameKitOptions gameKitOpts,
        ILogger<LobbyMigrationHostedService> logger)
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
                "AutoMigrate=false — skipping Lobby migration apply. Run migrations out-of-band before accepting traffic.");
            return;
        }

        var connectionString = !string.IsNullOrWhiteSpace(_gameKitOpts.MigrationsConnectionString)
            ? _gameKitOpts.MigrationsConnectionString!
            : _gameKitOpts.ConnectionString;

        await using var ctx = BuildLobbyMigrationContext(connectionString);
        _logger.LogInformation("Applying Lobby migrations (history table {Table}).",
            LobbyMigrationConstants.MigrationsHistoryTable);

        await MigrationRunner
            .MigrateWithLockAsync(ctx, LobbyMigrationConstants.AdvisoryLockKey, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Lobby migrations applied successfully.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static GameKitDbContext BuildLobbyMigrationContext(string connectionString)
    {
        // Lobby-only migration context. Uses LobbyMigrationModelCustomizer which applies
        // the two Lobby configurations directly and excludes every Core / Auth / Admin /
        // Rankings / Matchmaking entity from the migration diff (per-package migration
        // boundary, PITFALLS #3).
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(LobbyMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    LobbyMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, LobbyMigrationModelCustomizer>()
            // The hand-authored snapshot is structurally correct but does not match EF Core's
            // internal model hash exactly. Without this ignore, MigrateAsync raises
            // PendingModelChangesWarning as an exception on consumer startup.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
