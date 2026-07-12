// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;

namespace GameKit.Lobby.Hubs;

/// <summary>
/// Typed client interface for <c>LobbyHub</c>. Consumed via
/// <c>IHubContext&lt;LobbyHub, ILobbyClient&gt;</c> and by the hub's
/// <c>Clients.Group(...)</c> / <c>Clients.Caller</c> calls.
/// </summary>
public interface ILobbyClient
{
    /// <summary>
    /// Delivers an ephemeral chat message to every member of the lobby group.
    /// The message is relayed verbatim — GameKit does NOT sanitize the content.
    /// The consuming client is responsible for safe rendering (e.g. HTML-escaping
    /// before inserting into the DOM).
    /// </summary>
    /// <param name="senderId">Canonical player id of the sender (from their JWT claim).</param>
    /// <param name="message">The chat message text (max length governed by
    /// <see cref="GameKitLobbyOptions.MaxChatMessageLength"/>).</param>
    Task ReceiveChatMessageAsync(Guid senderId, string message);

    /// <summary>
    /// Delivers a state-change notification to every member of the lobby group.
    /// Broadcast after a SERIALIZABLE transaction commits inside
    /// <c>LobbyService.MarkReadyAsync</c> (LOBBY-03).
    /// </summary>
    /// <param name="update">The lobby state update payload.</param>
    Task ReceiveStateUpdateAsync(LobbyStateUpdate update);
}
