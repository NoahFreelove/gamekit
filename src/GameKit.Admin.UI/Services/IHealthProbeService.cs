// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Three-probe health check (D-10). Postgres connectivity via <c>SELECT 1</c> on an
/// <see cref="global::Npgsql.NpgsqlConnection"/> with a 2-second command timeout; Redis
/// connectivity via <c>IDatabase.PingAsync()</c> on the registered
/// <see cref="global::StackExchange.Redis.IConnectionMultiplexer"/>; recent error count from
/// <see cref="ErrorRateRingBuffer"/>. No outbound HTTP.
/// </summary>
public interface IHealthProbeService
{
    /// <summary>Runs all three probes and returns an aggregated <see cref="HealthReport"/>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<HealthReport> ProbeAsync(CancellationToken cancellationToken);
}
