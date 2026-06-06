// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Rankings.Entities;
using GameKit.Rankings.Http.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Rankings.Services;

/// <summary>
/// Default <see cref="ILeaderboardService"/> implementation. Queries the live <c>player_ranks</c>
/// table (when <c>seasonId</c> is null) or the <c>season_rank_archive</c> table (when scoped to a
/// season). Rank numbers are assigned in-memory after an already-sorted <c>ORDER BY Rating DESC</c>
/// SELECT — this avoids the EF Core 10 / Npgsql limitation where <c>ROW_NUMBER() OVER</c> projections
/// require raw SQL. The result set is capped at 500 rows (per-call), well within acceptable in-memory
/// allocation for leaderboard displays (RANK-08 / D-23).
/// </summary>
public sealed class LeaderboardService : ILeaderboardService
{
    private readonly GameKitDbContext _ctx;

    /// <summary>Constructs the service with a scoped <see cref="GameKitDbContext"/>.</summary>
    /// <param name="ctx">Scoped EF Core context contributing Rankings entities via <c>RankingsModelBuilderExtension</c>.</param>
    public LeaderboardService(GameKitDbContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaderboardRowDto>> TopAsync(
        Guid ladderId,
        int limit = 100,
        Guid? seasonId = null,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        if (seasonId is null)
        {
            // Live leaderboard — hit idx_player_ranks_ladder_rating (ladder_id, rating DESC).
            var rows = await _ctx.Set<PlayerRank>()
                .AsNoTracking()
                .Where(r => r.LadderId == ladderId)
                .OrderByDescending(r => r.Rating)
                .Take(limit)
                .Join(
                    _ctx.Set<Player>().AsNoTracking(),
                    rank => rank.PlayerId,
                    player => player.Id,
                    (rank, player) => new { rank, playerName = player.DisplayName })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return rows
                .Select((row, idx) => new LeaderboardRowDto(
                    Rank: idx + 1,
                    PlayerId: row.rank.PlayerId,
                    DisplayName: row.playerName,
                    // RANK-16 / T-08-01-01: hide raw rating while player is in placement.
                    Rating: row.rank.IsInPlacement ? (double?)null : row.rank.Rating,
                    RatingDeviation: row.rank.IsInPlacement ? (double?)null : row.rank.RatingDeviation,
                    Wins: row.rank.Wins,
                    Losses: row.rank.Losses,
                    Draws: row.rank.Draws,
                    IsInPlacement: row.rank.IsInPlacement,
                    PlacementMatchesRemaining: row.rank.PlacementMatchesRemaining))
                .ToList();
        }
        else
        {
            // Archived season leaderboard — composite index (ladder_id, season_id, rating DESC).
            var sId = seasonId.Value;
            var rows = await _ctx.Set<SeasonRankArchive>()
                .AsNoTracking()
                .Where(r => r.LadderId == ladderId && r.SeasonId == sId)
                .OrderByDescending(r => r.Rating)
                .Take(limit)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // Resolve display names for non-null PlayerIds in a single batch query.
            var playerIds = rows
                .Where(r => r.PlayerId.HasValue)
                .Select(r => r.PlayerId!.Value)
                .Distinct()
                .ToArray();

            var nameMap = playerIds.Length == 0
                ? new Dictionary<Guid, string>()
                : await _ctx.Set<Player>()
                    .AsNoTracking()
                    .Where(p => playerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct)
                    .ConfigureAwait(false);

            return rows
                .Select((row, idx) =>
                {
                    var displayName = row.PlayerId.HasValue && nameMap.TryGetValue(row.PlayerId.Value, out var n)
                        ? n
                        : "(deleted)";
                    var playerId = row.PlayerId ?? Guid.Empty;
                    // Season archive rows represent completed-placement ranks — IsInPlacement is always false.
                    return new LeaderboardRowDto(
                        Rank: idx + 1,
                        PlayerId: playerId,
                        DisplayName: displayName,
                        Rating: row.Rating,
                        RatingDeviation: row.RatingDeviation,
                        Wins: row.Wins,
                        Losses: row.Losses,
                        Draws: row.Draws,
                        IsInPlacement: false,
                        PlacementMatchesRemaining: 0);
                })
                .ToList();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaderboardRowDto>> AroundAsync(
        Guid ladderId,
        Guid playerId,
        int window = 5,
        Guid? seasonId = null,
        CancellationToken ct = default)
    {
        window = Math.Clamp(window, 1, 50);

        if (seasonId is null)
        {
            return await AroundLiveAsync(ladderId, playerId, window, ct).ConfigureAwait(false);
        }
        else
        {
            return await AroundArchiveAsync(ladderId, playerId, window, seasonId.Value, ct).ConfigureAwait(false);
        }
    }

    // ----- private helpers -----

    private async Task<IReadOnlyList<LeaderboardRowDto>> AroundLiveAsync(
        Guid ladderId,
        Guid playerId,
        int window,
        CancellationToken ct)
    {
        // Find target player's rank row.
        // WR-05: return an empty list (not throw) when the player has no rank row yet —
        // a freshly registered player who has not completed a ranked match is a normal
        // condition, not a 500. Callers that need a 404 can detect the empty result.
        var target = await _ctx.Set<PlayerRank>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.LadderId == ladderId && r.PlayerId == playerId, ct)
            .ConfigureAwait(false);
        if (target is null)
            return Array.Empty<LeaderboardRowDto>();

        // Rows above (higher rating) — ordered by Rating ASC so we get the closest ones.
        var above = await _ctx.Set<PlayerRank>()
            .AsNoTracking()
            .Where(r => r.LadderId == ladderId && r.Rating > target.Rating)
            .OrderBy(r => r.Rating)
            .Take(window)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Rows below (lower rating) — ordered by Rating DESC so we get the closest ones.
        var below = await _ctx.Set<PlayerRank>()
            .AsNoTracking()
            .Where(r => r.LadderId == ladderId && r.Rating < target.Rating)
            .OrderByDescending(r => r.Rating)
            .Take(window)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Count how many players are strictly above the target to assign correct rank numbers.
        var globalRankBase = await _ctx.Set<PlayerRank>()
            .AsNoTracking()
            .CountAsync(r => r.LadderId == ladderId && r.Rating > target.Rating, ct)
            .ConfigureAwait(false);

        // Merge: above (reversed to rating DESC) + target + below.
        var combined = above
            .OrderByDescending(r => r.Rating)
            .Append(target)
            .Concat(below)
            .ToList();

        // Resolve player names in one batch.
        var pids = combined.Select(r => r.PlayerId).Distinct().ToArray();
        var nameMap = pids.Length == 0
            ? new Dictionary<Guid, string>()
            : await _ctx.Set<Player>()
                .AsNoTracking()
                .Where(p => pids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct)
                .ConfigureAwait(false);

        // The rank of the first row in 'combined' (the highest-rated in the window) =
        //   (number of players with rating > max(above)) + 1.
        // Since 'above' are players between max(above) and target, the highest in 'above'
        // occupies rank = globalRankBase - above.Count + 1 ... globalRankBase, then target = globalRankBase + 1.
        // Simpler: assign ranks starting from (globalRankBase - above.Count + 1).
        var startRank = globalRankBase - above.Count + 1;

        return combined
            .Select((rank, idx) =>
            {
                var name = nameMap.TryGetValue(rank.PlayerId, out var n) ? n : "(deleted)";
                // RANK-16 / T-08-01-01: hide raw rating while player is in placement.
                return new LeaderboardRowDto(
                    Rank: startRank + idx,
                    PlayerId: rank.PlayerId,
                    DisplayName: name,
                    Rating: rank.IsInPlacement ? (double?)null : rank.Rating,
                    RatingDeviation: rank.IsInPlacement ? (double?)null : rank.RatingDeviation,
                    Wins: rank.Wins,
                    Losses: rank.Losses,
                    Draws: rank.Draws,
                    IsInPlacement: rank.IsInPlacement,
                    PlacementMatchesRemaining: rank.PlacementMatchesRemaining);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<LeaderboardRowDto>> AroundArchiveAsync(
        Guid ladderId,
        Guid playerId,
        int window,
        Guid seasonId,
        CancellationToken ct)
    {
        // Find target archive row.
        // WR-05: return empty (not throw) for missing archive rows — see AroundLiveAsync.
        var target = await _ctx.Set<SeasonRankArchive>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.LadderId == ladderId && r.SeasonId == seasonId && r.PlayerId == playerId, ct)
            .ConfigureAwait(false);
        if (target is null)
            return Array.Empty<LeaderboardRowDto>();

        var above = await _ctx.Set<SeasonRankArchive>()
            .AsNoTracking()
            .Where(r => r.LadderId == ladderId && r.SeasonId == seasonId && r.Rating > target.Rating)
            .OrderBy(r => r.Rating)
            .Take(window)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var below = await _ctx.Set<SeasonRankArchive>()
            .AsNoTracking()
            .Where(r => r.LadderId == ladderId && r.SeasonId == seasonId && r.Rating < target.Rating)
            .OrderByDescending(r => r.Rating)
            .Take(window)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var globalRankBase = await _ctx.Set<SeasonRankArchive>()
            .AsNoTracking()
            .CountAsync(r => r.LadderId == ladderId && r.SeasonId == seasonId && r.Rating > target.Rating, ct)
            .ConfigureAwait(false);

        // Combine in rating DESC order.
        var combined = above
            .OrderByDescending(r => r.Rating)
            .Select(r => (r.PlayerId, r.Rating, r.RatingDeviation, r.Volatility, r.Wins, r.Losses, r.Draws))
            .Append((target.PlayerId, target.Rating, target.RatingDeviation, target.Volatility, target.Wins, target.Losses, target.Draws))
            .Concat(below.Select(r => (r.PlayerId, r.Rating, r.RatingDeviation, r.Volatility, r.Wins, r.Losses, r.Draws)))
            .ToList();

        // Batch name resolution.
        var pids = combined
            .Where(t => t.PlayerId.HasValue)
            .Select(t => t.PlayerId!.Value)
            .Distinct()
            .ToArray();
        var nameMap = pids.Length == 0
            ? new Dictionary<Guid, string>()
            : await _ctx.Set<Player>()
                .AsNoTracking()
                .Where(p => pids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct)
                .ConfigureAwait(false);

        var startRank = globalRankBase - above.Count + 1;

        return combined
            .Select((t, idx) =>
            {
                var name = t.PlayerId.HasValue && nameMap.TryGetValue(t.PlayerId.Value, out var n)
                    ? n
                    : "(deleted)";
                var pid = t.PlayerId ?? Guid.Empty;
                // Season archive rows represent completed-placement ranks — IsInPlacement is always false.
                return new LeaderboardRowDto(
                    Rank: startRank + idx,
                    PlayerId: pid,
                    DisplayName: name,
                    Rating: t.Rating,
                    RatingDeviation: t.RatingDeviation,
                    Wins: t.Wins,
                    Losses: t.Losses,
                    Draws: t.Draws,
                    IsInPlacement: false,
                    PlacementMatchesRemaining: 0);
            })
            .ToList();
    }
}
