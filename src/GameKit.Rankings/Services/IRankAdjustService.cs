// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Rankings.Services;

/// <summary>
/// Admin manual rank-adjustment service (RANK-12 / D-19 / D-20).
/// Runs a SERIALIZABLE transaction that atomically updates <c>player_ranks</c>
/// and writes an <c>admin.player.rank_adjust</c> audit row.
/// </summary>
/// <remarks>
/// <para>
/// Manual rank-adjusts bypass the rating-period batch (D-20) — they take effect immediately
/// and are NOT replayed if the participant later appears in a batched update.
/// </para>
/// <para>
/// If the player has no existing rank row for the ladder, a new row is created lazily
/// with the operator's <c>newRating</c> and the ladder's default RD + volatility (RANK-07 carry).
/// </para>
/// </remarks>
public interface IRankAdjustService
{
    /// <summary>
    /// Adjusts a player's rating on a specific ladder.
    /// </summary>
    /// <param name="playerId">The target player id.</param>
    /// <param name="ladderId">The ladder whose rating should be adjusted.</param>
    /// <param name="newRating">The new rating value. Must be within <see cref="GameKitRankingsRankAdjustOptions"/> bounds.</param>
    /// <param name="reason">Operator-provided reason (3–512 chars, stored verbatim in the audit log).</param>
    /// <param name="actorId">The acting admin user id (written to the audit row).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="RankAdjustResult"/> describing the before/after state and delta.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// Thrown when the specified ladder does not exist.
    /// </exception>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Thrown when <paramref name="newRating"/> is outside the configured [MinRating, MaxRating] bounds.
    /// </exception>
    Task<RankAdjustResult> AdjustAsync(
        Guid playerId,
        Guid ladderId,
        double newRating,
        string reason,
        Guid actorId,
        CancellationToken ct);
}

/// <summary>
/// Result of a successful <see cref="IRankAdjustService.AdjustAsync"/> call (D-19).
/// </summary>
/// <param name="Before">
/// Rating before the adjustment. Undefined when <see cref="WasLazyCreated"/> is <c>true</c>
/// — callers should consult <see cref="WasLazyCreated"/> instead of relying on a sentinel value
/// (WR-02: <c>Before == 0</c> is ambiguous with configurations whose <c>MinRating</c> permits
/// a legitimate rating of 0).
/// </param>
/// <param name="After">Rating after the adjustment.</param>
/// <param name="Delta">
/// Signed rating change. When <see cref="WasLazyCreated"/> is <c>true</c> this is <see cref="After"/>
/// (there is no prior rating to subtract); callers showing a "delta" UI should suppress or label
/// it for the lazy-created case.
/// </param>
/// <param name="WasLazyCreated">
/// <c>true</c> when the call created a fresh <c>player_ranks</c> row because no prior row existed
/// for the <c>(playerId, ladderId)</c> pair; <c>false</c> when an existing row was updated in place.
/// </param>
public sealed record RankAdjustResult(double Before, double After, double Delta, bool WasLazyCreated);
