// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Core.Services;
using CoreHealthCheckService = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService;
using CoreHealthReport = Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport;
using CoreHealthStatus = Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Thin adapter over <see cref="CoreHealthCheckService"/> that projects the Core
/// <c>postgres</c> and <c>redis</c> health-check entries into <see cref="HealthTile"/> view
/// records, and appends the Admin-local error-rate tile sourced from
/// <see cref="ErrorRateRingBuffer"/> / <see cref="IRedisErrorRateCounter"/> (D-16).
/// </summary>
/// <remarks>
/// <para>
/// Postgres and Redis connectivity are probed exclusively via the shared
/// <c>HealthCheckService</c> registered by <c>AddGameKitHealthChecks()</c> (Plan 01, HLTH-06).
/// The previous direct <c>NpgsqlConnection</c> / <c>IDatabase.PingAsync</c> logic has been
/// removed to avoid duplicate round-trips and duplicate error-class leakage (T-14-09 mitigation).
/// </para>
/// <para>
/// When <c>AddGameKitHealthChecks()</c> has not been called (Admin-without-Core-health install),
/// the <c>postgres</c> / <c>redis</c> entries are absent from the report and the tiles degrade
/// gracefully to <c>Down</c> / <c>"not configured"</c> (T-14-10 mitigation).
/// </para>
/// </remarks>
public sealed class HealthProbeService : IHealthProbeService
{
    private readonly CoreHealthCheckService _healthCheckService;
    private readonly ErrorRateRingBuffer _errors;
    private readonly IClock _clock;
    private readonly IRedisErrorRateCounter? _redisErrors;

    /// <summary>Constructs the service.</summary>
    /// <param name="healthCheckService">
    /// Core <see cref="CoreHealthCheckService"/> from which Postgres and Redis tiles are projected.
    /// Registered by <c>AddHealthChecks()</c> / <c>AddGameKitHealthChecks()</c>.
    /// </param>
    /// <param name="errors">Shared error-rate ring buffer (Admin-local, D-16).</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="redisErrors">
    /// Optional Redis error counter (ADMIN-14). When present, <see cref="ProbeAsync"/> reads the
    /// cross-replica aggregate; when absent or when Redis returns <c>-1</c>, falls back to
    /// <paramref name="errors"/> for single-instance behavior.
    /// </param>
    public HealthProbeService(
        CoreHealthCheckService healthCheckService,
        ErrorRateRingBuffer errors,
        IClock clock,
        IRedisErrorRateCounter? redisErrors = null)
    {
        ArgumentNullException.ThrowIfNull(healthCheckService);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(clock);
        _healthCheckService = healthCheckService;
        _errors = errors;
        _clock = clock;
        _redisErrors = redisErrors;
    }

    /// <summary>
    /// Runs all health probes and returns a <see cref="HealthReport"/> snapshot.
    /// Postgres and Redis tiles are projected from the Core <see cref="CoreHealthCheckService"/>;
    /// the error-rate tile is sourced from the Admin-local <see cref="ErrorRateRingBuffer"/> (D-16).
    /// </summary>
    /// <param name="cancellationToken">Propagated to <c>HealthCheckService.CheckHealthAsync</c> and <c>ProbeErrorRateAsync</c>.</param>
    /// <returns>A <see cref="HealthReport"/> containing three tiles and the UTC check timestamp.</returns>
    public async Task<HealthReport> ProbeAsync(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService
            .CheckHealthAsync(cancellationToken)
            .ConfigureAwait(false);

        var pg    = GetTile(report, "postgres");
        var redis = GetTile(report, "redis");
        var err   = await ProbeErrorRateAsync(cancellationToken).ConfigureAwait(false);

        return new HealthReport(pg, redis, err, _clock.UtcNow);
    }

    private static HealthTile GetTile(CoreHealthReport report, string checkName)
    {
        if (!report.Entries.TryGetValue(checkName, out var entry))
            return new HealthTile("Down", "not configured", null);

        var status = entry.Status switch
        {
            CoreHealthStatus.Healthy   => "OK",
            CoreHealthStatus.Degraded  => "Degraded",
            CoreHealthStatus.Unhealthy => "Down",
            _                          => "Down",
        };

        return new HealthTile(status, entry.Description ?? string.Empty,
            entry.Duration.TotalMilliseconds);
    }

    private async Task<HealthTile> ProbeErrorRateAsync(CancellationToken ct)
    {
        long count;
        if (_redisErrors is not null)
        {
            count = await _redisErrors.RecentErrorCountAsync(ct).ConfigureAwait(false);
            if (count == -1)  // documented Redis-unavailable sentinel — fall back to ring buffer
                count = _errors.RecentErrorCount();
        }
        else
        {
            count = _errors.RecentErrorCount();
        }

        // Defensive clamp (WR-06): a stray negative (any non -1 sentinel, or future contract
        // drift) must never map to "OK" via the `< 10` bucket below.
        count = Math.Max(0, count);

        var status = count switch
        {
            < 10 => "OK",
            < 100 => "Degraded",
            _ => "Down",
        };
        return new HealthTile(status, $"{count} errors in window", null);
    }
}
