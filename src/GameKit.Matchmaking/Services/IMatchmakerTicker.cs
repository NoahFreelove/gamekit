// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Contract for the matchmaker ticker loop. Exposed as a public interface so integration
/// tests (notably the SC#4 phase-gate <c>MatchmakingLeaderElectionTests</c>) can drive a
/// single iteration deterministically without waiting for the <c>PeriodicTimer</c>.
/// </summary>
/// <remarks>
/// <para>
/// The concrete implementation is <c>MatchmakerTickerService</c>, which also extends
/// <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>. The matchmaking builder
/// (Plan 05-05 <c>MatchmakingBuilderExtensions.Ticker.cs</c>) wires both the
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> and the
/// <see cref="IMatchmakerTicker"/> singleton resolution against the same instance.
/// </para>
/// <para>
/// Mirrors <c>GameKit.Rankings.Services.IRankingsTicker</c> in shape; the matchmaker
/// ticker runs ~120× more frequently (500 ms vs 60 s) because it drives sub-second
/// match-formation against the live Redis queue.
/// </para>
/// </remarks>
public interface IMatchmakerTicker
{
    /// <summary>
    /// Executes a single ticker iteration:
    /// <list type="number">
    ///   <item>Acquire the Redis distributed leader-election lock (leader-only execution).</item>
    ///   <item>For each registered ladder/pool: renew the lease, scan candidates, invoke the strategy, run the atomic Lua claim, publish proposal events.</item>
    ///   <item>Run the proposal-sweeper to reap timed-out proposals (Pitfall §10).</item>
    ///   <item>Release the lock.</item>
    /// </list>
    /// </summary>
    /// <param name="ct">Cancellation token — passed to every async Redis / Postgres call.</param>
    /// <returns>A <see cref="MatcherTickResult"/> describing the outcome.</returns>
    Task<MatcherTickResult> RunOnceAsync(CancellationToken ct);
}
