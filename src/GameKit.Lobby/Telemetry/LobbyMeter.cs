// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics.Metrics;
using GameKit.Core.Telemetry;

namespace GameKit.Lobby.Telemetry;

/// <summary>
/// OpenTelemetry <see cref="Meter"/> for <c>GameKit.Lobby</c> diagnostics (OBS-05).
/// </summary>
/// <remarks>
/// <para>
/// Exposes the SignalR lobby instruments: a <c>lobby.connected_clients</c>
/// <see cref="ObservableGauge{T}"/> backed by a singleton <see cref="LobbyConnectionTracker"/>;
/// a <c>lobby.messages.sent</c> counter incremented per relayed chat message; and
/// <c>lobby.ready_check.started</c> / <c>lobby.ready_check.completed</c> counters fired at
/// lobby state-transition sites. Call <see cref="Init"/> from <c>AddLobby()</c> to supply
/// the tracker reference for the ObservableGauge callback (OBS-05).
/// </para>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> instruments are no-ops unless the host
/// registers <c>AddMeter("GameKit.Lobby")</c> in its OpenTelemetry SDK configuration.
/// The recommended integration is <c>AddGameKitObservability()</c> which calls this
/// for you.
/// </para>
/// <para>
/// Declared <see langword="internal"/> so external code cannot mutate the static instance;
/// <c>InternalsVisibleTo</c> grants in <c>AssemblyInfo.cs</c> let test assemblies subscribe
/// a <see cref="MeterListener"/> for verification.
/// </para>
/// </remarks>
internal static class LobbyMeter
{
    /// <summary>The Lobby meter name. Operators must register <c>AddMeter</c> with this exact value.</summary>
    /// <remarks>Must equal <see cref="GameKitTelemetry.LobbyMeterName"/>.</remarks>
    public const string MeterName = "GameKit.Lobby";

    /// <summary>The meter version, pinned to <c>1.0.0</c> for v1 wire compatibility.</summary>
    public const string MeterVersion = "1.0.0";

    /// <summary>The <see cref="Meter"/> instance backing every Lobby counter / gauge.</summary>
    public static readonly Meter Meter = new(MeterName, MeterVersion);

    // ── OBS-05: singleton tracker for ConnectedClients ObservableGauge ─────────
    // Set once at startup by Init(); the gauge callback is synchronous and tracker-safe
    // (Interlocked/Volatile — no Redis needed for a per-replica in-process count).
    private static LobbyConnectionTracker? _tracker;

    /// <summary>
    /// Supplies the singleton <see cref="LobbyConnectionTracker"/> that the
    /// <see cref="ConnectedClients"/> <see cref="ObservableGauge{T}"/> callback reads at scrape
    /// time (OBS-05). Call this once from <c>AddLobby()</c> after the tracker singleton is
    /// registered in DI.
    /// </summary>
    /// <param name="tracker">The singleton connection tracker.</param>
    /// <remarks>
    /// OBS-05: wires the ConnectedClients ObservableGauge to the singleton tracker.
    /// </remarks>
    internal static void Init(LobbyConnectionTracker tracker)
    {
        System.ArgumentNullException.ThrowIfNull(tracker);
        _tracker = tracker;
    }

    /// <summary>
    /// ObservableGauge reporting the current number of SignalR clients connected to
    /// <c>LobbyHub</c> on this replica. Backed by the singleton
    /// <see cref="LobbyConnectionTracker"/> — no Redis required (OBS-05).
    /// </summary>
    public static readonly ObservableGauge<int> ConnectedClients = Meter.CreateObservableGauge<int>(
        name: "lobby.connected_clients",
        unit: "connections",
        observeValue: ObserveConnectedClients,
        description: "Current number of connected SignalR clients to the LobbyHub (per-replica, OBS-05)");

    /// <summary>
    /// Counter incremented once per relayed chat message in
    /// <c>LobbyHub.SendChatMessageAsync</c> — only incremented when the message is actually
    /// relayed (inside the <c>if (relay)</c> block, after the relay succeeds).
    /// </summary>
    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>(
        name: "lobby.messages.sent",
        unit: "messages",
        description: "Count of chat messages relayed through LobbyHub.SendChatMessageAsync (OBS-05)");

    /// <summary>
    /// Counter incremented when a lobby transitions from <see cref="GameKit.Lobby.Entities.LobbyState.Open"/>
    /// to <see cref="GameKit.Lobby.Entities.LobbyState.ReadyChecking"/> (the fill-to-MaxMembers trigger
    /// in <c>LobbyService.JoinLobbyAsync</c>).
    /// </summary>
    public static readonly Counter<long> ReadyCheckStarted = Meter.CreateCounter<long>(
        name: "lobby.ready_check.started",
        unit: "checks",
        description: "Count of ready-check initiations (Open→ReadyChecking transitions). OBS-05.");

    /// <summary>
    /// Counter incremented when all lobby members are ready (the all-ready gate in
    /// <c>LobbyService.MarkReadyAsync</c>). Tag: <c>check.result</c>
    /// (== <see cref="GameKitTelemetry.AttrCheckResult"/>). Low-cardinality values:
    /// <c>"all_ready"</c>, <c>"timeout"</c>, <c>"cancelled"</c>.
    /// </summary>
    public static readonly Counter<long> ReadyCheckCompleted = Meter.CreateCounter<long>(
        name: "lobby.ready_check.completed",
        unit: "checks",
        description: "Count of ready-check completions. Tag: check.result=all_ready|timeout|cancelled. OBS-05.");

    // ── Private ObservableGauge callback ──────────────────────────────────────

    private static int ObserveConnectedClients() => _tracker?.Current ?? 0;
}
