// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Deliberate test seam for the Plan 05-09 chaos integration test (SC#2 phase gate). The
/// production default registration is <see cref="NullChaosInterceptor"/> — both methods return
/// <see cref="Task.CompletedTask"/>. The Matchmaking test harness replaces this binding with an
/// <c>AbortingChaosInterceptor</c> that throws <see cref="System.OperationCanceledException"/> at
/// configured probe points to simulate a process crash mid-match.
/// </summary>
/// <remarks>
/// <para>
/// <b>NOT a production fault-injection framework.</b> The interceptor exists exclusively so the
/// in-process chaos test (RESEARCH §Decision 14) can verify the system recovers from a crash
/// between match-formation and proposal writeback without spawning a child process. Production
/// deployments register the no-op <see cref="NullChaosInterceptor"/> via <c>TryAddSingleton</c>,
/// so an explicit operator override is required to swap in any other implementation. Future
/// maintainers MUST NOT refactor this seam away — it has no production callers, but the chaos
/// test cannot replicate the in-process abort semantics without it.
/// </para>
/// <para>
/// <b>Probe call sites:</b>
/// <list type="bullet">
///   <item>
///     <see cref="BeforeLuaClaim"/> is called by <c>MatchmakerTickerService.TryClaimMatchAsync</c>
///     immediately BEFORE <c>AtomicClaimScript.ExecuteAsync</c>. An abort here simulates a
///     crash AFTER candidate selection but BEFORE the proposal hash is written to Redis.
///   </item>
///   <item>
///     <see cref="BeforeSessionInsert"/> is called by <c>ProposalService.AcceptAsync</c> on the
///     all-accepted path immediately BEFORE the <c>GameSession</c> + participant rows are
///     written to Postgres. An abort here simulates a crash AFTER the Lua complete-script flips
///     the proposal to <c>state=complete</c> but BEFORE the durable session row exists — the
///     reconciler's orphan-session sweep must eventually clean this state.
///   </item>
/// </list>
/// </para>
/// </remarks>
public interface IChaosInterceptor
{
    /// <summary>
    /// Probe called by the ticker immediately before the Lua atomic-claim script executes.
    /// The production <see cref="NullChaosInterceptor"/> returns instantly; tests may throw to
    /// simulate a crash between match-formation and proposal writeback.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task BeforeLuaClaim(CancellationToken ct);

    /// <summary>
    /// Probe called by the proposal service on the all-accepted branch immediately before the
    /// <c>GameSession</c> + <c>SessionParticipant</c> INSERT. The production
    /// <see cref="NullChaosInterceptor"/> returns instantly; tests may throw to simulate a
    /// crash between accept-finalize and session creation.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task BeforeSessionInsert(CancellationToken ct);
}
