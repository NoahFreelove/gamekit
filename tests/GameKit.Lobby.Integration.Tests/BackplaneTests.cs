// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// SC#5 / LOBBY-06 — Cross-instance broadcast via the shared Redis backplane.
/// Two independent <see cref="LobbyTestApp"/> instances (AppA and AppB) share a single
/// Testcontainers Redis. A broadcast from AppA (via clientA) reaches clientB connected to
/// AppB — proving that the Redis backplane routes messages across hub instances.
/// A single-instance test would NOT exercise the backplane; this test uses two separate
/// in-process <see cref="Microsoft.AspNetCore.TestHost.TestServer"/> instances that share
/// the same <see cref="RedisFixture"/> connection string.
/// </summary>
[Collection("Lobby")]
[Trait("Category", "Integration")]
public sealed class BackplaneTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private LobbyTestApp _appA = default!;
    private LobbyTestApp _appB = default!;

    public BackplaneTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        // Both apps share the SAME RedisFixture connection string — same Testcontainers Redis.
        // The IConnectionMultiplexer replacement in LobbyTestApp.StartAsync connects each app
        // to the shared Redis, enabling cross-instance SignalR backplane delivery.
        _appA = new LobbyTestApp();
        _appB = new LobbyTestApp();

        // Start both apps. Each gets its own fresh Postgres database but shares Redis.
        await _appA.StartAsync(_pg, _redis);

        // AppB needs to reference the same player rows as AppA for the lobby membership check.
        // We use AppA's ConnectionString for lobby/player seeding, but AppB gets its own
        // fresh Postgres (it only needs the player + lobby rows for its own lobby service).
        // To share the lobby membership, we start AppB against a FRESH db of its own —
        // but seed the SAME players + lobby via AppA's helpers, and connect the clients
        // as if they were members. Both clients talk to different hub instances but the
        // same Redis backplane relays the broadcast.
        await _appB.StartAsync(_pg, _redis);
    }

    public async Task DisposeAsync()
    {
        await _appA.DisposeAsync();
        await _appB.DisposeAsync();
    }

    [Fact(DisplayName = "SC#5: broadcast from LobbyHub instance A reaches client on instance B via shared Redis backplane")]
    public async Task CrossInstance_Broadcast_Reaches_OtherServer()
    {
        // Seed players and lobbies in both apps' databases so IsMemberAsync succeeds on each.
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _appA.EnsurePlayerRow(playerA);
        _appA.EnsurePlayerRow(playerB);
        _appB.EnsurePlayerRow(playerA);
        _appB.EnsurePlayerRow(playerB);

        // Seed the SAME lobby id in both Postgres databases so JoinLobbyAsync succeeds on both.
        // We pass a fixed lobby id by seeding directly into each app's database.
        var lobbyId = Guid.NewGuid();
        await SeedSharedLobbyAsync(lobbyId, new[] { playerA, playerB }, _appA);
        await SeedSharedLobbyAsync(lobbyId, new[] { playerA, playerB }, _appB);

        // clientA connects to AppA, clientB connects to AppB.
        var connA = _appA.ConnectLobbyHubAsync(playerA);
        var connB = _appB.ConnectLobbyHubAsync(playerB);

        var tcs = new TaskCompletionSource<(Guid SenderId, string Message)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Register clientB's handler before starting — captures the cross-instance broadcast.
        connB.On<Guid, string>("ReceiveChatMessageAsync", (senderId, message) =>
        {
            tcs.TrySetResult((senderId, message));
        });

        try
        {
            await connA.StartAsync();
            await connB.StartAsync();

            // Both clients join the same lobby group on their respective hub instances.
            await connA.InvokeAsync("JoinLobbyAsync", lobbyId);
            await connB.InvokeAsync("JoinLobbyAsync", lobbyId);

            // clientA sends a chat message via AppA's hub instance.
            var testMessage = "SC#5-cross-instance-message";
            await connA.InvokeAsync("SendChatMessageAsync", lobbyId, testMessage);

            // clientB (on AppB) must receive the message delivered through the Redis backplane.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var received = await tcs.Task.WaitAsync(cts.Token);

            Assert.Equal(playerA, received.SenderId);
            Assert.Equal(testMessage, received.Message);
        }
        finally
        {
            await connA.StopAsync();
            await connB.StopAsync();
            await connA.DisposeAsync();
            await connB.DisposeAsync();
        }
    }

    // ---- helpers ----

    /// <summary>
    /// Seeds a specific lobby id into the given app's database so both AppA and AppB can
    /// validate membership (IsMemberAsync) for the shared lobby id used in the backplane test.
    /// </summary>
    private static async Task SeedSharedLobbyAsync(Guid lobbyId, Guid[] members, LobbyTestApp app)
    {
        var ownerId = members[0];
        var now = DateTimeOffset.UtcNow;

        await using var conn = new Npgsql.NpgsqlConnection(app.ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"INSERT INTO gamekit.lobbies
                (""Id"", ""OwnerId"", ""LadderId"", ""State"", ""MaxMembers"", ""CreatedAt"", ""UpdatedAt"")
                VALUES (@id, @ownerId, @ladderId, 1, 8, @now, @now)";
            cmd.Parameters.AddWithValue("id", lobbyId);
            cmd.Parameters.AddWithValue("ownerId", ownerId);
            cmd.Parameters.AddWithValue("ladderId", app.TestLadderId);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var playerId in members)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO gamekit.lobby_members
                (""Id"", ""LobbyId"", ""PlayerId"", ""Ready"", ""JoinedAt"")
                VALUES (@id, @lobbyId, @playerId, false, @now)";
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("lobbyId", lobbyId);
            cmd.Parameters.AddWithValue("playerId", playerId);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
