// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// SC#2 — WebSocket JWT authentication gate for /hubs/lobby.
/// An unauthenticated upgrade returns HTTP 401 before the handshake completes.
/// A valid player JWT in the access_token query string connects successfully.
/// </summary>
[Collection("Lobby")]
[Trait("Category", "Integration")]
public sealed class HubAuthTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private LobbyTestApp _app = default!;

    public HubAuthTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _app = new LobbyTestApp();
        await _app.StartAsync(_pg, _redis);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    [Fact(DisplayName = "SC#2: unauthenticated WebSocket upgrade to /hubs/lobby returns HTTP 401 before handshake")]
    public async Task Unauthenticated_Upgrade_Returns_401_Before_Handshake()
    {
        // Build a hub connection with no access token — the negotiate endpoint will reject it.
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/lobby", o =>
            {
                o.HttpMessageHandlerFactory = _ => _app.Server.CreateHandler();
                // No AccessTokenProvider — unauthenticated
            })
            .Build();

        // The negotiate POST to /hubs/lobby/negotiate should return 401.
        // HubConnection.StartAsync throws an HttpRequestException with status 401.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => connection.StartAsync());

        Assert.NotNull(ex);
        // The status code embedded in the message or StatusCode property must indicate 401.
        Assert.True(
            ex.StatusCode == HttpStatusCode.Unauthorized ||
            (ex.Message != null && ex.Message.Contains("401")),
            $"Expected 401 Unauthorized but got: {ex.StatusCode} / {ex.Message}");

        await connection.DisposeAsync();
    }

    [Fact(DisplayName = "SC#2: valid player JWT in access_token query string connects to /hubs/lobby")]
    public async Task Valid_PlayerJwt_Connects_Successfully()
    {
        var playerId = Guid.NewGuid();
        _app.EnsurePlayerRow(playerId);

        var connection = _app.ConnectLobbyHubAsync(playerId);
        try
        {
            // Should not throw — the JWT is valid.
            await connection.StartAsync();
            Assert.Equal(HubConnectionState.Connected, connection.State);
        }
        finally
        {
            await connection.StopAsync();
            await connection.DisposeAsync();
        }
    }
}
