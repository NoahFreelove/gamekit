// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Background service that drains the in-process <see cref="Channel{T}"/> of
/// <see cref="TicketEvent"/> records into Postgres in batches (D-15 / D-16 / D-18).
/// Runs on EVERY replica (each replica has its own in-process channel — RESEARCH §Decision 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not block on Postgres?</b> Matchmaking serves a 500 ms ticker hot path; a stalled
/// Postgres write would back-pressure the matcher and miss the SC#3 1k-concurrent-ticket
/// budget. The drain runs out-of-band: producer pushes into a bounded channel
/// (drop-newest on full per D-15) and this service flushes batches asynchronously.
/// </para>
/// <para>
/// <b>Polly v8 retry pipeline (RESEARCH §Decision 7):</b> 4 retry attempts, exponential
/// jitter, 500 ms base delay, 30 s per-attempt timeout. <see cref="NpgsqlException"/> and
/// <see cref="DbUpdateException"/> are treated as transient. On retry exhaustion the batch
/// is dropped and <see cref="MatchmakingMeter.DroppedEvents"/> increments with
/// <c>reason=polly_exhausted</c> so the operator's OTel pipeline raises an alert (D-16).
/// </para>
/// <para>
/// <b>Connection lifetime (Pitfall §8):</b> the Postgres connection is opened inside
/// <see cref="FlushBatchAsync"/> via a scoped <see cref="GameKitDbContext"/>, the batch is
/// INSERTed, the context is disposed — releasing the Npgsql pool slot before the Polly
/// retry sleep. The drain service therefore never holds a connection across a retry delay,
/// which protects the Npgsql pool from exhaustion under load.
/// </para>
/// <para>
/// <b>OTel meter registration (Pitfall §7):</b> increments to
/// <see cref="MatchmakingMeter.DroppedEvents"/> are no-ops unless the host registers
/// <c>AddMeter("GameKit.Matchmaking")</c> in its OpenTelemetry SDK setup. The XML doc on
/// <c>AddMatchmaking</c> repeats this guidance.
/// </para>
/// </remarks>
internal sealed class MatchmakingAnalyticsDrainService : BackgroundService, IMatchmakingAnalyticsDrain
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChannelReader<TicketEvent> _reader;
    private readonly GameKitMatchmakingAnalyticsOptions _opts;
    private readonly ILogger<MatchmakingAnalyticsDrainService> _logger;
    private readonly ResiliencePipeline _polly;

    /// <summary>
    /// Constructs the drain service and builds the Polly v8 resilience pipeline from the
    /// configured retry / timeout values.
    /// </summary>
    /// <param name="scopeFactory">DI scope factory for per-batch <see cref="GameKitDbContext"/>.</param>
    /// <param name="reader">Bounded-channel reader wired by Plan 05-04 (placeholder) and rebound by Plan 05-07.</param>
    /// <param name="options">Matchmaking options snapshot (analytics nested options).</param>
    /// <param name="logger">Structured logger for retry diagnostics.</param>
    public MatchmakingAnalyticsDrainService(
        IServiceScopeFactory scopeFactory,
        ChannelReader<TicketEvent> reader,
        IOptions<GameKitMatchmakingOptions> options,
        ILogger<MatchmakingAnalyticsDrainService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _reader = reader;
        _opts = options.Value.Analytics;
        _logger = logger;

        _polly = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _opts.PollyMaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(_opts.PollyBaseDelayMs),
                ShouldHandle = new PredicateBuilder()
                    .Handle<NpgsqlException>()
                    .Handle<DbUpdateException>(),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "MatchmakingAnalyticsDrainService: Postgres retry {Attempt} after {Delay}ms.",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(TimeSpan.FromSeconds(_opts.PollyTimeoutSeconds))
            .Build();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MatchmakingAnalyticsDrainService starting (batch={Batch}, drainInterval={Interval}s, retries={Retries}).",
            _opts.DrainBatchSize, _opts.DrainIntervalSeconds, _opts.PollyMaxRetryAttempts);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DrainOnceAsync(_opts.DrainBatchSize, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never throw out of ExecuteAsync — log and continue.
                    _logger.LogError(ex, "MatchmakingAnalyticsDrainService: unhandled exception during drain. Continuing.");
                    // Brief backoff before retry to avoid hot-spin on persistent failure.
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("MatchmakingAnalyticsDrainService stopped.");
    }

    /// <inheritdoc />
    public async Task<int> DrainOnceAsync(int maxBatch, CancellationToken ct)
    {
        if (maxBatch < 1)
            throw new ArgumentOutOfRangeException(nameof(maxBatch), maxBatch, "maxBatch must be >= 1.");

        var batch = await ReadBatchAsync(maxBatch, TimeSpan.FromSeconds(_opts.DrainIntervalSeconds), ct).ConfigureAwait(false);
        if (batch.Count == 0)
            return 0;

        try
        {
            await _polly.ExecuteAsync(
                async token => await FlushBatchAsync(batch, token).ConfigureAwait(false),
                ct).ConfigureAwait(false);

            return batch.Count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsTransientPostgresOutage(ex))
        {
            _logger.LogError(ex,
                "MatchmakingAnalyticsDrainService: dropping batch of {Count} events after Polly exhaustion.",
                batch.Count);

            MatchmakingMeter.DroppedEvents.Add(
                batch.Count,
                new KeyValuePair<string, object?>("reason", "polly_exhausted"));

            return 0;
        }
    }

    /// <summary>
    /// True when the exception (or any inner) is a Postgres transient outage / retry-exhaustion
    /// type the drain service treats as "drop the batch and emit the OTel counter."
    /// </summary>
    /// <remarks>
    /// EF Core's <see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/> wraps the
    /// raw <see cref="NpgsqlException"/> in <see cref="InvalidOperationException"/> with the
    /// message "An exception has been raised that is likely due to a transient failure." after
    /// its own retry budget is exhausted — we must unwrap to detect the underlying type.
    /// </remarks>
    private static bool IsTransientPostgresOutage(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException or DbUpdateException or TimeoutRejectedException)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Reads up to <paramref name="max"/> items from the channel within
    /// <paramref name="maxWait"/>. Returns whatever accumulated when either bound is hit.
    /// </summary>
    private async Task<List<TicketEvent>> ReadBatchAsync(int max, TimeSpan maxWait, CancellationToken ct)
    {
        var batch = new List<TicketEvent>(capacity: Math.Min(max, 256));

        // First, drain whatever is already available without blocking.
        while (batch.Count < max && _reader.TryRead(out var first))
            batch.Add(first);

        if (batch.Count >= max)
            return batch;

        // Wait for the first arrival or the bounded window — whichever comes first.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(maxWait);

        try
        {
            while (batch.Count < max && await _reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
            {
                while (batch.Count < max && _reader.TryRead(out var item))
                    batch.Add(item);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // maxWait elapsed — return what we accumulated.
        }

        return batch;
    }

    /// <summary>
    /// Inserts a single batch under a fresh scope and reflects terminal-state transitions
    /// onto the corresponding <c>matchmaking_tickets</c> rows. The scope's
    /// <see cref="GameKitDbContext"/> is disposed before Polly's retry sleep so the Npgsql
    /// connection returns to the pool (Pitfall §8 — never hold a connection across a Polly
    /// retry delay).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Status mirroring (Phase 5 SC#3 fix).</b> Beyond inserting the per-event row, the
    /// drain advances the <c>matchmaking_tickets.Status</c> column to reflect the latest
    /// observed event type. The row itself is created synchronously by
    /// <c>MatchmakingService.EnqueueAsync</c> at <see cref="TicketStatus.Queued"/>; this
    /// method walks the per-batch events in <c>OccurredAt</c> order and applies the highest
    /// terminal precedence. Mapping:
    /// <list type="bullet">
    ///   <item><see cref="TicketEventType.Proposed"/> → <see cref="TicketStatus.Proposed"/></item>
    ///   <item><see cref="TicketEventType.Accepted"/> → <see cref="TicketStatus.Accepted"/></item>
    ///   <item><see cref="TicketEventType.Matched"/> → <see cref="TicketStatus.Matched"/> + <c>TerminalAt</c></item>
    ///   <item><see cref="TicketEventType.Cancelled"/> → <see cref="TicketStatus.Cancelled"/> + <c>TerminalAt</c></item>
    ///   <item><see cref="TicketEventType.Declined"/> → <see cref="TicketStatus.Declined"/> + <c>TerminalAt</c></item>
    ///   <item><see cref="TicketEventType.TimedOut"/> → <see cref="TicketStatus.TimedOut"/> + <c>TerminalAt</c></item>
    ///   <item><see cref="TicketEventType.Expired"/> → <see cref="TicketStatus.Expired"/> + <c>TerminalAt</c></item>
    ///   <item><see cref="TicketEventType.Queued"/> (re-queue after partial accept) → <see cref="TicketStatus.Queued"/>, clear <c>TerminalAt</c></item>
    /// </list>
    /// The reconciler's stale-ticket sweep still owns the orphan-detection path (tickets
    /// that ended in Redis without a terminal event ever reaching us); this method only
    /// advances state on observed events.
    /// </para>
    /// </remarks>
    private async Task FlushBatchAsync(IReadOnlyList<TicketEvent> batch, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

        // 1) Append the per-event audit rows.
        ctx.Set<TicketEvent>().AddRange(batch);

        // 2) Compute the latest event per ticket, then bulk-load the affected ticket rows
        //    and apply the new status. We use ExecuteUpdateAsync-equivalent per ticket via
        //    tracked entities — the batch is small (default DrainBatchSize=100) and grouping
        //    by ticket keeps the round-trip to one round-trip per distinct ticket id.
        var latestPerTicket = new Dictionary<Guid, TicketEvent>(capacity: batch.Count);
        foreach (var ev in batch)
        {
            if (!latestPerTicket.TryGetValue(ev.TicketId, out var existing) || ev.OccurredAt > existing.OccurredAt)
                latestPerTicket[ev.TicketId] = ev;
        }

        if (latestPerTicket.Count > 0)
        {
            var ticketIds = latestPerTicket.Keys.ToArray();
            var rows = await ctx.Set<MatchmakingTicket>()
                .Where(t => ticketIds.Contains(t.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                if (!latestPerTicket.TryGetValue(row.Id, out var ev))
                    continue;
                ApplyStatusFromEvent(row, ev);
            }
        }

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Maps a <see cref="TicketEvent"/> onto the latest <see cref="TicketStatus"/>.</summary>
    private static void ApplyStatusFromEvent(MatchmakingTicket row, TicketEvent ev)
    {
        switch (ev.EventType)
        {
            case TicketEventType.Queued:
                row.Status = TicketStatus.Queued;
                row.TerminalAt = null;
                break;
            case TicketEventType.Proposed:
                // Only forward — do not roll back from a terminal status to Proposed.
                if (!IsTerminal(row.Status))
                    row.Status = TicketStatus.Proposed;
                break;
            case TicketEventType.Accepted:
                if (!IsTerminal(row.Status))
                    row.Status = TicketStatus.Accepted;
                break;
            case TicketEventType.Matched:
                row.Status = TicketStatus.Matched;
                row.TerminalAt = ev.OccurredAt;
                break;
            case TicketEventType.Cancelled:
                row.Status = TicketStatus.Cancelled;
                row.TerminalAt = ev.OccurredAt;
                break;
            case TicketEventType.Declined:
                row.Status = TicketStatus.Declined;
                row.TerminalAt = ev.OccurredAt;
                break;
            case TicketEventType.TimedOut:
                row.Status = TicketStatus.TimedOut;
                row.TerminalAt = ev.OccurredAt;
                break;
            case TicketEventType.Expired:
                row.Status = TicketStatus.Expired;
                row.TerminalAt = ev.OccurredAt;
                break;
        }
    }

    private static bool IsTerminal(TicketStatus s) =>
        s == TicketStatus.Matched || s == TicketStatus.Cancelled || s == TicketStatus.Declined ||
        s == TicketStatus.TimedOut || s == TicketStatus.Expired;
}
