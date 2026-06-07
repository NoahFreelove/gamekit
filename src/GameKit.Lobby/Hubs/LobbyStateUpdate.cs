// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Lobby.Entities;

namespace GameKit.Lobby.Hubs;

/// <summary>
/// Payload broadcast to all members of a lobby group when the lobby state changes.
/// Sent via <see cref="ILobbyClient.ReceiveStateUpdateAsync"/> after a SERIALIZABLE
/// transaction commits (LOBBY-03).
/// </summary>
/// <param name="LobbyId">Identifier of the lobby whose state changed.</param>
/// <param name="State">The new <see cref="LobbyState"/>.</param>
/// <param name="Detail">Optional human-readable detail (e.g. failure reason).</param>
public sealed record LobbyStateUpdate(
    Guid LobbyId,
    LobbyState State,
    string? Detail = null);
