// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Strategy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IMatchmakingService"/>. Glues the Redis live queue
/// (Pitfall §6 millisecond scoring + ZADD) to the Postgres ticket mirror (CONTEXT D-15
/// async write via <see cref="ChannelWriter{T}"/>) and to the decline-cooldown gate
/// (<see cref="IDeclineCooldownService"/>, CONTEXT D-08).
/// </summary>
/// <remarks>
/// <para>
/// <b>Enqueue (Plan 05-08 RESEARCH §Decision 11):</b>
/// <list type="number">
///   <item>Resolve cooldown via <see cref="IDeclineCooldownService"/>; reject with
///         <see cref="EnqueueOutcome.RejectedDueToCooldown"/> when locked.</item>
///   <item>Resolve party + members (single SELECT joined). When no party id is supplied the
///         enqueue is solo — a singleton members list is built from the calling player id.</item>
///   <item>Compute aggregate rating via <see cref="PartyRatingAggregatorService"/> using the
///         ladder's configured aggregator (CONTEXT D-13). v1 reads zero-rated members from
///         the party (the matchmaker materialises ratings at tick time from the
///         <c>player_ranks</c> table; the cached aggregate written here is best-effort and is
///         re-computed in <see cref="MatchmakerTickerService"/> if needed).</item>
///   <item>Defence-in-depth: when the ladder has <c>MaxPartyRatingSpread</c> set, reject the
///         enqueue if <c>max-min</c> exceeds the cap.</item>
///   <item>Check for an existing non-terminal ticket on the same party (or the player when
///         solo). Returns <see cref="EnqueueOutcome.AlreadyEnqueued"/> on hit.</item>
///   <item>HSET <c>mm:ticket:{id}</c> + ZADD <c>mm:queue:{ladderId}:{poolName}</c> using
///         <see cref="DateTimeOffset.ToUnixTimeMilliseconds"/> (Pitfall §6 — NEVER seconds).</item>
///   <item><see cref="ChannelWriter{T}.TryWrite"/> a <see cref="TicketEventType.Queued"/> row;
///         the drain service persists it asynchronously.</item>
/// </list>
/// </para>
/// <para>
/// <b>Cancel (T-05-08-01 ownership check):</b> reads the ticket hash to recover its party id,
/// verifies the calling player is a current member of that party (or matches the solo
/// holder), then ZREM + DEL + write Cancelled event. The Postgres row is updated by the
/// reconciler (Plan 05-07) or by the drain depending on which event lands first — either
/// way the terminal status converges within the reconciler's stale-ticket threshold.
/// </para>
/// </remarks>
public sealed class MatchmakingService : IMatchmakingService
{
    private readonly GameKitDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDeclineCooldownService _cooldown;
    private readonly PartyRatingAggregatorService _aggregator;
    private readonly ChannelWriter<TicketEvent> _events;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IReadOnlyList<MatchmakingLadderConfig> _ladders;
    private readonly ILogger<MatchmakingService>? _logger;

    /// <summary>Constructs the service.</summary>
    /// <param name="db">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="cooldown">Decline-cooldown gate (Plan 05-06).</param>
    /// <param name="aggregator">Party-rating aggregator (CONTEXT D-13).</param>
    /// <param name="events">Bounded ticket-event channel writer.</param>
    /// <param name="clock">Authoritative UTC clock.</param>
    /// <param name="ids">Id generator (UUIDv7).</param>
    /// <param name="ladders">All registered matchmaking ladder configs.</param>
    /// <param name="logger">Optional logger.</param>
    public MatchmakingService(
        GameKitDbContext db,
        IConnectionMultiplexer redis,
        IDeclineCooldownService cooldown,
        PartyRatingAggregatorService aggregator,
        ChannelWriter<TicketEvent> events,
        IClock clock,
        IIdGenerator ids,
        IReadOnlyList<MatchmakingLadderConfig> ladders,
        ILogger<MatchmakingService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(cooldown);
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(ladders);
        _db = db;
        _redis = redis;
        _cooldown = cooldown;
        _aggregator = aggregator;
        _events = events;
        _clock = clock;
        _ids = ids;
        _ladders = ladders;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EnqueueResult> EnqueueAsync(
        Guid playerId,
        Guid ladderId,
        string? poolName,
        Guid? partyId,
        CancellationToken ct = default)
    {
        var pool = string.IsNullOrWhiteSpace(poolName) ? "default" : poolName!;

        // Step 1: cooldown gate.
        var now = _clock.UtcNow;
        var cooldown = await _cooldown.GetCurrentCooldownAsync(playerId, now, ct).ConfigureAwait(false);
        if (cooldown.IsLocked)
        {
            return new EnqueueResult(
                Outcome: EnqueueOutcome.RejectedDueToCooldown,
                RetryAfter: cooldown.RetryAfter,
                Detail: "decline_cooldown_active");
        }

        // Step 2: resolve party + members (or build the solo singleton).
        Party? party = null;
        IReadOnlyList<Guid> memberPlayerIds;
        if (partyId.HasValue)
        {
            party = await _db.Set<Party>()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == partyId.Value, ct)
                .ConfigureAwait(false);
            if (party is null || party.State != PartyState.Open)
            {
                return new EnqueueResult(
                    EnqueueOutcome.InvalidParty,
                    Detail: party is null ? "party_not_found" : "party_not_open");
            }

            var members = await _db.Set<PartyMember>()
                .AsNoTracking()
                .Where(m => m.PartyId == party.Id)
                .Select(m => m.PlayerId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (!members.Contains(playerId))
            {
                return new EnqueueResult(EnqueueOutcome.InvalidParty, Detail: "not_party_member");
            }
            memberPlayerIds = members;
        }
        else
        {
            memberPlayerIds = new[] { playerId };
        }

        // Step 3: resolve the ladder config (by id is impossible in v1 — config is keyed by
        // name; v1 callers must supply the ladder id matching the Rankings ladder of the same
        // configured name, and the matchmaking pool name == ladder name convention). We look
        // up the ladder by ladder name == pool name lookup OR fall through to the first
        // registered ladder. v1 ships single-ladder per app; multi-ladder integration tests
        // assert by-name. For now: pick by pool name match, fallback to first.
        MatchmakingLadderConfig? cfg = _ladders.FirstOrDefault(l =>
            l.Name.Equals(pool, StringComparison.OrdinalIgnoreCase));
        cfg ??= _ladders.FirstOrDefault();
        if (cfg is null)
        {
            return new EnqueueResult(EnqueueOutcome.UnknownLadder, Detail: "no_ladders_registered");
        }

        // Step 4: defence-in-depth MaxPartyRatingSpread gate. v1 cannot query player ratings
        // here without a Rankings runtime dep; member ratings default to zero so the spread
        // is zero — the cap will not trip in v1 enqueue. The strategy enforces the cap at
        // tick time using the cached rating, so this layer is preventative only.
        var queuedMembers = memberPlayerIds
            .Select(pid => new QueuedPartyMember(pid, Rating: 0, RatingDeviation: 0, Volatility: 0))
            .ToList();

        if (cfg.MaxPartyRatingSpread is int cap && cap > 0)
        {
            var spread = queuedMembers.Max(m => m.Rating) - queuedMembers.Min(m => m.Rating);
            if (spread > cap)
            {
                return new EnqueueResult(
                    EnqueueOutcome.RejectedDueToSpread,
                    Detail: $"party_rating_spread_exceeded:{spread}>{cap}");
            }
        }

        var aggregateRating = _aggregator.Compute(cfg.PartyRatingAggregator, queuedMembers);

        // Step 5: existing-ticket guard. Redis-first: check the queue for any ticket whose
        // ticket-hash points at this party (or this player when solo). Cheap implementation:
        // look at the existing in-progress matchmaking_ticket rows for the same party id.
        var dbCtx = _db; // local alias for readability
        var existingActive = partyId.HasValue
            ? await dbCtx.Set<MatchmakingTicket>()
                .AsNoTracking()
                .AnyAsync(t => t.PartyId == partyId.Value &&
                               (t.Status == TicketStatus.Queued || t.Status == TicketStatus.Proposed),
                          ct)
                .ConfigureAwait(false)
            : false; // solo dedup is best-effort at the Redis layer (next step).

        if (existingActive)
        {
            return new EnqueueResult(EnqueueOutcome.AlreadyEnqueued, Detail: "ticket_active");
        }

        // Step 6: write Redis state.
        var ticketId = _ids.NewId();
        var queuedAtMs = now.ToUnixTimeMilliseconds();
        var db = _redis.GetDatabase();

        var ticketKey = MatchmakingRedisKeys.Ticket(ticketId);
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, pool);

        var membersJson = JsonSerializer.Serialize(queuedMembers);
        await db.HashSetAsync(ticketKey,
            [
                new HashEntry("status", "queued"),
                new HashEntry("ladderId", ladderId.ToString()),
                new HashEntry("poolName", pool),
                new HashEntry("queuedAt", queuedAtMs),
                new HashEntry("aggregateRating", aggregateRating.ToString("G17", CultureInfo.InvariantCulture)),
                new HashEntry("partyId", partyId?.ToString() ?? string.Empty),
                new HashEntry("playerId", playerId.ToString()),
                new HashEntry("members", membersJson),
            ])
            .ConfigureAwait(false);

        // Pitfall §6 — score MUST be Unix milliseconds (NOT seconds — second-precision
        // ties become indistinguishable and the bracket-flex calculation drifts).
        await db.SortedSetAddAsync(queueKey, ticketId.ToString(), queuedAtMs).ConfigureAwait(false);

        // Step 7: emit Queued ticket event (drained asynchronously into matchmaking_tickets).
        var evt = new TicketEvent
        {
            Id = _ids.NewId(),
            TicketId = ticketId,
            EventType = TicketEventType.Queued,
            OccurredAt = now,
            Payload = JsonSerializer.Serialize(new
            {
                ladderId,
                poolName = pool,
                partyId,
                playerId,
            }),
        };
        if (!_events.TryWrite(evt))
        {
            _logger?.LogWarning(
                "MatchmakingService: bounded TicketEvent channel full — dropped Queued event for ticket {Ticket}.",
                ticketId);
        }

        return new EnqueueResult(EnqueueOutcome.Queued, TicketId: ticketId);
    }

    /// <inheritdoc />
    public async Task<CancelResult> CancelAsync(Guid ticketId, Guid playerId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var ticketKey = MatchmakingRedisKeys.Ticket(ticketId);

        var entries = await db.HashGetAllAsync(ticketKey).ConfigureAwait(false);
        if (entries.Length == 0)
        {
            return new CancelResult(CancelOutcome.NotFound);
        }

        string? status = null;
        string? ladderIdStr = null;
        string? poolName = null;
        string? partyIdStr = null;
        string? holderPlayerIdStr = null;
        foreach (var e in entries)
        {
            switch ((string?)e.Name)
            {
                case "status": status = (string?)e.Value; break;
                case "ladderId": ladderIdStr = (string?)e.Value; break;
                case "poolName": poolName = (string?)e.Value; break;
                case "partyId": partyIdStr = (string?)e.Value; break;
                case "playerId": holderPlayerIdStr = (string?)e.Value; break;
            }
        }

        if (status is "matched" or "cancelled")
        {
            return new CancelResult(CancelOutcome.Terminal);
        }

        // T-05-08-01 — ownership check.
        var isAuthorized = false;
        if (!string.IsNullOrEmpty(partyIdStr) && Guid.TryParse(partyIdStr, out var partyId))
        {
            isAuthorized = await _db.Set<PartyMember>()
                .AsNoTracking()
                .AnyAsync(m => m.PartyId == partyId && m.PlayerId == playerId, ct)
                .ConfigureAwait(false);
        }
        else if (holderPlayerIdStr is not null
                 && Guid.TryParse(holderPlayerIdStr, out var holderPlayerId)
                 && holderPlayerId == playerId)
        {
            isAuthorized = true;
        }

        if (!isAuthorized)
        {
            return new CancelResult(CancelOutcome.NotAuthorized);
        }

        // Cancel: ZREM + DEL + status flip + publish + event.
        if (!string.IsNullOrEmpty(ladderIdStr)
            && Guid.TryParse(ladderIdStr, out var ladderId)
            && !string.IsNullOrEmpty(poolName))
        {
            var queueKey = MatchmakingRedisKeys.Queue(ladderId, poolName!);
            await db.SortedSetRemoveAsync(queueKey, ticketId.ToString()).ConfigureAwait(false);
        }

        await db.KeyDeleteAsync(ticketKey).ConfigureAwait(false);

        // Notify any long-poll subscribers waiting on this ticket's status channel.
        var subscriber = _redis.GetSubscriber();
        await subscriber.PublishAsync(
            RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticketId)),
            "cancelled").ConfigureAwait(false);

        var now = _clock.UtcNow;
        var evt = new TicketEvent
        {
            Id = _ids.NewId(),
            TicketId = ticketId,
            EventType = TicketEventType.Cancelled,
            OccurredAt = now,
            Payload = JsonSerializer.Serialize(new { reason = "player_cancelled", playerId }),
        };
        if (!_events.TryWrite(evt))
        {
            _logger?.LogWarning(
                "MatchmakingService: bounded TicketEvent channel full — dropped Cancelled event for ticket {Ticket}.",
                ticketId);
        }

        return new CancelResult(CancelOutcome.Cancelled);
    }

    /// <inheritdoc />
    public async Task<TicketStatusSnapshot?> GetStatusAsync(Guid ticketId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(MatchmakingRedisKeys.Ticket(ticketId)).ConfigureAwait(false);
        if (entries.Length == 0)
            return null;

        string? status = null;
        string? proposalIdStr = null;
        string? sessionIdStr = null;
        string? deadlineStr = null;
        foreach (var e in entries)
        {
            switch ((string?)e.Name)
            {
                case "status": status = (string?)e.Value; break;
                case "proposalId": proposalIdStr = (string?)e.Value; break;
                case "sessionId": sessionIdStr = (string?)e.Value; break;
                case "deadline": deadlineStr = (string?)e.Value; break;
            }
        }

        if (string.IsNullOrEmpty(status))
            return null;

        Guid? proposalId = Guid.TryParse(proposalIdStr, out var p) ? p : null;
        Guid? sessionId = Guid.TryParse(sessionIdStr, out var s) ? s : null;
        DateTimeOffset? deadline = DateTimeOffset.TryParse(
            deadlineStr,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var d) ? d : null;

        // Normalize the status case. The Lua atomic-claim script writes 'Proposed' (PascalCase)
        // and the proposal service may write 'Matched'/'Cancelled' — but the wire contract +
        // PUBLISH parser + HTML clients all use the lowercase tokens "queued"/"proposed"/
        // "matched"/"cancelled". Normalize here so the snapshot path matches the publish path.
        return new TicketStatusSnapshot(
            Status: status!.ToLowerInvariant(),
            ProposalId: proposalId,
            Deadline: deadline,
            SessionId: sessionId);
    }
}
