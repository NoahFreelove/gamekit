// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Core;
using GameKit.Core.Services;
using Npgsql;
using StackExchange.Redis;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Default <see cref="IHealthProbeService"/>. Three probes run sequentially (they are fast and
/// independent; parallelism would complicate latency attribution). Each probe swallows its
/// exception and maps it to a <c>Down</c> tile so one failed dependency does not mask the
/// others.
/// </summary>
public sealed class HealthProbeService : IHealthProbeService
{
    private readonly GameKitOptions _gameKitOpts;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ErrorRateRingBuffer _errors;
    private readonly IClock _clock;

    /// <summary>Constructs the service.</summary>
    /// <param name="gameKitOpts">Core options (supplies the Postgres connection string).</param>
    /// <param name="redis">Redis multiplexer if registered; null when no Redis connection is configured.</param>
    /// <param name="errors">Shared error-rate ring buffer.</param>
    /// <param name="clock">Clock abstraction.</param>
    public HealthProbeService(
        GameKitOptions gameKitOpts,
        ErrorRateRingBuffer errors,
        IClock clock,
        IConnectionMultiplexer? redis = null)
    {
        ArgumentNullException.ThrowIfNull(gameKitOpts);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(clock);
        _gameKitOpts = gameKitOpts;
        _redis = redis;
        _errors = errors;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<HealthReport> ProbeAsync(CancellationToken cancellationToken)
    {
        var pg = await ProbePostgresAsync(cancellationToken).ConfigureAwait(false);
        var redis = await ProbeRedisAsync(cancellationToken).ConfigureAwait(false);
        var err = ProbeErrorRate();
        return new HealthReport(pg, redis, err, _clock.UtcNow);
    }

    private async Task<HealthTile> ProbePostgresAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new NpgsqlConnection(_gameKitOpts.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 2;
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return result is 1
                ? new HealthTile("OK", "connected", sw.Elapsed.TotalMilliseconds)
                : new HealthTile("Degraded", $"unexpected result: {result}", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthTile("Down", ex.GetType().Name, sw.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<HealthTile> ProbeRedisAsync(CancellationToken cancellationToken)
    {
        if (_redis is null)
            return new HealthTile("Degraded", "not configured", null);

        var sw = Stopwatch.StartNew();
        try
        {
            var db = _redis.GetDatabase();
            var latency = await db.PingAsync().ConfigureAwait(false);
            sw.Stop();
            return new HealthTile("OK", "ping ok", latency.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthTile("Down", ex.GetType().Name, sw.Elapsed.TotalMilliseconds);
        }
    }

    private HealthTile ProbeErrorRate()
    {
        var count = _errors.RecentErrorCount();
        var status = count switch
        {
            0 => "OK",
            < 10 => "OK",
            < 100 => "Degraded",
            _ => "Down",
        };
        return new HealthTile(status, $"{count} errors in window", null);
    }
}
