// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="Meter"/> for <c>GameKit.Matchmaking</c> diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// Exposes the <c>matchmaking.analytics.dropped_events</c> counter (D-16) incremented by
/// <see cref="GameKit.Matchmaking.Services.MatchmakingAnalyticsDrainService"/> when a
/// <see cref="GameKit.Matchmaking.Entities.TicketEvent"/> batch is dropped — either because
/// the bounded <see cref="System.Threading.Channels.Channel{T}"/> is full
/// (<c>reason=channel_full</c>) or because the Polly retry pipeline exhausted on a
/// sustained Postgres outage (<c>reason=polly_exhausted</c>).
/// </para>
/// <para>
/// Phase 15 (OBS-04) adds the ticker-lag histogram, per-pool queue-depth ObservableGauge
/// (Redis <c>ZCARD</c> at scrape time), pool-sweep-duration histogram, and the leader-lock /
/// lease / matches-formed / budget-bail counters. Call <see cref="Init"/> from
/// <c>AddMatchmaking</c> to supply the Redis reference for the ObservableGauge callback.
/// </para>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> OpenTelemetry instruments are no-ops unless the
/// host application registers <c>AddMeter("GameKit.Matchmaking")</c> in its OpenTelemetry SDK
/// configuration. Without this registration, increments to
/// <see cref="DroppedEvents"/> are discarded silently — operators will not see the alerting
/// signal during a Postgres outage. The XML doc on
/// <c>MatchmakingBuilderExtensions.AddMatchmaking</c> repeats this guidance.
/// </para>
/// <para>
/// Declared <see langword="internal"/> so external code cannot mutate the static instance;
/// <c>InternalsVisibleTo</c> grants in <c>AssemblyInfo.cs</c> let the Matchmaking test
/// assemblies subscribe a <see cref="MeterListener"/> for verification.
/// </para>
/// </remarks>
internal static class MatchmakingMeter
{
    /// <summary>The Matchmaking meter name. Operators must register <c>AddMeter</c> with this exact value.</summary>
    public const string MeterName = "GameKit.Matchmaking";

    /// <summary>The meter version, pinned to <c>1.0.0</c> for v1 wire compatibility.</summary>
    public const string MeterVersion = "1.0.0";

    /// <summary>The <see cref="Meter"/> instance backing every Matchmaking counter / histogram.</summary>
    public static readonly Meter Meter = new(MeterName, MeterVersion);

    // ── OBS-04 Redis reference for QueueDepth ObservableGauge ─────────────────
    // Set once at startup by Init(); the gauge callback is synchronous (no async/await)
    // and Redis-error-safe (try/catch yields no measurement on RedisException — Pitfall 3).
    private static IConnectionMultiplexer? _multiplexer;

    /// <summary>
    /// Supplies the Redis connection multiplexer that the <see cref="QueueDepth"/>
    /// <see cref="ObservableGauge{T}"/> callback uses to issue synchronous <c>ZCARD</c> calls
    /// at scrape time (OBS-04). Call this once from <c>AddMatchmaking</c> after the
    /// <see cref="IConnectionMultiplexer"/> is registered in DI.
    /// </summary>
    /// <param name="multiplexer">The Redis connection multiplexer.</param>
    /// <remarks>
    /// OBS-04: wires the QueueDepth ObservableGauge Redis reference. The multiplexer is
    /// stored in a static field; the gauge callback issues synchronous IDatabase.SortedSetLength
    /// calls (never async) to avoid thread-pool starvation on the OTel scrape path (RESEARCH
    /// §ObservableGauge cost analysis). A RedisException inside the callback yields no
    /// measurements — the scrape pipeline never observes the exception (Pitfall 3).
    /// </remarks>
    internal static void Init(IConnectionMultiplexer multiplexer)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        _multiplexer = multiplexer;
    }

    /// <summary>
    /// Counter tracking the number of <see cref="GameKit.Matchmaking.Entities.TicketEvent"/>
    /// instances dropped without being persisted to Postgres.
    /// </summary>
    /// <remarks>
    /// <para>Tags:</para>
    /// <list type="bullet">
    ///   <item><c>reason=channel_full</c> — the bounded <see cref="System.Threading.Channels.Channel{T}"/>
    ///         rejected the write because the producer was faster than the drain (D-15).</item>
    ///   <item><c>reason=polly_exhausted</c> — the drain service's Polly retry pipeline gave up after
    ///         the configured maximum attempts on a sustained Postgres outage (D-16).</item>
    /// </list>
    /// </remarks>
    public static readonly Counter<long> DroppedEvents = Meter.CreateCounter<long>(
        name: "matchmaking.analytics.dropped_events",
        unit: "events",
        description: "Count of TicketEvents dropped due to bounded-channel-full or Polly retry exhaustion");

    // ── Phase 15 (OBS-04) additions ───────────────────────────────────────────

    /// <summary>
    /// Histogram recording the wall-clock duration of
    /// <c>MatchmakerTickerService.RunOnceAsync</c> from start to before lease release (ms).
    /// Emitted once per tick iteration that successfully acquires the leader lease.
    /// </summary>
    public static readonly Histogram<double> TickerLag = Meter.CreateHistogram<double>(
        name: "matchmaking.ticker.lag",
        unit: "ms",
        description: "Wall-clock duration of MatchmakerTickerService.RunOnceAsync from start to before lease release");

    /// <summary>
    /// Histogram recording the duration of each <c>ProcessPoolAsync</c> call (ms).
    /// Tag: <c>ladder.id</c> (operator-configured, low-cardinality).
    /// </summary>
    public static readonly Histogram<double> PoolSweepDuration = Meter.CreateHistogram<double>(
        name: "matchmaking.pool_sweep.duration",
        unit: "ms",
        description: "Duration of each ProcessPoolAsync call. Tag: ladder.id");

    /// <summary>
    /// ObservableGauge reporting the current count of tickets in each matchmaking pool
    /// sorted set (Redis <c>ZCARD</c>) at scrape time. Tags: <c>pool.name</c>, <c>ladder.id</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The callback issues synchronous <c>IDatabase.SortedSetLength</c> calls (never async —
    /// RESEARCH §ObservableGauge cost analysis; &lt;1ms per key on loopback Redis with a
    /// bounded pool count). A <see cref="RedisException"/> inside the callback yields no
    /// measurements so a Redis blip never throws out of the OTel scrape pipeline (Pitfall 3).
    /// </para>
    /// <para>
    /// Name is <c>matchmaking.queue.depth</c> with NO unit argument so the Prometheus metric
    /// name is <c>gamekit_matchmaking_queue_depth</c> (no unit suffix) — matches the existing
    /// dashboard query <c>gamekit_matchmaking_queue_depth</c> that Plan 06 corrects the PromQL
    /// for.
    /// </para>
    /// </remarks>
    public static readonly ObservableGauge<long> QueueDepth = Meter.CreateObservableGauge<long>(
        name: "matchmaking.queue.depth",
        observeValues: ObserveQueueDepths,
        description: "Current count of tickets in each matchmaking pool sorted set. Tags: pool.name, ladder.id");

    /// <summary>
    /// Counter incremented when <c>TryAcquireLeaseAsync</c> returns <see langword="false"/>
    /// (another replica holds the leader lock, or a Redis error occurred).
    /// </summary>
    public static readonly Counter<long> LockAcquisitionFailures = Meter.CreateCounter<long>(
        name: "matchmaking.leader_lock.acquisition_failures",
        unit: "failures",
        description: "Count of TryAcquireLeaseAsync calls that returned false (another replica holds leader or Redis error)");

    /// <summary>
    /// Counter incremented on each successful match proposal created. Tag: <c>ladder.id</c>.
    /// </summary>
    public static readonly Counter<long> MatchesFormed = Meter.CreateCounter<long>(
        name: "matchmaking.matches.formed",
        unit: "matches",
        description: "Count of match proposals created. Tag: ladder.id");

    /// <summary>
    /// Counter incremented when the per-pool iteration budget is exhausted before processing
    /// all candidates. Tag: <c>ladder.id</c>.
    /// </summary>
    /// <remarks>
    /// Name is <c>matchmaking.budget_bail</c> (no <c>ticker.</c> segment) so the Prometheus
    /// metric name is <c>gamekit_matchmaking_budget_bail_total</c> — matches the existing
    /// dashboard query. Plan 06 confirms and documents this naming choice.
    /// </remarks>
    public static readonly Counter<long> BudgetBail = Meter.CreateCounter<long>(
        name: "matchmaking.budget_bail",
        unit: "events",
        description: "Count of ticker iterations that exited early due to time-budget exhaustion. Tag: ladder.id");

    /// <summary>
    /// Counter incremented on each successful <c>TryAcquireLeaseAsync</c> call (this replica
    /// became the matchmaker leader for one tick).
    /// </summary>
    public static readonly Counter<long> LeaseAcquired = Meter.CreateCounter<long>(
        name: "matchmaking.lease.acquired",
        unit: "events",
        description: "Count of successful TryAcquireLeaseAsync calls (this replica became leader for one tick)");

    /// <summary>
    /// Counter incremented when a tick iteration returns <c>MatcherTickResult.LeaseLost</c>
    /// (Lua fencing check failed mid-tick — another replica stole the lock).
    /// </summary>
    public static readonly Counter<long> LeaseLost = Meter.CreateCounter<long>(
        name: "matchmaking.lease.lost",
        unit: "events",
        description: "Count of ticker iterations that returned MatcherTickResult.LeaseLost (Lua fencing check failed)");

    // ── Private ObservableGauge callback ──────────────────────────────────────

    /// <summary>
    /// Synchronous callback for <see cref="QueueDepth"/>. Scans Redis for all
    /// <c>mm:queue:*</c> keys and issues a synchronous <c>ZCARD</c> per key.
    /// Yields no measurements when Redis is unavailable (Pitfall 3).
    /// </summary>
    private static IEnumerable<Measurement<long>> ObserveQueueDepths()
    {
        var mux = _multiplexer;
        if (mux is null)
            yield break;

        IDatabase db;
        IServer server;
        try
        {
            var endpoints = mux.GetEndPoints();
            if (endpoints.Length == 0)
                yield break;
            db = mux.GetDatabase();
            server = mux.GetServer(endpoints[0]);
        }
        catch (RedisException)
        {
            yield break;
        }

        // Scan for all queue keys (mm:queue:*) — synchronous IServer.Keys uses SCAN under
        // the hood, not the blocking KEYS command (Pitfall §1).
        List<(string key, Guid ladderId, string poolName)> keyList;
        try
        {
            keyList = new List<(string, Guid, string)>();
            foreach (var redisKey in server.Keys(pattern: "mm:queue:*", pageSize: 100))
            {
                var keyStr = redisKey.ToString();
                if (TryParseQueueKey(keyStr, out var lid, out var pool))
                    keyList.Add((keyStr, lid, pool));
            }
        }
        catch (RedisException)
        {
            yield break;
        }

        foreach (var (key, ladderId, poolName) in keyList)
        {
            long depth;
            try
            {
                // Synchronous SortedSetLength — no async, no thread-pool starvation
                // (RESEARCH §ObservableGauge cost analysis; <1ms/key on loopback).
                depth = db.SortedSetLength(key);
            }
            catch (RedisException)
            {
                // Yield nothing for this key on Redis error (Pitfall 3).
                continue;
            }

            yield return new Measurement<long>(depth,
                new KeyValuePair<string, object?>(GameKitTelemetry.AttrPoolName, poolName),
                new KeyValuePair<string, object?>(GameKitTelemetry.AttrLadderId, ladderId.ToString()));
        }
    }

    /// <summary>
    /// Parses a <c>mm:queue:{ladderId}:{poolName}</c> key into its components.
    /// Returns <see langword="false"/> on shape mismatch.
    /// </summary>
    private static bool TryParseQueueKey(string key, out Guid ladderId, out string poolName)
    {
        ladderId = default;
        poolName = string.Empty;

        const string prefix = "mm:queue:";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var rest = key.AsSpan(prefix.Length);
        var sep = rest.IndexOf(':');
        if (sep < 0)
            return false;

        if (!Guid.TryParse(rest[..sep], out ladderId))
            return false;

        poolName = rest[(sep + 1)..].ToString();
        return poolName.Length > 0;
    }
}
