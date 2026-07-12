// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Rankings;

/// <summary>
/// Root options for <c>GameKit.Rankings</c>. Populated via
/// <c>services.AddGameKit(...).AddRankings(opts =&gt; ...)</c>.
/// </summary>
public sealed class GameKitRankingsOptions
{
    /// <summary>Options controlling the ranking ticker background service (plan 04-06).</summary>
    public GameKitRankingsTickerOptions Ticker { get; set; } = new();

    /// <summary>Glicko-2 algorithm hyper-parameters shared across ladders that use the default algorithm.</summary>
    public GameKitRankingsGlicko2Options Glicko2 { get; set; } = new();

    /// <summary>Options controlling the GDPR export endpoint (plan 04-08, D-18).</summary>
    public GameKitRankingsGdprExportOptions GdprExport { get; set; } = new();

    /// <summary>Options controlling manual rank-adjust bounds (plan 04-07, D-19).</summary>
    public GameKitRankingsRankAdjustOptions RankAdjust { get; set; } = new();

    /// <summary>Options controlling the session-complete endpoint (plan 04-05, D-10).</summary>
    public GameKitRankingsSessionCompleteOptions SessionComplete { get; set; } = new();

    /// <summary>
    /// Options controlling cleanup of audit / idempotency tables maintained by Rankings
    /// (CR-05). Includes the <c>pending_rating_updates</c> retention TTL.
    /// </summary>
    public GameKitRankingsCleanupOptions Cleanup { get; set; } = new();

    /// <summary>Options controlling the rank-decay background service (RANK-15).</summary>
    public GameKitRankingsDecayOptions Decay { get; set; } = new();
}

/// <summary>Options for the ranking ticker background service (D-01 / D-03 / D-04).</summary>
public sealed class GameKitRankingsTickerOptions
{
    /// <summary>
    /// How often the ticker wakes up to check each ladder's drain eligibility.
    /// Default <c>60</c> seconds. The ticker compares each ladder's <c>LastDrainedAt</c>
    /// against its configured <c>RatingPeriod</c> to decide whether to drain.
    /// </summary>
    public int TickIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Redis distributed-lock TTL in seconds. Default <c>90</c> (1.5× tick interval).
    /// Allows one missed self-renewal before the lock expires and a standby instance
    /// picks up leadership (D-03).
    /// </summary>
    public int LockTtlSeconds { get; set; } = 90;

    /// <summary>
    /// Redis key for the distributed leader-election lock (D-03).
    /// Default <c>"gamekit:rankings:ticker:lease"</c>.
    /// </summary>
    public string LockKey { get; set; } = "gamekit:rankings:ticker:lease";
}

/// <summary>Glicko-2 hyper-parameters (D-01 / RANK-05).</summary>
public sealed class GameKitRankingsGlicko2Options
{
    /// <summary>
    /// Glicko-2 system constant τ (tau). Controls volatility change speed.
    /// Default <c>0.5</c> per Glickman's recommendation — lower values reduce volatility
    /// swings. The <c>Glicko2Algorithm</c> reads this at construction time (plan 04-03).
    /// </summary>
    public double Tau { get; set; } = 0.5;

    /// <summary>
    /// Initial volatility σ₀. Default <c>0.06</c> per Glickman §2. Applied to new players
    /// before their first rating-period batch.
    /// </summary>
    public double InitVolatility { get; set; } = 0.06;
}

/// <summary>GDPR export options (D-18 / RANK-13).</summary>
public sealed class GameKitRankingsGdprExportOptions
{
    /// <summary>
    /// Maximum export payload size in bytes. Default <c>25 MiB</c> per D-18.
    /// Responses exceeding this threshold are rejected with HTTP 413 before serialization
    /// completes. Configurable for operators whose player-base generates larger histories.
    /// </summary>
    public int MaxBytes { get; set; } = 25 * 1024 * 1024;
}

/// <summary>Manual rank-adjust option bounds (D-19 / RANK-12).</summary>
public sealed class GameKitRankingsRankAdjustOptions
{
    /// <summary>Minimum rating value accepted by the rank-adjust endpoint. Default <c>100</c>.</summary>
    public double MinRating { get; set; } = 100;

    /// <summary>Maximum rating value accepted by the rank-adjust endpoint. Default <c>4000</c>.</summary>
    public double MaxRating { get; set; } = 4000;
}

/// <summary>Session-complete endpoint rate-limit + idempotency options (D-08 / D-10 / RANK-11).</summary>
public sealed class GameKitRankingsSessionCompleteOptions
{
    /// <summary>Rate-limit settings for <c>POST /api/sessions/{id}/complete</c> (D-10).</summary>
    public GameKitRankingsRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>
    /// TTL for <c>session_complete_idempotency</c> rows. Default <c>24 hours</c> per D-08.
    /// The nightly cleanup service deletes rows older than this value.
    /// </summary>
    public TimeSpan IdempotencyTtl { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>Rate-limit options for a single GameKit.Rankings endpoint.</summary>
public sealed class GameKitRankingsRateLimitOptions
{
    /// <summary>Maximum number of requests permitted per window. Default <c>300</c> per D-10.</summary>
    public int PermitLimit { get; set; } = 300;

    /// <summary>Sliding-window duration. Default <c>1 minute</c> per D-10.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Options controlling the rank-decay background service (RANK-15).</summary>
public sealed class GameKitRankingsDecayOptions
{
    /// <summary>How often the decay runner wakes up. Default <c>24 hours</c>.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Redis distributed-lock TTL in seconds. Default <c>120</c>.</summary>
    public int LockTtlSeconds { get; set; } = 120;

    /// <summary>
    /// Redis key for the decay leader-election lock.
    /// Default <c>"gamekit:rankings:decay:lease"</c>.
    /// MUST differ from <see cref="GameKitRankingsTickerOptions.LockKey"/> —
    /// reusing the ticker lease key causes decay and ticker to mutually exclude each other
    /// (RANK-15 / Pitfall 4 from RESEARCH.md).
    /// </summary>
    public string LockKey { get; set; } = "gamekit:rankings:decay:lease";

    /// <summary>
    /// Minimum rating above which decay applies. Players at or below this value are
    /// decay-immune. Default <c>1500</c> (Glicko-2 mean rating).
    /// </summary>
    public double DecayThresholdRating { get; set; } = 1500;

    /// <summary>Days of inactivity (since <c>LastMatchAt</c>) before decay is applied. Default <c>30</c>.</summary>
    public int InactivityDays { get; set; } = 30;

    /// <summary>Maximum rows processed per decay run (batch size). Default <c>500</c>.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Number of placement matches required before a player's visible rank is revealed.
    /// Default <c>10</c>. Used when lazily creating new <c>PlayerRank</c> rows in
    /// <c>RankingsTickerService</c> (RANK-16).
    /// </summary>
    public int PlacementMatchCount { get; set; } = 10;
}

/// <summary>
/// Cleanup options for Rankings audit / pending-row tables (CR-05).
/// </summary>
public sealed class GameKitRankingsCleanupOptions
{
    /// <summary>
    /// Retention TTL for <c>pending_rating_updates</c> rows after they have been marked
    /// <c>AppliedAt = now</c> by the ticker. Default <c>30 days</c>. Rows older than this
    /// are deleted by <c>IdempotencyCleanupService</c> alongside the session-complete
    /// idempotency cleanup pass.
    /// </summary>
    public TimeSpan PendingRetentionTtl { get; set; } = TimeSpan.FromDays(30);
}
