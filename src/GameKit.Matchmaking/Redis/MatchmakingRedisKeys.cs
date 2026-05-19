// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Redis;

/// <summary>
/// Centralised Redis key constants + formatters for <c>GameKit.Matchmaking</c>. Every Redis
/// key written by the package flows through this class so that key layout changes are made in
/// exactly one place. Mirrors PATTERNS.md §Redis Key Constants.
/// </summary>
/// <remarks>
/// <para>
/// Key namespaces:
/// <list type="bullet">
///   <item><c>mm:queue:{ladderId}:{pool}</c> — per-pool sorted set of queued tickets (RESEARCH §Decision 1).</item>
///   <item><c>mm:ticket:{id}</c> — per-ticket hash (status + party id + queuedAt).</item>
///   <item><c>mm:proposal:{id}</c> — per-proposal hash (TTL = AcceptTimeoutSeconds + grace).</item>
///   <item><c>mm:proposal:{id}:accepts</c> — per-proposal "accept tally" subkey (set of player ids who accepted).</item>
///   <item><c>mm:status:{ticketId}</c> — per-ticket pub/sub channel for long-poll status (RESEARCH §Decision 9).</item>
///   <item><c>gamekit:matchmaking:matcher:lock</c> — matchmaker leader-election lock (RESEARCH §Decision 11 + CONTEXT §Reusable Assets).</item>
///   <item><c>mm:control:paused</c> — admin pause-queue flag (CONTEXT §Domain).</item>
///   <item><c>mm:control:drain</c> — admin drain-queue flag (CONTEXT §Domain).</item>
/// </list>
/// </para>
/// <para>
/// <see cref="MatcherLock"/> is also the default value of
/// <see cref="GameKitMatchmakingTickerOptions.LockKey"/>; operators overriding the option
/// MUST update both surfaces consistently.
/// </para>
/// </remarks>
public static class MatchmakingRedisKeys
{
    /// <summary>
    /// Matchmaker leader-election lock key (per RESEARCH §Decision 11 / PATTERNS line 112).
    /// </summary>
    public const string MatcherLock = "gamekit:matchmaking:matcher:lock";

    /// <summary>
    /// Admin "pause queue" flag — when present (any value), the matchmaker skips its tick.
    /// </summary>
    public const string ControlPaused = "mm:control:paused";

    /// <summary>
    /// Admin "drain queue" flag — when present, the matchmaker continues to accept proposals
    /// but stops admitting new tickets.
    /// </summary>
    public const string ControlDrain = "mm:control:drain";

    /// <summary>
    /// Subkey suffix appended to a proposal's hash key to record which players have accepted.
    /// Full key is <c>mm:proposal:{id}:accepts</c>.
    /// </summary>
    public const string ProposalAcceptsSuffix = ":accepts";

    /// <summary>Per-pool queued-ticket sorted set key.</summary>
    /// <param name="ladderId">The ladder identifier.</param>
    /// <param name="pool">The pool name (operator-defined; required non-empty).</param>
    /// <returns>The fully-qualified Redis key.</returns>
    public static string Queue(Guid ladderId, string pool) => $"mm:queue:{ladderId}:{pool}";

    /// <summary>Per-ticket hash key.</summary>
    /// <param name="ticketId">The ticket identifier.</param>
    /// <returns>The fully-qualified Redis key.</returns>
    public static string Ticket(Guid ticketId) => $"mm:ticket:{ticketId}";

    /// <summary>Per-proposal hash key.</summary>
    /// <param name="proposalId">The proposal identifier.</param>
    /// <returns>The fully-qualified Redis key.</returns>
    public static string Proposal(Guid proposalId) => $"mm:proposal:{proposalId}";

    /// <summary>Per-proposal accept-tally subkey (set of <c>player:{playerId}</c> entries).</summary>
    /// <param name="proposalId">The proposal identifier.</param>
    /// <returns>The fully-qualified Redis key.</returns>
    public static string ProposalAccepts(Guid proposalId) => $"mm:proposal:{proposalId}{ProposalAcceptsSuffix}";

    /// <summary>Per-ticket long-poll status pub/sub channel.</summary>
    /// <param name="ticketId">The ticket identifier.</param>
    /// <returns>The fully-qualified Redis channel name.</returns>
    public static string StatusChannel(Guid ticketId) => $"mm:status:{ticketId}";
}
