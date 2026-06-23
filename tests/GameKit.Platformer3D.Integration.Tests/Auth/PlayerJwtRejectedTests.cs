// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.Auth.Http.Contracts;
using GameKit.Core.Entities;
using GameKit.Core.Http.Contracts;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests.Auth;

/// <summary>
/// R7 must-NOT: Verifies that a player JWT is rejected (401 or 403) when used to call
/// <c>POST /api/sessions/{id}/complete</c>. Only the service-token role (GameKitServiceToken
/// scheme with <c>RequiresServiceToken</c> policy) may complete sessions.
/// </summary>
/// <remarks>
/// The <c>RequiresServiceToken</c> policy is wired by <c>SessionEndpoints.MapSessions</c>
/// (confirmed: <c>src/GameKit.Core/Http/SessionEndpoints.cs</c>). A player Bearer JWT
/// (issued by GameKit's JwtBearer scheme) does not carry the <c>service-account</c> role
/// — this test asserts the privilege boundary is enforced end-to-end.
/// </remarks>
[Collection("Platformer3D")]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class PlayerJwtRejectedTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private PlatformerTestApp _app = default!;

    public PlayerJwtRejectedTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _app = new PlatformerTestApp();
        await _app.StartAsync(_pg, _redis);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    [Fact(DisplayName = "R7/must-NOT: Player JWT on POST /api/sessions/{id}/complete → 401 or 403")]
    public async Task PlayerJwt_On_SessionComplete_Returns_401Or403()
    {
        // Arrange: create a player and obtain a player JWT (NOT a service token).
        var playerId = Guid.NewGuid();
        _app.EnsurePlayerRow(playerId);

        using var client = _app.CreateAuthenticatedClient(playerId, isGuest: true);

        // A random session id — the auth check fires before any session lookup.
        var sessionId = Guid.NewGuid();

        var completeRequest = new SessionCompleteRequest(
            Participants: new List<SessionCompleteParticipant>
            {
                new SessionCompleteParticipant(
                    PlayerId: playerId,
                    Team: 0,
                    Result: SessionResult.Win,
                    Score: 45000),
            });

        // Act: attempt to complete a session using a player Bearer JWT.
        // Must provide Idempotency-Key header (IdempotencyKeyEndpointFilter validates it).
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"player-jwt-test-{sessionId}");
        var resp = await client.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/complete",
            completeRequest);

        // Assert: must be rejected (privilege boundary — player cannot self-report results).
        Assert.True(
            resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401 or 403 but got {(int)resp.StatusCode} {resp.StatusCode}. " +
            "Player JWT must not be accepted on session-complete (R7 must-NOT).");
    }

    [Fact(DisplayName = "R7/must-NOT: Unauthenticated request on POST /api/sessions/{id}/complete → 401")]
    public async Task Unauthenticated_On_SessionComplete_Returns_401()
    {
        // Arrange: no auth header.
        var sessionId = Guid.NewGuid();
        _app.Client.DefaultRequestHeaders.Authorization = null;

        var completeRequest = new SessionCompleteRequest(
            Participants: new List<SessionCompleteParticipant>
            {
                new SessionCompleteParticipant(
                    PlayerId: Guid.NewGuid(),
                    Team: 0,
                    Result: SessionResult.Win,
                    Score: 45000),
            });

        using var anonClient = _app.Server.CreateClient();
        anonClient.DefaultRequestHeaders.Add("Idempotency-Key", $"anon-test-{sessionId}");

        // Act: no Bearer token.
        var resp = await anonClient.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/complete",
            completeRequest);

        // Assert: 401 (not authenticated at all).
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact(DisplayName = "R7/must-NOT: Guest JWT on POST /api/sessions/{id}/complete → 401 or 403")]
    public async Task GuestJwt_On_SessionComplete_Returns_401Or403()
    {
        // Arrange: obtain a real guest JWT via the guest login endpoint.
        var loginResp = await _app.Client.PostAsJsonAsync(
            "/auth/login/guest",
            new LoginRequest(Username: null, Password: null));
        loginResp.EnsureSuccessStatusCode();

        var body = await loginResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        // camelCase: TokenResponse.AccessToken → accessToken (ASP.NET Core minimal API default)
        var guestToken = body.GetProperty("accessToken").GetString()!;

        var sessionId = Guid.NewGuid();

        using var guestClient = _app.Server.CreateClient();
        guestClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", guestToken);
        guestClient.DefaultRequestHeaders.Add("Idempotency-Key", $"guest-jwt-test-{sessionId}");

        var completeRequest = new SessionCompleteRequest(
            Participants: new List<SessionCompleteParticipant>
            {
                new SessionCompleteParticipant(
                    PlayerId: Guid.NewGuid(),
                    Team: 0,
                    Result: SessionResult.Win,
                    Score: 30000),
            });

        // Act: guest JWT on session-complete.
        var resp = await guestClient.PostAsJsonAsync(
            $"/api/sessions/{sessionId}/complete",
            completeRequest);

        // Assert: rejected (guest player JWT is still a player JWT, not a service token).
        Assert.True(
            resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401 or 403 but got {(int)resp.StatusCode} {resp.StatusCode}. " +
            "Guest JWT must not be accepted on session-complete (R7 must-NOT).");
    }
}
