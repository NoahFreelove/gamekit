// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
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

    // ─── Form-encoded POST /admin/login ──────────────────────────────────────────────
    // The static-SSR Blazor login page submits a real HTML form to /admin/login (not the
    // JSON /admin/api/login) so the BROWSER receives the Set-Cookie. AdminFormEndpoints
    // returns 302 on both success and failure (UX: redirect-back-to-login with ?error=...).
    // Tests below mirror the JSON tests but exercise the form pathway end-to-end including
    // the antiforgery token round-trip.

    [Fact]
    public async Task FormLogin_ValidCredentials_Returns302WithCookie()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var (csrfCookie, token) = await GetAntiforgeryAsync(host.Client);
        var resp = await PostFormAsync(host.Client, "/admin/login/submit",
            new Dictionary<string, string>
            {
                ["Username"] = "root",
                ["Password"] = "hunter2hunter2",
                ["RememberMe"] = "false",
                ["__RequestVerificationToken"] = token,
            },
            csrfCookie);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/admin", resp.Headers.Location?.ToString());
        Assert.True(resp.Headers.Contains("Set-Cookie"), "Form login must Set-Cookie on success.");
        var cookies = resp.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("gk_admin_session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FormLogin_InvalidCredentials_Returns302ToErrorPage_NoSessionCookie()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var (csrfCookie, token) = await GetAntiforgeryAsync(host.Client);
        var resp = await PostFormAsync(host.Client, "/admin/login/submit",
            new Dictionary<string, string>
            {
                ["Username"] = "root",
                ["Password"] = "wrong",
                ["RememberMe"] = "false",
                ["__RequestVerificationToken"] = token,
            },
            csrfCookie);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location?.ToString();
        Assert.NotNull(location);
        Assert.Contains("/admin/login?error=invalid", location, StringComparison.Ordinal);
        if (resp.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            Assert.DoesNotContain(cookies, c => c.StartsWith("gk_admin_session=", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task FormLogin_PreservesReturnUrl_OnFailureRedirect()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var (csrfCookie, token) = await GetAntiforgeryAsync(host.Client);
        var resp = await PostFormAsync(host.Client, "/admin/login/submit",
            new Dictionary<string, string>
            {
                ["Username"] = "root",
                ["Password"] = "wrong",
                ["RememberMe"] = "false",
                ["ReturnUrl"] = "/admin/audit",
                ["__RequestVerificationToken"] = token,
            },
            csrfCookie);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location?.ToString();
        Assert.NotNull(location);
        // Both the error code AND the original ReturnUrl must round-trip so the user lands
        // back on their intended destination after correcting the password.
        Assert.Contains("error=invalid", location, StringComparison.Ordinal);
        Assert.Contains("ReturnUrl=", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("/admin/audit"), location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormLogin_HonorsSafeReturnUrl_OnSuccessRedirect()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var (csrfCookie, token) = await GetAntiforgeryAsync(host.Client);
        var resp = await PostFormAsync(host.Client, "/admin/login/submit",
            new Dictionary<string, string>
            {
                ["Username"] = "root",
                ["Password"] = "hunter2hunter2",
                ["RememberMe"] = "false",
                ["ReturnUrl"] = "/admin/audit",
                ["__RequestVerificationToken"] = token,
            },
            csrfCookie);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/admin/audit", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task FormLogin_RejectsOpenRedirect_FallsBackToAdminRoot()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var (csrfCookie, token) = await GetAntiforgeryAsync(host.Client);
        var resp = await PostFormAsync(host.Client, "/admin/login/submit",
            new Dictionary<string, string>
            {
                ["Username"] = "root",
                ["Password"] = "hunter2hunter2",
                // Protocol-relative URL — SafeReturnUrl must reject this and fall back to /admin.
                ["ReturnUrl"] = "//evil.example.com/phish",
                ["__RequestVerificationToken"] = token,
            },
            csrfCookie);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/admin", resp.Headers.Location?.ToString());
    }

    [Fact]
    public async Task FormLogin_MissingAntiforgeryToken_Returns302UnavailableError()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // Skip the GET — we have no antiforgery cookie OR token. The middleware must reject.
        var resp = await PostFormAsync(host.Client, "/admin/login/submit",
            new Dictionary<string, string>
            {
                ["Username"] = "root",
                ["Password"] = "hunter2hunter2",
            },
            csrfCookie: null);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("error=unavailable", resp.Headers.Location?.ToString() ?? "", StringComparison.Ordinal);
    }

    /// <summary>
    /// GETs <c>/admin/login</c> and returns both the <c>gk_admin_csrf</c> cookie value (so the
    /// caller can attach it to the next request) and the <c>__RequestVerificationToken</c>
    /// from the rendered form. The two are paired by the antiforgery middleware.
    /// </summary>
    private static async Task<(string CsrfCookie, string Token)> GetAntiforgeryAsync(HttpClient client)
    {
        var get = await client.GetAsync("/admin/login");
        get.EnsureSuccessStatusCode();
        var setCookie = get.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("gk_admin_csrf=", StringComparison.Ordinal));
        // Strip everything after the first ';' so we keep only "name=value".
        var cookieValue = setCookie.Split(';', 2)[0];
        var html = await get.Content.ReadAsStringAsync();
        var m = Regex.Match(html, @"name=""__RequestVerificationToken""\s+(?:type=""hidden""\s+)?value=""([^""]+)""");
        if (!m.Success)
        {
            // Try the alternate attribute order Blazor sometimes emits.
            m = Regex.Match(html, @"value=""([^""]+)""\s+name=""__RequestVerificationToken""");
        }
        Assert.True(m.Success, $"GetAntiforgeryAsync: __RequestVerificationToken not found in /admin/login HTML. First 500 chars:\n{html[..Math.Min(500, html.Length)]}");
        return (cookieValue, m.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string url,
        IDictionary<string, string> form,
        string? csrfCookie)
    {
        using var content = new FormUrlEncodedContent(form);
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (csrfCookie is not null) req.Headers.Add("Cookie", csrfCookie);
        return await client.SendAsync(req);
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
