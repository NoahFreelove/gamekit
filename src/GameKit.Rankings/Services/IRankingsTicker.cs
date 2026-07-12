// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Rankings.Services;

/// <summary>
/// Contract for the rankings ticker loop. Exposed as a public interface so integration tests
/// can drive a single iteration deterministically without waiting for the <c>PeriodicTimer</c>.
/// </summary>
/// <remarks>
/// The concrete implementation is <see cref="RankingsTickerService"/> which also extends
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>. Register via
/// <c>AddRankings()</c> — the builder wires both the <see cref="Microsoft.Extensions.Hosting.IHostedService"/>
/// and the <see cref="IRankingsTicker"/> singleton registrations automatically.
/// </remarks>
public interface IRankingsTicker
{
    /// <summary>
    /// Executes a single ticker iteration:
    /// <list type="number">
    ///   <item>Try to acquire the Redis distributed lock.</item>
    ///   <item>Scan active ladders whose rating period has elapsed.</item>
    ///   <item>Drain all pending rating updates for each due ladder in a per-ladder transaction.</item>
    ///   <item>Release the lock.</item>
    /// </list>
    /// </summary>
    /// <param name="ct">Cancellation token — passed to all async database and Redis operations.</param>
    /// <returns>A <see cref="TickResult"/> describing the outcome of this iteration.</returns>
    Task<TickResult> RunOnceAsync(CancellationToken ct);
}

/// <summary>
/// Outcome of a single <see cref="IRankingsTicker.RunOnceAsync"/> iteration.
/// </summary>
public enum TickResult
{
    /// <summary>
    /// The Redis distributed lock was not acquired (another replica holds it).
    /// This instance skips the current tick and waits for the next interval.
    /// </summary>
    LockNotAcquired = 0,

    /// <summary>
    /// The lock was acquired but no ladders were due for a rating-period drain
    /// (all ladders have been drained more recently than their configured period).
    /// </summary>
    NoLaddersDue = 1,

    /// <summary>
    /// At least one ladder was drained successfully.
    /// </summary>
    Drained = 2,

    /// <summary>
    /// The drain was attempted but the per-ladder transaction was rolled back due to an error.
    /// The pending rows remain un-applied and will be retried on the next tick.
    /// </summary>
    DrainFailedRolledBack = 3,
}
