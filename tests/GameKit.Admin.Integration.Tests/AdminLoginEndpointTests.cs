// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Integration tests for <c>POST /admin/api/login</c>: valid credentials set the
/// <c>gk_admin_session</c> cookie via <c>HttpContext.SignInAsync("GameKitAdmin")</c>; invalid
/// credentials return 401 with no cookie; a 6th failed attempt trips the sliding-window
/// rate-limit policy <c>gamekit:admin:login</c> (5/min/IP).
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class AdminLoginEndpointTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public AdminLoginEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    [Fact]
    public async Task ValidCredentials_Set_AdminSessionCookie()
    {
        // Development env so the /admin/login route actually renders and we can assert the cookie
        // shape; Production would 404 anonymous hits. The login API endpoint itself is AllowAnonymous
        // regardless of env — see AdminCookieEvents.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var resp = await host.Client.PostAsJsonAsync("/admin/api/login",
            new { username = "root", password = "hunter2hunter2", rememberMe = false });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.Contains("Set-Cookie"), "Login response must Set-Cookie.");
        var cookies = resp.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("gk_admin_session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidCredentials_Return_401_NoCookie()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var resp = await host.Client.PostAsJsonAsync("/admin/api/login",
            new { username = "root", password = "wrong", rememberMe = false });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            Assert.DoesNotContain(cookies, c => c.StartsWith("gk_admin_session=", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task UnknownUser_Returns_401_WithoutThrowing()
    {
        // T-03-06-03 timing-parity — VerifyPasswordAsync runs BCrypt against DummyHash so wall-clock
        // time matches the hit path, and returns null. Endpoint maps null → 401.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var resp = await host.Client.PostAsJsonAsync("/admin/api/login",
            new { username = "no-such-user", password = "whatever12", rememberMe = false });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task EmptyUsername_Returns_400_ValidationProblem()
    {
        // ValidationEndpointFilter<LoginRequest> fires before the endpoint handler: presence rule
        // on Username is violated when empty, so the filter returns Results.ValidationProblem.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var resp = await host.Client.PostAsJsonAsync("/admin/api/login",
            new { username = "", password = "hunter2hunter2", rememberMe = false });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task RateLimit_After5Failures_Returns429()
    {
        // D-18: gamekit:admin:login is a sliding-window limiter at 5 permits / 1 minute / IP.
        // Under TestServer every request shares the same (null → "unknown") partition key, so a
        // fresh host gives a clean window. After 5 failing logins in quick succession, the 6th
        // must be rejected with TooManyRequests BEFORE the handler runs.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        for (var i = 0; i < 5; i++)
        {
            var r = await host.Client.PostAsJsonAsync("/admin/api/login",
                new { username = "root", password = "wrong", rememberMe = false });
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }

        var throttled = await host.Client.PostAsJsonAsync("/admin/api/login",
            new { username = "root", password = "wrong", rememberMe = false });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    private static void ResetAdminUsers(string connectionString)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "TRUNCATE TABLE gamekit.admin_users";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table does not yet exist — migrations run on first AdminTestHost construction.
        }
    }
}
