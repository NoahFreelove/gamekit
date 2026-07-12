// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics;
using GameKit.Core.Telemetry;

namespace GameKit.Lobby.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="ActivitySource"/> for GameKit.Lobby SignalR hub instrumentation
/// (OBS-06 lobby ready-check clause).
/// </summary>
/// <remarks>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> spans emitted via this source are no-ops
/// unless the host registers <c>AddSource("GameKit.Lobby")</c> in its OpenTelemetry SDK
/// setup. Without this registration, the lobby ready-check span + its parent context are
/// discarded silently. The recommended integration is <c>AddGameKitObservability()</c>.
/// </para>
/// <para>
/// <b>SourceName:</b> the literal <c>"GameKit.Lobby"</c>. Operators registering the source
/// MUST use this exact string — drift breaks the tracing pipeline silently.
/// Pinned as <see cref="SourceName"/> for tests + XML doc cross-references.
/// </para>
/// </remarks>
public static class LobbyActivitySource
{
    /// <summary>
    /// The OpenTelemetry source name. Operators MUST register
    /// <c>AddSource("GameKit.Lobby")</c> in their OTel SDK setup to subscribe.
    /// </summary>
    /// <remarks>Must equal <see cref="GameKitTelemetry.LobbySourceName"/>.</remarks>
    public const string SourceName = "GameKit.Lobby";

    /// <summary>
    /// The shared <see cref="ActivitySource"/> instance. Internal — external code must go
    /// through the typed helper <see cref="StartReadyCheckActivity"/> to guarantee consistent
    /// span naming.
    /// </summary>
    internal static readonly ActivitySource Source = new(SourceName, GameKitTelemetry.Version);

    /// <summary>
    /// Starts a span named <c>"ReadyCheck"</c> wrapping the ready-check broadcast in
    /// <c>LobbyService.MarkReadyAsync</c>. When <paramref name="parentContext"/> is supplied,
    /// the span is parented to it — enabling the hub-invocation span (captured at the SignalR
    /// server side via <c>Activity.Current</c>) to appear as the trace parent (OBS-06).
    /// </summary>
    /// <param name="parentContext">
    /// The parent <see cref="ActivityContext"/> captured from <c>Activity.Current</c> at the hub
    /// invocation site. Pass <see langword="default"/> when no parent is available (standalone
    /// service call).
    /// </param>
    /// <returns>The started <see cref="Activity"/>, or <see langword="null"/> if no listener is
    /// subscribed. The caller MUST use a <c>using</c> block.</returns>
    public static Activity? StartReadyCheckActivity(ActivityContext parentContext = default)
    {
        return parentContext == default
            ? Source.StartActivity("ReadyCheck")
            : Source.StartActivity("ReadyCheck", ActivityKind.Internal, parentContext);
    }
}
