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
using GameKit.Core.Services;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Strategy;
using GameKit.Matchmaking.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Live matchmaker BackgroundService (MATCH-07 + MATCH-08). Drives the match-formation
/// loop at the configured <see cref="GameKitMatchmakingTickerOptions.TickIntervalMs"/>
/// cadence (500 ms default). Leader-only via the Redis distributed lock + Polly v8 retry
/// (Plan 05-04 + Plan 05-05 / RESEARCH §Decision 6 + §Decision 11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Tick anatomy (one <see cref="RunOnceAsync"/> call):</b>
/// <list type="number">
///   <item>Acquire the matchmaker lease (<see cref="MatchmakerLeaseHelper.TryAcquireLeaseAsync"/>). On failure return <see cref="MatcherTickResult.LockNotAcquired"/>.</item>
///   <item>Open an OTel tick span (<see cref="MatchmakingActivitySource.StartTickActivity"/>).</item>
///   <item>For each registered ladder/pool combo: renew the lease (Pitfall §6 bail on false), ZRANGEBYSCORE candidate ticket ids, HGETALL each candidate, build the <see cref="QueuedParty"/> list, iterate candidates and invoke <see cref="IMatchmakingStrategy.Match"/>, run the Lua atomic-claim with the leader's <see cref="MatchmakerLeaseHelper.InstanceId"/> as the fencing token, PUBLISH <c>"proposed"</c> on each ticket's status channel, write <see cref="TicketEvent"/> rows.</item>
///   <item>After all pools: run <see cref="ProposalSweeper.SweepAsync"/> (Pitfall §10 partial-accept reap).</item>
///   <item>Release the lease (finally).</item>
/// </list>
/// </para>
/// <para>
/// <b>Why the same instance backs IHostedService + IMatchmakerTicker:</b> the host-side
/// <c>BackgroundService.ExecuteAsync</c> loop calls <see cref="RunOnceAsync"/> on a
/// <see cref="PeriodicTimer"/>; integration tests resolve <see cref="IMatchmakerTicker"/>
/// from DI and call <see cref="RunOnceAsync"/> directly to drive a single deterministic
/// tick — mirrors <c>GameKit.Rankings.Services.RankingsTickerService</c>.
/// </para>
/// <para>
/// <b>Fencing-token-safe atomic claim (Pitfall §2):</b> the <c>leaseValue</c> argument
/// passed to <c>AtomicClaimScript.ExecuteAsync</c> is always
/// <see cref="MatchmakerLeaseHelper.InstanceId"/>. The Lua script's first non-comment line
/// is <c>if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 'LEASE_LOST' end</c> — a
/// stale leader cannot write a proposal because its <c>InstanceId</c> no longer matches
/// the current lock value. When the script returns <see cref="AtomicClaimResult.LeaseLost"/>
/// the ticker bails with <see cref="MatcherTickResult.LeaseLost"/>.
/// </para>
/// <para>
/// <b>OpenTelemetry (Pitfall §7):</b> all spans are emitted via
/// <see cref="MatchmakingActivitySource"/>. Operators MUST register
/// <c>AddSource("GameKit.Matchmaking.Ticker")</c> in their OTel SDK to observe live
/// telemetry — without it, the spans are discarded silently.
/// </para>
/// </remarks>
internal sealed class MatchmakerTickerService : BackgroundService, IMatchmakerTicker
{
    private readonly ILogger<MatchmakerTickerService> _logger;
    private readonly GameKitMatchmakingOptions _opts;
    private readonly MatchmakerLeaseHelper _lease;
    private readonly IConnectionMultiplexer _redis;
    private readonly IMatchmakingStrategy _strategy;
    private readonly AtomicClaimScript _atomicClaim;
    private readonly ProposalSweeper _sweeper;
    private readonly IClock _clock;
    private readonly IReadOnlyList<MatchmakingLadderConfig> _ladders;
    private readonly ChannelWriter<TicketEvent> _eventWriter;
    private readonly IChaosInterceptor _chaos;

    /// <summary>Constructs the ticker service.</summary>
    /// <param name="logger">Structured logger.</param>
    /// <param name="opts">Matchmaking options snapshot.</param>
    /// <param name="lease">Redis distributed-lock helper.</param>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="strategy">The Scrutor-discovered <see cref="IMatchmakingStrategy"/>.</param>
    /// <param name="atomicClaim">Lua atomic-claim script executor (Plan 05-04).</param>
    /// <param name="sweeper">Proposal-sweeper (Plan 05-05 Pitfall §10).</param>
    /// <param name="clock">Authoritative UTC clock.</param>
    /// <param name="ladders">All registered matchmaking ladder configurations.</param>
    /// <param name="eventWriter">Channel writer for analytics ticket-events.</param>
    /// <param name="chaos">
    /// Test-only chaos seam (production default = <see cref="NullChaosInterceptor"/>).
    /// See <see cref="IChaosInterceptor"/> XML doc — the seam exists so the Plan 05-09 SC#2 chaos
    /// integration test can verify recovery from a crash between match-formation and proposal
    /// writeback without spawning a child process.
    /// </param>
    public MatchmakerTickerService(
        ILogger<MatchmakerTickerService> logger,
        IOptions<GameKitMatchmakingOptions> opts,
        MatchmakerLeaseHelper lease,
        IConnectionMultiplexer redis,
        IMatchmakingStrategy strategy,
        AtomicClaimScript atomicClaim,
        ProposalSweeper sweeper,
        IClock clock,
        IReadOnlyList<MatchmakingLadderConfig> ladders,
        ChannelWriter<TicketEvent> eventWriter,
        IChaosInterceptor chaos)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(atomicClaim);
        ArgumentNullException.ThrowIfNull(sweeper);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ladders);
        ArgumentNullException.ThrowIfNull(eventWriter);
        ArgumentNullException.ThrowIfNull(chaos);

        _logger = logger;
        _opts = opts.Value;
        _lease = lease;
        _redis = redis;
        _strategy = strategy;
        _atomicClaim = atomicClaim;
        _sweeper = sweeper;
        _clock = clock;
        _ladders = ladders;
        _eventWriter = eventWriter;
        _chaos = chaos;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MatchmakerTickerService starting (intervalMs={Interval}, lockTtl={Ttl}s, ladders={Count}).",
            _opts.Ticker.TickIntervalMs,
            _opts.Ticker.LockTtlSeconds,
            _ladders.Count);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opts.Ticker.TickIntervalMs));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var result = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                    _logger.LogDebug("MatchmakerTickerService tick: {Result}.", result);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never throw out of ExecuteAsync — log and continue.
                    _logger.LogError(ex, "MatchmakerTickerService: unhandled exception during tick. Continuing.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("MatchmakerTickerService stopped.");
    }

    /// <inheritdoc />
    public async Task<MatcherTickResult> RunOnceAsync(CancellationToken ct)
    {
        // Step 1: leader-election lock.
        var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
        if (!acquired)
        {
            _logger.LogDebug("MatchmakerTickerService: lock not acquired — another replica is leader.");
            return MatcherTickResult.LockNotAcquired;
        }

        using var tickActivity = MatchmakingActivitySource.StartTickActivity();
        var anyMatch = false;
        try
        {
            var now = _clock.UtcNow;

            // Admin pause flag — when set the matcher skips the tick (D-21 control surface).
            // Read once per tick; per-pool checks would be wasteful.
            var db = _redis.GetDatabase();
            var paused = await db.KeyExistsAsync(MatchmakingRedisKeys.ControlPaused).ConfigureAwait(false);
            if (paused)
            {
                _logger.LogDebug("MatchmakerTickerService: control:paused flag set — skipping match-formation.");
                tickActivity?.SetTag("paused", true);
            }
            else
            {
                foreach (var ladderCfg in _ladders)
                {
                    ct.ThrowIfCancellationRequested();

                    // Renew lease before processing each pool (Pitfall §6 — bail on false).
                    var renewed = await _lease.RenewLeaseAsync(ct).ConfigureAwait(false);
                    if (!renewed)
                    {
                        _logger.LogWarning(
                            "MatchmakerTickerService: lease lost mid-tick before pool '{Pool}'. " +
                            "Bailing with LeaseLost.", ladderCfg.Name);
                        return MatcherTickResult.LeaseLost;
                    }

                    var poolResult = await ProcessPoolAsync(ladderCfg, now, ct).ConfigureAwait(false);
                    if (poolResult == MatcherTickResult.LeaseLost)
                    {
                        return MatcherTickResult.LeaseLost;
                    }
                    if (poolResult == MatcherTickResult.Matched)
                    {
                        anyMatch = true;
                    }
                }
            }

            // Step 2: proposal-sweep (Pitfall §10).
            using (var sweepActivity = MatchmakingActivitySource.StartProposalSweepActivity())
            {
                var sweepRenewed = await _lease.RenewLeaseAsync(ct).ConfigureAwait(false);
                if (!sweepRenewed)
                {
                    _logger.LogWarning(
                        "MatchmakerTickerService: lease lost before proposal-sweep. Bailing with LeaseLost.");
                    return MatcherTickResult.LeaseLost;
                }
                try
                {
                    var reaped = await _sweeper.SweepAsync(ct).ConfigureAwait(false);
                    sweepActivity?.SetTag("reaped", reaped);
                }
                catch (RedisException ex)
                {
                    _logger.LogWarning(ex,
                        "MatchmakerTickerService: proposal-sweeper Redis error — continuing tick.");
                    return MatcherTickResult.RedisUnavailable;
                }
            }

            return anyMatch ? MatcherTickResult.Matched : MatcherTickResult.NoMatch;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "MatchmakerTickerService: Redis error during tick.");
            return MatcherTickResult.RedisUnavailable;
        }
        finally
        {
            // Always release the lock (Lua-script-verified — safe even if expired).
            await _lease.ReleaseLeaseAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes a single ladder/pool: ZRANGEBYSCORE candidates, evaluate strategy, run
    /// atomic-claim Lua, publish proposal events. Returns the per-pool sub-result.
    /// </summary>
    private async Task<MatcherTickResult> ProcessPoolAsync(
        MatchmakingLadderConfig ladderCfg, DateTimeOffset now, CancellationToken ct)
    {
        // v1: ladder name == pool name. The matchmaker queue is partitioned by
        // (ladderId, poolName). For the ticker to scan the queue it must resolve the ladder
        // id from the ladder name; in v1 we pull the candidate id from each ticket hash
        // ("ladderId" field) rather than maintain a separate name→id index. Each pool's
        // QueuedParty.LadderId field carries the correct id back to the strategy.
        //
        // The pool name uses the ladder's name in v1 (single-pool-per-ladder convention).
        var poolName = ladderCfg.Name;

        // To enumerate candidates we need the ladder Guid; the matchmaker writes the ladder
        // id into each ticket hash. v1 scans every queue key matching mm:queue:*:{poolName}
        // via SCAN — the operator typically has 1-3 ladders so the scan is cheap. Future
        // optimisation: maintain a per-pool registry in DI.
        var endpoints = _redis.GetEndPoints();
        if (endpoints.Length == 0)
            return MatcherTickResult.NoMatch;

        var server = _redis.GetServer(endpoints[0]);
        var db = _redis.GetDatabase();

        var anyMatchedInPool = false;
        var poolGlob = $"mm:queue:*:{poolName}";

        foreach (var queueKey in server.Keys(pattern: poolGlob, pageSize: 100))
        {
            ct.ThrowIfCancellationRequested();

            using var poolActivity = MatchmakingActivitySource.StartPoolActivity(
                ExtractLadderId(queueKey.ToString()), poolName);

            // Pull up to 200 candidate ticket ids (Unix-ms scored, oldest first).
            var entries = await db.SortedSetRangeByScoreAsync(
                queueKey, double.NegativeInfinity, double.PositiveInfinity,
                Exclude.None, Order.Ascending, 0, take: 200).ConfigureAwait(false);

            poolActivity?.SetTag("candidatesEvaluated", entries.Length);

            if (entries.Length < 2)
            {
                // Need at least two queued parties for a match.
                continue;
            }

            // Materialise candidates by HGETALL on each ticket hash.
            var candidates = new List<QueuedParty>(entries.Length);
            foreach (var entry in entries)
            {
                if (!Guid.TryParse(entry.ToString(), out var tid))
                    continue;
                var ticket = await BuildQueuedPartyAsync(tid, ladderCfg, ct).ConfigureAwait(false);
                if (ticket is not null)
                    candidates.Add(ticket);
            }

            if (candidates.Count < 2)
                continue;

            // Iterate candidates oldest-first; for each, the rest are the pool.
            // Track ids already claimed inside this loop to avoid trying to re-match a ticket
            // we just removed from the queue.
            var claimed = new HashSet<Guid>();
            var matchedInPoolCount = 0;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (claimed.Contains(candidate.TicketId))
                    continue;

                // Build the pool by excluding already-claimed candidates and the candidate itself.
                var pool = new List<QueuedParty>(candidates.Count - 1);
                for (var j = 0; j < candidates.Count; j++)
                {
                    if (i == j) continue;
                    if (claimed.Contains(candidates[j].TicketId)) continue;
                    pool.Add(candidates[j]);
                }
                if (pool.Count == 0)
                    continue;

                var match = _strategy.Match(candidate, pool, now);
                if (match is null)
                    continue;

                var claimResult = await TryClaimMatchAsync(match, queueKey.ToString(), ct)
                    .ConfigureAwait(false);
                switch (claimResult)
                {
                    case AtomicClaimResult.Success:
                        anyMatchedInPool = true;
                        matchedInPoolCount++;
                        foreach (var t in match.MatchedTickets)
                            claimed.Add(t.TicketId);
                        await PublishProposalEventsAsync(match, now, ct).ConfigureAwait(false);
                        break;

                    case AtomicClaimResult.LeaseLost:
                        _logger.LogWarning(
                            "MatchmakerTickerService: atomic-claim returned LEASE_LOST — bailing with LeaseLost.");
                        return MatcherTickResult.LeaseLost;

                    case AtomicClaimResult.TicketGone:
                        // Another tick / replica got there first. Continue.
                        _logger.LogDebug(
                            "MatchmakerTickerService: atomic-claim returned TICKET_GONE — continuing pool scan.");
                        break;

                    case AtomicClaimResult.RedisError:
                    default:
                        _logger.LogWarning(
                            "MatchmakerTickerService: atomic-claim returned RedisError for proposal {ProposalId}.",
                            match.ProposalId);
                        // Continue — the next tick will retry.
                        break;
                }
            }

            poolActivity?.SetTag("matchesFormed", matchedInPoolCount);
        }

        return anyMatchedInPool ? MatcherTickResult.Matched : MatcherTickResult.NoMatch;
    }

    /// <summary>
    /// Builds a <see cref="QueuedParty"/> from the per-ticket Redis hash. Returns
    /// <see langword="null"/> when the hash is missing required fields (defensive).
    /// </summary>
    private async Task<QueuedParty?> BuildQueuedPartyAsync(
        Guid ticketId, MatchmakingLadderConfig _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        var hash = await db.HashGetAllAsync(MatchmakingRedisKeys.Ticket(ticketId)).ConfigureAwait(false);
        if (hash.Length == 0)
            return null;

        var map = hash.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        if (!map.TryGetValue("ladderId", out var ladderIdStr) ||
            !Guid.TryParse(ladderIdStr, out var ladderId))
            return null;
        if (!map.TryGetValue("poolName", out var poolName))
            return null;
        if (!map.TryGetValue("queuedAt", out var queuedAtStr) ||
            !long.TryParse(queuedAtStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var queuedAtMs))
            return null;
        if (!map.TryGetValue("aggregateRating", out var aggStr) ||
            !double.TryParse(aggStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var aggregateRating))
            return null;

        Guid? partyId = null;
        if (map.TryGetValue("partyId", out var pidStr) && Guid.TryParse(pidStr, out var pid))
            partyId = pid;

        // Members blob is optional — v1 strategy uses AggregateRating directly; members are
        // only required for the spread-cap defense path in EloRangeMatchmakingStrategy.
        IReadOnlyList<QueuedPartyMember> members = Array.Empty<QueuedPartyMember>();
        if (map.TryGetValue("members", out var membersJson) && !string.IsNullOrEmpty(membersJson))
        {
            try
            {
                members = JsonSerializer.Deserialize<List<QueuedPartyMember>>(membersJson)
                    ?? new List<QueuedPartyMember>();
            }
            catch (JsonException)
            {
                _logger.LogWarning(
                    "MatchmakerTickerService: ticket {TicketId} has malformed 'members' JSON — skipping members.",
                    ticketId);
            }
        }

        return new QueuedParty(
            TicketId: ticketId,
            PartyId: partyId,
            LadderId: ladderId,
            PoolName: poolName,
            Members: members,
            AggregateRating: aggregateRating,
            QueuedAt: DateTimeOffset.FromUnixTimeMilliseconds(queuedAtMs));
    }

    /// <summary>
    /// Runs the atomic-claim Lua script against the live queue + writes the proposal hash.
    /// The fencing token is <see cref="MatchmakerLeaseHelper.InstanceId"/> (Pitfall §2).
    /// </summary>
    private async Task<AtomicClaimResult> TryClaimMatchAsync(
        MatchResult match, string queueKey, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var proposalKey = MatchmakingRedisKeys.Proposal(match.ProposalId);
        var ticketIds = match.MatchedTickets.Select(t => t.TicketId).ToArray();

        // The proposal "fields" JSON carries the deadline + team assignments for the
        // accept-step (Plan 05-06). The "tickets" CSV is read by ProposalSweeper (Pitfall §10)
        // to know which ticket ids participated; it's written via a separate HSET after the
        // atomic-claim because the Lua script only writes the "fields" entry.
        var deadline = _clock.UtcNow.AddSeconds(_opts.AcceptTimeoutSeconds);
        // Serialize the SHARED Services.ProposalFields shape so ProposalService.ParseFields
        // (Plan 05-06) sees the same payload structure on accept/decline. Earlier this method
        // serialized a ticker-private 2-field record (Deadline + Teams) — ProposalService's
        // deserializer then produced a default ProposalFields with Tickets=[], which made
        // every accept call return NotInProposal (403). The Teams precomputation is dropped:
        // ProposalService recomputes team assignment at all-accept time from
        // ProposalFields.Tickets[].PlayerIds, so the on-wire payload only carries inputs.
        var sharedFields = new ProposalFields
        {
            Deadline = deadline.ToString("O", CultureInfo.InvariantCulture),
            LadderId = match.MatchedTickets.Count > 0 ? match.MatchedTickets[0].LadderId : Guid.Empty,
            QueueKey = queueKey,
            Tickets = match.MatchedTickets.Select(qp => new ProposalTicket
            {
                TicketId = qp.TicketId,
                QueuedAtUnixMs = qp.QueuedAt.ToUnixTimeMilliseconds(),
                PlayerIds = qp.Members.Select(m => m.PlayerId).ToList(),
            }).ToList(),
        };
        var fieldsJson = JsonSerializer.Serialize(sharedFields);

        var ttl = _opts.AcceptTimeoutSeconds + 5; // +5s grace per Pitfall §10

        // Plan 05-09 chaos seam: production NullChaosInterceptor returns instantly. The SC#2
        // integration test's AbortingChaosInterceptor throws here to simulate a crash between
        // match-formation and the Lua claim — the reconciler must subsequently mark the orphan
        // tickets as Expired.
        await _chaos.BeforeLuaClaim(ct).ConfigureAwait(false);

        var result = await _atomicClaim.ExecuteAsync(
            db,
            leaseKey: _opts.Ticker.LockKey,
            leaseValue: _lease.InstanceId,
            queueKey: queueKey,
            proposalKey: proposalKey,
            ticketIds: ticketIds,
            proposalId: match.ProposalId,
            proposalFieldsJson: fieldsJson,
            ttlSeconds: ttl,
            ct: ct).ConfigureAwait(false);

        if (result == AtomicClaimResult.Success)
        {
            // Write the auxiliary fields the proposal-sweeper (Pitfall §10) needs that the
            // Lua script does not write itself:
            //   - "tickets" — comma-separated participant ticket ids (sweeper enumerates).
            //   - "deadlineMs" — Unix ms deadline (sweeper compares vs. _clock to detect
            //     timed-out proposals without depending on the Redis KEY TTL — which deletes
            //     the entire hash and prevents SCAN-based discovery).
            var ticketsCsv = string.Join(",", ticketIds.Select(t => t.ToString()));
            await db.HashSetAsync(
                proposalKey,
                [
                    new HashEntry("tickets", ticketsCsv),
                    new HashEntry("deadlineMs", deadline.ToUnixTimeMilliseconds()),
                ])
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Publishes the per-ticket <c>"proposed"</c> status to <c>mm:status:{ticketId}</c> and
    /// writes a <see cref="TicketEventType.Proposed"/> row into the analytics channel for
    /// every ticket in the formed match.
    /// </summary>
    private async Task PublishProposalEventsAsync(
        MatchResult match, DateTimeOffset now, CancellationToken ct)
    {
        var subscriber = _redis.GetSubscriber();

        foreach (var ticket in match.MatchedTickets)
        {
            ct.ThrowIfCancellationRequested();

            // Payload format is "proposed:{proposalId}" — LongPollStatusHandler.ParseStatusMessage
            // splits on ':' and populates TicketStatusResponse.ProposalId from the suffix. Without
            // the suffix, long-poll subscribers that receive this publish observe ProposalId=null
            // and cannot drive the accept/decline endpoints.
            await subscriber.PublishAsync(
                RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticket.TicketId)),
                $"proposed:{match.ProposalId:D}").ConfigureAwait(false);

            var payloadJson = JsonSerializer.Serialize(
                new ProposalEventPayload(match.ProposalId.ToString()));
            if (!_eventWriter.TryWrite(new TicketEvent
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.TicketId,
                EventType = TicketEventType.Proposed,
                OccurredAt = now,
                Payload = payloadJson,
            }))
            {
                _logger.LogDebug(
                    "MatchmakerTickerService: ticket-event channel full — dropped Proposed event for {TicketId}.",
                    ticket.TicketId);
            }
        }
    }

    /// <summary>
    /// Pulls the ladder id out of a queue key of the form <c>mm:queue:{ladderId}:{poolName}</c>.
    /// Returns <see cref="Guid.Empty"/> defensively if the key is malformed (the OTel tag
    /// then carries the empty Guid which is still a stable identifier).
    /// </summary>
    private static Guid ExtractLadderId(string queueKey)
    {
        // Format: "mm:queue:{ladderId}:{poolName}"
        var parts = queueKey.Split(':');
        if (parts.Length >= 4 && Guid.TryParse(parts[2], out var id))
            return id;
        return Guid.Empty;
    }

    // The proposal-hash payload type used to be a ticker-private record (Deadline + Teams) —
    // it collided in name with the SHARED Services.ProposalFields that ProposalService reads,
    // and the deserialize-as-shared on the read side silently returned defaults (Tickets=[]).
    // The shared type is now serialized directly above; this private record is gone.
    private sealed record ProposalEventPayload(string ProposalId);
}
