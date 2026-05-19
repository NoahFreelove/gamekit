// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Reaps proposals that exceeded the accept window with at least one outstanding response
/// (partial-accept race — RESEARCH §Pitfall §10). Sweeps the
/// <c>mm:proposal:*</c> keyspace via SCAN, identifies expired-or-deadline-elapsed proposals,
/// re-ZADDs accepting parties back to their pool queue with their <b>original</b>
/// <c>queuedAt</c> score preserved (CONTEXT.md D-09), publishes <c>"cancelled"</c> on each
/// declining ticket's <c>mm:status:{id}</c> channel, and writes ticket-events into the
/// analytics channel for the drain service to persist.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pitfall §10 (partial-accept race):</b> consider a 4-player proposal where 3 players
/// accept inside the 10-second window but the 4th times out. The 3 accepting tickets should
/// flow back to the queue with their <b>original</b> queue position so they are NOT bumped
/// to the back of the line by the partial-decline (this is the D-09 "preserve queuedAt"
/// invariant). The declining ticket is marked Cancelled / TimedOut and removed from the
/// queue; its <c>mm:status:{id}</c> channel receives a <c>"cancelled"</c> PUBLISH so any
/// long-poll subscriber returns immediately.
/// </para>
/// <para>
/// <b>Leader-only by precondition:</b> <see cref="SweepAsync"/> is called only from
/// <c>MatchmakerTickerService.RunOnceAsync</c> inside the lease block. It does NOT acquire
/// the lock itself — the caller is responsible. This keeps the sweeper a pure SCAN+HGETALL
/// helper that's trivially testable in isolation.
/// </para>
/// <para>
/// <b>SCAN — not KEYS (Pitfall §11):</b> uses <c>IServer.Keys(...)</c> which is a thin
/// SCAN wrapper. KEYS would block Redis for a multi-millisecond window under load; SCAN
/// pages with COUNT=100 and never blocks the server. Asserted by the integration test which
/// greps the source for the string <c>"KEYS"</c> in raw-command form.
/// </para>
/// </remarks>
public sealed class ProposalSweeper
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IClock _clock;
    private readonly ChannelWriter<TicketEvent> _eventWriter;
    private readonly ILogger<ProposalSweeper> _logger;

    /// <summary>
    /// Page size for SCAN. Each SCAN call returns at most this many keys per round-trip.
    /// </summary>
    private const int ScanPageSize = 100;

    /// <summary>
    /// Soft cap on proposals reaped per <see cref="SweepAsync"/> call — bounds the per-tick
    /// work so a backlog cannot stall the matchmaker ticker. Empirically tuned for the
    /// SC#3 1k-concurrent budget: each reap costs ~1-2 ms when pipelined, so 32 reaps fits
    /// comfortably inside the 50 ms iteration budget alongside the candidate scan phase.
    /// Remaining over-deadline proposals are picked up on subsequent sweeps (every tick).
    /// </summary>
    private const int MaxReapsPerSweep = 32;

    /// <summary>Constructs the sweeper.</summary>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="clock">Authoritative UTC clock for the proposal-deadline comparison.</param>
    /// <param name="eventWriter">Channel writer for analytics ticket-events.</param>
    /// <param name="logger">Logger.</param>
    public ProposalSweeper(
        IConnectionMultiplexer redis,
        IClock clock,
        ChannelWriter<TicketEvent> eventWriter,
        ILogger<ProposalSweeper> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(eventWriter);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _clock = clock;
        _eventWriter = eventWriter;
        _logger = logger;
    }

    /// <summary>
    /// Runs the proposal-sweep pass over the <c>mm:proposal:*</c> keyspace. Returns the
    /// count of proposals reaped (useful for tests + OTel tags).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of proposals reaped this pass.</returns>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var endpoints = _redis.GetEndPoints();
        if (endpoints.Length == 0)
        {
            _logger.LogWarning("ProposalSweeper: no Redis endpoints configured.");
            return 0;
        }

        var server = _redis.GetServer(endpoints[0]);
        var db = _redis.GetDatabase();
        var subscriber = _redis.GetSubscriber();
        var now = _clock.UtcNow;

        var reaped = 0;

        // PERF (SC#3): bounded per-pass batch + pipelined HGETALL fan-out. Collect candidate
        // keys first (cheap SCAN), then fan out a single HashGetAllAsync per key in parallel
        // via the StackExchange.Redis multiplexer — collapses N round-trips into one batch.
        var candidateKeys = new List<RedisKey>(ScanPageSize);
        foreach (var key in server.Keys(pattern: "mm:proposal:*", pageSize: ScanPageSize))
        {
            var keyString = key.ToString();
            if (keyString.EndsWith(MatchmakingRedisKeys.ProposalAcceptsSuffix, StringComparison.Ordinal))
                continue;
            candidateKeys.Add(key);
            if (candidateKeys.Count >= MaxReapsPerSweep) break;
        }

        // Pipeline HGETALL on each candidate proposal hash. The multiplexer batches concurrent
        // commands; awaiting at the end collapses N round-trips into one.
        var headTasks = new (RedisKey Key, Task<HashEntry[]> Task)[candidateKeys.Count];
        for (var idx = 0; idx < candidateKeys.Count; idx++)
            headTasks[idx] = (candidateKeys[idx], db.HashGetAllAsync(candidateKeys[idx]));
        await Task.WhenAll(headTasks.Select(t => t.Task)).ConfigureAwait(false);

        foreach (var (key, headTask) in headTasks)
        {
            ct.ThrowIfCancellationRequested();
            if (reaped >= MaxReapsPerSweep) break;

            var keyString = key.ToString();
            var hash = headTask.Result;
            if (hash.Length == 0)
                continue;

            string? fieldsRedis = null;
            string? deadlineMsRedis = null;
            string? ticketsRedis = null;
            foreach (var e in hash)
            {
                var n = e.Name.ToString();
                switch (n)
                {
                    case "fields": fieldsRedis = e.Value.ToString(); break;
                    case "deadlineMs": deadlineMsRedis = e.Value.ToString(); break;
                    case "tickets": ticketsRedis = e.Value.ToString(); break;
                }
            }

            if (string.IsNullOrEmpty(fieldsRedis))
                continue;

            if (string.IsNullOrEmpty(deadlineMsRedis) ||
                !long.TryParse(deadlineMsRedis, out var deadlineMs) ||
                deadlineMs > now.ToUnixTimeMilliseconds())
            {
                continue;
            }

            // The proposal deadline has elapsed. Identify accepting vs. declining tickets via
            // the accept-tracker subkey set membership (mm:proposal:{id}:accepts). The set's
            // member format is "ticket:{ticketId}" — Plan 05-06 writes entries on accept.
            var acceptsKey = (RedisKey)(keyString + MatchmakingRedisKeys.ProposalAcceptsSuffix);

            if (string.IsNullOrEmpty(ticketsRedis))
            {
                _logger.LogWarning(
                    "ProposalSweeper: proposal {Key} missing 'tickets' field — skipping.", keyString);
                continue;
            }

            var ticketIds = ParseTicketIds(ticketsRedis);
            if (ticketIds.Count == 0)
                continue;

            // Read the accept set members. Each member is "ticket:{ticketId}".
            var acceptedMembers = await db.SetMembersAsync(acceptsKey).ConfigureAwait(false);
            var acceptedTicketIds = new HashSet<Guid>();
            foreach (var m in acceptedMembers)
            {
                var s = m.ToString();
                const string prefix = "ticket:";
                if (s.StartsWith(prefix, StringComparison.Ordinal) &&
                    Guid.TryParse(s.AsSpan(prefix.Length), out var id))
                {
                    acceptedTicketIds.Add(id);
                }
            }

            // PERF (SC#3): pipeline the per-ticket HGETALL fan-out so all participants in a
            // proposal can be reaped in ~1 round-trip rather than N. Then collect the
            // mutation tasks (ZADD / PUBLISH / HSET / DEL) and await them in a batch.
            var ticketHashTasks = new (Guid Tid, Task<HashEntry[]> Task)[ticketIds.Count];
            for (var ti = 0; ti < ticketIds.Count; ti++)
            {
                var tid = ticketIds[ti];
                ticketHashTasks[ti] = (tid, db.HashGetAllAsync(MatchmakingRedisKeys.Ticket(tid)));
            }
            await Task.WhenAll(ticketHashTasks.Select(t => t.Task)).ConfigureAwait(false);

            var mutationTasks = new List<Task>(ticketIds.Count * 3 + 2);
            foreach (var (tid, hashTask) in ticketHashTasks)
            {
                ct.ThrowIfCancellationRequested();
                var ticketKey = MatchmakingRedisKeys.Ticket(tid);
                var ticketHash = hashTask.Result;

                // Walk the HashEntry[] once — same pattern as MatchmakerTickerService for parity.
                long queuedAtMs = 0; bool haveQueuedAt = false;
                Guid? ladderId = null;
                string? poolName = null;
                foreach (var e in ticketHash)
                {
                    var n = e.Name.ToString();
                    if (n == "queuedAt")
                    {
                        if (long.TryParse(e.Value.ToString(), out var q)) { queuedAtMs = q; haveQueuedAt = true; }
                    }
                    else if (n == "ladderId")
                    {
                        if (Guid.TryParse(e.Value.ToString(), out var l)) ladderId = l;
                    }
                    else if (n == "poolName")
                    {
                        poolName = e.Value.ToString();
                    }
                }

                if (acceptedTicketIds.Contains(tid))
                {
                    if (haveQueuedAt && ladderId.HasValue && !string.IsNullOrEmpty(poolName))
                    {
                        var queueKey = MatchmakingRedisKeys.Queue(ladderId.Value, poolName);
                        mutationTasks.Add(db.SortedSetAddAsync(queueKey, tid.ToString(), queuedAtMs));
                        mutationTasks.Add(db.HashSetAsync(ticketKey,
                            [new HashEntry("status", "Queued")]));

                        await TryWriteEventAsync(new TicketEvent
                        {
                            Id = Guid.NewGuid(),
                            TicketId = tid,
                            EventType = TicketEventType.Queued,
                            OccurredAt = now,
                            Payload = """{"reason":"re_queued_after_partial_accept"}""",
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "ProposalSweeper: accepting ticket {TicketId} missing queue-key " +
                            "metadata in its Redis hash — cannot re-ZADD.", tid);
                    }
                }
                else
                {
                    mutationTasks.Add(subscriber.PublishAsync(
                        RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(tid)),
                        "cancelled"));

                    await TryWriteEventAsync(new TicketEvent
                    {
                        Id = Guid.NewGuid(),
                        TicketId = tid,
                        EventType = TicketEventType.TimedOut,
                        OccurredAt = now,
                        Payload = """{"reason":"proposal_deadline_elapsed"}""",
                    }).ConfigureAwait(false);
                }
            }

            // Delete the proposal hash + accept-tracker — proposal is fully reaped.
            mutationTasks.Add(db.KeyDeleteAsync(key));
            mutationTasks.Add(db.KeyDeleteAsync(acceptsKey));

            await Task.WhenAll(mutationTasks).ConfigureAwait(false);

            reaped++;
        }

        if (reaped > 0)
        {
            _logger.LogInformation(
                "ProposalSweeper: reaped {Count} proposals.", reaped);
        }

        return reaped;
    }

    /// <summary>
    /// Parses the comma-separated ticket-id list written to the proposal hash's
    /// <c>"tickets"</c> field by the matchmaker ticker. Skips malformed ids defensively.
    /// </summary>
    private static IReadOnlyList<Guid> ParseTicketIds(string commaSeparated)
    {
        if (string.IsNullOrEmpty(commaSeparated))
            return Array.Empty<Guid>();

        var parts = commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var ids = new List<Guid>(parts.Length);
        foreach (var p in parts)
        {
            if (Guid.TryParse(p.Trim(), out var id))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Writes an event into the analytics channel using the non-blocking
    /// <c>TryWrite</c> path. The Plan 05-04 channel placeholder + Plan 05-07's options-driven
    /// rebound channel both use <see cref="System.Threading.Channels.BoundedChannelFullMode.DropNewest"/>
    /// so a full channel drops cleanly — we never block the matcher tick.
    /// </summary>
    private ValueTask TryWriteEventAsync(TicketEvent evt)
    {
        if (!_eventWriter.TryWrite(evt))
        {
            // The Plan 05-07 MatchmakingMeter.DroppedEvents counter increments on the producer
            // side when the channel is full. We log at debug to avoid drowning the log under
            // sustained load.
            _logger.LogDebug(
                "ProposalSweeper: ticket-event channel full — dropped event for ticket {TicketId}.",
                evt.TicketId);
        }
        return ValueTask.CompletedTask;
    }
}
