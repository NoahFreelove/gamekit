// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IBackfillService"/>. Creates
/// <see cref="MatchmakingTicketType.Backfill"/> tickets at Redis score <c>0</c> so they
/// are processed before all normal tickets by the existing ticker's ZRANGEBYSCORE Ascending
/// ordering — no ticker code change required (MATCH-19 SC#3).
/// </summary>
/// <remarks>
/// <para>
/// <b>BackfillAsync steps:</b>
/// <list type="number">
///   <item>Look up the <c>Ladder</c> row by <c>ladderId</c> to resolve the canonical
///         ladder name. Return <see cref="BackfillOutcome.UnknownLadder"/> when the id is not in
///         the database or when no matching <see cref="MatchmakingLadderConfig"/> is registered.</item>
///   <item>Compute pool from the <c>regionName</c> argument: null/whitespace
///         → <c>"default"</c>. Non-null → validate against <c>AllowedRegions</c> of the
///         correctly-resolved config (guards against cross-ladder region bypass).</item>
///   <item>Load the <see cref="GameSession"/> by <c>sessionId</c> (AsNoTracking). Return
///         <see cref="BackfillOutcome.SessionNotFound"/> or <see cref="BackfillOutcome.SessionNotActive"/>.</item>
///   <item>Dedup: return <see cref="BackfillOutcome.AlreadyEnqueued"/> if the player already
///         holds a non-terminal ticket for this ladder.</item>
///   <item>Create a <see cref="MatchmakingTicket"/> row with <c>TicketType = Backfill</c>.
///         SaveChanges (Postgres row first so FK on ticket_events is satisfied).</item>
///   <item>HSET <c>mm:ticket:{id}</c> + ZADD <c>mm:queue:{ladderId}:{pool}</c> with
///         <c>score = 0</c> (MATCH-19 SC#3 CRITICAL — NOT Unix milliseconds).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class BackfillService : IBackfillService
{
    private readonly GameKitDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ChannelWriter<TicketEvent> _events;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IReadOnlyList<MatchmakingLadderConfig> _ladders;
    private readonly ILogger<BackfillService>? _logger;

    /// <summary>Constructs the service.</summary>
    /// <param name="db">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="events">Bounded ticket-event channel writer.</param>
    /// <param name="clock">Authoritative UTC clock.</param>
    /// <param name="ids">Id generator (UUIDv7).</param>
    /// <param name="ladders">All registered matchmaking ladder configs.</param>
    /// <param name="logger">Optional logger.</param>
    public BackfillService(
        GameKitDbContext db,
        IConnectionMultiplexer redis,
        ChannelWriter<TicketEvent> events,
        IClock clock,
        IIdGenerator ids,
        IReadOnlyList<MatchmakingLadderConfig> ladders,
        ILogger<BackfillService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(ladders);
        _db = db;
        _redis = redis;
        _events = events;
        _clock = clock;
        _ids = ids;
        _ladders = ladders;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BackfillResult> BackfillAsync(
        Guid playerId,
        Guid ladderId,
        Guid sessionId,
        string? regionName,
        CancellationToken ct = default)
    {
        // Step 1: compute pool from regionName (same resolution as MatchmakingService / endpoint layer).
        var pool = string.IsNullOrWhiteSpace(regionName) ? "default" : regionName!;

        // Step 2: resolve ladder config by ladderId (not by pool/region name). Look up the
        // canonical ladder name from the DB first so AllowedRegions is always validated against
        // the ladder the caller actually requested — not a coincidentally-named config entry.
        // This prevents a multi-ladder cross-bypass where ladderId targets ladder B while a
        // pool-name match would resolve cfg for ladder A (whose AllowedRegions may be less
        // restrictive). The DB query is AsNoTracking and selects only the Name column.
        var ladderName = await _db.Set<Ladder>()
            .AsNoTracking()
            .Where(l => l.Id == ladderId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (ladderName is null)
        {
            _logger?.LogWarning("BackfillService: ladderId {LadderId} not found in DB — rejecting BackfillAsync for player {PlayerId}", ladderId, playerId);
            return new BackfillResult(BackfillOutcome.UnknownLadder, Detail: $"unknown_ladder:{ladderId}");
        }

        var cfg = _ladders.FirstOrDefault(l => l.Name.Equals(ladderName, StringComparison.OrdinalIgnoreCase));
        if (cfg is null)
        {
            _logger?.LogWarning("BackfillService: ladder '{LadderName}' is not configured for matchmaking — rejecting BackfillAsync for player {PlayerId}", ladderName, playerId);
            return new BackfillResult(BackfillOutcome.UnknownLadder, Detail: "ladder_not_configured_for_matchmaking");
        }

        // Step 3: region validation (mirrors MatchmakingService EnqueueAsync MATCH-18 guard).
        // Guard: AllowedRegions is non-null/non-empty AND pool != "default" AND not in list → reject.
        // cfg is correctly resolved by ladderId so this guard always runs against the right ladder.
        if (cfg.AllowedRegions is { Count: > 0 } && pool != "default"
            && !cfg.AllowedRegions.Contains(pool, StringComparer.OrdinalIgnoreCase))
        {
            return new BackfillResult(BackfillOutcome.InvalidRegion, Detail: $"region_not_allowed:{pool}");
        }

        // Step 4: validate the session — must exist and be Active.
        // GameSessionState is stored as string (HasConversion<string>()) — compare via enum value.
        var session = await _db.Set<GameSession>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);

        if (session is null)
            return new BackfillResult(BackfillOutcome.SessionNotFound);

        if (session.State != GameSessionState.Active)
            return new BackfillResult(BackfillOutcome.SessionNotActive);

        // Step 5: dedup guard — reject if a non-terminal Backfill ticket already exists in this
        // pool. MatchmakingTicket has no PlayerId column (solo dedup is best-effort per service
        // contract), so we guard at the Backfill-type level: a second burst call for the same
        // ladder/pool will find the first Backfill ticket still queued and return AlreadyEnqueued.
        // This prevents double-queueing a Backfill ticket at score 0 in the same pool.
        var existingActive = await _db.Set<MatchmakingTicket>()
            .AsNoTracking()
            .AnyAsync(t => t.LadderId == ladderId
                        && t.PoolName == pool
                        && t.TicketType == MatchmakingTicketType.Backfill
                        && (t.Status == TicketStatus.Queued || t.Status == TicketStatus.Proposed),
                      ct)
            .ConfigureAwait(false);

        if (existingActive)
            return new BackfillResult(BackfillOutcome.AlreadyEnqueued, Detail: "active_ticket_exists");

        // Step 6: write Postgres analytics row FIRST so the FK from ticket_events is satisfied.
        var ticketId = _ids.NewId();
        var now = _clock.UtcNow;

        var ticketRow = new MatchmakingTicket
        {
            Id = ticketId,
            PartyId = null,
            LadderId = ladderId,
            PoolName = pool,
            Status = TicketStatus.Queued,
            TicketType = MatchmakingTicketType.Backfill,   // MATCH-19 SC#3 — Backfill = 1
            QueuedAt = now,
        };
        _db.Set<MatchmakingTicket>().Add(ticketRow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Step 7: write Redis state.
        var db = _redis.GetDatabase();
        var ticketKey = MatchmakingRedisKeys.Ticket(ticketId);
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, pool);

        await db.HashSetAsync(ticketKey,
            [
                new HashEntry("status", "queued"),
                new HashEntry("ladderId", ladderId.ToString()),
                new HashEntry("poolName", pool),
                new HashEntry("queuedAt", 0),                  // score is also 0
                new HashEntry("aggregateRating", "0"),
                new HashEntry("partyId", string.Empty),
                new HashEntry("playerId", playerId.ToString()),
                new HashEntry("members", JsonSerializer.Serialize(new[] { new { PlayerId = playerId, Rating = 0.0 } })),
                new HashEntry("ticketType", "backfill"),
            ])
            .ConfigureAwait(false);

        // MATCH-19 SC#3 CRITICAL: score = 0 (NOT DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).
        // Score 0 < any real Unix-ms timestamp (~1.75e12), so backfill tickets sort FIRST when
        // the ticker uses ZRANGEBYSCORE Ascending. No ticker code change required.
        await db.SortedSetAddAsync(queueKey, ticketId.ToString(), score: 0).ConfigureAwait(false);

        _logger?.LogDebug(
            "BackfillService: queued backfill ticket {TicketId} for player {PlayerId} in pool '{Pool}' at score 0",
            ticketId, playerId, pool);

        return new BackfillResult(BackfillOutcome.Queued, TicketId: ticketId);
    }
}
