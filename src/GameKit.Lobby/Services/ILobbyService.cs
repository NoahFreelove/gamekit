// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using LobbyEntity = GameKit.Lobby.Entities.Lobby;
using LobbyState = GameKit.Lobby.Entities.LobbyState;

namespace GameKit.Lobby.Services;

/// <summary>
/// Application service for lobby CRUD, membership management, and the ready-check
/// state machine (LOBBY-02, LOBBY-03).
/// </summary>
/// <remarks>
/// All mutating operations use a Postgres SERIALIZABLE transaction with 40001 retry
/// for the all-ready transition (<see cref="MarkReadyAsync"/>) to prevent concurrent
/// double-transitions (T-11-03-06).
/// </remarks>
public interface ILobbyService
{
    /// <summary>
    /// Creates a new lobby owned by <paramref name="ownerId"/> and adds the owner as the
    /// first member. Applies <see cref="GameKitLobbyOptions.DefaultMaxMembers"/> when
    /// <paramref name="maxMembers"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="ownerId">Canonical player id of the lobby creator.</param>
    /// <param name="maxMembers">Optional member cap override.</param>
    /// <param name="ladderId">Optional ladder to associate with the lobby.</param>
    /// <param name="regionName">Optional pool-affinity name for matchmaking.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created <see cref="LobbyEntity"/>.</returns>
    Task<LobbyEntity> CreateLobbyAsync(
        Guid ownerId,
        int? maxMembers = null,
        Guid? ladderId = null,
        string? regionName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Adds <paramref name="playerId"/> to the lobby. Enforces the <c>MaxMembers</c> cap and
    /// rejects duplicate membership (server-side, NEVER trusting a client claim). When the join
    /// fills the lobby to <c>MaxMembers</c> and the lobby is <see cref="LobbyState.Open"/>, the
    /// lobby transitions to <see cref="LobbyState.ReadyChecking"/> and a state update is broadcast
    /// to the lobby group — this is the Open→ReadyChecking trigger that makes the LOBBY-03
    /// ready-check flow reachable through the public REST API without a separate admin call.
    /// </summary>
    /// <param name="lobbyId">Lobby identifier.</param>
    /// <param name="playerId">Canonical player id of the joining player.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="LobbyEntity"/>.</returns>
    Task<LobbyEntity> JoinLobbyAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Removes <paramref name="targetPlayerId"/> from the lobby. Only the lobby owner or the
    /// player themselves may remove a member (owner-or-self authorization).
    /// </summary>
    /// <param name="lobbyId">Lobby identifier.</param>
    /// <param name="actorId">Player id of the actor requesting removal.</param>
    /// <param name="targetPlayerId">Player id of the member to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveMemberAsync(
        Guid lobbyId,
        Guid actorId,
        Guid targetPlayerId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <see langword="true"/> when a <c>lobby_members</c> row exists for
    /// (<paramref name="lobbyId"/>, <paramref name="playerId"/>). This is the server-side
    /// authorization gate called by <c>LobbyHub</c> before <c>AddToGroupAsync</c> or chat
    /// relay — NEVER trust a client-supplied lobbyId (T-11-03-02).
    /// </summary>
    /// <param name="lobbyId">Lobby identifier.</param>
    /// <param name="playerId">Canonical player id to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the player is a current member; otherwise <see langword="false"/>.</returns>
    Task<bool> IsMemberAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Marks <paramref name="playerId"/> as ready. When all members are ready and the lobby
    /// is in <see cref="LobbyState.ReadyChecking"/>, transitions the state atomically in a
    /// SERIALIZABLE transaction and broadcasts the result via the SignalR hub group (LOBBY-03).
    /// </summary>
    /// <param name="lobbyId">Lobby identifier.</param>
    /// <param name="playerId">Player id marking themselves ready.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkReadyAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the lobby by id including its members, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="lobbyId">Lobby identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="LobbyEntity"/> with members populated, or <see langword="null"/>.</returns>
    Task<LobbyEntity?> GetLobbyAsync(Guid lobbyId, CancellationToken ct = default);

    /// <summary>
    /// Returns the ids of all lobbies the player currently belongs to. Used by
    /// <c>LobbyHub.OnConnectedAsync</c> to re-add the new connection to its lobby groups
    /// after a reconnect (RESEARCH Pitfall 2).
    /// </summary>
    /// <param name="playerId">Canonical player id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A collection of lobby ids the player is a member of.</returns>
    Task<System.Collections.Generic.IReadOnlyList<Guid>> GetPlayerLobbyIdsAsync(
        Guid playerId,
        CancellationToken ct = default);
}
