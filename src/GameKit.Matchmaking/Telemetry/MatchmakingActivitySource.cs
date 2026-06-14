// SPDX-License-Identifier: GPL-3.0-or-later
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
}
