// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// MATCH-06 reconciliation worker (CONTEXT.md "chaos recovery" / RESEARCH §Decision 6).
/// Periodically sweeps the Postgres analytics tables for orphaned matchmaking state and
/// marks rows terminal — but NEVER writes to Redis.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pitfall §1 — NEVER REHYDRATE REDIS:</b> the most important invariant of Phase 5.
/// Redis is the live source of truth; Postgres is analytics-only. After a Redis crash, the
/// reconciler's job is to mark abandoned <c>matchmaking_tickets</c> as <c>Expired</c> and
/// abandoned <c>game_sessions</c> as <c>Cancelled</c>. Re-inserting them into Redis would
/// produce duplicate-ticket bugs once clients re-enqueue. Zero <c>ZADD</c> / <c>HSET</c> /
/// <c>SADD</c> / <c>PUBLISH</c> calls anywhere in this service — <c>ZSCORE</c> is read-only.
/// </para>
/// <para>
/// <b>Leader-gated (RESEARCH §Decision 6):</b> acquires the shared matchmaker lease before
/// the sweep; returns <see cref="ReconcileResult.SkippedBecauseNotLeader"/> when another
/// replica holds it. Saves Postgres connections under load (1k-concurrent-ticket budget).
/// </para>
/// <para>
/// <b>Orphan-session detection — Phase 5 heuristic:</b> Phase 5 does not introduce a
/// participant-heartbeat mechanism (that's Phase 6 / PRES-03). Until then, orphan-detection
/// uses a simplified rule: <c>state = Active</c> AND
/// <c>CreatedAt &lt; (now - OrphanSessionThresholdMinutes)</c>. When PRES-03 lands the rule
/// becomes <c>last_heartbeat_at &lt; (now - threshold)</c> instead — at that point this
/// service is updated, not redesigned.
/// </para>
/// <para>
/// <b>Audit trail (D-22 port-and-adapter):</b> orphan-cancel emits an
/// <c>admin.matchmaking.session_orphan_cancelled</c> audit row via
/// <see cref="IAdminAuditWriter"/>. The action verb is duplicated locally as a private
/// constant — Matchmaking does not depend on the <c>AdminAuditActions</c> registry at the
/// runtime API level; Plan 05-08 will mirror the constant into the central registry +
/// <c>AuditSentenceTemplates</c>. Actor id is <see cref="Guid.Empty"/> because the action
/// is system-initiated (no admin user).
/// </para>
/// </remarks>
internal sealed class MatchmakingReconcilerService : BackgroundService, IMatchmakingReconciler
{
    /// <summary>
    /// Action verb written to <c>admin_audit_log.action</c> on orphan-session cancellation.
    /// Plan 05-08 mirrors this literal into <c>AdminAuditActions</c> + <c>AuditSentenceTemplates</c>;
    /// the local constant exists so Matchmaking never takes a runtime API dep on Admin.UI's
    /// registry.
    /// </summary>
    private const string AuditActionSessionOrphanCancelled = "admin.matchmaking.session_orphan_cancelled";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMatchmakerLease _lease;
    private readonly IConnectionMultiplexer _redis;
    private readonly IClock _clock;
    private readonly GameKitMatchmakingOptions _opts;
    private readonly ILogger<MatchmakingReconcilerService> _logger;

    /// <summary>Constructs the reconciler service.</summary>
    public MatchmakingReconcilerService(
        IServiceScopeFactory scopeFactory,
        IMatchmakerLease lease,
        IConnectionMultiplexer redis,
        IClock clock,
        IOptions<GameKitMatchmakingOptions> options,
        ILogger<MatchmakingReconcilerService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _lease = lease;
        _redis = redis;
        _clock = clock;
        _opts = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_opts.Reconciler.SweepIntervalSeconds);
        _logger.LogInformation(
            "MatchmakingReconcilerService starting (interval={Interval}s, staleTicket={Stale}m, orphanSession={Orphan}m).",
            interval.TotalSeconds, _opts.Reconciler.StaleTicketThresholdMinutes,
            _opts.Reconciler.OrphanSessionThresholdMinutes);

        // Startup-immediate pass — catches anything that accumulated during process downtime.
        try
        {
            await RunSweepOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MatchmakingReconcilerService: startup sweep failed. Will retry next interval.");
        }

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await RunSweepOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MatchmakingReconcilerService: sweep failed. Continuing.");
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }

        _logger.LogInformation("MatchmakingReconcilerService stopped.");
    }

    /// <inheritdoc />
    public async Task<ReconcileResult> RunSweepOnceAsync(CancellationToken ct)
    {
        // Leader-gate (RESEARCH §Decision 6) — bail out cleanly if another replica is leader.
        if (_opts.Reconciler.LeaderOnly)
        {
            var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
            if (!acquired)
            {
                _logger.LogDebug("MatchmakingReconcilerService: lease not acquired — another replica is leader.");
                return new ReconcileResult(0, 0, SkippedBecauseNotLeader: true);
            }
        }

        try
        {
            var ticketsExpired = await SweepStaleTicketsAsync(ct).ConfigureAwait(false);
            var sessionsCancelled = await SweepOrphanSessionsAsync(ct).ConfigureAwait(false);

            if (ticketsExpired > 0 || sessionsCancelled > 0)
            {
                _logger.LogInformation(
                    "MatchmakingReconcilerService: tickets expired={Tickets}, sessions cancelled={Sessions}.",
                    ticketsExpired, sessionsCancelled);
            }

            return new ReconcileResult(ticketsExpired, sessionsCancelled, false);
        }
        finally
        {
            if (_opts.Reconciler.LeaderOnly)
                await _lease.ReleaseLeaseAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Marks non-terminal tickets as <c>Expired</c> when (a) they're older than
    /// <c>StaleTicketThresholdMinutes</c> AND (b) the corresponding Redis sorted-set entry is
    /// gone. The Redis read is <c>ZSCORE</c> only — read-only, never a write.
    /// </summary>
    private async Task<int> SweepStaleTicketsAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var cutoff = now - TimeSpan.FromMinutes(_opts.Reconciler.StaleTicketThresholdMinutes);

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var db = _redis.GetDatabase();

        // Load candidate tickets in one query — non-terminal status + older than cutoff.
        // The (LadderId, PoolName, Status) index from Plan 05-02 backs this scan.
        var candidates = await ctx.Set<MatchmakingTicket>()
            .Where(t =>
                t.QueuedAt < cutoff &&
                (t.Status == TicketStatus.Queued || t.Status == TicketStatus.Proposed ||
                 t.Status == TicketStatus.Accepted))
            .OrderBy(t => t.QueuedAt)
            .Take(1000)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
            return 0;

        var expired = 0;

        foreach (var ticket in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // ZSCORE — read-only, NEVER a write (Pitfall §1). If the ticket is still
            // present in the live queue, leave it alone; the matcher will pick it up.
            var queueKey = MatchmakingRedisKeys.Queue(ticket.LadderId, ticket.PoolName);
            var score = await db.SortedSetScoreAsync(queueKey, ticket.Id.ToString()).ConfigureAwait(false);
            if (score.HasValue)
                continue;

            ticket.Status = TicketStatus.Expired;
            ticket.TerminalAt = now;
            expired++;
        }

        if (expired > 0)
        {
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.LogInformation(
                "MatchmakingReconcilerService: expired {Count} stale tickets (cutoff={Cutoff:O}).",
                expired, cutoff);
        }

        return expired;
    }

    /// <summary>
    /// Marks orphan <c>game_sessions</c> as <c>Cancelled</c> + writes a
    /// <c>session_orphan_cancelled</c> admin-audit row. See class-level remark on the
    /// Phase 5 heuristic (no heartbeat yet).
    /// </summary>
    private async Task<int> SweepOrphanSessionsAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var cutoff = now - TimeSpan.FromMinutes(_opts.Reconciler.OrphanSessionThresholdMinutes);

        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAdminAuditWriter>();

        var orphans = await ctx.Set<GameSession>()
            .Where(s => s.State == GameSessionState.Active && s.CreatedAt < cutoff)
            .OrderBy(s => s.CreatedAt)
            .Take(1000)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (orphans.Count == 0)
            return 0;

        var cancelled = 0;
        foreach (var session in orphans)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                session.Cancel(now);
                cancelled++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MatchmakingReconcilerService: invalid state transition for session {SessionId}; skipping.",
                    session.Id);
            }
        }

        if (cancelled == 0)
            return 0;

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        // Emit audit rows AFTER the state-transition commit so the audit log only records
        // confirmed mutations. Each row is small and per-session — bounded by the orphan
        // count (already capped at 1000 by the Take above).
        foreach (var session in orphans.Take(cancelled))
        {
            await audit.WriteAsync(
                action: AuditActionSessionOrphanCancelled,
                targetType: "game_session",
                targetId: session.Id,
                actorId: Guid.Empty,                         // system-initiated, no admin
                before: new { state = "Active", session.CreatedAt },
                after: new { state = "Cancelled", cancelledAt = now },
                reason: $"reconciler detected orphan (>{_opts.Reconciler.OrphanSessionThresholdMinutes} min, no heartbeat)",
                cancellationToken: ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "MatchmakingReconcilerService: cancelled {Count} orphan game_sessions (cutoff={Cutoff:O}).",
            cancelled, cutoff);

        return cancelled;
    }
}
