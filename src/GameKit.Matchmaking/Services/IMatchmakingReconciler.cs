// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Contract exposed by <see cref="MatchmakingReconcilerService"/> so integration tests can
/// drive a single sweep deterministically (without waiting for the periodic timer).
/// </summary>
/// <remarks>
/// The concrete implementation is <see cref="MatchmakingReconcilerService"/> which also
/// extends <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>. Register via
/// <c>AddMatchmaking()</c> → <c>AddBackgroundServices()</c> (Plan 05-07).
/// </remarks>
public interface IMatchmakingReconciler
{
    /// <summary>
    /// Runs a single reconciliation sweep:
    /// <list type="number">
    ///   <item>Acquire the matchmaker leader-election lock; return
    ///         <see cref="ReconcileResult.SkippedBecauseNotLeader"/> when not the leader.</item>
    ///   <item>Mark non-terminal <c>matchmaking_tickets</c> older than
    ///         <see cref="GameKitMatchmakingReconcilerOptions.StaleTicketThresholdMinutes"/>
    ///         and missing from Redis as <c>Expired</c>.</item>
    ///   <item>Mark <c>game_sessions</c> in <c>Active</c> state older than
    ///         <see cref="GameKitMatchmakingReconcilerOptions.OrphanSessionThresholdMinutes"/>
    ///         as <c>Cancelled</c>; emit an admin-audit row.</item>
    /// </list>
    /// </summary>
    Task<ReconcileResult> RunSweepOnceAsync(CancellationToken ct);
}

/// <summary>
/// Outcome of a single <see cref="IMatchmakingReconciler.RunSweepOnceAsync"/> call.
/// </summary>
/// <param name="TicketsExpired">Number of <c>matchmaking_tickets</c> rows marked Expired.</param>
/// <param name="SessionsCancelled">Number of orphan <c>game_sessions</c> rows marked Cancelled.</param>
/// <param name="SkippedBecauseNotLeader">True when the sweep aborted early because the lease was not acquired.</param>
public readonly record struct ReconcileResult(
    int TicketsExpired,
    int SessionsCancelled,
    bool SkippedBecauseNotLeader);
