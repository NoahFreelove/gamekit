// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking;

/// <summary>
/// Options controlling the matchmaking reconciler <c>BackgroundService</c> (MATCH-12 chaos
/// recovery). The reconciler sweeps Postgres for non-terminal tickets / orphan sessions and
/// marks them <c>Expired</c> / <c>Cancelled</c>; it NEVER writes back to Redis (Pitfall §1).
/// </summary>
/// <remarks>Defaults sourced from RESEARCH §Decision 6.</remarks>
public sealed class GameKitMatchmakingReconcilerOptions
{
    /// <summary>
    /// Sweep interval in seconds. Default <c>30</c>.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 6 (every 30s + on startup).</remarks>
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Age in minutes after which a non-terminal Postgres ticket missing from Redis is
    /// considered stale and marked <c>Expired</c>. Default <c>5</c>.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 6.</remarks>
    public int StaleTicketThresholdMinutes { get; set; } = 5;

    /// <summary>
    /// Age in minutes after which an active <c>game_session</c> with no participant
    /// heartbeat is treated as orphaned and marked <c>Cancelled</c>. Default <c>10</c>.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 6.</remarks>
    public int OrphanSessionThresholdMinutes { get; set; } = 10;

    /// <summary>
    /// When <c>true</c> the reconciler runs only on the leader replica (gated on the
    /// matchmaker Redis lock). Default <c>true</c>.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 6 (leader-gated to avoid double-sweep).</remarks>
    public bool LeaderOnly { get; set; } = true;
}
