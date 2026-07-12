// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
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
/// <b>Bounded ping (WR-03):</b> StackExchange.Redis async operations do NOT honor a
/// <see cref="CancellationToken"/> — they bound themselves by the multiplexer's
/// <c>AsyncTimeout</c> instead. To guarantee the readiness probe returns fast (symmetric with
/// the 2-second Postgres timeout) regardless of the consumer's multiplexer configuration, the
/// ping is explicitly raced against a 2-second delay; the timeout branch is reported as
/// <c>Unhealthy("redis unreachable")</c>.
/// </para>
/// <para>
/// <b>Infra-safety (D-12 / HLTH-05):</b> the <c>catch</c> and timeout branches return the
/// hand-authored constants <c>"ping failed"</c> / <c>"redis unreachable"</c>. Exception
/// messages are never surfaced.
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

            // WR-03: bound the ping to ~2s (symmetric with the Postgres command/connect
            // timeout). PingAsync ignores the CancellationToken (StackExchange.Redis honors
            // AsyncTimeout instead), so race it against an explicit delay to guarantee the
            // readiness probe returns fast even under a Redis partition with a long AsyncTimeout.
            var pingTask = db.PingAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            var winner = await Task.WhenAny(pingTask, timeoutTask).ConfigureAwait(false);

            if (winner != pingTask)
                // D-12: infra-free constant — no host/exception text.
                return HealthCheckResult.Unhealthy("redis unreachable");

            // Observe the ping result so a faulted ping surfaces via the catch below.
            await pingTask.ConfigureAwait(false);
            return HealthCheckResult.Healthy("ping ok");
        }
        catch
        {
            // D-12: exception text must never be surfaced in the health response body.
            return HealthCheckResult.Unhealthy("ping failed");
        }
    }
}
