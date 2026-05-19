// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameKit.Rankings.Services;

/// <summary>
/// Nightly background service that purges <c>session_complete_idempotency</c> rows older
/// than the configured TTL (default 24 hours, D-08 / T-04-06-IC).
/// </summary>
/// <remarks>
/// <para>
/// Runs immediately on startup (to catch any rows that accumulated while the process was
/// offline), then once every <c>CleanupInterval</c> (default 24 hours).
/// </para>
/// <para>
/// Uses EF Core's bulk <c>ExecuteDeleteAsync</c> to issue a single
/// <c>DELETE WHERE CreatedAt &lt; cutoff</c> — no per-row object tracking overhead.
/// </para>
/// <para>
/// The inner cleanup logic is exposed as <see cref="RunCleanupOnceAsync"/> to allow
/// integration tests to trigger a cleanup pass directly without waiting for the timer.
/// </para>
/// </remarks>
public sealed class IdempotencyCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IdempotencyCleanupService> _logger;
    private readonly GameKitRankingsOptions _opts;

    /// <summary>
    /// Cleanup interval. Defaults to 24 hours; overridable by callers at build time
    /// (driven by <see cref="GameKitRankingsSessionCompleteOptions.IdempotencyTtl"/>).
    /// </summary>
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Constructs the cleanup service.
    /// </summary>
    /// <param name="scopeFactory">Factory for creating DI scopes that provide <see cref="GameKitDbContext"/>.</param>
    /// <param name="opts">Rankings options snapshot containing the TTL value.</param>
    /// <param name="logger">Structured logger.</param>
    public IdempotencyCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<GameKitRankingsOptions> opts,
        ILogger<IdempotencyCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _opts = opts.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IdempotencyCleanupService starting (ttl={Ttl}h, interval={Interval}h).",
            _opts.SessionComplete.IdempotencyTtl.TotalHours,
            CleanupInterval.TotalHours);

        // Run immediately on startup per D-08 ("or on startup if the prior tick missed").
        await RunCleanupOnceAsync(stoppingToken).ConfigureAwait(false);

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
                    _logger.LogError(ex, "IdempotencyCleanupService: unhandled exception. Will retry next interval.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("IdempotencyCleanupService stopped.");
    }

    /// <summary>
    /// Executes one cleanup pass: deletes
    /// <list type="bullet">
    /// <item><description><c>session_complete_idempotency</c> rows older than
    /// <see cref="GameKitRankingsSessionCompleteOptions.IdempotencyTtl"/>.</description></item>
    /// <item><description>Applied <c>pending_rating_updates</c> rows (CR-05) older than
    /// <see cref="GameKitRankingsCleanupOptions.PendingRetentionTtl"/>. Without this pass,
    /// the audit-trail rows the ticker leaves behind grow unbounded — at 1k matches/hour with
    /// 2 participants each that's roughly 17M rows/year, all on the ticker's hot
    /// partial-index read path.</description></item>
    /// </list>
    /// Public for testing.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RunCleanupOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var idempotencyCutoff = now - _opts.SessionComplete.IdempotencyTtl;

        var deletedIdempotency = await ctx.Set<SessionCompleteIdempotency>()
            .Where(r => r.CreatedAt < idempotencyCutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (deletedIdempotency > 0)
        {
            _logger.LogInformation(
                "IdempotencyCleanupService: deleted {Count} session_complete_idempotency rows older than {Cutoff:O}.",
                deletedIdempotency, idempotencyCutoff);
        }
        else
        {
            _logger.LogDebug(
                "IdempotencyCleanupService: no idempotency rows to delete (cutoff={Cutoff:O}).",
                idempotencyCutoff);
        }

        // CR-05: cleanup applied pending_rating_updates rows.
        // Delete only rows where AppliedAt is set AND old enough — never delete unapplied rows
        // (those are the ticker's working set).
        var pendingCutoff = now - _opts.Cleanup.PendingRetentionTtl;

        var deletedPending = await ctx.Set<PendingRatingUpdate>()
            .Where(r => r.AppliedAt != null && r.AppliedAt < pendingCutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (deletedPending > 0)
        {
            _logger.LogInformation(
                "IdempotencyCleanupService: deleted {Count} pending_rating_updates rows applied before {Cutoff:O}.",
                deletedPending, pendingCutoff);
        }
        else
        {
            _logger.LogDebug(
                "IdempotencyCleanupService: no pending_rating_updates rows to delete (cutoff={Cutoff:O}).",
                pendingCutoff);
        }
    }
}
