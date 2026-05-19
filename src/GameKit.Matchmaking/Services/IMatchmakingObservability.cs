// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Live matchmaking-queue telemetry port consumed by the admin queue-depth panel (MATCH-14).
/// </summary>
/// <remarks>
/// <para>
/// The default <see cref="RedisMatchmakingObservability"/> adapter sources every field from
/// Redis (SCAN <c>mm:queue:*</c> + ZCARD per match + GET on the matcher lock key). It deliberately
/// does NOT consult the Postgres <c>matchmaking_tickets</c> reconciliation mirrors — Redis is the
/// source of truth (MATCH-04 / CONTEXT D-03) and the admin panel must reflect that invariant.
/// SC#6 (<c>MatchmakingObservabilityTests</c>) verifies the behaviour by deleting
/// <c>matchmaking_tickets</c> rows mid-test and asserting depth survives.
/// </para>
/// <para>
/// Resolved reflectively by the Phase 3 placeholder <c>QueueDepth.razor</c> page when
/// <c>GameKit.Matchmaking</c> is installed in the consumer's app; absent that reference the
/// admin panel renders <c>MissingPackageAlert</c>.
/// </para>
/// </remarks>
public interface IMatchmakingObservability
{
    /// <summary>
    /// Returns a live snapshot of every populated matchmaking queue + the current leader lease.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A populated <see cref="MatchmakingQueueStats"/>. When no pools currently exist the
    /// <see cref="MatchmakingQueueStats.Pools"/> list is empty.
    /// </returns>
    Task<MatchmakingQueueStats> GetQueueStatsAsync(CancellationToken ct = default);
}
