// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Health;
using GameKit.Lobby.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Lobby.Health;

/// <summary>
/// Reports whether all <c>GameKit.Lobby</c> migrations have been applied to the
/// <c>__ef_migrations_lobby</c> history table.
/// </summary>
/// <remarks>
/// <para>
/// Implements the <see cref="IMigrationReadinessReporter"/> latch contract (D-07):
/// once all Lobby migrations are observed as applied, subsequent calls return
/// <c>true</c> immediately without querying Postgres.
/// </para>
/// <para>
/// Registered as an enumerable singleton by <c>LobbyBuilderExtensions.AddLobby()</c>
/// alongside <c>LobbyMigrationHostedService</c>.
/// </para>
/// <para>
/// Lobby requires <c>ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))</c>
/// because the hand-authored migration snapshot does not match EF Core's internal model hash
/// exactly. Without this suppression, <c>GetPendingMigrationsAsync</c> would throw a
/// <c>PendingModelChangesWarning</c> as an exception on consumer startup (Pitfall 3).
/// </para>
/// </remarks>
internal sealed class LobbyMigrationReadinessReporter : IMigrationReadinessReporter
{
    private readonly GameKitOptions _opts;
    private volatile bool _latched;

    /// <summary>Initializes the reporter with the GameKit options needed to build the migration context.</summary>
    /// <param name="opts">GameKit options containing the connection string used for migration-readiness probes.</param>
    public LobbyMigrationReadinessReporter(GameKitOptions opts)
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

        await using var ctx = BuildLobbyMigrationContext(connStr);
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

    private static GameKitDbContext BuildLobbyMigrationContext(string connectionString)
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
            // internal model hash exactly. Without this ignore, GetPendingMigrationsAsync raises
            // PendingModelChangesWarning as an exception on consumer startup.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
