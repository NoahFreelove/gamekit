// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Rankings.Entities;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Build-time configuration for a single named ladder registered via
/// <c>AddRankings().AddLadder("name", config =&gt; ...)</c> (D-21 / RANK-09).
/// Defaults match the Glicko-2 paper recommendations. Fields are serialized into
/// the ladder's <c>Config</c> JSONB column at startup by <c>StartupLadderUpserter</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>RatingPeriod</c> defaults to one hour per D-02. The ticker service (plan 04-06)
/// reads this field from the JSONB config to determine when to drain pending rating
/// updates for each ladder.
/// </para>
/// <para>
/// Season-reset behaviour is governed by <c>ResetPolicy</c> and its associated fields
/// (<c>RegressionFactor</c>, <c>RdCeiling</c>, <c>RdBump</c>). The endpoint service
/// for ending seasons (plan 04-07) reads these fields to apply the chosen strategy.
/// </para>
/// </remarks>
public sealed class LadderConfig
{
    /// <summary>
    /// Operator-supplied ladder name. Must be non-empty. Case-insensitive uniqueness is
    /// enforced by the <c>citext</c> column type in Postgres.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ranking algorithm identifier. Must resolve to a registered <c>IRankingAlgorithm</c>
    /// implementation (default <c>"glicko2"</c>).
    /// </summary>
    public string Algorithm { get; set; } = "glicko2";

    /// <summary>Starting rating for new players on this ladder. Default <c>1500</c> per Glickman §2.</summary>
    public double DefaultRating { get; set; } = 1500;

    /// <summary>Starting rating deviation for new players. Default <c>350</c> per Glickman §2.</summary>
    public double DefaultRd { get; set; } = 350;

    /// <summary>Starting volatility for new players. Default <c>0.06</c> per Glickman §2.</summary>
    public double DefaultVolatility { get; set; } = 0.06;

    /// <summary>
    /// Window between rating batch drains. Default <c>1 hour</c> per D-02.
    /// Overridable per ladder. The ticker service reads this from the ladder's JSONB config.
    /// </summary>
    public TimeSpan RatingPeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Season-reset strategy applied when an operator triggers end-of-season via the admin UI.
    /// Default <c>SoftRegress</c> per D-12.
    /// </summary>
    public SeasonResetPolicy ResetPolicy { get; set; } = SeasonResetPolicy.SoftRegress;

    /// <summary>
    /// For <c>SoftRegress</c> resets: rating is pulled toward <c>DefaultRating</c> by this factor.
    /// Default <c>0.5</c> (halves the distance from default rating). Only used when
    /// <c>ResetPolicy == SoftRegress</c>.
    /// </summary>
    public double RegressionFactor { get; set; } = 0.5;

    /// <summary>
    /// For <c>SoftRegress</c> resets: RD is capped at this value after reset.
    /// Default <c>200</c>. Only used when <c>ResetPolicy == SoftRegress</c>.
    /// </summary>
    public double RdCeiling { get; set; } = 200;

    /// <summary>
    /// For <c>SoftRegress</c> resets: RD is increased by this amount (uncertainty bump).
    /// Default <c>50</c>. Only used when <c>ResetPolicy == SoftRegress</c>.
    /// </summary>
    public double RdBump { get; set; } = 50;

    /// <summary>
    /// Minimum fraction [0.0–1.0] of a session a participant (e.g. a backfill player) must
    /// have been present for in order to receive a rating change. When <see langword="null"/>,
    /// no participation guard is applied and all participants receive rating updates (backwards-
    /// compatible v1 behaviour). When set, any participant whose
    /// <c>session_participants.ParticipationFraction</c> is non-null and below this threshold
    /// will not have a <c>PendingRatingUpdate</c> row inserted — the player never enters the
    /// rating batch and their rating is unchanged for that session (MATCH-19 SC#4).
    /// Persisted into the ladder JSONB <c>Config</c> at startup by
    /// <c>StartupLadderUpserter</c> under the property name
    /// <c>"MinParticipationFractionForRating"</c>, and read at session-complete time by
    /// <c>PendingRatingUpdatesAdapter.OnCompletedAsync</c>. Null fraction on a participant row
    /// (pre-Phase-9 rows or fully-present players) bypasses the guard entirely.
    /// </summary>
    public double? MinParticipationFractionForRating { get; set; }
}
