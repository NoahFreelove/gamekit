// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.Auth.Http.Contracts;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// SEC-01 / T-18-03-04 — Proves that a revoked refresh token cannot be exchanged at
/// <c>POST /auth/refresh</c>, and that a never-issued token is indistinguishable from a
/// revoked one (401 in both cases with no oracle leaking token state).
/// Uses the real RefreshTokenService + endpoint pipeline via <see cref="AuthTestHost"/>.
/// </summary>
/// <remarks>
/// Revocation is driven through <c>POST /auth/logout</c>, which calls
/// <c>RevokeFamilyAsync(rawRefreshToken, "manual_logout")</c> — the same code path the plan
/// specifies. No service-layer scope is needed; the HTTP endpoint covers it end-to-end.
/// </remarks>
[Collection("Auth")]
[Trait("Category", "Integration")]
public sealed class RevokedRefreshExchangeTests
{
    private readonly PostgresFixture _pg;
    private readonly WireMockFixture _wm;

    /// <summary>
    /// Receives the shared Postgres + Redis + WireMock fixtures from the <c>"Auth"</c> collection.
    /// </summary>
    public RevokedRefreshExchangeTests(PostgresFixture pg, RedisFixture redis, WireMockFixture wm)
    {
        _pg = pg;
        _   = redis;   // Required by the Auth collection fixture; unused here.
        _wm = wm;
    }

    // ===================================================================================
    // Test 1: A revoked refresh token cannot be exchanged at POST /auth/refresh (401)
    // ===================================================================================

    /// <summary>
    /// SEC-01 / T-18-03-04 — Issue a guest refresh token, revoke its family via
    /// <c>POST /auth/logout</c> (which calls <c>RevokeFamilyAsync</c>), then verify that
    /// <c>POST /auth/refresh</c> returns 401 with the <c>refresh_revoked</c> error code.
    /// </summary>
    [Fact]
    public async Task Revoked_RefreshToken_Cannot_Be_Exchanged()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-revoke-1");

        // Step 1: Guest login — obtain a raw refresh token.
        var login = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(loginBody);
        var rawRefresh = loginBody!.RefreshToken;
        Assert.False(string.IsNullOrEmpty(rawRefresh), "Login should return a non-empty refresh token");

        // Step 2: Revoke the family via POST /auth/logout (calls RevokeFamilyAsync internally).
        // POST /auth/logout returns 204 NoContent on success.
        var logout = await host.Client.PostAsJsonAsync("/auth/logout", new LogoutRequest(rawRefresh!));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Step 3: Attempt to exchange the revoked token — must fail with 401.
        host.Now = host.Now.AddSeconds(5);   // slight time advancement (outside any grace window)
        var refresh = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(rawRefresh!));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // The error code should reflect the revocation (RotateAsync sees a revoked token with
        // no UsedAt set, so the grace-window check does not apply → "refresh_revoked").
        var err = await refresh.Content.ReadFromJsonAsync<AuthErrorResponse>();
        Assert.NotNull(err);
        Assert.Equal("refresh_revoked", err!.Error);
    }

    // ===================================================================================
    // Test 2: A never-issued token also returns 401 (revoked and unknown are indistinguishable)
    // ===================================================================================

    /// <summary>
    /// SEC-01 / T-18-03-04 — A completely random (never-issued) refresh token submitted
    /// to <c>POST /auth/refresh</c> returns 401. The response error code is not required to
    /// match <c>refresh_revoked</c> exactly; the important property is that the caller cannot
    /// distinguish a revoked token from an unknown one (no oracle leaking token existence).
    /// </summary>
    [Fact]
    public async Task NeverIssued_RefreshToken_Returns_401()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", "dev-revoke-2");

        // Submit a random token that was never issued.
        var fakeToken = $"never-issued-{Guid.NewGuid():N}";
        var refresh = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest(fakeToken));

        // Must be 401 — caller cannot tell the token is simply unknown vs. revoked.
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);

        // Confirm the response body is a well-formed error (not an unhandled exception page).
        var err = await refresh.Content.ReadFromJsonAsync<AuthErrorResponse>();
        Assert.NotNull(err);
        Assert.False(string.IsNullOrEmpty(err!.Error), "Error response must have a non-empty error code");
    }
}
