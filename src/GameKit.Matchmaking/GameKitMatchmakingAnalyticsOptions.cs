// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking;

/// <summary>
/// Options controlling the bounded <c>Channel&lt;TicketEvent&gt;</c> + drain
/// <c>BackgroundService</c> that asynchronously persists matchmaking lifecycle events to
/// Postgres (D-15 / D-16 / D-18). All defaults sourced from RESEARCH §Decision 7.
/// </summary>
/// <remarks>
/// Matchmaking never blocks on Postgres writes — terminal/transition events are pushed into
/// a bounded channel and drained out-of-band. On sustained Postgres outage Polly retries
/// exhaust, the batch is dropped, and an OpenTelemetry counter
/// (<c>matchmaking.analytics.dropped_events</c>) is incremented for operator alerting.
/// </remarks>
public sealed class GameKitMatchmakingAnalyticsOptions
{
    /// <summary>
    /// Bounded channel capacity for queued ticket events. Default <c>10000</c>.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 7 / D-15 (drop-newest on full).</remarks>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>
    /// Maximum number of events drained per Postgres write batch. Default <c>100</c>.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 7.</remarks>
    public int DrainBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum drain interval in seconds (flush partial batches if no new events arrive).
    /// Default <c>5</c> seconds.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 7.</remarks>
    public int DrainIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum Polly retry attempts when the drain insert encounters a transient
    /// <c>NpgsqlException</c> / <c>DbUpdateException</c>. Default <c>4</c>.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 7 (Polly v8 retry pipeline).</remarks>
    public int PollyMaxRetryAttempts { get; set; } = 4;

    /// <summary>
    /// Base delay between Polly retries, in milliseconds (exponential backoff with jitter).
    /// Default <c>500</c> ms.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 7.</remarks>
    public int PollyBaseDelayMs { get; set; } = 500;

    /// <summary>
    /// Polly per-attempt timeout in seconds. Default <c>30</c>.
    /// </summary>
    /// <remarks>Default per RESEARCH §Decision 7.</remarks>
    public int PollyTimeoutSeconds { get; set; } = 30;
}
