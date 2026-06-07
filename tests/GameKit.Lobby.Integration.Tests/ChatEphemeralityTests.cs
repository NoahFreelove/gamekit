// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// SC#4 — Ephemeral chat: a chat message sent via the hub reaches all connected members of
/// the same lobby group in real time AND nothing is written to Postgres (no lobby_message%
/// table exists; row counts across all gamekit tables remain unchanged on send).
/// </summary>
[Collection("Lobby")]
[Trait("Category", "Integration")]
public sealed class ChatEphemeralityTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private LobbyTestApp _app = default!;

    public ChatEphemeralityTests(PostgresFixture pg, RedisFixture redis)
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

    [Fact(DisplayName = "SC#4: chat message relayed to other member in real time with no Postgres write")]
    public async Task Chat_Delivered_Realtime_And_No_Postgres_Write()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _app.EnsurePlayerRow(playerA);
        _app.EnsurePlayerRow(playerB);

        // Seed a lobby in ReadyChecking with both players.
        var lobbyId = await _app.SeedLobbyAsync(new[] { playerA, playerB }, _app.TestLadderId);

        var connA = _app.ConnectLobbyHubAsync(playerA);
        var connB = _app.ConnectLobbyHubAsync(playerB);

        var tcs = new TaskCompletionSource<(Guid SenderId, string Message)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Register client B's handler BEFORE starting the connection.
        connB.On<Guid, string>("ReceiveChatMessageAsync", (senderId, message) =>
        {
            tcs.TrySetResult((senderId, message));
        });

        try
        {
            await connA.StartAsync();
            await connB.StartAsync();

            // Both clients join the lobby group.
            await connA.InvokeAsync("JoinLobbyAsync", lobbyId);
            await connB.InvokeAsync("JoinLobbyAsync", lobbyId);

            // Capture row counts before the send.
            var countsBefore = await GetAllTableRowCountsAsync(_app.ConnectionString);

            // Verify no lobby_message% table exists (LOBBY-04 anti-feature).
            var chatTableCount = await GetLobbyMessageTableCountAsync(_app.ConnectionString);
            Assert.Equal(0, chatTableCount);

            // Player A sends a chat message.
            var testMessage = "SC#4-test-message";
            await connA.InvokeAsync("SendChatMessageAsync", lobbyId, testMessage);

            // Player B must receive it within 5 seconds.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var received = await tcs.Task.WaitAsync(cts.Token);

            Assert.Equal(playerA, received.SenderId);
            Assert.Equal(testMessage, received.Message);

            // Capture row counts after the send — NO table should have gained rows.
            var countsAfter = await GetAllTableRowCountsAsync(_app.ConnectionString);

            foreach (var (table, beforeCount) in countsBefore)
            {
                if (countsAfter.TryGetValue(table, out var afterCount))
                {
                    Assert.True(
                        afterCount == beforeCount,
                        $"Table '{table}' gained {afterCount - beforeCount} row(s) during chat send — " +
                        $"chat must be ephemeral (LOBBY-04 anti-feature).");
                }
            }
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

    private static async Task<long> GetLobbyMessageTableCountAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'gamekit' AND table_name LIKE 'lobby_message%'";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : Convert.ToInt64(result);
    }

    private static async Task<Dictionary<string, long>> GetAllTableRowCountsAsync(string cs)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Get all table names in the gamekit schema.
        List<string> tableNames = new();
        await using (var listCmd = conn.CreateCommand())
        {
            listCmd.CommandText = @"SELECT table_name FROM information_schema.tables
                WHERE table_schema = 'gamekit' AND table_type = 'BASE TABLE'";
            await using var reader = await listCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tableNames.Add(reader.GetString(0));
        }

        // Count rows in each table.
        foreach (var table in tableNames)
        {
            await using var countCmd = conn.CreateCommand();
            // Safe: table name comes from information_schema, not user input.
            countCmd.CommandText = $"SELECT COUNT(*) FROM gamekit.\"{table}\"";
            var result = await countCmd.ExecuteScalarAsync();
            counts[table] = result is long l ? l : Convert.ToInt64(result);
        }

        return counts;
    }
}
