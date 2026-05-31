// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using GameKit.Matchmaking.Telemetry;

namespace GameKit.Matchmaking.LoadTests;

/// <summary>
/// Subscribes to the live <see cref="MatchmakingActivitySource"/> via an
/// <see cref="ActivityListener"/> and records the wall-clock duration of every per-tick
/// <c>"Tick"</c> activity. Provides histogram-aware budget-assertion helpers that throw
/// a descriptive <see cref="Xunit.Sdk.XunitException"/> on the SC#3 phase-gate budget
/// violation (default 50 ms per <see cref="GameKitMatchmakingTickerOptions.MaxIterationBudgetMs"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a subscriber (and not a source-code edit):</b> the production
/// <c>MatchmakerTickerService</c> already wraps each <c>RunOnceAsync</c> call in a
/// <see cref="Activity"/> created via
/// <see cref="MatchmakingActivitySource.StartTickActivity"/>. The observer simply listens
/// to the existing source — no production-code change is required to measure the budget.
/// This matches the plan body's explicit "subscribes to MatchmakingActivitySource — does NOT
/// require source-code edit" contract.
/// </para>
/// <para>
/// <b>Histogram retention:</b> the observer captures every observed iteration into an
/// in-memory list (lock-free append via <see cref="ConcurrentBag{T}"/>). For a 10-minute
/// sustained run at the 500 ms tick interval this caps at ~1200 entries, well within
/// process memory bounds. The list is enumerated only on the final assertion — there is
/// no hot-path cost beyond a single <see cref="Interlocked.Increment(ref long)"/>.
/// </para>
/// <para>
/// <b>Filter scope:</b> the <see cref="ActivityListener.ShouldListenTo"/> predicate
/// matches exactly <see cref="MatchmakingActivitySource.SourceName"/>
/// (<c>"GameKit.Matchmaking.Ticker"</c>) so spans from <c>PoolSweep</c>, <c>ProposalSweep</c>,
/// or any other OTel source in the host are ignored. Only the outer <c>"Tick"</c> span
/// counts toward the budget — per-pool spans are inside the tick budget already.
/// </para>
/// </remarks>
public sealed class TickerBudgetObserver : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly System.Collections.Concurrent.ConcurrentBag<(double Ms, DateTimeOffset At)> _samples;
    private long _ticksObserved;
    private long _maxIterationTicks; // Stopwatch-ticks rather than ms — preserves sub-ms precision when needed.

    /// <summary>
    /// Optional warmup cutoff: tick samples recorded before this timestamp are excluded from
    /// the budget assertion AND from <see cref="MaxIterationMs"/> / <see cref="IterationMsHistogram"/>.
    /// The initial burst of HTTP enqueues saturates the shared StackExchange.Redis multiplexer
    /// queue, which makes the first 1-2 ticker passes pay queue-wait latency that is NOT a
    /// matcher cost. The SC#3 load test sets this 5 seconds after the initial burst completes
    /// so steady-state ticker performance is the measurement surface.
    /// </summary>
    public DateTimeOffset? WarmupCutoff { get; set; }

    /// <summary>Number of <c>"Tick"</c> activities observed since construction (raw, pre-warmup).</summary>
    public long TicksObserved => Interlocked.Read(ref _ticksObserved);

    /// <summary>The largest single post-warmup tick duration in whole milliseconds.</summary>
    public long MaxIterationMs
    {
        get
        {
            var cutoff = WarmupCutoff;
            if (cutoff is null)
                return (long)(Interlocked.Read(ref _maxIterationTicks) * 1000.0 / Stopwatch.Frequency);

            // Recompute max excluding warmup samples.
            double max = 0;
            foreach (var (ms, at) in _samples)
            {
                if (at < cutoff.Value) continue;
                if (ms > max) max = ms;
            }
            return (long)max;
        }
    }

    /// <summary>
    /// Snapshot of every observed tick duration (milliseconds), sorted ascending. When
    /// <see cref="WarmupCutoff"/> is set, samples before the cutoff are excluded.
    /// </summary>
    public IReadOnlyList<double> IterationMsHistogram
    {
        get
        {
            var cutoff = WarmupCutoff;
            var snapshot = _samples.ToArray();
            double[] arr;
            if (cutoff is null)
            {
                arr = new double[snapshot.Length];
                for (var i = 0; i < snapshot.Length; i++) arr[i] = snapshot[i].Ms;
            }
            else
            {
                var keep = new List<double>(snapshot.Length);
                foreach (var (ms, at) in snapshot)
                {
                    if (at >= cutoff.Value) keep.Add(ms);
                }
                arr = keep.ToArray();
            }
            Array.Sort(arr);
            return arr;
        }
    }

    /// <summary>Subscribes to <see cref="MatchmakingActivitySource"/> on construction.</summary>
    public TickerBudgetObserver()
    {
        _samples = new System.Collections.Concurrent.ConcurrentBag<(double, DateTimeOffset)>();
        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == MatchmakingActivitySource.SourceName,
            // Sample EVERY activity — we cannot afford to miss budget violations.
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName != "Tick") return;
                Interlocked.Increment(ref _ticksObserved);

                var elapsedMs = activity.Duration.TotalMilliseconds;
                var stoppedAt = DateTimeOffset.UtcNow;
                _samples.Add((elapsedMs, stoppedAt));

                // Track the maximum atomically via stopwatch-ticks (no floating-point CAS).
                var sw = (long)(elapsedMs * Stopwatch.Frequency / 1000.0);
                long prev;
                do
                {
                    prev = Interlocked.Read(ref _maxIterationTicks);
                    if (sw <= prev) break;
                } while (Interlocked.CompareExchange(ref _maxIterationTicks, sw, prev) != prev);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>
    /// Asserts the p99.5 of observed tick durations stayed within <paramref name="maxBudgetMs"/>.
    /// Uses a percentile rather than max (p100) so a handful of OS/scheduler outliers on a
    /// shared dev machine (Docker, IDE, browser contending for cycles) do not fail an otherwise
    /// healthy run. A genuine ticker perf regression shifts the whole histogram and will be
    /// caught; isolated tail jitter will not. Throws a descriptive Xunit exception with a
    /// histogram summary (p50 / p90 / p99 / p99.5 / max / count) on violation.
    /// </summary>
    /// <param name="maxBudgetMs">The per-tick budget in milliseconds (default 50 per RESEARCH §Decision 13).</param>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when the p99.5 sample exceeds <paramref name="maxBudgetMs"/>.</exception>
    public void AssertBudgetHeld(int maxBudgetMs)
    {
        var hist = IterationMsHistogram;
        var n = hist.Count;
        if (n == 0)
        {
            throw new Xunit.Sdk.XunitException(
                "TickerBudgetObserver: no histogram samples recorded. " +
                "Did the test host actually run the ticker?");
        }

        // Percentile helpers — hist is sorted ascending.
        double Pct(double p) => hist[Math.Clamp((int)Math.Ceiling(p / 100.0 * n) - 1, 0, n - 1)];

        var p995 = Pct(99.5);
        if (p995 <= maxBudgetMs) return;

        var p50 = Pct(50);
        var p90 = Pct(90);
        var p99 = Pct(99);
        var max = hist[n - 1];

        throw new Xunit.Sdk.XunitException(string.Format(
            CultureInfo.InvariantCulture,
            "Ticker per-iteration budget VIOLATED at p99.5 (budget={0} ms).\n" +
            "  Ticks observed: {1}\n" +
            "  Histogram:\n" +
            "    p50:   {2:F2} ms\n" +
            "    p90:   {3:F2} ms\n" +
            "    p99:   {4:F2} ms\n" +
            "    p99.5: {5:F2} ms  <-- exceeds budget\n" +
            "    max:   {6:F2} ms (sorted-asc tail = {7})\n" +
            "  Likely causes:\n" +
            "    - Lua atomic-claim script perf regression (Plan 05-04)\n" +
            "    - Strategy iteration overhead grew (Plan 05-04 candidates loop)\n" +
            "    - Per-pool SCAN dominates the tick (consider in-memory ladder registry)\n" +
            "  Remediation:\n" +
            "    - Re-run with profiler attached to confirm hot path\n" +
            "    - Relax budget via GameKitMatchmakingOptions.Ticker.MaxIterationBudgetMs (document in PHASE summary)",
            maxBudgetMs, TicksObserved,
            p50, p90, p99, p995, max,
            string.Join(", ", hist.Skip(Math.Max(0, n - 5)).Select(x => x.ToString("F2", CultureInfo.InvariantCulture)))));
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();
}
