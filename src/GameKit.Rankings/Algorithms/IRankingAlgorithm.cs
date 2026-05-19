// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Rankings.Algorithms;

/// <summary>
/// Strategy interface for skill-rating algorithms (RANK-04).
/// Implementations are discovered via Scrutor assembly scanning and registered as
/// <c>IEnumerable&lt;IRankingAlgorithm&gt;</c>. The active algorithm for a ladder is selected
/// by matching <see cref="Name"/> against the ladder's configuration.
/// </summary>
/// <remarks>
/// <b>BATCHED-ONLY contract (Pitfall §1 / RANK-04):</b> there is exactly ONE public method,
/// <see cref="Apply"/>. No per-match overload exists or should be added. Calling Apply with a
/// single-outcome batch instead of accumulating the full rating period is mathematically
/// invalid for Glicko-2: the algorithm converges on the set of opponents seen in a period —
/// updating after each individual match produces different (incorrect) ratings than updating
/// once per period against all opponents simultaneously.
/// <para/>
/// <b>Numerical stability (Pitfall §9):</b> the default <see cref="Glicko2Algorithm"/> uses
/// Glickman's convergence tolerance ε = 0.000001 and has been validated against the published
/// worked example (Glickman, 2012 §3.1). Custom implementations that replace the convergence
/// loop MUST document their tolerance and bound the iteration count. An unbounded convergence
/// loop on adversarial input is a denial-of-service vector.
/// <para/>
/// <b>Determinism requirement:</b> <see cref="Apply"/> must produce identical output for
/// identical input. Non-deterministic algorithms (e.g. relying on wall-clock, PRNG seeded
/// from <c>Environment.TickCount</c>, or parallelism with races) break the ticker's idempotency
/// guarantee and are not supported.
/// </remarks>
public interface IRankingAlgorithm
{
    /// <summary>
    /// Stable discriminator used to match this algorithm to a ladder's configuration.
    /// Use lowercase ASCII, e.g. <c>"glicko2"</c>, <c>"elo"</c>, <c>"trueskill"</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Apply one rating-period batch to the current state and return the updated state.
    /// </summary>
    /// <param name="state">
    /// Immutable snapshot of the current ratings for all players who appear in
    /// <paramref name="batch"/>. Players absent from <paramref name="state"/> receive
    /// algorithm defaults.
    /// </param>
    /// <param name="batch">
    /// The complete set of match outcomes for this rating period. Implementations MUST
    /// process all outcomes together — splitting the batch or calling Apply once per match
    /// produces mathematically invalid results for Glicko-2 (Pitfall §1).
    /// </param>
    /// <returns>
    /// A new <see cref="RankingState"/> with the updated ratings. The input
    /// <paramref name="state"/> is never mutated. Players who appear in
    /// <paramref name="batch"/> but have no outcomes in this period receive updated rating
    /// deviations (Glicko-2 step 6 applies regardless of participation count).
    /// </returns>
    /// <remarks>
    /// <b>Implementations must be deterministic for the same input batch.</b> The default
    /// <see cref="Glicko2Algorithm"/> uses Glickman epsilon = 0.000001 convergence tolerance
    /// and has been validated against the published worked example. Custom implementations
    /// inherit numerical-stability responsibility; this interface imposes no convergence
    /// guarantee — that is the implementer's contract.
    /// <para/>
    /// <b>Thread-safety (WR-12):</b> implementations MUST either be safe for concurrent
    /// invocations (e.g. construct any mutable per-call state inside <see cref="Apply"/> and
    /// hold no instance fields that change across calls), or document their concurrency model
    /// in their XML doc. The default registration is a singleton; the ticker calls
    /// <see cref="Apply"/> single-threaded inside a per-ladder lease, but consumers writing
    /// alternate harnesses (tests, future fan-out paths) may invoke a singleton from multiple
    /// threads. <see cref="Glicko2Algorithm"/> satisfies the safe-by-construction discipline:
    /// every <see cref="Apply"/> call builds a fresh <c>RatingCalculator</c> and never mutates
    /// shared state.
    /// </remarks>
    RankingState Apply(RankingState state, RankingBatch batch);
}
