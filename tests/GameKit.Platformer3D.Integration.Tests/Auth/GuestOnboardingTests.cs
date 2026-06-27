// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.Auth.Http.Contracts;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests.Auth;

/// <summary>
/// R8 + must-NOT (no PII): Verifies that <c>POST /auth/login/guest</c> from a fresh session
/// produces an authenticated player with no email / OAuth identity / credential rows, and that
/// the resulting JWT allows the player to enter the platformer matchmaking queue.
/// </summary>
[Collection("Platformer3D")]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class GuestOnboardingTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private PlatformerTestApp _app = default!;

    public GuestOnboardingTests(PostgresFixture pg, RedisFixture redis)
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

    [Fact(DisplayName = "R8: POST /auth/login/guest → 200 OK with access_token + refresh_token")]
    public async Task GuestLogin_Returns_Tokens()
    {
        // Act: POST /auth/login/guest with empty body (LoginRequest with null username/password)
        var resp = await _app.Client.PostAsJsonAsync(
            "/auth/login/guest",
            new LoginRequest(Username: null, Password: null));

        // Assert: 200 with tokens
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // TokenResponse serializes as camelCase: AccessToken → accessToken (ASP.NET Core default)
        Assert.True(body.TryGetProperty("accessToken", out var at),
            "Response missing 'accessToken' (camelCase — ASP.NET Core minimal API default)");
        Assert.False(string.IsNullOrWhiteSpace(at.GetString()),
            "accessToken is empty");

        Assert.True(body.TryGetProperty("refreshToken", out var rt),
            "Response missing 'refreshToken'");
        Assert.False(string.IsNullOrWhiteSpace(rt.GetString()),
            "refreshToken is empty");
    }

    [Fact(DisplayName = "R8/must-NOT: Guest player has no PII — no player_identities or player_credentials rows")]
    public async Task GuestLogin_Creates_Player_With_No_PII()
    {
        // Arrange: login as guest.
        var (playerId, _) = await LoginAsGuestAsync();

        // Assert: no identity rows (no Steam/Discord/OAuth link).
        var identityCount = await _app.CountPlayerIdentitiesAsync(playerId);
        Assert.Equal(0, identityCount);

        // Assert: no credential rows (no password/email).
        var credentialCount = await _app.CountPlayerCredentialsAsync(playerId);
        Assert.Equal(0, credentialCount);
    }

    [Fact(DisplayName = "R8: Guest player has an auto-generated display name (not empty)")]
    public async Task GuestLogin_Player_Has_Auto_Display_Name()
    {
        // Arrange: login as guest.
        var (playerId, _) = await LoginAsGuestAsync();

        // Assert: display name is non-empty and auto-generated.
        var displayName = await _app.GetPlayerDisplayNameAsync(playerId);
        Assert.False(string.IsNullOrWhiteSpace(displayName),
            "Guest player has no display name");

        // GuestOAuthProvider generates "Guest-{playerId-N[..8]}" when no displayName provided.
        Assert.StartsWith("Guest-", displayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "R8: Guest JWT allows entering the platformer matchmaking queue")]
    public async Task GuestJwt_Allows_Enqueue_Into_Platformer_Ladder()
    {
        // Arrange: login as guest, obtain JWT.
        var (_, accessToken) = await LoginAsGuestAsync();

        // Build an authenticated client using the real guest JWT.
        using var client = _app.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Act: enqueue into the platformer ladder.
        var enqueueBody = new EnqueueRequest(
            LadderId: _app.PlatformerLadderId,
            PoolName: null,
            PartyId: null);

        var resp = await client.PostAsJsonAsync("/api/mm/queue", enqueueBody);

        // Assert: accepted (queued).
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("status", out var status));
        Assert.Equal("queued", status.GetString());
    }

    [Fact(DisplayName = "R8: Two separate guest logins produce two distinct player rows")]
    public async Task Two_GuestLogins_Produce_Distinct_Player_Rows()
    {
        var (pid1, _) = await LoginAsGuestAsync();
        var (pid2, _) = await LoginAsGuestAsync();

        Assert.NotEqual(pid1, pid2);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls <c>POST /auth/login/guest</c> and returns <c>(playerId, accessToken)</c>.
    /// Parses the player id from the JWT <c>sub</c> claim.
    /// </summary>
    private async Task<(Guid PlayerId, string AccessToken)> LoginAsGuestAsync()
    {
        var resp = await _app.Client.PostAsJsonAsync(
            "/auth/login/guest",
            new LoginRequest(Username: null, Password: null));
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // ASP.NET Core minimal APIs serialize TokenResponse with camelCase property names by default.
        var accessToken = body.GetProperty("accessToken").GetString()!;

        // Decode the JWT to extract the 'sub' claim (player id).
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(accessToken);
        var sub = token.Subject
            ?? token.Claims.FirstOrDefaultByType("sub")?.Value
            ?? throw new InvalidOperationException("JWT missing 'sub' claim");

        var playerId = Guid.Parse(sub);
        return (playerId, accessToken);
    }
}

// ─── Extension helpers ─────────────────────────────────────────────────────────

file static class ClaimsExtensions
{
    public static System.Security.Claims.Claim? FirstOrDefaultByType(
        this System.Collections.Generic.IEnumerable<System.Security.Claims.Claim> claims,
        string type)
    {
        foreach (var c in claims)
        {
            if (c.Type == type)
                return c;
        }
        return null;
    }
}
