// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Lobby;
using GameKit.Lobby.Hubs;
using GameKit.Lobby.Services;
using GameKit.Lobby.Telemetry;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Xunit;
using LobbyEntity = GameKit.Lobby.Entities.Lobby;

namespace GameKit.Lobby.Integration.Tests.Telemetry;

/// <summary>
/// Regression coverage for CR-01: the <c>lobby.connected_clients</c> gauge must not leak
/// when <see cref="LobbyHub.OnConnectedAsync"/> throws. SignalR does NOT invoke
/// <c>OnDisconnectedAsync</c> when <c>OnConnectedAsync</c> throws, so without the
/// connect-path try/catch the matching <see cref="LobbyConnectionTracker.Decrement"/> would
/// never run and the gauge would drift permanently upward under sustained connect failures.
/// </summary>
/// <remarks>
/// Drives <see cref="LobbyHub"/> directly with hand-rolled fakes — no SignalR host, DB, or Redis.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class LobbyConnectionGaugeLeakTests
{
    /// <summary>
    /// CR-01: when the awaited connect-path dependency throws, the tracker is decremented so the
    /// net effect on the gauge is zero (no permanent over-count).
    /// </summary>
    [Fact]
    public async Task OnConnectedAsync_WhenDependencyThrows_DecrementsTrackerSoGaugeDoesNotLeak()
    {
        var tracker = new LobbyConnectionTracker();
        var hub = new LobbyHub(
            new ThrowingLobbyService(),
            new NoOpMessageHandler(),
            Options.Create(new GameKitLobbyOptions()),
            tracker)
        {
            Context = new FakeHubCallerContext(playerId: Guid.NewGuid()),
        };

        // The simulated backplane/DB failure must propagate...
        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.OnConnectedAsync());

        // ...and the gauge must be back at zero — Increment was undone on the failure path.
        Assert.Equal(0, tracker.Current);
    }

    /// <summary>
    /// The fix must not over-decrement on the happy path: a successful connect leaves the
    /// gauge at 1 (a single Increment, no catch-path Decrement), and the matching disconnect
    /// brings it back to 0.
    /// </summary>
    [Fact]
    public async Task OnConnectedAsync_OnSuccess_CountsOnce_AndDisconnectDecrementsToZero()
    {
        var tracker = new LobbyConnectionTracker();
        var hub = new LobbyHub(
            new ThrowingLobbyService(),
            new NoOpMessageHandler(),
            Options.Create(new GameKitLobbyOptions()),
            tracker)
        {
            // No NameIdentifier claim → GetPlayerIdOrNull() returns null, so the connect path
            // skips the (throwing) GetPlayerLobbyIdsAsync entirely and completes cleanly.
            Context = new FakeHubCallerContext(playerId: null),
        };

        await hub.OnConnectedAsync();
        Assert.Equal(1, tracker.Current);

        await hub.OnDisconnectedAsync(exception: null);
        Assert.Equal(0, tracker.Current);
    }

    // ---- fakes ----

    /// <summary>An <see cref="ILobbyService"/> whose connect-path query always throws.</summary>
    private sealed class ThrowingLobbyService : ILobbyService
    {
        public Task<IReadOnlyList<Guid>> GetPlayerLobbyIdsAsync(Guid playerId, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated Postgres failure during connect");

        public Task<LobbyEntity> CreateLobbyAsync(Guid ownerId, int? maxMembers = null, Guid? ladderId = null, string? regionName = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<LobbyEntity> JoinLobbyAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RemoveMemberAsync(Guid lobbyId, Guid actorId, Guid targetPlayerId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> IsMemberAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task MarkReadyAsync(Guid lobbyId, Guid playerId, CancellationToken ct = default, ActivityContext parentContext = default)
            => throw new NotSupportedException();

        public Task<LobbyEntity?> GetLobbyAsync(Guid lobbyId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>A relay handler stub — never reached by the connect-path tests.</summary>
    private sealed class NoOpMessageHandler : ILobbyMessageHandler
    {
        public Task<bool> OnMessageAsync(Guid lobbyId, Guid senderId, string message, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    /// <summary>
    /// Minimal <see cref="HubCallerContext"/> exposing only what <see cref="LobbyHub"/>'s
    /// connect/disconnect path reads: a (possibly absent) NameIdentifier claim, a connection id,
    /// and a cancellation token.
    /// </summary>
    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly ClaimsPrincipal _user;
        private readonly IDictionary<object, object?> _items = new Dictionary<object, object?>();
        private readonly IFeatureCollection _features = new FeatureCollection();

        public FakeHubCallerContext(Guid? playerId)
        {
            var identity = new ClaimsIdentity();
            if (playerId.HasValue)
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, playerId.Value.ToString()));
            _user = new ClaimsPrincipal(identity);
        }

        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => _user;
        public override IDictionary<object, object?> Items => _items;
        public override IFeatureCollection Features => _features;
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
