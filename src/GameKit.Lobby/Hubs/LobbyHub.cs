// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Lobby.Services;
using GameKit.Lobby.Telemetry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace GameKit.Lobby.Hubs;

/// <summary>
/// SignalR hub for real-time lobby operations: group subscription, ready-checks, and
/// ephemeral chat relay (LOBBY-03, LOBBY-04, LOBBY-06).
/// </summary>
/// <remarks>
/// <para>
/// <b>Authorization:</b> the <see cref="AuthorizeAttribute"/> attribute gates the hub.
/// The JWT is extracted from the <c>?access_token</c> query string by
/// <c>LobbyJwtBearerPostConfigure</c> before the WebSocket handshake completes, so
/// unauthenticated upgrade attempts receive HTTP 401 before a WebSocket connection is
/// established (SC#2 / T-11-03-01).
/// </para>
/// <para>
/// <b>Player identity:</b> extracted from <see cref="HubCallerContext.User"/> via
/// <see cref="GetPlayerId"/>. The HTTP context accessor is NOT used for player identity —
/// <c>HttpContext</c> is <see langword="null"/> during hub invocations (RESEARCH Pitfall 1 /
/// T-11-03-05).
/// </para>
/// <para>
/// <b>Membership authorization:</b> <see cref="JoinLobbyAsync"/> and
/// <see cref="SendChatMessageAsync"/> both gate on <see cref="ILobbyService.IsMemberAsync"/>
/// before performing any group action or relay — NEVER trusting a client-supplied
/// <c>lobbyId</c> (T-11-03-02).
/// </para>
/// <para>
/// <b>Chat persistence:</b> <see cref="SendChatMessageAsync"/> performs ZERO Postgres writes
/// (LOBBY-04 anti-feature). The only extension point — <see cref="ILobbyMessageHandler"/> —
/// has no persistence method.
/// </para>
/// </remarks>
[Authorize]
public sealed class LobbyHub : Hub<ILobbyClient>
{
    private readonly ILobbyService _lobby;
    private readonly ILobbyMessageHandler _messageHandler;
    private readonly GameKitLobbyOptions _options;
    private readonly LobbyConnectionTracker _connectionTracker;

    /// <summary>Constructs the hub with its required dependencies.</summary>
    public LobbyHub(
        ILobbyService lobby,
        ILobbyMessageHandler messageHandler,
        IOptions<GameKitLobbyOptions> options,
        LobbyConnectionTracker connectionTracker)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        ArgumentNullException.ThrowIfNull(messageHandler);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionTracker);
        _lobby = lobby;
        _messageHandler = messageHandler;
        _options = options.Value;
        _connectionTracker = connectionTracker;
    }

    /// <summary>
    /// Re-adds the new <see cref="HubCallerContext.ConnectionId"/> to all lobby groups the
    /// player currently belongs to. SignalR group membership is per-connection and is lost on
    /// reconnect — this override restores it from the durable <c>lobby_members</c> rows in
    /// Postgres (RESEARCH Pitfall 2). Also increments the connected-clients counter (OBS-05).
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // OBS-05: track connected clients for the lobby.connected_clients ObservableGauge.
        _connectionTracker.Increment();

        var playerId = GetPlayerIdOrNull();
        if (playerId.HasValue)
        {
            // Query all lobbies the player currently belongs to and re-add this connection.
            var lobbyIds = await _lobby.GetPlayerLobbyIdsAsync(playerId.Value, Context.ConnectionAborted)
                .ConfigureAwait(false);
            var addTasks = lobbyIds.Select(id =>
                Groups.AddToGroupAsync(Context.ConnectionId, $"lobby:{id}", Context.ConnectionAborted));
            await Task.WhenAll(addTasks).ConfigureAwait(false);
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Decrements the connected-clients counter when a SignalR connection is torn down
    /// (OBS-05).
    /// </summary>
    /// <param name="exception">The exception that caused the disconnection, or
    /// <see langword="null"/> for a clean close.</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // OBS-05: decrement the connected-clients counter on clean or exception disconnect.
        _connectionTracker.Decrement();
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes the current connection to the lobby's SignalR group. Performs a server-side
    /// membership check via <see cref="ILobbyService.IsMemberAsync"/> — the client-supplied
    /// <paramref name="lobbyId"/> is NEVER trusted without DB verification (T-11-03-02).
    /// </summary>
    /// <param name="lobbyId">The lobby to join.</param>
    /// <exception cref="HubException">
    /// Thrown when the player is not a member of the specified lobby.
    /// </exception>
    public async Task JoinLobbyAsync(Guid lobbyId)
    {
        var playerId = GetPlayerId();
        if (!await _lobby.IsMemberAsync(lobbyId, playerId, Context.ConnectionAborted)
                .ConfigureAwait(false))
        {
            throw new HubException("Player is not a member of this lobby.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"lobby:{lobbyId}", Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Relays an ephemeral chat message to all connections in the lobby group.
    /// Performs NO Postgres writes (LOBBY-04 anti-feature). Enforces
    /// <see cref="GameKitLobbyOptions.MaxChatMessageLength"/> and server-side membership
    /// before relay (T-11-03-02, T-11-03-03, T-11-03-04).
    /// </summary>
    /// <param name="lobbyId">The lobby to send the message to.</param>
    /// <param name="message">The chat message text.</param>
    /// <remarks>
    /// The message is relayed as an opaque string. GameKit does NOT sanitize the content;
    /// the consuming client is responsible for safe rendering (e.g. HTML-escaping before
    /// inserting into the DOM).
    /// </remarks>
    /// <exception cref="HubException">
    /// Thrown when the message exceeds <see cref="GameKitLobbyOptions.MaxChatMessageLength"/>,
    /// or when the player is not a member of the lobby.
    /// </exception>
    public async Task SendChatMessageAsync(Guid lobbyId, string message)
    {
        // Treat null as empty — consistent with relay behaviour; coerce before the length guard
        // so null is not silently passed through (IN-02: null?.Length > N evaluates false).
        message ??= string.Empty;
        if (message.Length > _options.MaxChatMessageLength)
            throw new HubException(
                $"Message exceeds maximum length of {_options.MaxChatMessageLength} characters.");

        var playerId = GetPlayerId();

        // Server-side membership check — cross-lobby injection prevention (T-11-03-02).
        if (!await _lobby.IsMemberAsync(lobbyId, playerId, Context.ConnectionAborted)
                .ConfigureAwait(false))
        {
            throw new HubException("Player is not a member of this lobby.");
        }

        // Relay/gate seam — relay-only, NEVER persists (LOBBY-04 / T-11-03-03).
        var relay = await _messageHandler
            .OnMessageAsync(lobbyId, playerId, message, Context.ConnectionAborted)
            .ConfigureAwait(false);

        if (relay)
        {
            await Clients
                .Group($"lobby:{lobbyId}")
                .ReceiveChatMessageAsync(playerId, message)
                .ConfigureAwait(false);
            // OBS-05: count relayed messages (only after the relay succeeds, inside the if-relay block).
            LobbyMeter.MessagesSent.Add(1);
        }
    }

    /// <summary>
    /// Marks the calling player as ready for this lobby. Performs a server-side membership
    /// check consistent with <see cref="JoinLobbyAsync"/> and <see cref="SendChatMessageAsync"/>
    /// before triggering the SERIALIZABLE transaction (T-11-03-02, CR-03, WR-01).
    /// Delegates to <see cref="ILobbyService.MarkReadyAsync"/> which runs a SERIALIZABLE
    /// transaction and broadcasts the resulting state via
    /// <c>IHubContext&lt;LobbyHub, ILobbyClient&gt;</c> after commit (LOBBY-03 / T-11-03-06).
    /// </summary>
    /// <param name="lobbyId">The lobby for which the player is marking themselves ready.</param>
    /// <exception cref="HubException">
    /// Thrown when the player is not a member of the specified lobby.
    /// </exception>
    public async Task MarkReadyAsync(Guid lobbyId)
    {
        var playerId = GetPlayerId();

        // Consistent with JoinLobbyAsync and SendChatMessageAsync — verify membership
        // server-side before the SERIALIZABLE transaction (T-11-03-02).
        // Also uses Context.ConnectionAborted (not GetHttpContext()?.RequestAborted which is
        // always null in hub invocations — WR-01).
        if (!await _lobby.IsMemberAsync(lobbyId, playerId, Context.ConnectionAborted)
                .ConfigureAwait(false))
        {
            throw new HubException("Player is not a member of this lobby.");
        }

        // OBS-06: capture Activity.Current SERVER-SIDE at the hub invocation, not from client
        // input — the SignalR HTTP span is the parent. Passed as an optional param so the
        // service can parent the ReadyCheck span to this hub invocation (T-15-05-TRACE).
        var callerContext = Activity.Current?.Context ?? default;

        await _lobby.MarkReadyAsync(lobbyId, playerId, Context.ConnectionAborted, callerContext)
            .ConfigureAwait(false);
    }

    // ---- helpers ----

    /// <summary>
    /// Extracts the canonical player id from <see cref="HubCallerContext.User"/>.
    /// Throws <see cref="HubException"/> when the claim is absent or unparseable.
    /// </summary>
    /// <remarks>
    /// Reads <see cref="HubCallerContext.User"/> directly — the <c>HttpContextAccessor</c>
    /// path is not used because <c>HttpContext</c> is <see langword="null"/> inside SignalR
    /// hub invocations (T-11-03-05).
    /// </remarks>
    private Guid GetPlayerId()
    {
        var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? Context.User?.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var id))
            throw new HubException("Player identity not found in JWT.");
        return id;
    }

    /// <summary>
    /// Extracts the player id without throwing — returns <see langword="null"/> when the
    /// claim is absent (e.g. during <see cref="OnConnectedAsync"/> on a transient anonymous
    /// connection that will be rejected by the <see cref="AuthorizeAttribute"/> guard).
    /// </summary>
    private Guid? GetPlayerIdOrNull()
    {
        var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? Context.User?.FindFirst("sub")?.Value;
        return sub is not null && Guid.TryParse(sub, out var id) ? id : null;
    }
}
