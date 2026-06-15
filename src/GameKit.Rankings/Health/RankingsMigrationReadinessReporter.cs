// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Health;
using GameKit.Rankings.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Rankings.Health;

/// <summary>
/// Reports whether all <c>GameKit.Rankings</c> migrations have been applied to the
/// <c>__ef_migrations_rankings</c> history table.
/// </summary>
/// <remarks>
/// <para>
/// Implements the <see cref="IMigrationReadinessReporter"/> latch contract (D-07):
/// once all Rankings migrations are observed as applied, subsequent calls return
/// <c>true</c> immediately without querying Postgres.
/// </para>
/// <para>
/// Registered as an enumerable singleton by <c>RankingsBuilderExtensions.AddRankings()</c>
/// alongside <c>RankingsMigrationHostedService</c>.
/// </para>
/// <para>
/// Rankings requires <c>ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))</c>
/// because the hand-authored migration snapshot does not match EF Core's internal model hash
/// exactly. Without this suppression, <c>GetPendingMigrationsAsync</c> would throw a
/// <c>PendingModelChangesWarning</c> as an exception on consumer startup (Pitfall 3).
/// </para>
/// </remarks>
internal sealed class RankingsMigrationReadinessReporter : IMigrationReadinessReporter
{
    private readonly GameKitOptions _opts;
    private volatile bool _latched;

    /// <summary>Initializes the reporter with the GameKit options needed to build the migration context.</summary>
    /// <param name="opts">GameKit options containing the connection string used for migration-readiness probes.</param>
    public RankingsMigrationReadinessReporter(GameKitOptions opts)
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

        await using var ctx = BuildRankingsMigrationContext(connStr);
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
            // GetPendingMigrationsAsync raises PendingModelChangesWarning as an exception on
            // consumer startup.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
