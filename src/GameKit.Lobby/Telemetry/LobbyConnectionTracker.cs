// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;

namespace GameKit.Lobby.Telemetry;

/// <summary>
/// Singleton counter backing the <c>lobby.connected_clients</c>
/// <see cref="System.Diagnostics.Metrics.ObservableGauge{T}"/> in <see cref="LobbyMeter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread safety:</b> <see cref="Increment"/> and <see cref="Decrement"/> use
/// <see cref="Interlocked"/> operations; <see cref="Current"/> uses
/// <c>Volatile.Read</c> — both are safe for concurrent access from multiple
/// SignalR hub connection threads.
/// </para>
/// <para>
/// Registered as a singleton by <c>AddLobby()</c> and injected into
/// <c>LobbyHub</c>. The OTel scrape path reads <see cref="Current"/> synchronously from the
/// <c>LobbyMeter.ConnectedClients</c> ObservableGauge callback — no Redis needed (OBS-05).
/// </para>
/// </remarks>
public sealed class LobbyConnectionTracker
{
    private int _count;

    /// <summary>
    /// Increments the connected-clients counter by 1. Called from
    /// <c>LobbyHub.OnConnectedAsync</c> on each new SignalR connection.
    /// </summary>
    public void Increment() => Interlocked.Increment(ref _count);

    /// <summary>
    /// Decrements the connected-clients counter by 1. Called from
    /// <c>LobbyHub.OnDisconnectedAsync</c> on each SignalR disconnection.
    /// </summary>
    public void Decrement() => Interlocked.Decrement(ref _count);

    /// <summary>
    /// Returns the current connected-clients count using <c>Volatile.Read</c>
    /// so the OTel scrape callback always reads the latest value.
    /// </summary>
    public int Current => Volatile.Read(ref _count);
}
