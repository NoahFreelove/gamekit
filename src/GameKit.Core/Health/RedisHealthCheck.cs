// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace GameKit.Core.Health;

/// <summary>
/// Readiness check that issues a Redis <c>PING</c> command (D-09). Tagged <c>"ready"</c> so it
/// participates in <c>/health/ready</c> but not <c>/health/live</c>.
/// </summary>
/// <remarks>
/// <para>
/// This check is only registered when an <see cref="IConnectionMultiplexer"/> is present in DI
/// (i.e., when a Redis-using package — Matchmaking, Presence, or Lobby — has been installed).
/// The conditional registration guard lives in
/// <c>GameKitHealthBuilderExtensions.AddGameKitHealthChecks</c> (D-09).
/// </para>
/// <para>
/// <b>Infra-safety (D-12 / HLTH-05):</b> the <c>catch</c> branch returns the hand-authored
/// constant <c>"ping failed"</c>. Exception messages are never surfaced.
/// </para>
/// </remarks>
internal sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    /// <summary>Constructs the health check.</summary>
    /// <param name="redis">
    /// The shared Redis multiplexer. Non-optional here — this check is only registered when the
    /// multiplexer is confirmed present in DI.
    /// </param>
    public RedisHealthCheck(IConnectionMultiplexer redis)
        => _redis = redis;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.PingAsync().ConfigureAwait(false);
            return HealthCheckResult.Healthy("ping ok");
        }
        catch
        {
            // D-12: exception text must never be surfaced in the health response body.
            return HealthCheckResult.Unhealthy("ping failed");
        }
    }
}
