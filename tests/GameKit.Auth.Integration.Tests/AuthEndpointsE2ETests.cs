// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.Auth.Http.Contracts;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// End-to-end proofs for the /auth/* HTTP surface. Hosts a WebApplicationFactory-style
/// <see cref="AuthTestHost"/> wired against the shared Postgres + WireMock fixtures. Covers:
/// <list type="bullet">
///   <item>Guest + Password login (ROADMAP success criterion #1, 2 of 4 providers covered e2e).</item>
///   <item>Steam valid assertion (ROADMAP #1, third provider).</item>
///   <item>Steam forged assertion — WireMock returns <c>is_valid:false</c> (ROADMAP #2 e2e).</item>
///   <item>Concurrent refresh within grace + matching fingerprint (ROADMAP #3 e2e — grace arm).</item>
///   <item>Refresh with mismatched fingerprint → family revoke (ROADMAP #3 e2e — revoke arm).</item>
///   <item><c>/auth/me</c> succeeds with valid JWT (proves T-02-15 middleware-order mitigation).</item>
///   <item>Cross-player link collision returns 409 + SHA-256 hash (ROADMAP #5 e2e, T-02-10 mitigation).</item>
/// </list>
/// </summary>
[Collection("Auth")]
[Trait("Category", "Integration")]
public sealed class AuthEndpointsE2ETests
{
    private readonly PostgresFixture _pg;
    private readonly WireMockFixture _wm;

    public AuthEndpointsE2ETests(PostgresFixture pg, RedisFixture redis, WireMockFixture wm)
    {
        _pg = pg;
        _ = redis;   // Required by the Auth collection fixture; unused here.
        _wm = wm;
    }

    // --- ROADMAP Success #1: per-provider e2e ---

    [Fact]
    public async Task Guest_Login_Returns_200_With_Tokens_And_IsGuest_True_Claim()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-guest-1");

        var resp = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        Assert.False(string.IsNullOrEmpty(body.RefreshToken));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.AccessToken);
        Assert.Equal("true", jwt.Claims.Single(c => c.Type == "is_guest").Value);
        Assert.Equal("guest", jwt.Claims.Single(c => c.Type == "provider").Value);
    }

    [Fact]
    public async Task Password_Register_Then_Login_Round_Trip()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-pw-1");

        var uniqueName = $"alice-{Guid.NewGuid():N}".Substring(0, 16);
        var reg = await host.Client.PostAsJsonAsync("/auth/register",
            new RegisterRequest(uniqueName, "hunter2-strong-pw", "Alice"));
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);

        var login = await host.Client.PostAsJsonAsync("/auth/login/password",
            new LoginRequest(uniqueName, "hunter2-strong-pw"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body!.AccessToken);
        Assert.Equal("false", jwt.Claims.Single(c => c.Type == "is_guest").Value);
        Assert.Equal("password", jwt.Claims.Single(c => c.Type == "provider").Value);
    }

    [Fact]
    public async Task Steam_Callback_Valid_Assertion_Returns_Tokens()
    {
        _wm.ResetDefaultStubs();   // default stub: is_valid:true
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-steam-1");

        var steamId = "76561198000001234";
        var qs = $"openid.mode=id_res&openid.claimed_id=https%3A%2F%2Fsteamcommunity.com%2Fopenid%2Fid%2F{steamId}"
            + $"&openid.identity=https%3A%2F%2Fsteamcommunity.com%2Fopenid%2Fid%2F{steamId}"
            + "&openid.op_endpoint=https%3A%2F%2Fsteamcommunity.com%2Fopenid%2Flogin"
            + "&openid.return_to=https%3A%2F%2Fgamekit-test.example.com%2Fauth%2Fcallback%2Fsteam"
            + "&openid.response_nonce=nonce&openid.assoc_handle=h&openid.signed=signed&openid.sig=sig";

        var resp = await host.Client.GetAsync($"/auth/callback/steam?{qs}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task Steam_Callback_Forged_Assertion_Returns_400_InvalidAssertion()
    {
        // ROADMAP Success Criterion #2 (e2e).
        WireMockSteamStubs.StubIsValidFalse(_wm.Server);
        try
        {
            await using var host = new AuthTestHost();
            await host.StartAsync(_pg, _wm);
            host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-steam-forger");

            var qs = "openid.mode=id_res&openid.claimed_id=https%3A%2F%2Fsteamcommunity.com%2Fopenid%2Fid%2F76561198999999999"
                + "&openid.identity=https%3A%2F%2Fsteamcommunity.com%2Fopenid%2Fid%2F76561198999999999"
                + "&openid.op_endpoint=https%3A%2F%2Fsteamcommunity.com%2Fopenid%2Flogin"
                + "&openid.return_to=https%3A%2F%2Fgamekit-test.example.com%2Fauth%2Fcallback%2Fsteam"
                + "&openid.response_nonce=nonce&openid.assoc_handle=h&openid.signed=signed&openid.sig=forged";

            var resp = await host.Client.GetAsync($"/auth/callback/steam?{qs}");
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var err = await resp.Content.ReadFromJsonAsync<AuthErrorResponse>();
            Assert.Equal("invalid_assertion", err!.Error);
            Assert.Equal("steam", err.Provider);
        }
        finally
        {
            _wm.ResetDefaultStubs();
        }
    }

    // --- ROADMAP Success #3: concurrent refresh e2e ---

    [Fact]
    public async Task Refresh_Within_Grace_With_Matching_Fingerprint_Returns_Null_Refresh_Idempotent()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-grace");

        var login = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<TokenResponse>();
        var raw0 = loginBody!.RefreshToken!;

        // Rotate once normally (5 min after login).
        host.Now = host.Now.AddMinutes(5);
        var r1 = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(raw0));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Replay the parent within the 45 s grace window, same X-GameKit-Device.
        host.Now = host.Now.AddSeconds(20);
        var r2 = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(raw0));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var replay = await r2.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.Null(replay!.RefreshToken);   // server's idempotent-replay signal (RefreshTokenService line 132)
        Assert.False(string.IsNullOrEmpty(replay.AccessToken));
    }

    [Fact]
    public async Task Refresh_With_Mismatched_Fingerprint_After_Rotate_Returns_401_Revoked()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-alpha");

        var login = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        var loginBody = await login.Content.ReadFromJsonAsync<TokenResponse>();
        var raw0 = loginBody!.RefreshToken!;

        // First rotation succeeds.
        host.Now = host.Now.AddMinutes(5);
        var r1 = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(raw0));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        // Replay the parent with a DIFFERENT X-GameKit-Device inside the grace window → revoke.
        host.Now = host.Now.AddSeconds(20);
        host.Client.DefaultRequestHeaders.Remove("X-GameKit-Device");
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-ATTACKER");
        var r2 = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(raw0));
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
        var err = await r2.Content.ReadFromJsonAsync<AuthErrorResponse>();
        Assert.Equal("refresh_revoked", err!.Error);
    }

    // --- /auth/me + middleware-ordering proof ---

    [Fact]
    public async Task Me_With_Valid_Jwt_Returns_200_Proving_Middleware_Order()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-me");

        var login = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        var body = await login.Content.ReadFromJsonAsync<TokenResponse>();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        req.Headers.Add("X-GameKit-Device", "dev-me");
        var resp = await host.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);   // T-02-15 mitigation proof
    }

    [Fact]
    public async Task Me_Without_Jwt_Returns_401()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);

        var resp = await host.Client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Unknown_Provider_Returns_400()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-unk");

        var resp = await host.Client.PostAsJsonAsync("/auth/login/nope", new LoginRequest(null, null));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<AuthErrorResponse>();
        Assert.Equal("unknown_provider", err!.Error);
    }

    // --- ROADMAP Success #5: cross-player link collision e2e ---

    [Fact]
    public async Task Link_Cross_Player_Collision_Returns_409_With_Hash_No_Raw_ExternalId()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-A");

        // Player A logs in as guest and links steam.
        var loginA = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        var bodyA = await loginA.Content.ReadFromJsonAsync<TokenResponse>();
        const string sharedSteam = "76561198111111111";

        using (var req = new HttpRequestMessage(HttpMethod.Post, "/auth/link/steam")
        {
            Content = JsonContent.Create(new LinkRequest(sharedSteam)),
        })
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bodyA!.AccessToken);
            req.Headers.Add("X-GameKit-Device", "dev-A");
            var r = await host.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        // Player B logs in as a different guest.
        host.Client.DefaultRequestHeaders.Remove("X-GameKit-Device");
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-B");
        var loginB = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        var bodyB = await loginB.Content.ReadFromJsonAsync<TokenResponse>();

        // Player B tries to link the SAME steam id → 409 identity_already_linked + hash.
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/auth/link/steam")
        {
            Content = JsonContent.Create(new LinkRequest(sharedSteam)),
        })
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bodyB!.AccessToken);
            req.Headers.Add("X-GameKit-Device", "dev-B");
            var r = await host.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var err = await r.Content.ReadFromJsonAsync<AuthErrorResponse>();
            Assert.Equal("identity_already_linked", err!.Error);
            Assert.Equal("steam", err.Provider);
            Assert.False(string.IsNullOrEmpty(err.ExternalIdHash));
            // T-02-10: the raw external id must never appear in the error body.
            Assert.DoesNotContain(sharedSteam, err.ExternalIdHash, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Logout_Then_Refresh_Returns_401()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-logout");

        var login = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens?.RefreshToken);

        using (var req = new HttpRequestMessage(HttpMethod.Post, "/auth/logout")
        {
            Content = JsonContent.Create(new LogoutRequest(tokens!.RefreshToken!)),
        })
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            req.Headers.Add("X-GameKit-Device", "dev-logout");
            var r = await host.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        }

        // The family is revoked. Next rotation attempt returns 401.
        host.Now = host.Now.AddMinutes(1);
        var refresh = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(tokens.RefreshToken!));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }
}
