// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Core.Telemetry;
using GameKit.Rankings.Algorithms;
using GameKit.Rankings.Entities;
using GameKit.Rankings.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameKit.Rankings.Services;

/// <summary>
/// Background service that periodically drains pending rating updates in per-ladder
/// batches and applies the configured <see cref="IRankingAlgorithm"/> exactly once per
/// rating period (RANK-04 / RANK-06 / RANK-07 / D-01 / D-03 / D-04).
/// </summary>
/// <remarks>
/// <para>
/// <b>Leader election (D-03 / T-04-06-DD):</b> before any drain work, this service acquires
/// a Redis distributed lock via <see cref="RankingsTickerLeaseHelper"/>. Only one replica
/// executes the drain per tick; others return <see cref="TickResult.LockNotAcquired"/> and
/// wait for the next interval.
/// </para>
/// <para>
/// <b>Batched-only invariant (RANK-04 / Pitfall §1):</b> <see cref="IRankingAlgorithm.Apply"/>
/// is called EXACTLY ONCE per ladder per tick with the full accumulated batch of pending
/// rating updates. Calling Apply once per match is mathematically invalid for Glicko-2.
/// </para>
/// <para>
/// <b>Lazy rank creation (RANK-07):</b> players with pending updates but no
/// <see cref="PlayerRank"/> row for the ladder receive one lazily inside the drain transaction.
/// </para>
/// <para>
/// <b>Failure isolation (D-04):</b> each ladder drain runs in its own
/// <c>ReadCommitted</c> transaction. A deadlock on ladder A does not block ladders B and C.
/// On rollback, pending rows stay un-applied for the next tick.
/// </para>
/// <para>
/// <b>Observability (opt-in, D-04):</b> drain events are emitted via
/// <see cref="RankingsActivitySource"/> (<c>ActivitySource("GameKit.Rankings.Ticker")</c>).
/// Register <c>AddSource(GameKitTelemetry.RankingsTickerSourceName)</c> in your
/// OpenTelemetry setup to subscribe.
/// </para>
/// </remarks>
internal sealed class RankingsTickerService : BackgroundService, IRankingsTicker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RankingsTickerLeaseHelper _lease;
    private readonly IRankingAlgorithm _algorithm;
    private readonly IClock _clock;
    private readonly GameKitRankingsOptions _opts;
    private readonly ILogger<RankingsTickerService> _logger;

    /// <summary>
    /// Constructs the ticker service.
    /// </summary>
    /// <param name="scopeFactory">Factory used to open per-tick DI scopes for <see cref="GameKitDbContext"/>.</param>
    /// <param name="lease">Redis distributed-lock helper.</param>
    /// <param name="algorithm">The configured ranking algorithm (default: Glicko-2).</param>
    /// <param name="clock">Authoritative UTC clock for rating-period comparisons.</param>
    /// <param name="opts">Rankings options snapshot.</param>
    /// <param name="logger">Structured logger.</param>
    public RankingsTickerService(
        IServiceScopeFactory scopeFactory,
        RankingsTickerLeaseHelper lease,
        IRankingAlgorithm algorithm,
        IClock clock,
        IOptions<GameKitRankingsOptions> opts,
        ILogger<RankingsTickerService> logger)
    {
        _scopeFactory = scopeFactory;
        _lease = lease;
        _algorithm = algorithm;
        _clock = clock;
        _opts = opts.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RankingsTickerService starting (interval={Interval}s, lockTtl={Ttl}s).",
            _opts.Ticker.TickIntervalSeconds,
            _opts.Ticker.LockTtlSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_opts.Ticker.TickIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var result = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                    _logger.LogInformation("RankingsTickerService tick completed: {Result}.", result);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never throw out of ExecuteAsync — log and continue.
                    _logger.LogError(ex, "RankingsTickerService: unhandled exception during tick. Continuing.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("RankingsTickerService stopped.");
    }

    /// <inheritdoc />
    public async Task<TickResult> RunOnceAsync(CancellationToken ct)
    {
        // Step 1: acquire distributed lock.
        var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
        if (!acquired)
        {
            _logger.LogDebug("RankingsTickerService: lock not acquired — another replica is leader.");
            return TickResult.LockNotAcquired;
        }

        try
        {
            // Step 2: open a scope for the DB context.
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var now = _clock.UtcNow;

            // Step 3: find ladders whose rating period has elapsed.
            // Load all active ladders into memory first (RatingPeriod lives in JSONB Config,
            // which cannot be translated to a SQL predicate by EF Core), then filter client-side.
            var allActiveLadders = await ctx.Set<Ladder>()
                .AsNoTracking()
                .Where(l => l.IsActive)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var dueLadders = allActiveLadders
                .Where(l => l.LastDrainedAt == null ||
                            l.LastDrainedAt.Value.Add(ReadRatingPeriod(l)) <= now)
                .ToList();

            if (dueLadders.Count == 0)
            {
                _logger.LogDebug("RankingsTickerService: no ladders due for drain.");
                return TickResult.NoLaddersDue;
            }

            _logger.LogInformation(
                "RankingsTickerService: {Count} ladder(s) due for drain.", dueLadders.Count);

            var anyDrained = false;

            foreach (var ladder in dueLadders)
            {
                ct.ThrowIfCancellationRequested();

                // Pitfall 6: renew lease mid-tick; bail out if lost.
                var renewed = await _lease.RenewLeaseAsync(ct).ConfigureAwait(false);
                if (!renewed)
                {
                    _logger.LogWarning(
                        "RankingsTickerService: lock lease lost mid-tick (expired before renewal). " +
                        "Stopping after ladder '{LadderName}' — remaining ladders deferred.", ladder.Name);
                    break;
                }

                var result = await DrainLadderAsync(ctx, ladder, now, ct).ConfigureAwait(false);

                if (result == TickResult.Drained)
                    anyDrained = true;
            }

            return anyDrained ? TickResult.Drained : TickResult.NoLaddersDue;
        }
        finally
        {
            // Always release the lock (Lua-script-verified — safe even if expired).
            await _lease.ReleaseLeaseAsync(ct).ConfigureAwait(false);
        }
    }

    // Maximum pending rows to drain per ladder per tick — bounds per-tick work.
    private const int DrainBatchLimit = 1000;

    private async Task<TickResult> DrainLadderAsync(
        GameKitDbContext ctx,
        Ladder ladder,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var activity = RankingsActivitySource.StartDrainLadderActivity();
        activity?.SetTag(GameKitTelemetry.AttrLadderId, ladder.Id.ToString());
        activity?.SetTag(GameKitTelemetry.AttrLadderName, ladder.Name);

        await using var tx = await ctx.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);

        try
        {
            // Load pending (un-applied) rows for this ladder.
            // Pitfall 12: skip rows where PlayerId is NULL (GDPR-erased).
            var pendingRows = await ctx.Set<PendingRatingUpdate>()
                .Where(p =>
                    p.LadderId == ladder.Id &&
                    p.AppliedAt == null &&
                    p.PlayerId != null)
                .OrderBy(p => p.EnqueuedAt)
                .Take(DrainBatchLimit)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (pendingRows.Count == 0)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return TickResult.NoLaddersDue;
            }

            _logger.LogInformation(
                "RankingsTickerService: draining {Count} pending rows for ladder '{Name}'.",
                pendingRows.Count, ladder.Name);

            // Collect distinct player IDs referenced by the batch.
            var playerIds = pendingRows
                .Select(r => r.PlayerId!.Value)
                .Distinct()
                .ToHashSet();

            // Lazy rank creation (RANK-07): for each player without an existing rank row,
            // insert one with the ladder defaults derived from the JSONB Config.
            var defaults = ReadLadderDefaults(ladder);
            var existingRankIds = await ctx.Set<PlayerRank>()
                .Where(r => r.LadderId == ladder.Id && playerIds.Contains(r.PlayerId))
                .Select(r => r.PlayerId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var existingPlayerIds = existingRankIds.ToHashSet();
            var newRankRows = new List<PlayerRank>();

            foreach (var playerId in playerIds)
            {
                if (!existingPlayerIds.Contains(playerId))
                {
                    newRankRows.Add(new PlayerRank
                    {
                        Id = Guid.NewGuid(),
                        PlayerId = playerId,
                        LadderId = ladder.Id,
                        Rating = defaults.DefaultRating,
                        RatingDeviation = defaults.DefaultRd,
                        Volatility = defaults.DefaultVolatility,
                        Wins = 0,
                        Losses = 0,
                        Draws = 0,
                        LastMatchAt = null,
                        // RANK-16: new ranks start in placement; visible rating hidden until N matches complete.
                        IsInPlacement = true,
                        PlacementMatchesRemaining = _opts.Decay.PlacementMatchCount,
                    });
                }
            }

            if (newRankRows.Count > 0)
            {
                ctx.Set<PlayerRank>().AddRange(newRankRows);
                await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "RankingsTickerService: lazily created {Count} player_ranks row(s) for ladder '{Name}'.",
                    newRankRows.Count, ladder.Name);
            }

            // Load current ranks (includes freshly-inserted rows) for all players in the batch.
            var currentRanks = await ctx.Set<PlayerRank>()
                .Where(r => r.LadderId == ladder.Id && playerIds.Contains(r.PlayerId))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var rankByPlayer = currentRanks.ToDictionary(r => r.PlayerId);

            // Build RankingState from current ranks.
            var stateDictionary = rankByPlayer.ToDictionary(
                kv => kv.Key,
                kv => new PlayerRatingSnapshot(
                    PlayerId: kv.Key,
                    Rating: kv.Value.Rating,
                    RatingDeviation: kv.Value.RatingDeviation,
                    Volatility: kv.Value.Volatility));

            var state = new RankingState(stateDictionary);

            // Build RankingBatch from pending rows, pairing players within the same session.
            // Group by session to build symmetric match outcomes (Win↔Loss pairs, draws).
            var outcomes = BuildMatchOutcomes(pendingRows);
            var batch = new RankingBatch(outcomes);

            // Apply algorithm EXACTLY ONCE per drain (RANK-04 / Pitfall §1).
            var updatedState = _algorithm.Apply(state, batch);

            // Update player_ranks with algorithm output.
            var sessionIds = pendingRows.Select(r => r.SessionId).Distinct().ToHashSet();

            // Track wins/losses/draws aggregates per player from the pending rows.
            var wins = new Dictionary<Guid, int>();
            var losses = new Dictionary<Guid, int>();
            var draws = new Dictionary<Guid, int>();

            foreach (var row in pendingRows)
            {
                if (!row.PlayerId.HasValue) continue;
                var pid = row.PlayerId.Value;

                switch (row.Result.ToLowerInvariant())
                {
                    case "win":
                        wins[pid] = wins.GetValueOrDefault(pid) + 1;
                        break;
                    case "loss":
                    case "forfeit":
                    case "abandoned":
                        losses[pid] = losses.GetValueOrDefault(pid) + 1;
                        break;
                    case "draw":
                        draws[pid] = draws.GetValueOrDefault(pid) + 1;
                        break;
                }
            }

            foreach (var (playerId, updatedRating) in updatedState.Ratings)
            {
                if (!rankByPlayer.TryGetValue(playerId, out var rank)) continue;

                rank.Rating = updatedRating.Rating;
                rank.RatingDeviation = updatedRating.RatingDeviation;
                rank.Volatility = updatedRating.Volatility;
                rank.LastMatchAt = now;
                rank.Wins += wins.GetValueOrDefault(playerId);
                rank.Losses += losses.GetValueOrDefault(playerId);
                rank.Draws += draws.GetValueOrDefault(playerId);
            }

            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

            // Update session_participants with RatingAfter + RatingDelta for drained sessions.
            //
            // Glicko-2 batches ALL outcomes from the period into a single update — there is no
            // per-session intermediate state we can reliably attribute to one session vs. another.
            // For v1, attribute the entire period-aggregate delta to the player's LATEST session
            // (by EnqueuedAt) and leave earlier sessions in the same drain with
            // RatingAfter = pre-drain rating, RatingDelta = 0. This is a known limitation:
            // per-session deltas are accurate when a player participates in exactly one session
            // per drain (the common case) and approximate (single-session attribution) otherwise.
            //
            // Computed in a single pass per player using a dictionary lookup; the SQL is one
            // ExecuteUpdateAsync per (player, session) pair we need to write, batched by player.
            var sessionsByPlayer = pendingRows
                .Where(r => r.PlayerId.HasValue)
                .GroupBy(r => r.PlayerId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(r => r.EnqueuedAt).Select(r => r.SessionId).Distinct().ToList());

            foreach (var pid in playerIds)
            {
                if (!updatedState.Ratings.TryGetValue(pid, out var newRatingSnapshot)) continue;
                if (!sessionsByPlayer.TryGetValue(pid, out var playerSessions) || playerSessions.Count == 0) continue;

                var newRating = newRatingSnapshot.Rating;
                var oldRating = stateDictionary.TryGetValue(pid, out var old) ? old.Rating : defaults.DefaultRating;
                var delta = newRating - oldRating;
                var lastSessionId = playerSessions[^1];

                // Latest session: full period-aggregate delta.
                await ctx.SessionParticipants
                    .Where(sp => sp.SessionId == lastSessionId && sp.PlayerId == pid)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(sp => sp.RatingAfter, newRating)
                            .SetProperty(sp => sp.RatingDelta, delta),
                        ct)
                    .ConfigureAwait(false);

                // Earlier sessions (if any): pre-drain rating, zero delta.
                if (playerSessions.Count > 1)
                {
                    var earlierSessionIds = playerSessions.Take(playerSessions.Count - 1).ToList();
                    await ctx.SessionParticipants
                        .Where(sp => earlierSessionIds.Contains(sp.SessionId) && sp.PlayerId == pid)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(sp => sp.RatingAfter, (double?)oldRating)
                                .SetProperty(sp => sp.RatingDelta, (double?)0.0),
                            ct)
                        .ConfigureAwait(false);
                }
            }

            // Mark pending rows as applied.
            var pendingIds = pendingRows.Select(r => r.Id).ToList();
            await ctx.Set<PendingRatingUpdate>()
                .Where(p => pendingIds.Contains(p.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(p => p.AppliedAt, now),
                    ct)
                .ConfigureAwait(false);

            // Update ladder's LastDrainedAt.
            await ctx.Set<Ladder>()
                .Where(l => l.Id == ladder.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(l => l.LastDrainedAt, now),
                    ct)
                .ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);

            activity?.SetTag(GameKitTelemetry.AttrResult, "Drained");
            _logger.LogInformation(
                "RankingsTickerService: ladder '{Name}' drained {Count} rows successfully.",
                ladder.Name, pendingRows.Count);

            return TickResult.Drained;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RankingsTickerService: drain failed for ladder '{Name}' (id={Id}). Rolling back.",
                ladder.Name, ladder.Id);

            try { await tx.RollbackAsync(ct).ConfigureAwait(false); } catch { /* ignore rollback errors */ }

            activity?.SetTag(GameKitTelemetry.AttrResult, "DrainFailedRolledBack");
            activity?.SetTag(GameKitTelemetry.AttrErrorType, ex.GetType().Name);

            return TickResult.DrainFailedRolledBack;
        }
    }

    /// <summary>
    /// Builds match outcomes from pending rating update rows, grouping by session.
    /// </summary>
    /// <remarks>
    /// Emits ONE <see cref="MatchOutcome"/> per pairwise match using a deterministic
    /// canonical perspective (the participant whose <c>PlayerId</c> sorts lowest). Emitting both
    /// perspectives — as an earlier revision did — causes <see cref="Glicko2Algorithm.Apply"/> to
    /// double-count each match: both <c>(A wins, B loses)</c> and <c>(B loses, A wins)</c> records
    /// resolve to the same <c>RatingPeriodResults.AddResult(winner, loser)</c> call, so the
    /// underlying <c>_results</c> list contains two identical <c>Result</c> entries and every
    /// player sees their match twice during <c>UpdateRatings</c>. A single canonical perspective
    /// is sufficient because <c>Result.GetScore</c> mirrors the score for the opponent side.
    /// </remarks>
    private static IReadOnlyList<MatchOutcome> BuildMatchOutcomes(List<PendingRatingUpdate> rows)
    {
        // Group by session — within a session, pair each participant against every other.
        var bySession = rows
            .Where(r => r.PlayerId.HasValue)
            .GroupBy(r => r.SessionId)
            .ToList();

        var outcomes = new List<MatchOutcome>();

        foreach (var sessionGroup in bySession)
        {
            var participants = sessionGroup.ToList();

            // For each pair of participants in the session, emit ONE outcome using the
            // participant with the lowest PlayerId as the canonical perspective. See remarks.
            for (var i = 0; i < participants.Count; i++)
            {
                for (var j = i + 1; j < participants.Count; j++)
                {
                    var a = participants[i];
                    var b = participants[j];

                    if (!a.PlayerId.HasValue || !b.PlayerId.HasValue)
                        continue;

                    // Pick canonical perspective deterministically by PlayerId ordering.
                    PendingRatingUpdate canonical;
                    PendingRatingUpdate opponent;
                    if (a.PlayerId.Value.CompareTo(b.PlayerId.Value) <= 0)
                    {
                        canonical = a;
                        opponent = b;
                    }
                    else
                    {
                        canonical = b;
                        opponent = a;
                    }

                    outcomes.Add(new MatchOutcome(
                        canonical.PlayerId!.Value,
                        opponent.PlayerId!.Value,
                        ParseResult(canonical.Result)));
                }
            }
        }

        return outcomes;
    }

    private static MatchResult ParseResult(string result) =>
        result.ToLowerInvariant() switch
        {
            "win" => MatchResult.Win,
            "loss" => MatchResult.Loss,
            "draw" => MatchResult.Draw,
            "forfeit" or "abandoned" => MatchResult.Forfeit,
            _ => MatchResult.Loss, // default: treat unknown as loss
        };

    /// <summary>Reads the RatingPeriod from the ladder's JSONB Config (falls back to 1 hour).</summary>
    private static TimeSpan ReadRatingPeriod(Ladder ladder)
    {
        if (ladder.Config is null) return TimeSpan.FromHours(1);

        try
        {
            if (ladder.Config.RootElement.TryGetProperty("RatingPeriodSeconds", out var elem) &&
                elem.TryGetInt64(out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        catch
        {
            // Ignore JSON parse errors — fall back to default.
        }

        return TimeSpan.FromHours(1);
    }

    /// <summary>Reads default rating values from the ladder's JSONB Config.</summary>
    private static (double DefaultRating, double DefaultRd, double DefaultVolatility) ReadLadderDefaults(Ladder ladder)
    {
        const double defaultRating = 1500;
        const double defaultRd = 350;
        const double defaultVolatility = 0.06;

        if (ladder.Config is null)
            return (defaultRating, defaultRd, defaultVolatility);

        try
        {
            var root = ladder.Config.RootElement;
            var rating = root.TryGetProperty("DefaultRating", out var r) && r.TryGetDouble(out var rv)
                ? rv : defaultRating;
            var rd = root.TryGetProperty("DefaultRd", out var d) && d.TryGetDouble(out var dv)
                ? dv : defaultRd;
            var vol = root.TryGetProperty("DefaultVolatility", out var v) && v.TryGetDouble(out var vv)
                ? vv : defaultVolatility;

            return (rating, rd, vol);
        }
        catch
        {
            return (defaultRating, defaultRd, defaultVolatility);
        }
    }
}
