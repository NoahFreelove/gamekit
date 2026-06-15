// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace GameKit.Core.Health;

/// <summary>
/// Readiness check that issues <c>SELECT 1</c> against the configured Postgres instance with a
/// 2-second command timeout (D-08). Tagged <c>"ready"</c> so it participates in
/// <c>/health/ready</c> but not <c>/health/live</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Infra-safety (D-12 / HLTH-05):</b> the <c>catch</c> branch returns the hand-authored
/// constant <c>"database unreachable"</c> — Npgsql exceptions embed <c>host:port</c> in their
/// message, which must never appear in the health response body. <c>ex.Message</c>,
/// <c>ex.GetType().Name</c>, and <c>ex.ToString()</c> are explicitly forbidden here.
/// </para>
/// </remarks>
internal sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly GameKitOptions _opts;

    /// <summary>Constructs the health check.</summary>
    /// <param name="opts">
    /// Core runtime options. <see cref="GameKitOptions.ConnectionString"/> is used for the probe
    /// (the same connection the application uses at runtime).
    /// </param>
    public PostgresHealthCheck(GameKitOptions opts)
        => _opts = opts;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_opts.ConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 2;
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is 1
                ? HealthCheckResult.Healthy("connected")
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch
        {
            // D-12: ex.Message MUST NOT be surfaced — Npgsql embeds host:port in exception text.
            return HealthCheckResult.Unhealthy("database unreachable");
        }
    }
}
