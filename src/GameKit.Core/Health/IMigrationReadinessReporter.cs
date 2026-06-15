// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Health;

/// <summary>
/// Implemented once per package that owns migrations. Returns readiness for the
/// migrations-aggregate <c>"migrations"</c>
/// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck"/>.
/// </summary>
/// <remarks>
/// <para>
/// Six implementations exist across the GameKit package family: Core, Auth, Admin.UI, Rankings,
/// Matchmaking, and Lobby. Each is registered as an enumerable singleton:
/// <c>services.AddSingleton&lt;IMigrationReadinessReporter, TReporter&gt;()</c>.
/// </para>
/// <para>
/// <b>Latch contract (D-07):</b> once a reporter returns <c>true</c> it <em>must</em> continue
/// returning <c>true</c> on every subsequent call without querying Postgres. Migrations are
/// never un-applied at runtime, so steady-state probes incur no database round-trip after the
/// first successful check.
/// </para>
/// </remarks>
public interface IMigrationReadinessReporter
{
    /// <summary>
    /// Returns <c>true</c> when all migrations for this package are applied.
    /// After the first <c>true</c> result, subsequent calls return <c>true</c> immediately
    /// without querying Postgres (latch pattern per D-07).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when no pending migrations remain for this package's migration history table;
    /// <c>false</c> while any migration is still pending.
    /// </returns>
    ValueTask<bool> IsReadyAsync(CancellationToken ct);
}
