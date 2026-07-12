// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Core;
using GameKit.Core.Data;
using GameKit.Core.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Auth.Health;

/// <summary>
/// Reports whether all <c>GameKit.Auth</c> migrations have been applied to the
/// <c>__ef_migrations_auth</c> history table.
/// </summary>
/// <remarks>
/// <para>
/// Implements the <see cref="IMigrationReadinessReporter"/> latch contract (D-07):
/// once all Auth migrations are observed as applied, subsequent calls return
/// <c>true</c> immediately without querying Postgres.
/// </para>
/// <para>
/// Registered as an enumerable singleton by <c>AuthBuilderExtensions.AddAuth()</c>
/// alongside <c>AuthMigrationHostedService</c>.
/// </para>
/// <para>
/// Auth does <b>not</b> need <c>ConfigureWarnings(PendingModelChangesWarning)</c>
/// because the Auth migration snapshot matches the EF Core model hash exactly.
/// </para>
/// </remarks>
internal sealed class AuthMigrationReadinessReporter : IMigrationReadinessReporter
{
    private readonly GameKitOptions _opts;
    private volatile bool _latched;

    /// <summary>Initializes the reporter with the GameKit options needed to build the migration context.</summary>
    /// <param name="opts">GameKit options containing the connection string used for migration-readiness probes.</param>
    public AuthMigrationReadinessReporter(GameKitOptions opts)
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

        await using var ctx = BuildAuthMigrationContext(connStr);
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
        // NOTE: Auth does NOT need ConfigureWarnings — its snapshot matches the model hash.

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
