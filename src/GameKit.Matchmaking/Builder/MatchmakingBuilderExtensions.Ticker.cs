// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Matchmaking.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Partial-class extension methods registering the Plan 05-05 matchmaker ticker
/// (BackgroundService + lease helper + proposal-sweeper) — the live match-formation engine
/// (MATCH-07 + MATCH-08).
/// </summary>
/// <remarks>
/// <para>
/// <b>Wave-3 lease unification:</b> Plan 05-07 (sibling wave) ships a minimal
/// <c>RedisMatchmakerLease</c> default behind the <see cref="IMatchmakerLease"/> contract so
/// the reconciler / retention sweeps work standalone. This file registers
/// <see cref="MatchmakerLeaseHelper"/> as a singleton AND replaces the <see cref="IMatchmakerLease"/>
/// service binding so all three matchmaker BackgroundServices (ticker, reconciler, retention)
/// share a single Polly-wrapped lease helper keyed on the same fencing-token
/// <see cref="MatchmakerLeaseHelper.InstanceId"/>. This closes the orchestrator-merge
/// ambiguity flagged in Plan 05-07 SUMMARY §Wave-3 Parallel-Plan Coordination Notes.
/// </para>
/// <para>
/// <b>Operator OTel registration (Pitfall §7):</b> the ticker emits spans via the
/// <see cref="GameKit.Matchmaking.Telemetry.MatchmakingActivitySource"/>
/// (<c>"GameKit.Matchmaking.Ticker"</c>). To observe them, register
/// <c>AddSource("GameKit.Matchmaking.Ticker")</c> in the host's OpenTelemetry SDK.
/// </para>
/// </remarks>
public static partial class MatchmakingBuilderExtensions
{
    /// <summary>
    /// Registers the matchmaker ticker BackgroundService and its lease/sweeper collaborators.
    /// Idempotent: <see cref="ServiceCollectionDescriptorExtensions.Replace"/> is used for the
    /// <see cref="IMatchmakerLease"/> binding so the unified ticker-owned helper supersedes the
    /// Plan 05-07 <c>RedisMatchmakerLease</c> default.
    /// </summary>
    /// <param name="services">The service collection being configured by <c>AddMatchmaking</c>.</param>
    internal static void AddTickerServices(this IServiceCollection services)
    {
        // 0. Plan 05-09 chaos seam — production default is NullChaosInterceptor (no-op). The
        //    chaos integration test (SC#2 phase gate) registers an AbortingChaosInterceptor
        //    BEFORE calling AddMatchmaking; TryAddSingleton honours that explicit override.
        //    See IChaosInterceptor XML doc for rationale (RESEARCH §Decision 14 — in-process
        //    abort over child-process simulation).
        services.TryAddSingleton<IChaosInterceptor, NullChaosInterceptor>();

        // 1. The Polly-wrapped lease helper. Singleton so its InstanceId is stable per process
        //    (the Plan 05-04 AtomicClaimScript fencing-token check uses InstanceId; the
        //    Plan 05-07 admin observability surface displays it as the leader identity).
        services.AddSingleton<MatchmakerLeaseHelper>();

        // 2. Replace IMatchmakerLease binding so the reconciler + retention sweeps (Plan 05-07)
        //    share the same fencing-token instance as the ticker. Both the ticker and the
        //    sweeps then race for and renew the SAME Redis lock — preventing the audit-trail
        //    ambiguity flagged in Plan 05-07 SUMMARY §Wave-3 Parallel-Plan Coordination Notes.
        services.Replace(ServiceDescriptor.Singleton<IMatchmakerLease>(sp =>
            sp.GetRequiredService<MatchmakerLeaseHelper>()));

        // 3. The proposal-sweeper (Pitfall §10 partial-accept reap). Singleton — stateless.
        services.AddSingleton<ProposalSweeper>();

        // 4. The matchmaker ticker BackgroundService. Registered as a singleton so the same
        //    instance backs both the IHostedService loop and the IMatchmakerTicker resolved by
        //    integration tests for deterministic single-tick execution. Mirrors the Rankings
        //    precedent (RankingsTickerService).
        services.AddSingleton<MatchmakerTickerService>();
        services.AddHostedService(sp => sp.GetRequiredService<MatchmakerTickerService>());
        services.AddSingleton<IMatchmakerTicker>(sp => sp.GetRequiredService<MatchmakerTickerService>());

        // Plan 05-06's MatchmakingBuilderExtensions.Accept.cs (AddProposalServices) ships
        // the real IProposalService binding. The Wave-3 placeholder stub registration that
        // briefly lived here is gone — proposal accept/decline now uses the real service.
    }
}
