// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Admin.UI.Health;

/// <summary>
/// Reports whether all <c>GameKit.Admin.UI</c> migrations have been applied to the
/// <c>__ef_migrations_admin</c> history table.
/// </summary>
/// <remarks>
/// <para>
/// Implements the <see cref="IMigrationReadinessReporter"/> latch contract (D-07):
/// once all Admin migrations are observed as applied, subsequent calls return
/// <c>true</c> immediately without querying Postgres.
/// </para>
/// <para>
/// Registered as an enumerable singleton by <c>AdminBuilderExtensions.AddGameKitAdmin()</c>
/// alongside <c>AdminMigrationHostedService</c>. This is the sixth and final
/// <see cref="IMigrationReadinessReporter"/> in the six-reporter set
/// (Core / Auth / Rankings / Lobby / Matchmaking / Admin), consumed by
/// <c>MigrationAggregateHealthCheck</c> via <c>IEnumerable&lt;IMigrationReadinessReporter&gt;</c>.
/// </para>
/// <para>
/// Admin does <b>not</b> need <c>ConfigureWarnings(PendingModelChangesWarning)</c>
/// because the Admin migration snapshot matches the EF Core model hash exactly
/// (per-package variation table in PATTERNS.md).
/// </para>
/// </remarks>
internal sealed class AdminMigrationReadinessReporter : IMigrationReadinessReporter
{
    private readonly GameKitOptions _opts;
    private volatile bool _latched;

    /// <summary>
    /// Initializes the reporter with the GameKit options needed to build the migration context.
    /// </summary>
    /// <param name="opts">GameKit options containing the connection string used for migration-readiness probes.</param>
    public AdminMigrationReadinessReporter(GameKitOptions opts)
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

        await using var ctx = BuildAdminMigrationContext(connStr);
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
        // NOTE: Admin does NOT need ConfigureWarnings — its snapshot matches the model hash.

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
