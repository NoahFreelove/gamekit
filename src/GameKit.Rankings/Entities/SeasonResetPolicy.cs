// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Rankings.Entities;

/// <summary>
/// Strategy applied when an admin ends the current season on a ladder.
/// Mirrors the <c>GameSessionState</c> string-stored enum precedent from Phase 1 (D-13).
/// </summary>
public enum SeasonResetPolicy
{
    /// <summary>
    /// Default strategy. Each player's new starting rating regresses toward the ladder default:
    /// <c>newRating = defaultRating + (rating - defaultRating) * RegressionFactor</c>.
    /// Rating deviation is clamped to <c>min(RdCeiling, currentRd + RdBump)</c>.
    /// Volatility is reset to the ladder default.
    /// </summary>
    SoftRegress = 0,

    /// <summary>Rating, RD, and volatility are all reset to ladder defaults.</summary>
    HardReset = 1,

    /// <summary>
    /// Archive row is written; live <c>player_ranks</c> are unchanged.
    /// Seasons become a passive query-time concept — useful when operators want
    /// historical snapshots without disrupting the live leaderboard.
    /// </summary>
    ArchiveOnly = 2,
}
