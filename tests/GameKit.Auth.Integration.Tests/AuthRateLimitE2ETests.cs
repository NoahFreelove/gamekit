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
/// ROADMAP success criterion #6 (e2e): bursts that cross the Auth rate-limit thresholds
/// return 429 with Retry-After. Separate test class so each burst starts with a cold partition
/// and isn't polluted by the OK/FAIL volume of <see cref="AuthEndpointsE2ETests"/>.
/// </summary>
[Collection("Auth")]
[Trait("Category", "Integration")]
public sealed class AuthRateLimitE2ETests
{
    private readonly PostgresFixture _pg;
    private readonly WireMockFixture _wm;

    public AuthRateLimitE2ETests(PostgresFixture pg, RedisFixture redis, WireMockFixture wm)
    {
        _pg = pg;
        _ = redis;
        _wm = wm;
    }

    [Fact]
    public async Task Login_11th_Request_In_Same_Window_Returns_429()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        // Unique fingerprint keeps this test's partition cold across suite runs (IP is shared across
        // TestServer and can accumulate counts from sibling tests, but IP+fp is unique).
        var fp = $"dev-login-burst-{Guid.NewGuid():N}";
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", fp);

        for (int i = 0; i < 10; i++)
        {
            var ok = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var rejected = await host.Client.PostAsJsonAsync("/auth/login/guest", new LoginRequest(null, null));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        // The OnRejected hook ought to set Retry-After; some test-host configurations can also
        // surface it via RateLimiter metadata directly. Accept either.
        var hasRetryAfter = rejected.Headers.RetryAfter is not null
            || rejected.Headers.Contains("Retry-After");
        Assert.True(hasRetryAfter);
    }

    [Fact]
    public async Task Register_6th_Request_In_Same_Window_Returns_429()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        var fp = $"dev-register-burst-{Guid.NewGuid():N}";
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", fp);

        for (int i = 0; i < 5; i++)
        {
            var ok = await host.Client.PostAsJsonAsync("/auth/register",
                new RegisterRequest($"user-{Guid.NewGuid():N}".Substring(0, 16), "strong-pw-12ch", null));
            // Successful register = 200; collision on CITEXT username = 409. Both consume a permit.
            Assert.True(
                ok.StatusCode == HttpStatusCode.OK || ok.StatusCode == HttpStatusCode.Conflict,
                $"unexpected status {ok.StatusCode} on register burst iteration {i}");
        }

        var rejected = await host.Client.PostAsJsonAsync("/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}".Substring(0, 16), "strong-pw-12ch", null));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task Refresh_61st_Request_In_Same_Window_Returns_429()
    {
        _wm.ResetDefaultStubs();
        await using var host = new AuthTestHost();
        await host.StartAsync(_pg, _wm);
        var fp = $"dev-refresh-burst-{Guid.NewGuid():N}";
        host.Client.DefaultRequestHeaders.Add("X-GameKit-Device", fp);

        // Use an obviously-invalid token so we don't have to generate 61 valid refreshes.
        // Every /auth/refresh call consumes a permit regardless of result (the rate limiter
        // runs in the pipeline before the endpoint filter). Invalid tokens return 401.
        for (int i = 0; i < 60; i++)
        {
            var r = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest("nope-not-a-token"));
            // 401 (unknown_refresh) is the expected happy-path for an invalid token inside the burst.
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        var rejected = await host.Client.PostAsJsonAsync("/auth/refresh", new RefreshRequest("nope-not-a-token"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }
}
