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
/// The description surfaces only the per-process GUID token of the holding
/// replica (the portion of <c>InstanceId</c> after the first <c>':'</c>) — never
/// the machine name — so the anonymous <c>/health/ready</c> payload contains no
/// hostname (HLTH-05 / D-12) while still uniquely identifying the holder (HLTH-04).
/// The full <c>MachineName:Guid</c> remains available to the authenticated admin
/// panel via the lease, not the anonymous probe.
/// </summary>
internal sealed class MatchmakingLeaderHealthCheck : IHealthCheck
{
    private readonly IMatchmakerLease _lease;

    /// <summary>Constructs the check with the injected lease.</summary>
    /// <param name="lease">The matchmaker lease service.</param>
    public MatchmakingLeaderHealthCheck(IMatchmakerLease lease)
        => _lease = lease;

    /// <summary>
    /// Extracts the per-process GUID token from an <c>InstanceId</c> of the form
    /// <c>MachineName:Guid</c>, surfacing only the portion after the first <c>':'</c>.
    /// When the id is <c>null</c> the lock is unheld; when no <c>':'</c> is present
    /// (<c>IndexOf</c> returns -1, so <c>+1</c> yields index 0) the whole string is
    /// returned as a safe fallback. The machine name is never surfaced (HLTH-05).
    /// </summary>
    private static string ReplicaToken(string? instanceId)
        => instanceId is null ? "(unheld)" : instanceId[(instanceId.IndexOf(':') + 1)..];

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _lease.QueryLeaseAsync(cancellationToken).ConfigureAwait(false);

        // D-13: only the per-process GUID token (NOT the machine name) is surfaced so the
        // anonymous /health/ready payload carries no hostname (HLTH-05) while still uniquely
        // identifying the holding replica (HLTH-04).
        if (status.HolderInstanceId == _lease.InstanceId)
            return HealthCheckResult.Healthy(
                $"leader: {ReplicaToken(_lease.InstanceId)}, ttl: {status.Ttl?.TotalSeconds:F0}s");

        // D-10: Degraded (not Unhealthy) — follower stays in rotation
        return HealthCheckResult.Degraded(
            status.HolderInstanceId is not null
                ? $"not leader; holder: {ReplicaToken(status.HolderInstanceId)}, ttl: {status.Ttl?.TotalSeconds:F0}s"
                : "not leader; lock currently unheld");
    }
}
