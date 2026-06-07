// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net.Http;
using System.Net.Http.Json;
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
/// Covers the Open→ReadyChecking edge that was missing before quick fix 260607-j3p.
/// <para>
/// A lobby created through the public REST API (<c>POST /api/lobbies</c>) was stuck in
/// <c>LobbyState.Open</c> forever. The LOBBY-03 ready-check→matchmaking→InGame flow was
/// unreachable in real usage because <c>LobbyService</c> had no Open→ReadyChecking transition.
/// Existing tests in <see cref="ReadyCheckTests"/> only exercised the flow because
/// <c>SeedLobbyAsync</c> inserts <c>State=1 (ReadyChecking)</c> directly via raw Npgsql,
/// masking the missing edge.
/// </para>
/// <para>
/// These tests do NOT use <c>SeedLobbyAsync</c> — every lobby is created via the public REST API.
/// </para>
/// </summary>
[Collection("Lobby")]
[Trait("Category", "Integration")]
public sealed class OpenToReadyCheckTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private LobbyTestApp _app = default!;

    /// <summary>Constructs the test class.</summary>
    public OpenToReadyCheckTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _app = new LobbyTestApp();
        await _app.StartAsync(_pg, _redis);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    [Fact(DisplayName = "Open→ReadyChecking→InGame: maxMembers=2 lobby created + filled via public API reaches InGame + party created")]
    public async Task FullLifecycle_FromOpen_Through_InGame_With_PartyCreated()
    {
        // Two players: owner (playerA), joiner (playerB).
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _app.EnsurePlayerRow(playerA);
        _app.EnsurePlayerRow(playerB);

        // Step 1: Owner creates the lobby via REST — State must be "Open".
        using var clientA = _app.CreateClient(playerA);
        var createResp = await clientA.PostAsJsonAsync("/api/lobbies", new
        {
            maxMembers = 2,
            ladderId = _app.TestLadderId,
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, createResp.StatusCode);

        var createBody = await createResp.Content.ReadFromJsonAsync<CreateLobbyResponse>();
        Assert.NotNull(createBody);
        Assert.Equal("Open", createBody!.State);
        var lobbyId = createBody.LobbyId;

        // Step 2: Register hub handlers BEFORE joining, then subscribe the owner to the lobby group.
        var readyCheckingTcs = new TaskCompletionSource<LobbyStateUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inGameTcs = new TaskCompletionSource<LobbyStateUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var connA = _app.ConnectLobbyHubAsync(playerA);
        connA.On<LobbyStateUpdate>("ReceiveStateUpdateAsync", update =>
        {
            if (update.State == LobbyState.ReadyChecking)
                readyCheckingTcs.TrySetResult(update);
            if (update.State == LobbyState.InGame)
                inGameTcs.TrySetResult(update);
        });

        try
        {
            await connA.StartAsync();
            // Owner is already a member (CreateLobbyAsync adds them); IsMemberAsync passes.
            await connA.InvokeAsync("JoinLobbyAsync", lobbyId);

            // Step 3: playerB REST-joins — this fills the lobby (count goes from 1 to 2 = MaxMembers).
            // The fix: JoinLobbyAsync should now transition Open→ReadyChecking and broadcast.
            using var clientB = _app.CreateClient(playerB);
            var joinResp = await clientB.PostAsync($"/api/lobbies/{lobbyId}/join", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, joinResp.StatusCode);

            var joinBody = await joinResp.Content.ReadFromJsonAsync<JoinLobbyResponse>();
            Assert.NotNull(joinBody);
            // Member-count must be 2 (owner + joiner), never 3 (the old over-count bug).
            Assert.Equal(2, joinBody!.MemberCount);

            // Step 4: Assert DB state = ReadyChecking (1) and owner observed the broadcast.
            var dbStateAfterFill = await GetLobbyStateAsync(_app.ConnectionString, lobbyId);
            Assert.Equal((int)LobbyState.ReadyChecking, dbStateAfterFill);

            using var rcCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var rcUpdate = await readyCheckingTcs.Task.WaitAsync(rcCts.Token);
            Assert.Equal(LobbyState.ReadyChecking, rcUpdate.State);

            // Step 5: Drive to InGame — connect playerB's hub and both mark ready.
            var connB = _app.ConnectLobbyHubAsync(playerB);
            connB.On<LobbyStateUpdate>("ReceiveStateUpdateAsync", update =>
            {
                if (update.State == LobbyState.InGame)
                    inGameTcs.TrySetResult(update);
            });

            try
            {
                await connB.StartAsync();
                await connB.InvokeAsync("JoinLobbyAsync", lobbyId);

                await connA.InvokeAsync("MarkReadyAsync", lobbyId);
                await connB.InvokeAsync("MarkReadyAsync", lobbyId);

                // Step 6: Assert InGame broadcast received, DB state = InGame (3), party exists.
                using var igCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var igUpdate = await inGameTcs.Task.WaitAsync(igCts.Token);
                Assert.Equal(LobbyState.InGame, igUpdate.State);

                var dbStateAfterReady = await GetLobbyStateAsync(_app.ConnectionString, lobbyId);
                Assert.Equal((int)LobbyState.InGame, dbStateAfterReady);

                var partyCreated = await PartyExistsForOwnerAsync(_app.ConnectionString, playerA);
                Assert.True(partyCreated,
                    $"Expected a party row in gamekit.parties for owner {playerA} after all-ready, " +
                    "but none was found. TryStartMatchmakingAsync must call IPartyService.CreateAsync.");
            }
            finally
            {
                await connB.StopAsync();
                await connB.DisposeAsync();
            }
        }
        finally
        {
            await connA.StopAsync();
            await connA.DisposeAsync();
        }
    }

    [Fact(DisplayName = "member count is correct (not over-counted) after a REST join")]
    public async Task MemberCount_IsNotOverCounted_AfterRestJoin()
    {
        var playerA = Guid.NewGuid(); // owner
        var playerB = Guid.NewGuid(); // joiner

        _app.EnsurePlayerRow(playerA);
        _app.EnsurePlayerRow(playerB);

        // Step 1: Owner creates a maxMembers=8 lobby — large enough that the join does NOT fill it.
        // This keeps the lobby Open after the join, isolating the member-count regression check.
        using var clientA = _app.CreateClient(playerA);
        var createResp = await clientA.PostAsJsonAsync("/api/lobbies", new
        {
            maxMembers = 8,
            ladderId = _app.TestLadderId,
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, createResp.StatusCode);

        var createBody = await createResp.Content.ReadFromJsonAsync<CreateLobbyResponse>();
        Assert.NotNull(createBody);
        var lobbyId = createBody!.LobbyId;

        // Step 2: playerB REST-joins.
        using var clientB = _app.CreateClient(playerB);
        var joinResp = await clientB.PostAsync($"/api/lobbies/{lobbyId}/join", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, joinResp.StatusCode);

        var joinBody = await joinResp.Content.ReadFromJsonAsync<JoinLobbyResponse>();
        Assert.NotNull(joinBody);
        // Regression guard: must be 2, NOT 3 (EF fixup double-add over-count bug).
        Assert.Equal(2, joinBody!.MemberCount);

        // Step 3: Also verify via GET /api/lobbies/{id} that memberCount == 2 and state == "Open".
        var getResp = await clientA.GetAsync($"/api/lobbies/{lobbyId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, getResp.StatusCode);

        var getBody = await getResp.Content.ReadFromJsonAsync<GetLobbyResponse>();
        Assert.NotNull(getBody);
        Assert.Equal(2, getBody!.MemberCount);
        Assert.Equal("Open", getBody.State);
    }

    // ---- response shapes ----

    private sealed record CreateLobbyResponse(
        Guid LobbyId,
        string State,
        int MaxMembers,
        string? RegionName,
        Guid? LadderId,
        DateTimeOffset CreatedAt);

    private sealed record JoinLobbyResponse(
        Guid LobbyId,
        string State,
        int MaxMembers,
        int MemberCount);

    private sealed record GetLobbyResponse(
        Guid LobbyId,
        Guid OwnerId,
        Guid? LadderId,
        string State,
        int MaxMembers,
        string? RegionName,
        int MemberCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    // ---- raw-Npgsql helpers (mirrors ReadyCheckTests) ----

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
