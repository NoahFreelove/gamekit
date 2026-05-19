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
    /// work so a backlog cannot stall the matchmaker ticker.
    /// </summary>
    private const int MaxReapsPerSweep = 256;

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

        // IServer.Keys is a thin SCAN wrapper (NOT KEYS) — the Redis-internal implementation
        // pages with COUNT=ScanPageSize without ever blocking the server. We narrow the scan
        // to proposal hashes only and skip the per-proposal accept-tracker subkeys.
        foreach (var key in server.Keys(pattern: "mm:proposal:*", pageSize: ScanPageSize))
        {
            ct.ThrowIfCancellationRequested();
            if (reaped >= MaxReapsPerSweep) break;

            var keyString = key.ToString();
            // Skip the per-proposal accept-tracker subkey (suffix ":accepts") — we look at it
            // via HGETALL on the parent below.
            if (keyString.EndsWith(MatchmakingRedisKeys.ProposalAcceptsSuffix, StringComparison.Ordinal))
                continue;

            // HGETALL the proposal hash. The ticker writes a "deadlineMs" field (Unix ms)
            // alongside the "fields" JSON + "tickets" CSV — the sweeper compares deadlineMs
            // to the current clock to detect timed-out proposals. We do NOT rely on the
            // Redis KEY TTL because the Lua script's EXPIRE on the proposal hash is the
            // proposal-service's expiry-cleanup signal (Plan 05-06): once the TTL expires,
            // Redis deletes the hash entirely, and SCAN never sees it. The sweeper instead
            // needs to find proposals whose deadline is past BUT whose hash is still present.
            var fieldsRedis = await db.HashGetAsync(key, "fields").ConfigureAwait(false);
            var deadlineMsRedis = await db.HashGetAsync(key, "deadlineMs").ConfigureAwait(false);

            // Pitfall §11 / consistency: the proposal could have completed between SCAN and
            // HGETALL — the hash is gone. Skip silently.
            if (!fieldsRedis.HasValue)
                continue;

            // If the deadlineMs field is missing OR the deadline is in the future, leave the
            // proposal alone. The proposal service (Plan 05-06) clears the deadline on
            // all-accept or all-decline so the sweeper does NOT race the happy path.
            if (!deadlineMsRedis.HasValue ||
                !long.TryParse(deadlineMsRedis.ToString(), out var deadlineMs) ||
                deadlineMs > now.ToUnixTimeMilliseconds())
            {
                continue;
            }

            // The proposal deadline has elapsed. Identify accepting vs. declining tickets via
            // the accept-tracker subkey set membership (mm:proposal:{id}:accepts). The set's
            // member format is "ticket:{ticketId}" — Plan 05-06 will write entries on accept.
            var acceptsKey = (RedisKey)(keyString + MatchmakingRedisKeys.ProposalAcceptsSuffix);

            // Parse the participating ticket ids from the "tickets" field of the proposal hash.
            // The Lua claim writes the JSON blob into "fields"; for the sweeper to know which
            // ticket ids participated, the proposal hash also gets a "tickets" comma-separated
            // field (the ticker writes this alongside "fields" — see MatchmakerTickerService
            // BuildProposalHashFields).
            var ticketsRedis = await db.HashGetAsync(key, "tickets").ConfigureAwait(false);
            if (!ticketsRedis.HasValue)
            {
                // Proposal hash is malformed; skip + log once. Do NOT delete — the proposal
                // service / reconciler will eventually clean it up.
                _logger.LogWarning(
                    "ProposalSweeper: proposal {Key} missing 'tickets' field — skipping.", keyString);
                continue;
            }

            var ticketIds = ParseTicketIds(ticketsRedis.ToString());
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

            // For each accepting ticket: re-ZADD into the pool queue with the ORIGINAL
            // queuedAt score (read from the ticket's hash, preserved per D-09). Write
            // a TimedOut + back-to-Queued ticket-event into the analytics channel.
            // For each declining/timed-out ticket: PUBLISH "cancelled" to mm:status:{id}
            // and write a Cancelled ticket-event.
            foreach (var tid in ticketIds)
            {
                ct.ThrowIfCancellationRequested();

                var ticketKey = MatchmakingRedisKeys.Ticket(tid);
                var ticketHash = await db.HashGetAllAsync(ticketKey).ConfigureAwait(false);
                var hashMap = ticketHash.ToDictionary(
                    e => e.Name.ToString(), e => e.Value.ToString());

                if (acceptedTicketIds.Contains(tid))
                {
                    // Accepting party — re-ZADD with original queuedAt score (D-09).
                    if (hashMap.TryGetValue("queuedAt", out var queuedAtStr) &&
                        long.TryParse(queuedAtStr, out var queuedAtMs) &&
                        hashMap.TryGetValue("ladderId", out var ladderIdStr) &&
                        Guid.TryParse(ladderIdStr, out var ladderId) &&
                        hashMap.TryGetValue("poolName", out var poolName))
                    {
                        var queueKey = MatchmakingRedisKeys.Queue(ladderId, poolName);
                        await db.SortedSetAddAsync(queueKey, tid.ToString(), queuedAtMs)
                            .ConfigureAwait(false);
                        await db.HashSetAsync(
                            ticketKey,
                            [new HashEntry("status", "Queued")])
                            .ConfigureAwait(false);

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
                    // Declining or timed-out ticket — PUBLISH cancellation + write event.
                    await subscriber.PublishAsync(
                        RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(tid)),
                        "cancelled")
                        .ConfigureAwait(false);

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
            await db.KeyDeleteAsync(key).ConfigureAwait(false);
            await db.KeyDeleteAsync(acceptsKey).ConfigureAwait(false);

            reaped++;

            _logger.LogInformation(
                "ProposalSweeper: reaped proposal {Key} ({Accepted}/{Total} accepted; rest cancelled).",
                keyString, acceptedTicketIds.Count, ticketIds.Count);
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
