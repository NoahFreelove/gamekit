// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GameKit.Matchmaking.Health;

/// <summary>
/// Readiness check for the matchmaker leader-election lock. Reports
/// <c>Healthy</c> when this replica holds the lock, <c>Degraded</c> (never
/// <c>Unhealthy</c>) when it does not — so a follower replica stays in the
/// load-balancer rotation (HLTH-03 / D-10).
/// The description surfaces the holder <c>InstanceId</c> and remaining TTL so
/// operators can identify which replica leads and how long the lease lasts
/// (HLTH-04 / D-13).
/// </summary>
internal sealed class MatchmakingLeaderHealthCheck : IHealthCheck
{
    private readonly IMatchmakerLease _lease;

    /// <summary>Constructs the check with the injected lease.</summary>
    /// <param name="lease">The matchmaker lease service.</param>
    public MatchmakingLeaderHealthCheck(IMatchmakerLease lease)
        => _lease = lease;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _lease.QueryLeaseAsync(cancellationToken).ConfigureAwait(false);

        // D-13: InstanceId is intentionally surfaced (HLTH-04 requires replica identity)
        if (status.HolderInstanceId == _lease.InstanceId)
            return HealthCheckResult.Healthy(
                $"leader: {_lease.InstanceId}, ttl: {status.Ttl?.TotalSeconds:F0}s");

        // D-10: Degraded (not Unhealthy) — follower stays in rotation
        return HealthCheckResult.Degraded(
            status.HolderInstanceId is not null
                ? $"not leader; holder: {status.HolderInstanceId}, ttl: {status.Ttl?.TotalSeconds:F0}s"
                : "not leader; lock currently unheld");
    }
}
