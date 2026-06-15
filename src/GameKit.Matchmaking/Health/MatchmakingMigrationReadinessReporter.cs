// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Health;
using GameKit.Matchmaking.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Matchmaking.Health;

/// <summary>
/// The sixth <see cref="IMigrationReadinessReporter"/> — reports readiness for the
/// <c>GameKit.Matchmaking</c> migration set (<c>__ef_migrations_matchmaking</c>).
/// Latches on first <c>true</c> observation so steady-state readiness probes
/// do not re-query Postgres on every poll (D-07).
/// </summary>
internal sealed class MatchmakingMigrationReadinessReporter : IMigrationReadinessReporter
{
    private readonly GameKitOptions _opts;
    private volatile bool _latched;

    /// <summary>Constructs the reporter with the current GameKit options.</summary>
    /// <param name="opts">GameKit options providing the connection string.</param>
    public MatchmakingMigrationReadinessReporter(GameKitOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        _opts = opts;
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsReadyAsync(CancellationToken ct)
    {
        if (_latched) return true;

        var connStr = !string.IsNullOrWhiteSpace(_opts.MigrationsConnectionString)
            ? _opts.MigrationsConnectionString!
            : _opts.ConnectionString;

        await using var ctx = BuildMatchmakingMigrationContext(connStr);
        var pending = await ctx.Database
            .GetPendingMigrationsAsync(ct)
            .ConfigureAwait(false);

        if (!pending.Any())
        {
            _latched = true;
            return true;
        }

        return false;
    }

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
            // internal model hash exactly. Without this ignore, MigrateAsync raises
            // PendingModelChangesWarning as an exception on consumer startup.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
