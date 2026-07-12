// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Cross-replica error-rate counter (ADMIN-14). When <see cref="RedisErrorRateCounter"/>
/// is registered, <see cref="HealthProbeService"/> reads from this interface instead of
/// <see cref="ErrorRateRingBuffer"/> so the health panel is correct across all replicas.
/// Implementations MUST NOT throw — fire-and-forget contract on writes.
/// </summary>
public interface IRedisErrorRateCounter
{
    /// <summary>
    /// Increments the current time bucket. Fire-and-forget — must not throw.
    /// </summary>
    void IncrementError();

    /// <summary>
    /// Returns the aggregate error count across all replicas for the current sliding window.
    /// Returns <c>-1</c> when Redis is unavailable (caller falls back to in-memory buffer).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<long> RecentErrorCountAsync(CancellationToken ct = default);
}
