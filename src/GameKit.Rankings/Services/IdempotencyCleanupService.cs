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
    /// Executes one cleanup pass: deletes <c>session_complete_idempotency</c> rows older than
    /// the configured <see cref="GameKitRankingsSessionCompleteOptions.IdempotencyTtl"/>. Public for testing.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RunCleanupOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var cutoff = clock.UtcNow - _opts.SessionComplete.IdempotencyTtl;

        var deleted = await ctx.Set<SessionCompleteIdempotency>()
            .Where(r => r.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "IdempotencyCleanupService: deleted {Count} rows older than {Cutoff:O}.",
                deleted, cutoff);
        }
        else
        {
            _logger.LogDebug(
                "IdempotencyCleanupService: no rows to delete (cutoff={Cutoff:O}).", cutoff);
        }
    }
}
