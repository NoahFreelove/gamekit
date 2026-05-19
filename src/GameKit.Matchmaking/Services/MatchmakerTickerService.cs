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
    /// <remarks>
    /// PERF (SC#3): a per-pool wall-clock budget terminates the candidate loop early so a
    /// single tick never exceeds <see cref="GameKitMatchmakingTickerOptions.MaxIterationBudgetMs"/>.
    /// Remaining candidates are picked up on the next tick — partial drain is fine because
    /// the ticker runs at 500 ms cadence and ZRANGEBYSCORE Ascending returns the oldest
    /// waiters first regardless of which tick observes them.
    /// </remarks>
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

        // Per-tick budget for candidate materialisation. At N=1000 the previous N=200 take
        // combined with sequential HGETALL fan-out blew the 50ms budget (~200ms+ on
        // localhost Docker Redis). We cap candidates per tick AND pipeline the per-ticket
        // HGETALLs so the cost is O(1) round-trip-batch per pool rather than O(N).
        //
        // 16 is the SC#3 calibration target: at 500ms tick interval the matcher can drain
        // up to 32 tickets per second (16 pairs/tick × 2 ticks/sec) — well above the SC#3
        // 100 matches/minute floor. Combined with MaxMatchesPerTick=6, this caps the tick
        // budget at ~30 ms steady-state (6 × ~5 ms per Lua claim).
        const int CandidatesPerTick = 16;

        foreach (var queueKey in server.Keys(pattern: poolGlob, pageSize: 100))
        {
            ct.ThrowIfCancellationRequested();

            using var poolActivity = MatchmakingActivitySource.StartPoolActivity(
                ExtractLadderId(queueKey.ToString()), poolName);

            // Pull up to CandidatesPerTick candidate ticket ids (Unix-ms scored, oldest first).
            var entries = await db.SortedSetRangeByScoreAsync(
                queueKey, double.NegativeInfinity, double.PositiveInfinity,
                Exclude.None, Order.Ascending, 0, take: CandidatesPerTick).ConfigureAwait(false);

            poolActivity?.SetTag("candidatesEvaluated", entries.Length);

            if (entries.Length < 2)
            {
                // Need at least two queued parties for a match.
                continue;
            }

            var phaseSw = System.Diagnostics.Stopwatch.StartNew();

            // Materialise candidates by pipelining HGETALL via StackExchange.Redis' Task
            // multiplexing — the multiplexer batches concurrent commands onto a single
            // round-trip when called without awaiting between each issue. Issuing first,
            // awaiting all at once yields ~1 round-trip cost rather than N.
            var hashTasks = new List<(Guid Tid, Task<HashEntry[]> Task)>(entries.Length);
            foreach (var entry in entries)
            {
                if (!Guid.TryParse(entry.ToString(), out var tid))
                    continue;
                hashTasks.Add((tid, db.HashGetAllAsync(MatchmakingRedisKeys.Ticket(tid))));
            }
            // Force the pipeline flush + collect.
            await Task.WhenAll(hashTasks.Select(t => t.Task)).ConfigureAwait(false);

            var candidates = new List<QueuedParty>(hashTasks.Count);
            foreach (var (tid, task) in hashTasks)
            {
                var hash = task.Result;
                var qp = BuildQueuedPartyFromHash(tid, hash);
                if (qp is not null)
                    candidates.Add(qp);
            }
            var hashFanoutMs = phaseSw.ElapsedMilliseconds;
            poolActivity?.SetTag("phase.hashFanoutMs", hashFanoutMs);

            if (candidates.Count < 2)
                continue;

            // Iterate candidates oldest-first; for each, the rest are the pool.
            // Track ids already claimed inside this loop to avoid trying to re-match a ticket
            // we just removed from the queue.
            //
            // PERF (SC#3): the previous implementation rebuilt a fresh `pool` list of N-1
            // entries every iteration AND the strategy resorted it on each Match() call —
            // O(N²) list construction + O(N² log N) sort cost. Since candidates is already
            // ordered oldest-first (ZRANGEBYSCORE Ascending), we pass a filtered view that
            // skips claimed entries inline. We also pre-allocate the reusable scratch list
            // outside the loop to avoid GC churn at 200+ candidates per tick.
            var claimed = new HashSet<Guid>();
            var matchedInPoolCount = 0;
            var poolScratch = new List<QueuedParty>(candidates.Count);

            // Per-pool budget: stop forming more matches when the configured per-iteration
            // budget has been spent. Partial drain is fine — the next tick continues.
            var budgetSw = System.Diagnostics.Stopwatch.StartNew();
            var budgetMs = _opts.Ticker.MaxIterationBudgetMs;

            // SC#3 throughput cap: each match incurs one synchronous Lua claim + one HSET +
            // pipelined publishes (~5ms total on local Docker Redis). Capping matches per
            // tick keeps the iteration budget bounded; at 6 matches × 2 ticks/sec = 12/sec
            // we drain the SC#3 1k-concurrent design depth in ~85s — well within the
            // 10-minute sustain window AND faster than the SC#3 100 matches/minute floor.
            // 6 chosen (down from 8) to leave headroom for budgetSw drift + budget margin.
            const int MaxMatchesPerTick = 6;

            for (var i = 0; i < candidates.Count; i++)
            {
                // Stop early when we've consumed the per-tick budget OR hit the per-tick
                // match cap. The remaining oldest-first candidates will be re-discovered
                // next tick (~500 ms later).
                if (budgetSw.ElapsedMilliseconds >= budgetMs)
                {
                    poolActivity?.SetTag("budgetBail", true);
                    break;
                }
                if (matchedInPoolCount >= MaxMatchesPerTick)
                {
                    poolActivity?.SetTag("matchCapBail", true);
                    break;
                }

                var candidate = candidates[i];
                if (claimed.Contains(candidate.TicketId))
                    continue;

                // Reuse the scratch list — keeps allocations to one per pool sweep, not per
                // candidate. Cleared rather than reallocated; the underlying array survives.
                poolScratch.Clear();
                for (var j = 0; j < candidates.Count; j++)
                {
                    if (i == j) continue;
                    if (claimed.Contains(candidates[j].TicketId)) continue;
                    poolScratch.Add(candidates[j]);
                }
                if (poolScratch.Count == 0)
                    continue;

                var match = _strategy.Match(candidate, poolScratch, now);
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
            poolActivity?.SetTag("phase.matchLoopMs", budgetSw.ElapsedMilliseconds);
            poolActivity?.SetTag("phase.totalMs", phaseSw.ElapsedMilliseconds);
        }

        return anyMatchedInPool ? MatcherTickResult.Matched : MatcherTickResult.NoMatch;
    }

    /// <summary>
    /// Builds a <see cref="QueuedParty"/> from an already-fetched ticket hash. Returns
    /// <see langword="null"/> when the hash is missing required fields (defensive).
    /// </summary>
    /// <remarks>
    /// PERF (SC#3): split out from the original <c>BuildQueuedPartyAsync</c> so the per-tick
    /// fan-out path can issue HGETALLs concurrently via the StackExchange.Redis multiplexer
    /// and resolve them in a single batched round-trip. The previous sequential per-ticket
    /// HGETALL pattern was the dominant cost in the iteration budget.
    /// </remarks>
    private QueuedParty? BuildQueuedPartyFromHash(Guid ticketId, HashEntry[] hash)
    {
        if (hash.Length == 0)
            return null;

        // Avoid the dictionary allocation + ToString twice per entry — walk the array once
        // and pick out the fields we care about. At 64 candidates per tick the per-call
        // overhead matters for the 50ms budget.
        Guid? ladderId = null;
        string? poolName = null;
        long queuedAtMs = 0; bool haveQueuedAt = false;
        double aggregateRating = 0; bool haveRating = false;
        Guid? partyId = null;
        string? membersJson = null;

        foreach (var e in hash)
        {
            var name = e.Name.ToString();
            switch (name)
            {
                case "ladderId":
                    if (Guid.TryParse(e.Value.ToString(), out var lid)) ladderId = lid;
                    break;
                case "poolName":
                    poolName = e.Value.ToString();
                    break;
                case "queuedAt":
                    if (long.TryParse(e.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var q))
                    {
                        queuedAtMs = q; haveQueuedAt = true;
                    }
                    break;
                case "aggregateRating":
                    if (double.TryParse(e.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                    {
                        aggregateRating = r; haveRating = true;
                    }
                    break;
                case "partyId":
                    if (Guid.TryParse(e.Value.ToString(), out var pid)) partyId = pid;
                    break;
                case "members":
                    membersJson = e.Value.ToString();
                    break;
            }
        }

        if (ladderId is null || poolName is null || !haveQueuedAt || !haveRating)
            return null;

        IReadOnlyList<QueuedPartyMember> members = Array.Empty<QueuedPartyMember>();
        if (!string.IsNullOrEmpty(membersJson))
        {
            try
            {
                members = JsonSerializer.Deserialize<List<QueuedPartyMember>>(membersJson)
                    ?? (IReadOnlyList<QueuedPartyMember>)Array.Empty<QueuedPartyMember>();
            }
            catch (JsonException)
            {
                // Malformed JSON — silently skip members for this ticket. Loud logging on
                // the hot path would blow the budget under load.
            }
        }

        return new QueuedParty(
            TicketId: ticketId,
            PartyId: partyId,
            LadderId: ladderId.Value,
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

        // PERF (SC#3): pipeline the per-ticket PUBLISH commands. Each PUBLISH is a
        // network round-trip; awaiting in a loop blew the per-tick budget when many
        // matches formed in a single tick. Issuing without await + WhenAll-batch
        // collapses N round-trips into one.
        var payloadJson = JsonSerializer.Serialize(
            new ProposalEventPayload(match.ProposalId.ToString()));
        var publishMessage = $"proposed:{match.ProposalId:D}";
        var publishTasks = new List<Task>(match.MatchedTickets.Count);

        foreach (var ticket in match.MatchedTickets)
        {
            ct.ThrowIfCancellationRequested();

            // Payload format is "proposed:{proposalId}" — LongPollStatusHandler.ParseStatusMessage
            // splits on ':' and populates TicketStatusResponse.ProposalId from the suffix.
            publishTasks.Add(subscriber.PublishAsync(
                RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticket.TicketId)),
                publishMessage));

            _eventWriter.TryWrite(new TicketEvent
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.TicketId,
                EventType = TicketEventType.Proposed,
                OccurredAt = now,
                Payload = payloadJson,
            });
        }

        await Task.WhenAll(publishTasks).ConfigureAwait(false);
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
