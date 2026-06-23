// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;

namespace Platformer3D.Strategy;

/// <summary>
/// Custom <see cref="IMatchmakingStrategy"/> for the Platformer3D demo ladder (D-06/D-07/D-08).
/// Pairs players whose <see cref="QueuedParty.AggregateRating"/> values are within a window that
/// widens linearly with queue time. Fresh guests with no rating history get a neutral wide bracket
/// so they always find a match quickly (D-08 cold-start).
/// </summary>
/// <remarks>
/// <para>
/// <b>Name discriminator (D-07):</b> <see cref="Name"/> is <c>"best-time"</c> (not <c>"elo-range"</c>).
/// Registered via <c>services.Replace(...)</c> after <c>AddMatchmaking()</c> so the single
/// <see cref="IMatchmakingStrategy"/> resolved by <c>MatchmakerTickerService</c> is this class (A3).
/// </para>
/// <para>
/// <b>Bracket formula (D-06):</b>
/// <c>bracket(t) = min(InitialBracketMs + (MaxBracketMs − InitialBracketMs) · t / RampSeconds, MaxBracketMs)</c>
/// where <c>t = (now − QueuedAt).TotalSeconds</c>. The bracket half-width is in the same units as
/// <see cref="QueuedParty.AggregateRating"/> — a linear ramp from 5,000 to 30,000 over 60 seconds.
/// </para>
/// <para>
/// <b>Cold-start (D-08):</b> a party whose every member has
/// <see cref="QueuedPartyMember.RatingDeviation"/> ≥ <see cref="ColdStartRdThreshold"/> is treated
/// as unrated. It receives a flat <see cref="NeutralBracketMs"/> (60,000) bracket so it can be
/// matched by any pool entry whose own bracket contains the rating difference.
/// </para>
/// <para>
/// <b>Symmetric conjunctive overlap:</b> tickets A (rating <c>rA</c>, bracket <c>bA</c>) and B
/// (rating <c>rB</c>, bracket <c>bB</c>) match if <c>|rA − rB| ≤ bA AND |rA − rB| ≤ bB</c>.
/// Both brackets must contain the difference — preventing a wide-bracket cold-start ticket from
/// pulling in a tight-bracket opponent who hasn't waited long.
/// </para>
/// <para>
/// <b>Stateless + thread-safe (IMatchmakingStrategy contract):</b> the only instance field is the
/// readonly <c>_ladders</c> list injected at construction. All per-call state is built inside
/// <see cref="Match"/> — safe for concurrent singleton invocations (T-21-05).
/// </para>
/// </remarks>
public sealed class BestTimeMatchmakingStrategy : IMatchmakingStrategy
{
    // ─── Cold-start constants (D-08) ─────────────────────────────────────────

    /// <summary>
    /// RatingDeviation threshold above which a player is considered unrated (cold-start).
    /// Default Glicko-2 RD for a new player is 350; this threshold catches all fresh guests.
    /// </summary>
    public const double ColdStartRdThreshold = 300.0;

    /// <summary>Flat bracket width (in AggregateRating units) for cold-start parties (D-08).</summary>
    public const double NeutralBracketMs = 60_000.0;

    // ─── Bracket ramp constants (D-06) ───────────────────────────────────────

    /// <summary>Starting bracket half-width at t=0 (before any waiting).</summary>
    public const double InitialBracketMs = 5_000.0;

    /// <summary>Maximum bracket half-width after full ramp.</summary>
    public const double MaxBracketMs = 30_000.0;

    /// <summary>Seconds over which the bracket ramps from <see cref="InitialBracketMs"/> to <see cref="MaxBracketMs"/>.</summary>
    public const double RampSeconds = 60.0;

    // ─── Instance state (immutable) ──────────────────────────────────────────

    private readonly IReadOnlyList<MatchmakingLadderConfig> _ladders;
    // No other instance fields — stateless (T-21-05 / IMatchmakingStrategy contract)

    /// <summary>Constructs the strategy.</summary>
    /// <param name="ladders">All ladders registered via <c>AddLadder(...)</c>.</param>
    public BestTimeMatchmakingStrategy(IReadOnlyList<MatchmakingLadderConfig> ladders)
    {
        ArgumentNullException.ThrowIfNull(ladders);
        _ladders = ladders;
    }

    /// <inheritdoc />
    public string Name => "best-time";   // D-07: Name != "elo-range"

    /// <inheritdoc />
    /// <remarks>
    /// All per-call state built here — no mutable instance fields (IMatchmakingStrategy contract).
    /// </remarks>
    public MatchResult? Match(QueuedParty candidate, IReadOnlyList<QueuedParty> pool, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(pool);

        // Locate per-ladder config (defensive: return null if no matching ladder found).
        var cfg = FindLadderConfig(candidate);
        if (cfg is null)
            return null;

        // Compute candidate's bracket (or neutral if cold-start).
        var candidateQueueSeconds = (now - candidate.QueuedAt).TotalSeconds;
        var candidateBracket = BracketMs(candidate, candidateQueueSeconds);

        for (var idx = 0; idx < pool.Count; idx++)
        {
            var p = pool[idx];

            // A ticket cannot match itself.
            if (p.TicketId == candidate.TicketId)
                continue;

            // Verify the pool entry has a config (defensive; skip invalid entries).
            var pCfg = FindLadderConfig(p);
            if (pCfg is null)
                continue;

            // Compute pool entry's bracket.
            var poolQueueSeconds = (now - p.QueuedAt).TotalSeconds;
            var poolBracket = BracketMs(p, poolQueueSeconds);

            // Symmetric conjunctive overlap (D-06): both brackets must contain the difference.
            // Cold-start exception (D-08): when the CANDIDATE is cold-start (neutral bracket),
            // it "matches anyone" — only the candidate's neutral bracket must contain the diff.
            // When the POOL ENTRY is cold-start, the pool entry's neutral bracket also contributes.
            var diff = Math.Abs(candidate.AggregateRating - p.AggregateRating);
            var candidateIsCold = IsColdStart(candidate);
            if (diff <= candidateBracket && (candidateIsCold || diff <= poolBracket))
            {
                return BuildMatchResult(candidate, p);
            }
        }

        return null;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the ladder config that matches the party's pool, or the only ladder in a
    /// single-ladder list. Mirrors EloRangeMatchmakingStrategy.FindLadderConfig convention.
    /// </summary>
    private MatchmakingLadderConfig? FindLadderConfig(QueuedParty party)
    {
        foreach (var cfg in _ladders)
        {
            if (_ladders.Count == 1)
                return cfg;
            if (string.Equals(cfg.Name, party.PoolName, StringComparison.OrdinalIgnoreCase))
                return cfg;
        }
        return null;
    }

    /// <summary>
    /// Computes the bracket half-width for <paramref name="party"/> at <paramref name="secondsInQueue"/>.
    /// Cold-start parties (all members RD ≥ <see cref="ColdStartRdThreshold"/>) always get
    /// <see cref="NeutralBracketMs"/>; others get a linear ramp from <see cref="InitialBracketMs"/>
    /// to <see cref="MaxBracketMs"/> over <see cref="RampSeconds"/>.
    /// </summary>
    private static double BracketMs(QueuedParty party, double secondsInQueue)
    {
        if (IsColdStart(party))
            return NeutralBracketMs;

        // Linear ramp: clamp t to [0, RampSeconds] then interpolate.
        var t = Math.Max(0.0, secondsInQueue);
        var raw = InitialBracketMs + (MaxBracketMs - InitialBracketMs) * t / RampSeconds;
        return Math.Min(raw, MaxBracketMs);
    }

    /// <summary>
    /// Returns <see langword="true"/> when every member of <paramref name="party"/> has
    /// <see cref="QueuedPartyMember.RatingDeviation"/> ≥ <see cref="ColdStartRdThreshold"/>
    /// (D-08 cold-start detection). An empty member list is treated as NOT cold-start (defensive).
    /// </summary>
    private static bool IsColdStart(QueuedParty party)
    {
        if (party.Members.Count == 0)
            return false;
        foreach (var m in party.Members)
        {
            if (m.RatingDeviation < ColdStartRdThreshold)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Builds the <see cref="MatchResult"/> for a successful match between two parties.
    /// Team assignment is random (CSPRNG) — the one acceptable source of non-determinism
    /// per the <see cref="IMatchmakingStrategy"/> contract. Copied verbatim from
    /// <c>EloRangeMatchmakingStrategy.BuildMatchResult</c>.
    /// </summary>
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
            var team = RandomNumberGenerator.GetInt32(0, 2);
            teamAssignments[pid] = team;
        }

        return new MatchResult(
            ProposalId: Guid.NewGuid(),
            MatchedTickets: new[] { a, b },
            TeamAssignments: teamAssignments);
    }
}
