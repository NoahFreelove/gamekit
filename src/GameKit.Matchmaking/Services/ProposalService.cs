// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IProposalService"/>. Drives the D-06 accept-step proposal flow against
/// Redis (proposal hash + acceptors set + status pub/sub) and Postgres (<c>game_sessions</c> +
/// <c>session_participants</c> on all-accept; <c>decline_history</c> on decline).
/// </summary>
/// <remarks>
/// <para>
/// <b>State machine:</b>
/// <list type="bullet">
///   <item><see cref="AcceptAsync"/> → <see cref="ProposalScripts.CompleteLuaSource"/> atomic
///         SADD+SCARD. On <c>COMPLETE</c>, creates the <c>GameSession</c> + participants and
///         publishes "matched"; on <c>PENDING</c>, emits <see cref="TicketEventType.Accepted"/>
///         and returns. On <c>ALREADY</c>, returns <see cref="AcceptResult.AlreadyAccepted"/>.
///         On <c>COMPLETED</c>, returns <see cref="AcceptResult.AlreadyAccepted"/> (T-05-06-04
///         late-accept idempotency).</item>
///   <item><see cref="DeclineAsync"/> → <c>decline_history</c> INSERT FIRST (durability over
///         the Redis teardown — T-05-06-03), THEN
///         <see cref="ProposalScripts.DeclineLuaSource"/> atomic re-ZADD + DEL. PUBLISHes
///         "cancelled" / "requeued" + emits <see cref="TicketEventType.Declined"/> /
///         <see cref="TicketEventType.Cancelled"/> events.</item>
/// </list>
/// </para>
/// <para>
/// <b>Channel drop accounting (D-15):</b> the <see cref="ChannelWriter{T}.TryWrite"/> path
/// returns <see langword="false"/> when the channel is full (capacity 1000 placeholder /
/// 10000 production). On <see langword="false"/>, the service logs a warning — the
/// dropped-events OTel counter is owned by the drain service (Plan 05-07), and this
/// producer does not double-count.
/// </para>
/// <para>
/// <b>Channel payload type:</b> the bounded channel is registered as
/// <c>Channel&lt;GameKit.Matchmaking.Entities.TicketEvent&gt;</c> by Plan 05-04 / 05-07. The
/// plan's "Services/TicketEvent.cs" record was envisaged before that channel-shape decision
/// landed; in the live tree we write the <see cref="TicketEvent"/> entity directly into the
/// channel and the drain service persists the same instances (D-15 path).
/// </para>
/// </remarks>
public sealed class ProposalService : IProposalService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeclineHistoryReader _declineHistory;
    private readonly TeamAssignmentService _teamAssignment;
    private readonly ChannelWriter<TicketEvent> _eventChannel;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly GameKitMatchmakingOptions _opts;
    private readonly IChaosInterceptor _chaos;
    private readonly ILogger<ProposalService>? _logger;

    /// <summary>Constructs the service.</summary>
    /// <param name="redis">Redis multiplexer; the database handle is fetched per call.</param>
    /// <param name="scopeFactory">DI scope factory for per-call <see cref="GameKitDbContext"/> on the all-accept path.</param>
    /// <param name="declineHistory">Decline history reader/writer for the D-08 cooldown row.</param>
    /// <param name="teamAssignment">Team assignment service (CSPRNG random v1).</param>
    /// <param name="eventChannel">Bounded channel writer for analytics events (D-15).</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">Id generator (UUIDv7).</param>
    /// <param name="options">Matchmaking options snapshot.</param>
    /// <param name="chaos">
    /// Test-only chaos seam (production default = <see cref="NullChaosInterceptor"/>). See
    /// <see cref="IChaosInterceptor"/> XML doc — the Plan 05-09 chaos test uses this to simulate
    /// a crash between Lua complete-script success and the durable <c>GameSession</c> INSERT.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public ProposalService(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        IDeclineHistoryReader declineHistory,
        TeamAssignmentService teamAssignment,
        ChannelWriter<TicketEvent> eventChannel,
        IClock clock,
        IIdGenerator ids,
        IOptions<GameKitMatchmakingOptions> options,
        IChaosInterceptor chaos,
        ILogger<ProposalService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(declineHistory);
        ArgumentNullException.ThrowIfNull(teamAssignment);
        ArgumentNullException.ThrowIfNull(eventChannel);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(chaos);
        _redis = redis;
        _scopeFactory = scopeFactory;
        _declineHistory = declineHistory;
        _teamAssignment = teamAssignment;
        _eventChannel = eventChannel;
        _clock = clock;
        _ids = ids;
        _opts = options.Value;
        _chaos = chaos;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AcceptResult> AcceptAsync(
        Guid proposalId, Guid ticketId, Guid playerId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);
        var acceptsKey = MatchmakingRedisKeys.ProposalAccepts(proposalId);

        // Step 1: read the proposal hash.
        var entries = await db.HashGetAllAsync(proposalKey).ConfigureAwait(false);
        if (entries.Length == 0)
            return AcceptResult.ProposalNotFound;

        var fields = ParseFields(entries);
        if (fields is null)
            return AcceptResult.ProposalNotFound;

        // Step 2: T-05-06-01 — verify this ticket id is in the proposal.
        if (!fields.Tickets.Any(t => t.TicketId == ticketId))
            return AcceptResult.NotInProposal;

        // Step 3: run the Lua complete script atomically.
        var keys = new RedisKey[] { proposalKey, acceptsKey };
        var args = new RedisValue[] { ticketId.ToString(), fields.Tickets.Count, _opts.AcceptTimeoutSeconds };
        var reply = (string?)await db.ScriptEvaluateAsync(
            ProposalScripts.CompleteLuaSource, keys, args).ConfigureAwait(false);

        switch (reply)
        {
            case "ALREADY":
            case "COMPLETED":
                return AcceptResult.AlreadyAccepted;

            case "PENDING":
                // Emit Accepted event for this ticket.
                EmitTicketEvent(ticketId, TicketEventType.Accepted, payload: JsonPayload("proposalId", proposalId));
                return AcceptResult.Accepted;

            case "COMPLETE":
                // All accepted — create the GameSession + participants and PUBLISH "matched".
                var sessionId = await CreateSessionAsync(proposalId, fields, ct).ConfigureAwait(false);
                // Update each ticket hash to status=matched (+ sessionId) BEFORE publishing.
                // Without this, a long-poll that arrives via the snapshot fast-path after the
                // matched PUBLISH was already sent reads status="proposed" forever — pub/sub
                // is a fire-and-forget signal that doesn't replay for late subscribers.
                foreach (var t in fields.Tickets)
                {
                    await db.HashSetAsync(
                        MatchmakingRedisKeys.Ticket(t.TicketId),
                        [
                            new HashEntry("status", "matched"),
                            new HashEntry("sessionId", sessionId.ToString()),
                        ])
                        .ConfigureAwait(false);
                }
                await PublishMatchedAsync(db, fields, sessionId).ConfigureAwait(false);
                foreach (var t in fields.Tickets)
                    EmitTicketEvent(t.TicketId, TicketEventType.Matched, payload: JsonPayload("sessionId", sessionId));
                return AcceptResult.AllAccepted;

            default:
                _logger?.LogError(
                    "ProposalService.AcceptAsync: unexpected Lua reply '{Reply}' for proposal {Proposal}.",
                    reply ?? "<null>", proposalId);
                return AcceptResult.ProposalNotFound;
        }
    }

    /// <inheritdoc />
    public async Task<DeclineResult> DeclineAsync(
        Guid proposalId, Guid ticketId, Guid playerId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);
        var acceptsKey = MatchmakingRedisKeys.ProposalAccepts(proposalId);

        // Step 1: read the proposal hash.
        var entries = await db.HashGetAllAsync(proposalKey).ConfigureAwait(false);
        if (entries.Length == 0)
            return DeclineResult.ProposalNotFound;

        var fields = ParseFields(entries);
        if (fields is null)
            return DeclineResult.ProposalNotFound;

        // Step 2: T-05-06-01 — verify this ticket id is in the proposal.
        if (!fields.Tickets.Any(t => t.TicketId == ticketId))
            return DeclineResult.NotInProposal;

        // Step 3: T-05-06-03 — write DeclineHistory FIRST. Postgres durability guarantees
        // the cooldown effect persists even if the subsequent Redis teardown fails. On Redis
        // failure the proposal's TTL guarantees eventual cleanup; the reconciler (Plan 05-07)
        // catches any stuck accepting tickets within StaleTicketThresholdMinutes.
        await _declineHistory.RecordDeclineAsync(playerId, proposalId, _clock.UtcNow, ct).ConfigureAwait(false);

        // Step 4: atomic decline-and-reap Lua script.
        // KEYS: [proposalKey, acceptsKey, queueKey]
        var keys = new RedisKey[] { proposalKey, acceptsKey, fields.QueueKey };
        // ARGV: [decliningTicketId, ticketCount, t1, score1, t2, score2, ...]
        var args = new RedisValue[2 + fields.Tickets.Count * 2];
        args[0] = ticketId.ToString();
        args[1] = fields.Tickets.Count;
        for (var i = 0; i < fields.Tickets.Count; i++)
        {
            args[2 + i * 2] = fields.Tickets[i].TicketId.ToString();
            args[3 + i * 2] = fields.Tickets[i].QueuedAtUnixMs;
        }

        var reply = (string?)await db.ScriptEvaluateAsync(
            ProposalScripts.DeclineLuaSource, keys, args).ConfigureAwait(false);
        if (reply != "OK")
        {
            _logger?.LogError(
                "ProposalService.DeclineAsync: unexpected Lua reply '{Reply}' for proposal {Proposal}.",
                reply ?? "<null>", proposalId);
        }

        // Step 5: update terminal ticket-hash state BEFORE publishing so snapshot-path
        // polls return the correct status even if they arrive after the publish fires.
        // The decliner's ticket flips to cancelled; partners are re-queued (the decline-and-
        // reap Lua already ZADD'd them back, so their hash status returns to queued).
        foreach (var t in fields.Tickets)
        {
            var newStatus = t.TicketId == ticketId ? "cancelled" : "queued";
            await db.HashSetAsync(
                MatchmakingRedisKeys.Ticket(t.TicketId),
                "status", newStatus).ConfigureAwait(false);
        }

        // Step 5b: PUBLISH "cancelled" to the decliner, "requeued" to the partners.
        var publisher = _redis.GetSubscriber();
        foreach (var t in fields.Tickets)
        {
            var ch = MatchmakingRedisKeys.StatusChannel(t.TicketId);
            if (t.TicketId == ticketId)
                await publisher.PublishAsync(RedisChannel.Literal(ch), "cancelled").ConfigureAwait(false);
            else
                await publisher.PublishAsync(RedisChannel.Literal(ch), "requeued").ConfigureAwait(false);
        }

        // Step 6: emit Declined / Cancelled events.
        EmitTicketEvent(ticketId, TicketEventType.Declined, payload: JsonPayload("proposalId", proposalId));
        foreach (var t in fields.Tickets)
        {
            if (t.TicketId == ticketId) continue;
            EmitTicketEvent(t.TicketId, TicketEventType.Cancelled, payload: JsonPayload("proposalId", proposalId, "reason", "partner_declined"));
        }

        return DeclineResult.Declined;
    }

    /// <summary>
    /// Create the <see cref="GameSession"/> + <see cref="SessionParticipant"/> rows on the
    /// all-accept path. Uses a fresh scoped <see cref="GameKitDbContext"/> so the write is
    /// independent of any caller-scoped transaction.
    /// </summary>
    /// <param name="proposalId">
    /// The proposal id used as the <c>IdempotencyKey</c> on the <c>game_sessions</c> row
    /// (SCALE-03). The INSERT uses <c>ON CONFLICT ("IdempotencyKey") DO NOTHING</c> so that
    /// a split-brain second replica attempting the same formation produces zero extra rows.
    /// On conflict (rows-affected == 0), the existing session id is resolved by a follow-up
    /// query and returned without inserting participants again.
    /// </param>
    /// <param name="fields">Proposal fields (tickets, ladder id, queue key).</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<Guid> CreateSessionAsync(Guid proposalId, ProposalFields fields, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var now = _clock.UtcNow;

        var sessionId = _ids.NewId();
        var idempotencyKey = proposalId.ToString();

        // Re-build QueuedParty inputs from the proposal fields so the TeamAssignment service
        // can drive its CSPRNG split. The strategy + ticker carry the full members list in
        // ProposalFields.Tickets[].PlayerIds.
        var parties = fields.Tickets
            .Select(t => new QueuedParty(
                TicketId: t.TicketId,
                PartyId: null,
                LadderId: fields.LadderId,
                PoolName: "default",
                Members: t.PlayerIds
                    .Select(pid => new QueuedPartyMember(pid, Rating: 0, RatingDeviation: 0, Volatility: 0))
                    .ToList(),
                AggregateRating: 0,
                QueuedAt: now))
            .ToList();

        var teams = _teamAssignment.AssignTeams(parties);

        // Anti-abuse (Phase 21 — inter-party 1v1): a match is UNRANKED when two OPPOSING
        // participants came from the SAME party/ticket. That only happens for an inter-party
        // match (two friends who queued together as one party, split across teams for a 1v1).
        // Awarding rating there is trivially exploitable — "party up, let your friend AFK,
        // farm free elo" — so the session is created with a NULL LadderId, which the rating
        // pipeline treats as unranked end-to-end (SessionCompleteService builds null-ladder
        // snapshots → PendingRatingUpdatesAdapter skips the PlayerRank read AND the rating
        // update). Normal stranger matchmaking keeps every party wholly on one team (party
        // cohesion), so each ticket maps to a single team and this never trips → stays ranked.
        var isInterPartyMatch = parties.Any(p =>
            p.Members
                .Select(m => teams.TryGetValue(m.PlayerId, out var t) ? t : -1)
                .Distinct()
                .Count() > 1);
        Guid? sessionLadderId = isInterPartyMatch ? null : fields.LadderId;
        if (isInterPartyMatch)
        {
            _logger?.LogInformation(
                "ProposalService.CreateSessionAsync: proposal {ProposalId} is an inter-party match " +
                "(opposing players share a party) — creating session {SessionId} as UNRANKED (null LadderId).",
                proposalId, sessionId);
        }

        var participants = new List<SessionParticipant>();
        foreach (var (playerId, team) in teams)
        {
            participants.Add(new SessionParticipant
            {
                Id = _ids.NewId(),
                SessionId = sessionId,
                PlayerId = playerId,
                Team = team,
            });
        }

        // Plan 05-09 chaos seam: production NullChaosInterceptor returns instantly. The SC#2
        // integration test's AbortingChaosInterceptor throws here to simulate a crash AFTER the
        // Lua complete-script flipped the proposal to state=complete but BEFORE the durable
        // GameSession row exists — the reconciler's orphan-session sweep must eventually
        // mark such a session as Cancelled (NOT a duplicate of any subsequently-created session).
        // Plan 16-04 split-brain test also uses this seam to pause Replica A past the TTL.
        await _chaos.BeforeSessionInsert(ct).ConfigureAwait(false);

        // SCALE-03: Insert the game_sessions row idempotently via ON CONFLICT DO NOTHING.
        // The session starts in "Active" state (Pending → Active via Start(now) transition
        // is reflected here as the literal state value 'Active').
        // NpgsqlParameter bindings prevent any SQL injection from proposal / ladder ids.
        // CancellationToken is passed via the IEnumerable overload to prevent ct from being
        // interpreted as a SQL parameter by the params object[] overload.
        var sqlParams = new object[]
        {
            new NpgsqlParameter("id", sessionId),
            new NpgsqlParameter("ladderId", (object?)sessionLadderId ?? DBNull.Value),
            new NpgsqlParameter("idempotencyKey", idempotencyKey),
            new NpgsqlParameter("createdAt", now),
            new NpgsqlParameter("startedAt", now),
        };
        var rowsInserted = await ctx.Database.ExecuteSqlRawAsync(
            @"INSERT INTO gamekit.game_sessions
                  (""Id"", ""State"", ""LadderId"", ""IdempotencyKey"", ""CreatedAt"", ""StartedAt"")
              VALUES (@id, 'Active', @ladderId, @idempotencyKey, @createdAt, @startedAt)
              ON CONFLICT (""IdempotencyKey"") WHERE ""IdempotencyKey"" IS NOT NULL DO NOTHING",
            sqlParams,
            ct).ConfigureAwait(false);

        if (rowsInserted == 0)
        {
            // ON CONFLICT DO NOTHING fired — a concurrent replica already created this session.
            // Resolve and return the canonical session id; do NOT insert participants again.
            _logger?.LogInformation(
                "ProposalService.CreateSessionAsync: duplicate formation for proposal {ProposalId} — returning existing session.",
                proposalId);

            var existing = await ctx.Set<GameSession>()
                .Where(s => s.IdempotencyKey == idempotencyKey)
                .Select(s => s.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (existing == Guid.Empty)
            {
                // ON CONFLICT DO NOTHING fired but the follow-up query found no row —
                // this is logically impossible (the conflict implies a row exists) and
                // indicates a severe data-integrity problem (e.g. a concurrent DELETE on
                // game_sessions, or a bug in the partial-index predicate). Returning the
                // never-inserted sessionId would produce a dangling session reference, so
                // we fail loudly instead.
                _logger?.LogError(
                    "ProposalService.CreateSessionAsync: ON CONFLICT DO NOTHING fired for proposal {ProposalId} " +
                    "(idempotencyKey={IdempotencyKey}) but follow-up query returned no row. " +
                    "This is logically impossible — a concurrent DELETE or index bug is suspected.",
                    proposalId, idempotencyKey);
                throw new InvalidOperationException(
                    $"ProposalService.CreateSessionAsync: conflict guard fired for proposal {proposalId} " +
                    "but the canonical game_sessions row is missing. " +
                    "Cannot return a valid session id — see logs for diagnostics.");
            }

            return existing;
        }

        // Primary path: we created the row — now insert participants.
        ctx.Set<SessionParticipant>().AddRange(participants);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        return sessionId;
    }

    /// <summary>
    /// PUBLISH "matched" with the new session id to every member's status channel so any
    /// waiting long-poll wakes immediately (RESEARCH §Decision 9).
    /// </summary>
    private static async Task PublishMatchedAsync(IDatabase db, ProposalFields fields, Guid sessionId)
    {
        var publisher = db.Multiplexer.GetSubscriber();
        var payload = $"matched:{sessionId}";
        foreach (var t in fields.Tickets)
        {
            var ch = MatchmakingRedisKeys.StatusChannel(t.TicketId);
            await publisher.PublishAsync(RedisChannel.Literal(ch), payload).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Parse the JSON blob written under the proposal hash's <c>fields</c> entry by the
    /// ticker's <see cref="AtomicClaimScript"/>. Returns <see langword="null"/> on any parse
    /// failure (the proposal is treated as "not found").
    /// </summary>
    private ProposalFields? ParseFields(HashEntry[] entries)
    {
        string? json = null;
        foreach (var e in entries)
        {
            if ((string?)e.Name == "fields")
            {
                json = (string?)e.Value;
                break;
            }
        }
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ProposalFields>(json);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "ProposalService: failed to parse proposal fields JSON.");
            return null;
        }
    }

    /// <summary>
    /// Write a ticket event to the bounded channel. On full (TryWrite returns false), log a
    /// warning — the dropped-events OTel counter is owned by the drain service (Plan 05-07).
    /// </summary>
    private void EmitTicketEvent(Guid ticketId, TicketEventType eventType, string? payload)
    {
        var evt = new TicketEvent
        {
            Id = _ids.NewId(),
            TicketId = ticketId,
            EventType = eventType,
            OccurredAt = _clock.UtcNow,
            Payload = payload,
        };
        if (!_eventChannel.TryWrite(evt))
        {
            _logger?.LogWarning(
                "ProposalService: bounded TicketEvent channel full — dropped {EventType} event for ticket {Ticket}.",
                eventType, ticketId);
        }
    }

    private static string JsonPayload(string key1, object? value1, string? key2 = null, object? value2 = null)
    {
        var dict = new Dictionary<string, object?> { [key1] = value1 };
        if (key2 is not null) dict[key2] = value2;
        return JsonSerializer.Serialize(dict);
    }
}
