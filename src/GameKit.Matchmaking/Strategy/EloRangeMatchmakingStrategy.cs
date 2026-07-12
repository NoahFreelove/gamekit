// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using GameKit.Core.Services;
using GameKit.Matchmaking.Builder;

namespace GameKit.Matchmaking.Strategy;

/// <summary>
/// Default <see cref="IMatchmakingStrategy"/> implementation (CONTEXT D-11; MATCH-09 + MATCH-10).
/// Implements the linear bracket-flex curve from RESEARCH §Decision 4 and the symmetric-overlap
/// match rule. Picks the oldest waiter in the pool first (Pitfall §6 — sorted-set order).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bracket formula (RESEARCH §Decision 4):</b>
/// <c>bracket(t) = min(BracketStart + (BracketEnd − BracketStart) · t / BracketRampSeconds, BracketEnd)</c>
/// where <c>t = (now − queuedAt).TotalSeconds</c>. Computed independently for the
/// candidate and each pool entry.
/// </para>
/// <para>
/// <b>Symmetric-overlap rule (RESEARCH §Decision 4):</b> tickets A (rating <c>rA</c>, bracket <c>bA</c>)
/// and B (rating <c>rB</c>, bracket <c>bB</c>) match if <c>|rA − rB| ≤ bA AND |rA − rB| ≤ bB</c>.
/// Both brackets must contain the rating difference — conjunctive, not disjunctive. This
/// prevents a low-rated ticket with a very wide bracket from pulling in a high-rated
/// ticket whose own bracket hasn't widened yet.
/// </para>
/// <para>
/// <b>Stateless + thread-safe:</b> all per-call state is built inside <see cref="Match"/>;
/// the per-ladder configuration list is injected as an <see cref="IReadOnlyList{T}"/>
/// (immutable from the strategy's perspective). Random team assignment uses
/// <see cref="RandomNumberGenerator"/> (CSPRNG) — safe from multiple threads.
/// </para>
/// </remarks>
public sealed class EloRangeMatchmakingStrategy : IMatchmakingStrategy
{
    private readonly IReadOnlyList<MatchmakingLadderConfig> _ladders;
    private readonly PartyRatingAggregatorService _aggregator;
    private readonly IClock _clock;

    /// <summary>Constructs the strategy.</summary>
    /// <param name="ladders">All ladders registered via <c>AddLadder(...)</c>.</param>
    /// <param name="aggregator">Aggregator service (kept for parity / future spread-cap checks).</param>
    /// <param name="clock">Clock abstraction. Not consulted inside <see cref="Match"/> — caller passes <c>now</c>. Held for symmetry with strategies that may want sub-second time.</param>
    public EloRangeMatchmakingStrategy(
        IReadOnlyList<MatchmakingLadderConfig> ladders,
        PartyRatingAggregatorService aggregator,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ladders);
        ArgumentNullException.ThrowIfNull(aggregator);
        ArgumentNullException.ThrowIfNull(clock);
        _ladders = ladders;
        _aggregator = aggregator;
        _clock = clock;
    }

    /// <inheritdoc />
    public string Name => "elo-range";

    /// <inheritdoc />
    public MatchResult? Match(QueuedParty candidate, IReadOnlyList<QueuedParty> pool, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(pool);

        // Locate the per-ladder config (case-insensitive name match is handled at registration;
        // here we match on LadderId which is the JOIN KEY against the Rankings ladder. The
        // strategy resolves the matching config by index lookup — for v1 we look up by Name
        // when the ticker passes the QueuedParty.LadderId-bound config; for simplicity we
        // currently scan the list. With a small number of ladders this is acceptable; if a
        // future operator registers >10 ladders, switch to a Dictionary in the ticker layer.
        //
        // Defensive: the ticker is responsible for ensuring candidate.LadderId corresponds to
        // a registered ladder. If we cannot find a config, refuse to match.
        var cfg = FindLadderConfig(candidate);
        if (cfg is null)
            return null;

        // Defense-in-depth: enforce MaxPartyRatingSpread cap. The enqueue path (Plan 05-08)
        // already validates this and returns 400 PartyRatingSpreadExceeded; we double-check
        // so a misconfigured caller (e.g. a test that bypasses the endpoint) cannot smuggle
        // a wide-spread party through the strategy. CONTEXT D-14.
        if (cfg.MaxPartyRatingSpread is int cap && cap > 0 && PartySpread(candidate) > cap)
            return null;

        var candidateElapsed = (now - candidate.QueuedAt).TotalSeconds;
        // MATCH-17: suppress bracket expansion when pool is below minimum depth.
        // pool is candidate-exclusive (ticker strips the candidate before calling Match),
        // so pool.Count is the exact count of OTHER parties — no adjustment needed.
        if (cfg.MinPoolDepthBeforeBracketExpansion.HasValue
            && pool.Count < cfg.MinPoolDepthBeforeBracketExpansion.Value)
        {
            candidateElapsed = 0; // force bracket to BracketStart
        }
        var candidateBracket = Bracket(cfg, candidateElapsed);

        // PERF (SC#3): caller (MatchmakerTickerService) sources pool from ZRANGEBYSCORE
        // Ascending → already oldest-first. The previous defensive `.OrderBy(...)` on every
        // Match() invocation cost O(N log N) per candidate × N candidates = O(N² log N)
        // overhead on the 50ms hot path. We assume the documented contract (caller passes
        // oldest-first) and iterate directly. Unit tests pass pre-sorted lists too.
        for (var idx = 0; idx < pool.Count; idx++)
        {
            var p = pool[idx];

            // A ticket cannot match itself.
            if (p.TicketId == candidate.TicketId)
                continue;

            // Defense-in-depth: same spread check for the pool entry.
            var pCfg = FindLadderConfig(p);
            if (pCfg is null)
                continue;
            if (pCfg.MaxPartyRatingSpread is int pcap && pcap > 0 && PartySpread(p) > pcap)
                continue;

            var poolElapsed = (now - p.QueuedAt).TotalSeconds;
            // MATCH-17: same depth guard for the pool entry's bracket.
            // pool is candidate-exclusive — pool.Count is the exact count of OTHER parties.
            if (pCfg.MinPoolDepthBeforeBracketExpansion.HasValue
                && pool.Count < pCfg.MinPoolDepthBeforeBracketExpansion.Value)
            {
                poolElapsed = 0;
            }
            var poolBracket = Bracket(pCfg, poolElapsed);
            var diff = Math.Abs(candidate.AggregateRating - p.AggregateRating);

            // Symmetric (conjunctive) overlap — BOTH brackets must contain the difference
            // (RESEARCH §Decision 4).
            if (diff <= candidateBracket && diff <= poolBracket)
            {
                return BuildMatchResult(candidate, p);
            }
        }

        return null;
    }

    private MatchmakingLadderConfig? FindLadderConfig(QueuedParty party)
    {
        // v1 maps party.LadderId via the ticker layer; the strategy walks the registered list.
        // The candidate's pool name additionally narrows the config but is not consulted here —
        // ladder + pool partitioning is the ticker's responsibility.
        // For v1 we ALSO accept name-based lookup so tests can pass a ladder list whose Name
        // matches PoolName; the production ticker passes a list whose Name == ladder.Name.
        foreach (var cfg in _ladders)
        {
            // The strategy does not know the ladder Guid → Name mapping (Rankings owns the
            // Guid; Matchmaking is keyed by Name). In v1 the ticker wires the candidate's
            // PoolName to the ladder name; tests pass single-ladder lists. The convention:
            // for v1 ALWAYS return the first matching ladder by name — the ticker is
            // responsible for funnelling each pool to its correct strategy invocation.
            // For symmetric tests we accept any single-ladder list as the candidate's match.
            if (_ladders.Count == 1)
                return cfg;
            // Multi-ladder: match by pool name (the convention the ticker layer uses).
            if (string.Equals(cfg.Name, party.PoolName, StringComparison.OrdinalIgnoreCase))
                return cfg;
        }
        return null;
    }

    private static double PartySpread(QueuedParty p)
    {
        if (p.Members.Count == 0)
            return 0;
        double min = double.MaxValue, max = double.MinValue;
        foreach (var m in p.Members)
        {
            if (m.Rating < min) min = m.Rating;
            if (m.Rating > max) max = m.Rating;
        }
        return max - min;
    }

    /// <summary>
    /// Linear bracket-flex formula (RESEARCH §Decision 4). Public for unit-testability;
    /// production callers go through <see cref="Match"/>. Returns a <see cref="double"/> —
    /// the formula yields fractional rating-point bracket half-widths for sub-second t
    /// values; rounding for display is the caller's concern.
    /// </summary>
    /// <param name="cfg">Per-ladder bracket configuration.</param>
    /// <param name="secondsInQueue"><c>t = (now − queuedAt).TotalSeconds</c>.</param>
    /// <returns>The bracket half-width in rating points at time <paramref name="secondsInQueue"/>.</returns>
    public static double Bracket(MatchmakingLadderConfig cfg, double secondsInQueue)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        // Defensive: negative time means the ticket was queued in the future — clamp to zero.
        if (secondsInQueue < 0)
            secondsInQueue = 0;
        var raw = cfg.BracketStart + (cfg.BracketEnd - cfg.BracketStart) * secondsInQueue / cfg.BracketRampSeconds;
        var capped = Math.Min(raw, cfg.BracketEnd);
        // MATCH-17: hard cap — never exceed MaxBracketWidth regardless of wait time.
        if (cfg.MaxBracketWidth.HasValue)
            capped = Math.Min(capped, cfg.MaxBracketWidth.Value);
        return capped;
    }

    private static MatchResult BuildMatchResult(QueuedParty a, QueuedParty b)
    {
        var allMembers = new List<Guid>(a.Members.Count + b.Members.Count);
        foreach (var m in a.Members) allMembers.Add(m.PlayerId);
        foreach (var m in b.Members) allMembers.Add(m.PlayerId);

        // Random team assignment via CSPRNG (v1: 2 teams, random 0/1 split). MMR-balanced
        // split is deferred to a future phase (CONTEXT §Phase Boundary).
        var teamAssignments = new Dictionary<Guid, int>(allMembers.Count);
        foreach (var pid in allMembers)
        {
            // Use CSPRNG to pick 0 or 1 for each player. NOT cryptographically meaningful
            // — we just want non-deterministic team assignment without seeding System.Random.
            var team = RandomNumberGenerator.GetInt32(0, 2);
            teamAssignments[pid] = team;
        }

        return new MatchResult(
            ProposalId: Guid.NewGuid(),
            MatchedTickets: new[] { a, b },
            TeamAssignments: teamAssignments);
    }
}
