// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Lobby;

/// <summary>
/// Root options for <c>GameKit.Lobby</c>. Populated via
/// <c>services.AddGameKit(...).AddLobby(opts =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// All values are tunable defaults. Consumers override them in the <c>configure</c>
/// callback passed to <c>AddLobby()</c>.
/// </remarks>
public sealed class GameKitLobbyOptions
{
    /// <summary>
    /// Default maximum members per lobby when no value is supplied to
    /// <see cref="Services.ILobbyService.CreateLobbyAsync"/>. Default <c>8</c>.
    /// </summary>
    public int DefaultMaxMembers { get; set; } = 8;

    /// <summary>
    /// Maximum length (in characters) of a single chat message accepted by
    /// <c>LobbyHub.SendChatMessageAsync</c>. Default <c>500</c>.
    /// </summary>
    /// <remarks>
    /// Messages that exceed this limit are rejected with a <c>HubException</c> before relay
    /// (T-11-03-04 DoS mitigation).
    /// </remarks>
    public int MaxChatMessageLength { get; set; } = 500;

    /// <summary>
    /// Fallback region / pool name used when a lobby has no explicit <c>RegionName</c>.
    /// Passed to <c>IMatchmakingService.EnqueueAsync</c> as the <c>poolName</c> argument.
    /// Default <c>"default"</c>.
    /// </summary>
    public string DefaultPoolName { get; set; } = "default";
}
