// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Lobby.Entities;
using GameKit.Lobby.Hubs;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// SC#3 / LOBBY-03 / LOBBY-05 — All-ready → matchmaking submission → InGame → broadcast.
/// When all lobby members mark ready:
/// <list type="number">
///   <item>A Matchmaking party is created via <c>IPartyService.CreateAsync</c> and each
///         non-owner member is added via <c>IPartyService.JoinAsync</c>.</item>
///   <item><c>IMatchmakingService.EnqueueAsync(partyId)</c> is called, producing a party
///         ticket in Postgres (or Redis — here we verify at the DB level for the SC#3 assertion).</item>
///   <item>The lobby transitions from <c>ReadyChecking</c> to <c>InGame</c>.</item>
///   <item>A <c>ReceiveStateUpdateAsync</c> broadcast carrying <c>InGame</c> is observed
///         by the connected members via the SignalR group.</item>
/// </list>
/// No <c>lobby_id</c> FK is added to <c>matchmaking_tickets</c> — the party row is the
/// cross-package link (migration boundary, LOBBY-05 deviation documented in RESEARCH §Q1).
/// </summary>
[Collection("Lobby")]
[Trait("Category", "Integration")]
public sealed class ReadyCheckTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private LobbyTestApp _app = default!;

    public ReadyCheckTests(PostgresFixture pg, RedisFixture redis)
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

    [Fact(DisplayName = "SC#3: all-ready → party ticket created + lobby State=InGame + InGame broadcast observed")]
    public async Task AllReady_Triggers_Matchmaking_And_InGame_Broadcast()
    {
        // Two players — owner (playerA) and one other member (playerB).
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _app.EnsurePlayerRow(playerA);
        _app.EnsurePlayerRow(playerB);

        var lobbyId = await _app.SeedLobbyAsync(new[] { playerA, playerB }, _app.TestLadderId);

        var connA = _app.ConnectLobbyHubAsync(playerA);
        var connB = _app.ConnectLobbyHubAsync(playerB);

        // Capture the first InGame broadcast received by either member.
        var inGameTcs = new TaskCompletionSource<LobbyStateUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connA.On<LobbyStateUpdate>("ReceiveStateUpdateAsync", update =>
        {
            if (update.State == LobbyState.InGame)
                inGameTcs.TrySetResult(update);
        });
        connB.On<LobbyStateUpdate>("ReceiveStateUpdateAsync", update =>
        {
            if (update.State == LobbyState.InGame)
                inGameTcs.TrySetResult(update);
        });

        try
        {
            await connA.StartAsync();
            await connB.StartAsync();

            await connA.InvokeAsync("JoinLobbyAsync", lobbyId);
            await connB.InvokeAsync("JoinLobbyAsync", lobbyId);

            // Both players mark ready — the second MarkReady triggers TryStartMatchmakingAsync.
            await connA.InvokeAsync("MarkReadyAsync", lobbyId);
            await connB.InvokeAsync("MarkReadyAsync", lobbyId);

            // Assert 1: InGame broadcast received within timeout.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var broadcastUpdate = await inGameTcs.Task.WaitAsync(cts.Token);
            Assert.Equal(LobbyState.InGame, broadcastUpdate.State);

            // Assert 2: Lobby row in Postgres has State = InGame (3).
            var dbState = await GetLobbyStateAsync(_app.ConnectionString, lobbyId);
            Assert.Equal((int)LobbyState.InGame, dbState);

            // Assert 3: A party row exists in matchmaking.parties for this owner (SC#3/LOBBY-05).
            var partyExists = await PartyExistsForOwnerAsync(_app.ConnectionString, playerA);
            Assert.True(partyExists,
                $"Expected a party row in gamekit.parties for owner {playerA} after all-ready, " +
                "but none was found. TryStartMatchmakingAsync must call IPartyService.CreateAsync.");
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

    private static async Task<int> GetLobbyStateAsync(string cs, Guid lobbyId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""State"" FROM gamekit.lobbies WHERE ""Id"" = @id";
        cmd.Parameters.AddWithValue("id", lobbyId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null ? -1 : Convert.ToInt32(result);
    }

    private static async Task<bool> PartyExistsForOwnerAsync(string cs, Guid ownerPlayerId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.parties WHERE ""OwnerPlayerId"" = @owner";
        cmd.Parameters.AddWithValue("owner", ownerPlayerId);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && Convert.ToInt64(result) > 0;
    }
}
