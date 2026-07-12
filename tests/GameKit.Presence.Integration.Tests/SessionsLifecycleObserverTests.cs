// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using GameKit.Core.Entities;
using GameKit.Presence;
using GameKit.TestFixtures;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Presence.Integration.Tests;

/// <summary>
/// End-to-end integration test proving the Plan 06-04 + 06-05 wire-up empirically validates
/// the ROADMAP SC#1 authoritative wording (game-server-authoritative in-match transition).
/// </summary>
/// <remarks>
/// <para>
/// These three tests exercise the full cross-package observer chain:
/// <list type="number">
///   <item><see cref="InMatchSetByStart"/> — POST /api/sessions/{id}/start fires
///       <c>PresenceSessionObserver.OnSessionStartedAsync</c> inside the transaction;
///       Redis <c>presence:{playerId}</c> = <c>in_match</c> for every participant.</item>
///   <item><see cref="InMatchClearedByComplete"/> — POST /api/sessions/{id}/complete fires
///       <c>PresenceSessionObserver.OnSessionCompletedAsync</c>; in-match cleared back to <c>online</c>.</item>
///   <item><see cref="InMatchClearedByAbandon"/> — POST /api/sessions/{id}/abandon fires
///       <c>PresenceSessionObserver.OnSessionAbandonedAsync</c>; in-match cleared back to <c>online</c>.</item>
/// </list>
/// </para>
/// <para>
/// PRES-05 acceptance criterion: game-server is the authoritative trigger; presence inference
/// is never the trigger. These tests are the empirical proof.
/// </para>
/// </remarks>
[Collection("Presence")]
[Trait("Category", "Integration")]
public sealed class SessionsLifecycleObserverTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public SessionsLifecycleObserverTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact]
    public async Task InMatchSetByStart()
    {
        await using var app = new SessionLifecycleTestApp();
        await app.StartAsync(_pg, _redis);

        var (sessionId, p1Id, p2Id) = await SeedPendingSessionAsync(app.ConnectionString, "lifecycle-ladder");

        using var client = await app.CreateServiceTokenClient("game-server-start");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/start")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // PresenceSessionObserver.OnSessionStartedAsync wrote presence:{playerId} = "in_match".
        var db = app.Multiplexer.GetDatabase();
        await AssertPresenceValueAsync(db, p1Id, PresenceValues.InMatch);
        await AssertPresenceValueAsync(db, p2Id, PresenceValues.InMatch);
    }

    [Fact]
    public async Task InMatchClearedByComplete()
    {
        await using var app = new SessionLifecycleTestApp();
        await app.StartAsync(_pg, _redis);

        var (sessionId, p1Id, p2Id) = await SeedPendingSessionAsync(app.ConnectionString, "lifecycle-ladder");

        using var startClient = await app.CreateServiceTokenClient("game-server-complete");

        // Step 1: /start to reach the InMatch baseline.
        using (var startReq = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/start"))
        {
            startReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var startResp = await startClient.SendAsync(startReq);
            Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        }

        var db = app.Multiplexer.GetDatabase();
        await AssertPresenceValueAsync(db, p1Id, PresenceValues.InMatch);

        // Step 2: /complete with a minimal participants body.
        var completeBody = $$"""
            {
              "participants": [
                { "playerId": "{{p1Id}}", "team": 0, "result": 0, "score": 1 },
                { "playerId": "{{p2Id}}", "team": 1, "result": 1, "score": 0 }
              ]
            }
            """;
        using var completeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
        completeReq.Headers.Add("Idempotency-Key", $"complete-{Guid.NewGuid():N}");
        completeReq.Content = new StringContent(completeBody, Encoding.UTF8, "application/json");
        var completeResp = await startClient.SendAsync(completeReq);
        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);

        // PresenceSessionObserver.OnSessionCompletedAsync wrote presence:{playerId} = "online".
        await AssertPresenceValueAsync(db, p1Id, PresenceValues.Online);
        await AssertPresenceValueAsync(db, p2Id, PresenceValues.Online);
    }

    [Fact]
    public async Task InMatchClearedByAbandon()
    {
        await using var app = new SessionLifecycleTestApp();
        await app.StartAsync(_pg, _redis);

        var (sessionId, p1Id, p2Id) = await SeedPendingSessionAsync(app.ConnectionString, "lifecycle-ladder");

        using var client = await app.CreateServiceTokenClient("game-server-abandon");

        // Step 1: /start to reach the InMatch baseline.
        using (var startReq = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/start"))
        {
            startReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var startResp = await client.SendAsync(startReq);
            Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        }

        var db = app.Multiplexer.GetDatabase();
        await AssertPresenceValueAsync(db, p1Id, PresenceValues.InMatch);

        // Step 2: /abandon — observer clears in-match.
        using var abandonReq = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/abandon")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        var abandonResp = await client.SendAsync(abandonReq);
        Assert.Equal(HttpStatusCode.OK, abandonResp.StatusCode);

        // PresenceSessionObserver.OnSessionAbandonedAsync called WriteOnlineAsync — key is "online"
        // (with refreshed TTL, mirroring the heartbeat shape).
        await AssertPresenceValueAsync(db, p1Id, PresenceValues.Online);
        await AssertPresenceValueAsync(db, p2Id, PresenceValues.Online);
    }

    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static async Task AssertPresenceValueAsync(IDatabase db, Guid playerId, string expected)
    {
        var key = PresenceRedisKeys.Player(playerId);
        var value = (string?)await db.StringGetAsync(key);
        Assert.Equal(expected, value);
    }

    /// <summary>Seeds players + ladder + Pending session + 2 participants. Returns ids.</summary>
    private static async Task<(Guid SessionId, Guid P1Id, Guid P2Id)> SeedPendingSessionAsync(
        string cs, string ladderName)
    {
        var sessionId = Guid.NewGuid();
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES ('{p1Id}', 'P1', '{now:O}'), ('{p2Id}', 'P2', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        object? ladderId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"Id\" FROM gamekit.ladders WHERE \"Name\" = '{ladderName}'";
            ladderId = await cmd.ExecuteScalarAsync();
        }
        // The StartupLadderUpserter (Rankings) will have created the ladder by name on host
        // startup (AddLadder("lifecycle-ladder")). If for some reason it's missing, insert one.
        if (ladderId is null)
        {
            var newLadderId = Guid.NewGuid();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
                VALUES ('{newLadderId}', '{ladderName}', 'glicko2', true, '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
            ladderId = newLadderId;
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.game_sessions (""Id"", ""State"", ""LadderId"", ""CreatedAt"")
                VALUES ('{sessionId}', '{nameof(GameSessionState.Pending)}', '{ladderId}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        var sp1Id = Guid.NewGuid();
        var sp2Id = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.session_participants (""Id"", ""SessionId"", ""PlayerId"", ""Team"")
                VALUES ('{sp1Id}', '{sessionId}', '{p1Id}', 0),
                       ('{sp2Id}', '{sessionId}', '{p2Id}', 1)";
            await cmd.ExecuteNonQueryAsync();
        }

        return (sessionId, p1Id, p2Id);
    }
}
