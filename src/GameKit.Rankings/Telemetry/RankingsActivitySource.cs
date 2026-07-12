// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics;
using GameKit.Core.Telemetry;

namespace GameKit.Rankings.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="ActivitySource"/> for the rankings ticker (Plan 04-07, extracted
/// in Plan 13-03). Exposes <see cref="StartDrainLadderActivity"/> so the ticker wraps each
/// per-ladder drain in a span. Mirrors <c>MatchmakingActivitySource</c> in
/// <c>GameKit.Matchmaking</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> spans emitted via this source are no-ops
/// unless the host registers
/// <c>AddSource("GameKit.Rankings.Ticker")</c> in its OpenTelemetry SDK setup. Without
/// this registration, the ticker's per-drain spans + tags (<c>ladder.id</c>,
/// <c>ladder.name</c>, <c>result</c>, <c>error.type</c>) are discarded silently.
/// Operators MUST wire the source to observe live rankings telemetry — this is the
/// standard ActivitySource opt-in pattern (matches what ASP.NET Core, EF Core, and
/// StackExchange.Redis do).
/// </para>
/// <para>
/// <b>SourceName:</b> the literal <c>"GameKit.Rankings.Ticker"</c>, pinned to
/// <see cref="GameKitTelemetry.RankingsTickerSourceName"/>. Operators registering the
/// source MUST use this exact string — drift here breaks the tracing pipeline silently.
/// </para>
/// </remarks>
public static class RankingsActivitySource
{
    /// <summary>
    /// The OpenTelemetry source name. Operators MUST register
    /// <c>AddSource("GameKit.Rankings.Ticker")</c> in their OTel SDK setup to subscribe.
    /// </summary>
    public const string SourceName = GameKitTelemetry.RankingsTickerSourceName;

    /// <summary>
    /// The shared <see cref="ActivitySource"/> instance. Internal — external code must go
    /// through the typed helper <see cref="StartDrainLadderActivity"/> to guarantee
    /// consistent span naming.
    /// </summary>
    internal static readonly ActivitySource Source = new(SourceName, GameKitTelemetry.Version);

    /// <summary>
    /// Starts a span named <c>"DrainLadder"</c> wrapping a single per-ladder drain in
    /// <c>RankingsTickerService.DrainLadderAsync</c>. Returns <see langword="null"/> if
    /// no listener is subscribed — the caller MUST use a <c>using</c> block (the
    /// <see cref="Activity"/> implements <see cref="System.IDisposable"/>).
    /// </summary>
    /// <returns>The started <see cref="Activity"/>, or <see langword="null"/> if no listener.</returns>
    public static Activity? StartDrainLadderActivity() => Source.StartActivity("DrainLadder");
}
