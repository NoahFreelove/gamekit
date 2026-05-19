// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Inserts a single batch under a fresh scope. The scope's <see cref="GameKitDbContext"/>
    /// is disposed before Polly's retry sleep so the Npgsql connection returns to the pool
    /// (Pitfall §8 — never hold a connection across a Polly retry delay).
    /// </summary>
    private async Task FlushBatchAsync(IReadOnlyList<TicketEvent> batch, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        ctx.Set<TicketEvent>().AddRange(batch);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
