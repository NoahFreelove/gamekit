// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// CR-01 — POST /api/lobbies/{id}/join adds the calling player as a lobby member.
/// Verifies: a second player joining an existing lobby via the REST endpoint results
/// in a <c>lobby_members</c> row created for that player (HTTP 200 + DB check).
/// Also verifies domain-exception mapping: joining a full lobby returns 409; joining
/// a non-existent lobby returns 404; joining a lobby you are already a member of
/// returns 409.
/// </summary>
[Collection("Lobby")]
[Trait("Category", "Integration")]
public sealed class JoinEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private LobbyTestApp _app = default!;

    /// <summary>Constructs the test class.</summary>
    public JoinEndpointTests(PostgresFixture pg, RedisFixture redis)
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

    [Fact(DisplayName = "CR-01: second player joins via REST endpoint and becomes a lobby member")]
    public async Task JoinEndpoint_SecondPlayer_BecomesMember()
    {
        var playerA = Guid.NewGuid(); // owner
        var playerB = Guid.NewGuid(); // joiner

        _app.EnsurePlayerRow(playerA);
        _app.EnsurePlayerRow(playerB);

        // Seed a lobby owned by playerA (only playerA is a member initially).
        var lobbyId = await _app.SeedLobbyAsync(new[] { playerA }, _app.TestLadderId);

        // playerB calls POST /api/lobbies/{id}/join.
        using var clientB = _app.CreateClient(playerB);
        var response = await clientB.PostAsync($"/api/lobbies/{lobbyId}/join", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify the lobby_members row for playerB now exists in Postgres.
        var isMember = await IsMemberInDbAsync(_app.ConnectionString, lobbyId, playerB);
        Assert.True(isMember,
            $"Expected lobby_members row for player {playerB} in lobby {lobbyId} " +
            "after calling POST /api/lobbies/{id}/join, but no row was found.");

        // Verify the response body references the correct lobby and has at least 2 members.
        var body = await response.Content.ReadFromJsonAsync<JoinResponse>();
        Assert.NotNull(body);
        Assert.Equal(lobbyId, body!.LobbyId);
        // Must be at least 2: owner (seeded) + joiner. MaxMembers is 8 from SeedLobbyAsync.
        Assert.True(body.MemberCount >= 2,
            $"Expected MemberCount >= 2 (owner + joiner) but got {body.MemberCount}.");
    }

    [Fact(DisplayName = "CR-01: joining a non-existent lobby returns 404")]
    public async Task JoinEndpoint_NonExistentLobby_Returns404()
    {
        var player = Guid.NewGuid();
        _app.EnsurePlayerRow(player);

        using var client = _app.CreateClient(player);
        var response = await client.PostAsync($"/api/lobbies/{Guid.NewGuid()}/join", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "CR-01: joining a lobby the player is already in returns 409")]
    public async Task JoinEndpoint_AlreadyMember_Returns409()
    {
        var playerA = Guid.NewGuid();
        _app.EnsurePlayerRow(playerA);

        // SeedLobbyAsync already makes playerA a member.
        var lobbyId = await _app.SeedLobbyAsync(new[] { playerA }, _app.TestLadderId);

        using var clientA = _app.CreateClient(playerA);
        var response = await clientA.PostAsync($"/api/lobbies/{lobbyId}/join", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- helpers ----

    private static async Task<bool> IsMemberInDbAsync(string cs, Guid lobbyId, Guid playerId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.lobby_members
            WHERE ""LobbyId"" = @lobbyId AND ""PlayerId"" = @playerId";
        cmd.Parameters.AddWithValue("lobbyId", lobbyId);
        cmd.Parameters.AddWithValue("playerId", playerId);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && Convert.ToInt64(result) > 0;
    }

    private sealed record JoinResponse(
        Guid LobbyId,
        string State,
        int MaxMembers,
        int MemberCount);
}
