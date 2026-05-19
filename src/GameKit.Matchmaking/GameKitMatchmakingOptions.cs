// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking;

/// <summary>
/// Root options for <c>GameKit.Matchmaking</c>. Populated via
/// <c>services.AddGameKit(...).AddMatchmaking(opts =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// Pins every default value sourced from the Phase 5 decisions list (CONTEXT D-07 / D-08 /
/// D-15 / D-17 and RESEARCH §Decision 6 / §Decision 7 / §Decision 10 / §Decision 13). Sub
/// options are nested sealed classes; downstream Plans 05-04+ inject these via
/// <c>IOptions&lt;GameKitMatchmakingOptions&gt;</c>.
/// </remarks>
public sealed class GameKitMatchmakingOptions
{
    /// <summary>Options for the matchmaking ticker <c>BackgroundService</c> (MATCH-07).</summary>
    public GameKitMatchmakingTickerOptions Ticker { get; set; } = new();

    /// <summary>Escalating decline-cooldown options (D-08).</summary>
    public GameKitMatchmakingCooldownOptions Cooldown { get; set; } = new();

    /// <summary>Bounded-channel + drain options for analytics persistence (D-15 / D-16).</summary>
    public GameKitMatchmakingAnalyticsOptions Analytics { get; set; } = new();

    /// <summary>Reconciler options for chaos recovery (MATCH-12).</summary>
    public GameKitMatchmakingReconcilerOptions Reconciler { get; set; } = new();

    /// <summary>
    /// Accept-step proposal timeout in seconds. Default <c>10</c> seconds (CS:GO-style).
    /// </summary>
    /// <remarks>Default per CONTEXT D-07. Single global value — no per-ladder override in v1.</remarks>
    public int AcceptTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Retention window in days for <c>matchmaking_tickets</c>. Default <c>30</c> days.
    /// </summary>
    /// <remarks>Default per CONTEXT D-17. Enforced by the retention cleanup service (Plan 05-07).</remarks>
    public int TicketRetentionDays { get; set; } = 30;

    /// <summary>
    /// Per-player enqueue rate limit (requests per minute). Default <c>5</c>.
    /// </summary>
    /// <remarks>
    /// Default per RESEARCH §Decision 10 / MATCH-11. The rate-limit policy lives in
    /// <c>IGameKitRateLimitPolicies.MmEnqueue</c> and is partitioned by
    /// <c>ClaimTypes.NameIdentifier</c> (canonical PlayerId).
    /// </remarks>
    public int MatchmakingEnqueueRatePerMinute { get; set; } = 5;

    /// <summary>
    /// Maximum time (seconds) the long-poll <c>GET /api/mm/queue/{ticketId}/status</c>
    /// holds the connection waiting for a status PUBLISH. Default <c>30</c>.
    /// </summary>
    /// <remarks>
    /// Default per RESEARCH §Decision 9. Operators on bandwidth-constrained edges (mobile,
    /// PWA) may lower this to 15 s; load-test scenarios use 2 s for fast iteration. Pitfall §5
    /// connection-leak guard is invariant to the value — the linked CTS handles whatever
    /// timeout the operator picks.
    /// </remarks>
    public int LongPollTimeoutSeconds { get; set; } = 30;
}
