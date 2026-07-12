// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Lobby.Services;

/// <summary>
/// Default no-op implementation of <see cref="ILobbyMessageHandler"/>. Unconditionally
/// returns <see langword="true"/> so every chat message is relayed. Registered as a
/// Singleton via <c>TryAddSingleton</c> in <c>AddLobby()</c> so consumers may replace it
/// before registering the Lobby package.
/// </summary>
internal sealed class NullLobbyMessageHandler : ILobbyMessageHandler
{
    /// <inheritdoc />
    public Task<bool> OnMessageAsync(
        Guid lobbyId,
        Guid senderId,
        string message,
        CancellationToken ct)
        => Task.FromResult(true);
}
