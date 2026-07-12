// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Health;

/// <summary>
/// <see cref="IMigrationReadinessReporter"/> implementation for <c>GameKit.Core</c>.
/// Reports readiness by calling <c>GetPendingMigrationsAsync</c> against
/// the Core migration history table (<c>__ef_migrations_core</c>).
/// </summary>
/// <remarks>
/// <para>
/// Unlike sibling-package reporters, Core does NOT build a custom migration context because the
/// DI-registered <see cref="GameKitDbContext"/> already targets the correct
/// <c>__ef_migrations_core</c> history table (per PATTERNS §Per-package variation table, D-07).
/// A scoped <see cref="GameKitDbContext"/> is resolved via <see cref="IServiceScopeFactory"/>
/// on each call so the health-check singleton does not capture a scoped service.
/// </para>
/// <para>
/// <b>Latch pattern (D-07):</b> once all migrations are observed as applied, subsequent calls
/// return <c>true</c> immediately without a Postgres round-trip. Migrations are never
/// un-applied at runtime.
/// </para>
/// </remarks>
internal sealed class CoreMigrationReadinessReporter : IMigrationReadinessReporter
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// <c>true</c> after the first successful observation of zero pending migrations.
    /// Volatile ensures the flag is visible across threads without a full lock.
    /// </summary>
    private volatile bool _latched;

    /// <summary>Constructs the reporter.</summary>
    /// <param name="scopeFactory">
    /// Factory used to resolve a scoped <see cref="GameKitDbContext"/> per probe call.
    /// </param>
    public CoreMigrationReadinessReporter(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    /// <inheritdoc />
    public async ValueTask<bool> IsReadyAsync(CancellationToken ct)
    {
        // Fast path: once latched, no DB round-trip needed.
        if (_latched) return true;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

        var pending = await db.Database
            .GetPendingMigrationsAsync(ct)
            .ConfigureAwait(false);

        if (!pending.Any())
        {
            _latched = true;
            return true;
        }

        return false;
    }
}
