// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Nightly retention cleanup for <c>matchmaking_tickets</c> (D-17, default 30 days) and
/// <c>decline_history</c> (configurable rolling window). Mirrors
/// <c>GameKit.Rankings.Services.IdempotencyCleanupService</c> verbatim — bulk
/// <c>ExecuteDeleteAsync</c> on a periodic timer with a startup-immediate pass.
/// </summary>
/// <remarks>
/// <para>
/// <b>Leader-gated (RESEARCH §Decision 6 "Any one replica" recommendation):</b> a nightly
/// DELETE on every replica wastes Postgres connections but doesn't corrupt data. We
/// nonetheless leader-gate to avoid the wasted load under the SC#3 1k-concurrent budget.
/// </para>
/// <para>
/// <b>Two cleanup queries per pass:</b>
/// <list type="bullet">
///   <item><see cref="MatchmakingTicket"/>: <c>TerminalAt &lt; now - TicketRetentionDays</c>.
///         Non-terminal tickets are never deleted — the reconciler is responsible for moving
///         them to a terminal state first.</item>
///   <item><see cref="DeclineHistory"/>: <c>DeclinedAt &lt; now - (WindowMinutes * 2)</c> —
///         keep enough cooldown history to evaluate the rolling window twice over but bounded
///         to prevent unbounded growth.</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class MatchmakingRetentionCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMatchmakerLease _lease;
    private readonly IClock _clock;
    private readonly GameKitMatchmakingOptions _opts;
    private readonly ILogger<MatchmakingRetentionCleanupService> _logger;

    /// <summary>
    /// Cleanup interval. Defaults to 24 hours — overridable from tests by re-resolving the
    /// service with a different value before <c>StartAsync</c>.
    /// </summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Constructs the cleanup service.</summary>
    public MatchmakingRetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        IMatchmakerLease lease,
        IClock clock,
        IOptions<GameKitMatchmakingOptions> options,
        ILogger<MatchmakingRetentionCleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _lease = lease;
        _clock = clock;
        _opts = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MatchmakingRetentionCleanupService starting (ticketRetention={Retention}d, interval={Interval}h).",
            _opts.TicketRetentionDays, CleanupInterval.TotalHours);

        try
        {
            await RunCleanupOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MatchmakingRetentionCleanupService: startup cleanup failed. Will retry next interval.");
        }

        using var timer = new PeriodicTimer(CleanupInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await RunCleanupOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MatchmakingRetentionCleanupService: cleanup failed. Continuing.");
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }

        _logger.LogInformation("MatchmakingRetentionCleanupService stopped.");
    }

    /// <summary>
    /// Executes one cleanup pass. Public so integration tests can drive the cleanup
    /// deterministically. Leader-gated.
    /// </summary>
    public async Task<RetentionResult> RunCleanupOnceAsync(CancellationToken ct)
    {
        var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
        if (!acquired)
        {
            _logger.LogDebug("MatchmakingRetentionCleanupService: lease not acquired — skipping.");
            return new RetentionResult(0, 0, SkippedBecauseNotLeader: true);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var now = _clock.UtcNow;

            var ticketCutoff = now - TimeSpan.FromDays(_opts.TicketRetentionDays);
            var ticketsDeleted = await ctx.Set<MatchmakingTicket>()
                .Where(t => t.TerminalAt != null && t.TerminalAt < ticketCutoff)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            if (ticketsDeleted > 0)
            {
                _logger.LogInformation(
                    "MatchmakingRetentionCleanupService: deleted {Count} matchmaking_tickets older than {Cutoff:O}.",
                    ticketsDeleted, ticketCutoff);
            }

            var declineCutoff = now - TimeSpan.FromMinutes(_opts.Cooldown.WindowMinutes * 2);
            var declinesDeleted = await ctx.Set<DeclineHistory>()
                .Where(d => d.DeclinedAt < declineCutoff)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            if (declinesDeleted > 0)
            {
                _logger.LogInformation(
                    "MatchmakingRetentionCleanupService: deleted {Count} decline_history rows older than {Cutoff:O}.",
                    declinesDeleted, declineCutoff);
            }

            return new RetentionResult(ticketsDeleted, declinesDeleted, false);
        }
        finally
        {
            // CancellationToken.None — not the stopping token — so the release survives SIGTERM
            // (SCALE-02: the stopping token is already cancelled in finally paths on shutdown).
            await _lease.ReleaseLeaseAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Outcome of a single retention cleanup pass.
/// </summary>
/// <param name="TicketsDeleted">Number of <c>matchmaking_tickets</c> rows deleted.</param>
/// <param name="DeclineHistoriesDeleted">Number of <c>decline_history</c> rows deleted.</param>
/// <param name="SkippedBecauseNotLeader">True if the pass was skipped because another replica is leader.</param>
public readonly record struct RetentionResult(
    int TicketsDeleted,
    int DeclineHistoriesDeleted,
    bool SkippedBecauseNotLeader);
