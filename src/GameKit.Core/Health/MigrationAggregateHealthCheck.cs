// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GameKit.Core.Health;

/// <summary>
/// Aggregates <see cref="IMigrationReadinessReporter"/> implementations from all installed
/// GameKit packages into a single <c>"migrations"</c> readiness check (D-06).
/// Returns <see cref="HealthCheckResult.Unhealthy"/> while any reporter reports pending
/// migrations, and <see cref="HealthCheckResult.Healthy"/> once all report applied.
/// </summary>
/// <remarks>
/// Registered with tag <c>"ready"</c> so it participates in <c>/health/ready</c> but not
/// <c>/health/live</c>. Descriptions are hand-authored constants — exception text and connection
/// strings are never surfaced (D-12 / HLTH-05).
/// </remarks>
internal sealed class MigrationAggregateHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IMigrationReadinessReporter> _reporters;

    /// <summary>Constructs the aggregate check.</summary>
    /// <param name="reporters">
    /// All <see cref="IMigrationReadinessReporter"/> implementations registered by installed
    /// GameKit packages. Resolved from DI as <see cref="IEnumerable{T}"/>.
    /// </param>
    public MigrationAggregateHealthCheck(
        IEnumerable<IMigrationReadinessReporter> reporters)
        => _reporters = reporters;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var pendingCount = 0;
        var totalCount = 0;

        foreach (var reporter in _reporters)
        {
            totalCount++;
            if (!await reporter.IsReadyAsync(cancellationToken).ConfigureAwait(false))
                pendingCount++;
        }

        if (pendingCount > 0)
            // D-12: hand-authored description — no exception text, no connection string
            return HealthCheckResult.Unhealthy(
                $"{pendingCount} of {totalCount} migration sets pending");

        return HealthCheckResult.Healthy("all migration sets applied");
    }
}
