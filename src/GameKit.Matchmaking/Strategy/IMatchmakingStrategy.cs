// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;

namespace GameKit.Matchmaking.Strategy;

/// <summary>
/// Strategy interface for matchmaking algorithms (MATCH-09). Implementations are
/// discovered via Scrutor assembly scanning and registered as singletons; the matchmaker
/// ticker (Plan 05-05) resolves them by <see cref="Name"/> against the per-ladder
/// configuration. Mirrors the <c>IRankingAlgorithm</c> pattern from Phase 4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Statelessness + thread-safety (MANDATORY):</b> implementations MUST be stateless
/// and safe to invoke from multiple threads concurrently. The matchmaker ticker is
/// single-threaded per pool, but the same singleton may be invoked from leader-election
/// races, the reconciler sweep, and tests at the same time. Hold no mutable instance
/// fields; build any per-call state inside <see cref="Match"/>.
/// </para>
/// <para>
/// <b>Determinism:</b> <see cref="Match"/> must produce the same result for the same
/// inputs (candidate, pool snapshot, <c>now</c>). The one acceptable source of
/// non-determinism is the random team-assignment in the produced <see cref="MatchResult"/>,
/// which is sourced from <see cref="System.Security.Cryptography.RandomNumberGenerator"/>.
/// Bracket-overlap and pool-selection must be deterministic.
/// </para>
/// <para>
/// <b>Caller responsibilities:</b> the ticker is responsible for passing a consistent
/// <c>now</c> snapshot for an entire tick — per-candidate clock skew within a single
/// tick is a correctness bug (RESEARCH §Decision 4). The ticker is also responsible
/// for re-queuing the candidate if <see cref="Match"/> returns <see langword="null"/>.
/// </para>
/// </remarks>
public interface IMatchmakingStrategy
{
    /// <summary>
    /// Stable discriminator used to select this strategy for a ladder. Default
    /// implementation: <c>"elo-range"</c> (<see cref="EloRangeMatchmakingStrategy"/>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Try to form a match for <paramref name="candidate"/> from <paramref name="pool"/>.
    /// </summary>
    /// <param name="candidate">The party currently being considered. The strategy decides whether to match it now or wait.</param>
    /// <param name="pool">
    /// All other parties currently queued in the same pool (same ladder + pool name).
    /// May be empty. Order is caller-controlled but conventionally oldest-waiter-first
    /// (Pitfall §6 — Unix millisecond sorted-set score).
    /// </param>
    /// <param name="now">
    /// UTC snapshot of the current tick. Used to compute bracket flex
    /// (<c>t = (now - queuedAt).TotalSeconds</c>). The ticker MUST pass the same value to
    /// every <see cref="Match"/> call inside a single tick.
    /// </param>
    /// <returns>
    /// A <see cref="MatchResult"/> on success (proposal id + matched tickets + team
    /// assignments), or <see langword="null"/> if no match can be formed in this tick.
    /// </returns>
    /// <remarks>
    /// Implementations MUST be deterministic for the bracket-overlap + pool-selection
    /// logic. Random team assignment via <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
    /// is the one acceptable source of per-call variance. See class-level XML doc for the
    /// full thread-safety contract.
    /// </remarks>
    MatchResult? Match(QueuedParty candidate, IReadOnlyList<QueuedParty> pool, DateTimeOffset now);
}
