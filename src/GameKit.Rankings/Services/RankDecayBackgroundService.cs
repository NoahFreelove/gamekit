// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using GameKit.Rankings.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameKit.Rankings.Services;

/// <summary>
/// Background service that periodically applies the Glicko-2 inactivity step (RD inflation)
/// to inactive players above a configurable rating threshold, stamping <c>last_decay_at</c>
/// (RANK-15).
/// </summary>
/// <remarks>
/// <para>
/// <b>Leader election (RANK-15):</b> before any decay work, this service acquires a Redis
/// distributed lock via <see cref="RankDecayLeaseHelper"/>. Only one replica executes the
/// decay per interval; others skip the run. The lock key is
/// <c>gamekit:rankings:decay:lease</c> — DISTINCT from the ticker's
/// <c>gamekit:rankings:ticker:lease</c> so the two services never mutually exclude each other.
/// </para>
/// <para>
/// <b>Inactivity step:</b> inflates <c>RatingDeviation</c> using the Glicko-2 formula
/// φ' = √(φ² + σ²) with proper ÷173.7178 / ×173.7178 scale conversion. <c>Rating</c> and
/// <c>Volatility</c> are NEVER modified.
/// </para>
/// <para>
/// <b>Candidate filter:</b> players where <c>IsInPlacement = false</c>, <c>Rating &gt;
/// DecayThresholdRating</c>, <c>LastMatchAt</c> is non-null and older than
/// <c>InactivityDays</c> ago. Players below threshold, never-played, and placement players
/// are excluded.
/// </para>
/// </remarks>
internal sealed class RankDecayBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RankDecayLeaseHelper _lease;
    private readonly IClock _clock;
    private readonly GameKitRankingsOptions _opts;
    private readonly ILogger<RankDecayBackgroundService> _logger;

    /// <summary>
    /// Constructs the decay background service.
    /// </summary>
    /// <param name="scopeFactory">Factory used to open per-tick DI scopes for <see cref="GameKitDbContext"/>.</param>
    /// <param name="lease">Redis distributed-lock helper for the decay runner.</param>
    /// <param name="clock">Authoritative UTC clock.</param>
    /// <param name="opts">Rankings options snapshot.</param>
    /// <param name="logger">Structured logger.</param>
    public RankDecayBackgroundService(
        IServiceScopeFactory scopeFactory,
        RankDecayLeaseHelper lease,
        IClock clock,
        IOptions<GameKitRankingsOptions> opts,
        ILogger<RankDecayBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _lease = lease;
        _clock = clock;
        _opts = opts.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RankDecayBackgroundService starting (interval={Interval}, lockTtl={Ttl}s).",
            _opts.Decay.Interval,
            _opts.Decay.LockTtlSeconds);

        using var timer = new PeriodicTimer(_opts.Decay.Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never throw out of ExecuteAsync — log and continue.
                    _logger.LogError(ex, "RankDecayBackgroundService: unhandled exception during tick. Continuing.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("RankDecayBackgroundService stopped.");
    }

    /// <summary>
    /// Executes a single decay run: acquires the lease, iterates ladders, applies the
    /// Glicko-2 inactivity step to eligible players, and releases the lease.
    /// </summary>
    /// <remarks>
    /// Exposed as <c>internal</c> so integration tests can drive a single run
    /// deterministically without waiting for the <see cref="PeriodicTimer"/>.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        // Step 1: acquire distributed lock.
        var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
        if (!acquired)
        {
            _logger.LogDebug("RankDecayBackgroundService: lock not acquired — another replica is leader.");
            return;
        }

        // OBS-04: start Stopwatch AFTER lease acquisition (Pitfall 5 — excludes lock-wait time).
        // OBS-06: fresh root span (no inbound traceparent — background job, T-15-04-TRACE).
        var decaySw = Stopwatch.StartNew();
        using var decayActivity = RankingsActivitySource.Source.StartActivity("RankDecay");

        try
        {
            // Step 2: open a scope for the DB context.
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var now = _clock.UtcNow;

            // Step 3: load all active ladders.
            var allActiveLadders = await ctx.Set<Ladder>()
                .AsNoTracking()
                .Where(l => l.IsActive)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (allActiveLadders.Count == 0)
            {
                _logger.LogDebug("RankDecayBackgroundService: no active ladders found.");
                return;
            }

            _logger.LogInformation(
                "RankDecayBackgroundService: running decay pass over {Count} active ladder(s).", allActiveLadders.Count);

            // Step 4: for each ladder, renew the lease then apply the decay batch.
            // Mirrors RankingsTickerService's renewal pattern — a long multi-ladder
            // pass could exceed LockTtlSeconds, allowing a standby replica to acquire
            // the lock and double-inflate RD. Renewing before each ladder keeps the TTL
            // fresh; aborting on renewal failure prevents concurrent decay runs.
            foreach (var ladder in allActiveLadders)
            {
                ct.ThrowIfCancellationRequested();

                var renewed = await _lease.RenewLeaseAsync(ct).ConfigureAwait(false);
                if (!renewed)
                {
                    _logger.LogWarning(
                        "RankDecayBackgroundService: lease lost mid-run before ladder {LadderId}. Deferring remaining ladders.",
                        ladder.Id);
                    break;
                }

                await DecayLadderAsync(ctx, ladder.Id, now, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // OBS-04: record decay duration (post-lease, pre-release — Pitfall 5 compliant).
            decaySw.Stop();
            RankingsMeter.DecayDuration.Record(decaySw.Elapsed.TotalMilliseconds);

            // Always release the lock (Lua-script-verified — safe even if expired).
            // CancellationToken.None — not the stopping token — so the release survives SIGTERM
            // (SCALE-02: the stopping token is already cancelled in finally paths on shutdown).
            await _lease.ReleaseLeaseAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Glicko-2 scale multiplier (RatingCalculator.cs line 29).</summary>
    private const double Multiplier = 173.7178;

    private async Task DecayLadderAsync(
        GameKitDbContext ctx,
        Guid ladderId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var cutoff = now.AddDays(-_opts.Decay.InactivityDays);

        // Load decay candidates as tracked entities so EF Core tracks mutations.
        // Candidates: non-placement, above rating threshold, has a prior match, inactive long enough.
        var candidates = await ctx.Set<PlayerRank>()
            .Where(r => r.LadderId == ladderId
                     && !r.IsInPlacement
                     && r.Rating > _opts.Decay.DecayThresholdRating
                     && r.LastMatchAt != null
                     && r.LastMatchAt < cutoff)
            .OrderBy(r => r.LastMatchAt)
            .Take(_opts.Decay.BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "RankDecayBackgroundService: no decay candidates for ladder {LadderId}.", ladderId);
            return;
        }

        _logger.LogInformation(
            "RankDecayBackgroundService: applying decay to {Count} candidate(s) on ladder {LadderId}.",
            candidates.Count, ladderId);

        foreach (var rank in candidates)
        {
            // Scale-correct Glicko-2 inactivity step (Glickman §6):
            //   RatingDeviation is stored on the Glicko-1 scale (~150–350).
            //   Volatility is dimensionless (Glicko-2 scale).
            //   We must convert RD to Glicko-2 scale, apply φ'=√(φ²+σ²), convert back.
            //   Rating and Volatility are NEVER modified.
            double phiG2 = rank.RatingDeviation / Multiplier;          // → Glicko-2 scale
            double phiPrimeG2 = Math.Sqrt(phiG2 * phiG2 + rank.Volatility * rank.Volatility); // φ'=√(φ²+σ²)
            rank.RatingDeviation = phiPrimeG2 * Multiplier;            // → back to original scale
            rank.LastDecayAt = now;
        }

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        // OBS-04: record how many player_ranks rows were updated this ladder pass.
        // No PII tags — only the aggregate row count (T-15-04-PII mitigation).
        RankingsMeter.DecayRowsUpdated.Add(candidates.Count);

        _logger.LogInformation(
            "RankDecayBackgroundService: decay persisted for {Count} candidate(s) on ladder {LadderId}.",
            candidates.Count, ladderId);
    }
}
