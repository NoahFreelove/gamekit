// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Contract exposed by <see cref="MatchmakingAnalyticsDrainService"/> so integration tests can
/// drive a single drain pass deterministically (without waiting for the bounded-time loop).
/// </summary>
/// <remarks>
/// The concrete implementation is <see cref="MatchmakingAnalyticsDrainService"/> which also
/// extends <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>. Register via
/// <c>AddMatchmaking()</c> → <c>AddBackgroundServices()</c> (Plan 05-07) — the builder wires both
/// the <see cref="Microsoft.Extensions.Hosting.IHostedService"/> and the
/// <see cref="IMatchmakingAnalyticsDrain"/> singleton registrations.
/// </remarks>
public interface IMatchmakingAnalyticsDrain
{
    /// <summary>
    /// Drains up to <paramref name="maxBatch"/> events from the bounded
    /// <see cref="System.Threading.Channels.Channel{T}"/> and flushes them to Postgres via
    /// the Polly v8 retry pipeline. Returns the number of events that were persisted (0 if
    /// the batch was empty or dropped after Polly retry exhaustion).
    /// </summary>
    /// <param name="maxBatch">Maximum batch size to drain in one Postgres INSERT.</param>
    /// <param name="ct">Cancellation token — propagated to channel reads and Polly execution.</param>
    /// <returns>The number of <see cref="GameKit.Matchmaking.Entities.TicketEvent"/> rows persisted.</returns>
    Task<int> DrainOnceAsync(int maxBatch, CancellationToken ct);
}
