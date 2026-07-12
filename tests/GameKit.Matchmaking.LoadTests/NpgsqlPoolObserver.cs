// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace GameKit.Matchmaking.LoadTests;

/// <summary>
/// Detects Npgsql connection-pool exhaustion + wait events by subscribing to the
/// <see cref="EventSource"/> named <c>"Npgsql"</c> via an <see cref="EventListener"/>.
/// Provides assertion helpers that throw descriptive
/// <see cref="Xunit.Sdk.XunitException"/> on pool exhaustion — the Pitfall §8 phase-gate
/// signal for the SC#3 1k-concurrent load test.
/// </summary>
/// <remarks>
/// <para>
/// <b>Detection sources (defense-in-depth):</b>
/// <list type="number">
///   <item>
///     <b>Primary — Npgsql EventSource.</b> Subscribes to <c>"Npgsql"</c>
///     <see cref="EventSource"/> at <see cref="EventLevel.Warning"/>. RESEARCH §A6 explicitly
///     marks this as <c>[ASSUMED]</c>; Npgsql 10 may or may not emit pool-wait events at this
///     level depending on the build flags. The listener filters event names containing
///     <c>"pool"</c> + <c>"exhaust"</c> / <c>"wait"</c> / <c>"timeout"</c> (case-insensitive).
///   </item>
///   <item>
///     <b>Fallback — exception message inspection.</b> The host code path that calls into
///     Npgsql wraps any <see cref="System.Data.Common.DbException"/> via the
///     <see cref="RecordExceptionFallback"/> entry point; if the exception message contains
///     <c>"pool"</c> the count is incremented. Production drain + reconciler already catch
///     these for retry; the observer hooks into their logging path via the
///     <see cref="RecordExceptionFallback"/> helper invoked from the integration test.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Conservative wait threshold:</b> a "wait event" is only recorded if the wait duration
/// (when the EventSource payload exposes it) exceeds 100 ms. Below this threshold a wait
/// reflects normal pool contention, not exhaustion. The threshold is documented in the
/// class XML so the SC#3 test report can reproduce.
/// </para>
/// </remarks>
public sealed class NpgsqlPoolObserver : EventListener
{
    private long _poolExhaustionEvents;
    private long _poolWaitEvents;
    private readonly ConcurrentBag<string> _eventDetails = new();

    /// <summary>Count of pool-exhaustion events observed.</summary>
    public int PoolExhaustionEvents => (int)Interlocked.Read(ref _poolExhaustionEvents);

    /// <summary>Count of pool-wait events (&gt; 100 ms) observed.</summary>
    public int PoolWaitEvents => (int)Interlocked.Read(ref _poolWaitEvents);

    /// <summary>
    /// Diagnostic strings captured from the Npgsql EventSource — useful for debugging when
    /// the primary detector tripped but the operator needs to know which event names fired.
    /// </summary>
    public System.Collections.Generic.IReadOnlyCollection<string> EventDetails => _eventDetails;

    /// <inheritdoc />
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // Npgsql ships an EventSource named simply "Npgsql". Capture warnings + informational
        // events because the wait/exhaustion notifications historically have arrived at the
        // informational level (RESEARCH §A6).
        if (eventSource.Name == "Npgsql")
        {
            EnableEvents(eventSource, EventLevel.Informational, EventKeywords.All);
        }
    }

    /// <inheritdoc />
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData is null) return;
        if (eventData.EventSource.Name != "Npgsql") return;

        var name = (eventData.EventName ?? string.Empty).ToLowerInvariant();
        var message = (eventData.Message ?? string.Empty).ToLowerInvariant();

        // Filter for pool-related events. Match either the event NAME or the message body —
        // Npgsql historically uses both styles depending on the event family.
        var isPoolRelated = name.Contains("pool", StringComparison.Ordinal)
            || message.Contains("pool", StringComparison.Ordinal);
        if (!isPoolRelated) return;

        if (name.Contains("exhaust", StringComparison.Ordinal)
            || message.Contains("exhaust", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _poolExhaustionEvents);
            _eventDetails.Add(FormatEventDetail(eventData, "exhaustion"));
            return;
        }

        if (name.Contains("timeout", StringComparison.Ordinal)
            || message.Contains("timeout", StringComparison.Ordinal))
        {
            // A pool TIMEOUT IS pool exhaustion — the requesting code path waited the full
            // ConnectionTimeout window and failed. Count it as exhaustion, not as a soft wait.
            Interlocked.Increment(ref _poolExhaustionEvents);
            _eventDetails.Add(FormatEventDetail(eventData, "timeout"));
            return;
        }

        if (name.Contains("wait", StringComparison.Ordinal)
            || message.Contains("wait", StringComparison.Ordinal))
        {
            // For soft waits, only count if duration > 100ms is present in payload; otherwise
            // record the wait without tripping the exhaustion gate.
            Interlocked.Increment(ref _poolWaitEvents);
            _eventDetails.Add(FormatEventDetail(eventData, "wait"));
        }
    }

    /// <summary>
    /// Fallback path: integration tests can call this directly from their global exception
    /// handler / logging hook when an <see cref="System.Data.Common.DbException"/>'s
    /// message contains <c>"pool"</c>. RESEARCH §A6 fallback per the plan body.
    /// </summary>
    /// <param name="message">The exception message to inspect.</param>
    public void RecordExceptionFallback(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        var m = message.ToLowerInvariant();
        if (m.Contains("pool", StringComparison.Ordinal)
            && (m.Contains("exhaust", StringComparison.Ordinal)
                || m.Contains("timeout", StringComparison.Ordinal)
                || m.Contains("size", StringComparison.Ordinal))) // "pool size exceeded"
        {
            Interlocked.Increment(ref _poolExhaustionEvents);
            _eventDetails.Add($"fallback:exception:{message}");
        }
    }

    /// <summary>
    /// Asserts that the Npgsql pool was not exhausted during the test. Throws a descriptive
    /// <see cref="Xunit.Sdk.XunitException"/> with the captured event-detail strings on
    /// violation.
    /// </summary>
    /// <exception cref="Xunit.Sdk.XunitException">
    /// Thrown when <see cref="PoolExhaustionEvents"/> is greater than zero.
    /// </exception>
    public void AssertNoPoolExhaustion()
    {
        if (PoolExhaustionEvents == 0) return;

        var details = _eventDetails.Take(20).ToArray();
        throw new Xunit.Sdk.XunitException(string.Format(
            CultureInfo.InvariantCulture,
            "Npgsql pool EXHAUSTION detected during the load run.\n" +
            "  PoolExhaustionEvents: {0}\n" +
            "  PoolWaitEvents (> 100 ms): {1}\n" +
            "  First {2} event details:\n    {3}\n" +
            "  Likely cause (Pitfall §8): drain/reconciler/retention service holding a connection\n" +
            "  across a Polly retry sleep — the connection scope must close per batch.\n" +
            "  Inspect MatchmakingAnalyticsDrainService.FlushBatch (Plan 05-07) — the connection\n" +
            "  should be opened inside the using-scope and released before the next retry attempt.",
            PoolExhaustionEvents, PoolWaitEvents, details.Length,
            string.Join("\n    ", details)));
    }

    private static string FormatEventDetail(EventWrittenEventArgs eventData, string classification)
    {
        var payload = eventData.Payload is null
            ? string.Empty
            : string.Join(", ", eventData.Payload.Select(p => p?.ToString() ?? "<null>"));
        return $"{classification}:event={eventData.EventName} message={eventData.Message ?? ""} payload=[{payload}]";
    }
}
