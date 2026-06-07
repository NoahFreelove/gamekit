// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Lobby.Services;

/// <summary>
/// Optional relay/gate seam invoked by <c>LobbyHub.SendChatMessageAsync</c> before a chat
/// message is relayed to the SignalR group.
/// </summary>
/// <remarks>
/// <para>
/// <b>LOBBY-04 anti-feature enforcement:</b> this interface MUST NOT write the message to
/// Postgres or any durable store. There is intentionally no <c>SaveAsync</c> / <c>PersistAsync</c>
/// method on this interface. Chat is ephemeral — messages relay through the SignalR group and
/// are never written to the database (SC#4).
/// </para>
/// <para>
/// Intended uses: rate-limit checks, structured logging, per-message telemetry.
/// The default implementation is <c>NullLobbyMessageHandler</c> which unconditionally relays.
/// </para>
/// <para>
/// Consumers may replace the default via:
/// <code>
/// services.AddSingleton&lt;ILobbyMessageHandler, MyRateLimitedHandler&gt;();
/// </code>
/// before calling <c>AddLobby()</c> (which uses <c>TryAddSingleton</c>).
/// </para>
/// </remarks>
public interface ILobbyMessageHandler
{
    /// <summary>
    /// Called before the message is relayed to the lobby SignalR group.
    /// </summary>
    /// <param name="lobbyId">The lobby the message targets.</param>
    /// <param name="senderId">Canonical player id of the sender (from their JWT claim).</param>
    /// <param name="message">The chat message text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> to relay the message to the group;
    /// <see langword="false"/> to suppress it (e.g. rate-limit exceeded).
    /// </returns>
    Task<bool> OnMessageAsync(
        Guid lobbyId,
        Guid senderId,
        string message,
        CancellationToken ct);
}
