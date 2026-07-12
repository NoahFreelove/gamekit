// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Diagnostics;
using GameKit.Core.Telemetry;

namespace GameKit.Matchmaking.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="ActivitySource"/> for the matchmaker ticker (Plan 05-05). Mirrors
/// the Phase 4 <c>ActivitySource("GameKit.Rankings.Ticker")</c> pattern. Exposes
/// <see cref="StartTickActivity"/> + <see cref="StartPoolActivity"/> helpers so the ticker
/// can wrap each tick + each per-pool sweep in a span.
/// </summary>
/// <remarks>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> spans emitted via this source are no-ops
/// unless the host registers
/// <c>AddSource("GameKit.Matchmaking.Ticker")</c> in its OpenTelemetry SDK setup. Without
/// this registration, the ticker's per-tick spans + tags (<c>ladder.id</c>, <c>pool.name</c>,
/// <c>candidates.evaluated</c>, <c>matches.formed</c>) are discarded silently. Operators MUST
/// wire the source to observe live matchmaker telemetry — this is the standard ActivitySource
/// opt-in pattern (matches what ASP.NET Core, EF Core, and StackExchange.Redis do).
/// </para>
/// <para>
/// <b>SourceName:</b> the literal <c>"GameKit.Matchmaking.Ticker"</c>. Operators registering
/// the source MUST use this exact string — drift here breaks the tracing pipeline silently.
/// Pinned as <see cref="SourceName"/> for tests + XML doc cross-references.
/// </para>
/// </remarks>
public static class MatchmakingActivitySource
{
    /// <summary>
    /// The OpenTelemetry source name. Operators MUST register
    /// <c>AddSource("GameKit.Matchmaking.Ticker")</c> in their OTel SDK setup to subscribe.
    /// </summary>
    public const string SourceName = "GameKit.Matchmaking.Ticker";

    /// <summary>
    /// The shared <see cref="ActivitySource"/> instance. Internal — external code must go
    /// through the typed helpers <see cref="StartTickActivity"/> + <see cref="StartPoolActivity"/>
    /// to guarantee consistent span naming.
    /// </summary>
    internal static readonly ActivitySource Source = new(SourceName, GameKitTelemetry.Version);

    /// <summary>
    /// Starts a span named <c>"Tick"</c> wrapping a single <c>MatchmakerTickerService.RunOnceAsync</c>
    /// iteration. Returns <see langword="null"/> if no listener is subscribed — the caller
    /// MUST use a <c>using</c> block (the <see cref="Activity"/> implements <see cref="IDisposable"/>).
    /// </summary>
    /// <returns>The started <see cref="Activity"/>, or <see langword="null"/> if no listener.</returns>
    public static Activity? StartTickActivity() => Source.StartActivity("Tick");

    /// <summary>
    /// Starts a span named <c>"PoolSweep"</c> wrapping the per-pool match-formation step
    /// inside a single tick. The span carries the ladder id + pool name as tags so the
    /// telemetry pipeline can group spans per pool.
    /// </summary>
    /// <param name="ladderIdValue">Ladder identifier value (tag <c>ladder.id</c>).</param>
    /// <param name="poolNameValue">Pool name value (tag <c>pool.name</c>).</param>
    /// <returns>The started <see cref="Activity"/>, or <see langword="null"/> if no listener.</returns>
    public static Activity? StartPoolActivity(Guid ladderIdValue, string poolNameValue)
    {
        var activity = Source.StartActivity("PoolSweep");
        if (activity is not null)
        {
            activity.SetTag(GameKitTelemetry.AttrLadderId, ladderIdValue.ToString());
            activity.SetTag(GameKitTelemetry.AttrPoolName, poolNameValue);
        }
        return activity;
    }

    /// <summary>
    /// Starts a span named <c>"ProposalSweep"</c> wrapping the proposal-sweeper step that
    /// runs after match-formation in the same tick (Pitfall §10). The sweeper is a
    /// per-tick operation, not per-pool, so this is the only required tag layer.
    /// </summary>
    /// <returns>The started <see cref="Activity"/>, or <see langword="null"/> if no listener.</returns>
    public static Activity? StartProposalSweepActivity() => Source.StartActivity("ProposalSweep");

    /// <summary>
    /// Starts a span named <c>"MatchFormation"</c> on the existing
    /// <c>GameKit.Matchmaking.Ticker</c> source (OBS-06 / D-02 / D-03).
    /// </summary>
    /// <param name="parentContext">
    /// The restored W3C parent <see cref="ActivityContext"/> from the originating enqueue
    /// trace. Pass <see langword="default"/> to start a root span (no parent). When the
    /// parent's <c>TraceFlags</c> does not include <c>Recorded</c> (i.e. the enqueue span
    /// was not sampled), <see cref="ActivitySource"/> will return <see langword="null"/>
    /// from the parent-context overload — callers MUST treat <see langword="null"/> as a
    /// no-op (Pitfall §1 — do not null-guard by forcing a new root span; propagate the
    /// sampling decision from the originating trace).
    /// </param>
    /// <returns>
    /// The started <see cref="Activity"/>, or <see langword="null"/> when no listener is
    /// subscribed or the parent context is non-sampled.
    /// </returns>
    public static Activity? StartMatchFormationActivity(ActivityContext parentContext = default) =>
        parentContext == default
            ? Source.StartActivity("MatchFormation")
            : Source.StartActivity("MatchFormation", ActivityKind.Internal, parentContext);
}
