// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
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
/// ROADMAP Phase 3 Success Criterion #5: CSRF and CSP integration tests confirm mutations
/// require a valid anti-CSRF token and that admin pages ship a CSP header blocking framing.
/// Anchors:
/// <list type="bullet">
///   <item>Every admin response carries a <c>Content-Security-Policy</c> header with the seven
///   mandatory directives (D-15 / RESEARCH §UI Hardening).</item>
///   <item>The per-request nonce changes between sequential responses.</item>
///   <item>Non-admin paths never receive the CSP header (the middleware scopes to <c>/admin/*</c>).</item>
///   <item>A POST mutation lacking the antiforgery token returns <c>400</c> with body
///   <c>csrf_validation_failed</c> (D-16, AntiforgeryValidationFilter).</item>
/// </list>
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class CspAndAntiforgeryTests
{
    // The seven mandatory CSP directives that AdminCspNonceMiddleware emits. Drawn verbatim
    // from src/GameKit.Admin.UI/Middleware/AdminCspNonceMiddleware.cs (sans the per-request
    // nonce which is asserted separately by ExtractNonce + the uniqueness fact below).
    private static readonly string[] MandatoryDirectives =
    {
        "default-src 'self'",
        "frame-ancestors 'none'",
        "script-src 'self'",
        "style-src 'self'",
        "base-uri 'self'",
        "form-action 'self'",
        "img-src 'self' data:",
    };

    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public CspAndAntiforgeryTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetTables(_pg.OwnerConnectionString);
    }

    [Fact(DisplayName = "SC#5: /admin/login response includes Content-Security-Policy with all 7 mandatory directives")]
    public async Task AdminResponse_Has_ContentSecurityPolicy_Header()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var resp = await host.Client.GetAsync("/admin/login");
        Assert.True(resp.Headers.TryGetValues("Content-Security-Policy", out var vals),
            "Admin response must carry Content-Security-Policy header.");
        var csp = string.Join(";", vals);
        foreach (var directive in MandatoryDirectives)
        {
            Assert.Contains(directive, csp);
        }
    }

    [Fact(DisplayName = "SC#5: Two sequential admin responses carry different per-request nonces")]
    public async Task TwoSequentialAdminResponses_Have_DifferentNonces()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var r1 = await host.Client.GetAsync("/admin/login");
        var r2 = await host.Client.GetAsync("/admin/login");
        var n1 = ExtractNonce(r1.Headers.GetValues("Content-Security-Policy").First());
        var n2 = ExtractNonce(r2.Headers.GetValues("Content-Security-Policy").First());
        Assert.NotEqual(string.Empty, n1);
        Assert.NotEqual(string.Empty, n2);
        Assert.NotEqual(n1, n2);
    }

    [Fact(DisplayName = "SC#5: CSP header is scoped to /admin/* — non-admin path responses do NOT receive it")]
    public async Task NonAdminResponse_Has_No_CSP_Header()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // Use a public player-side endpoint that responds without an auth header. /auth/login/guest
        // accepts anonymous POST. Whether the response is 200 or a validation failure is irrelevant
        // to the question "does AdminCspNonceMiddleware scope itself to /admin/*?" — what matters
        // is that the response carries no CSP header.
        var resp = await host.Client.PostAsJsonAsync("/auth/login/guest",
            new { device = "csp-scope-probe" });
        Assert.False(resp.Headers.Contains("Content-Security-Policy"),
            "CSP header must be scoped to /admin/* requests; non-admin paths must not receive it.");
    }

    [Fact(DisplayName = "SC#5: POST /admin/api/players/{id}/ban without antiforgery token returns 400 csrf_validation_failed")]
    public async Task BanMutation_Without_Antiforgery_Returns_400_CsrfValidationFailed()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // AntiforgeryOptions.Cookie.SecurePolicy = SameAsRequest (per AddGameKitAdmin). HTTP is
        // fine for the SameAsRequest policy in the test host; the antiforgery middleware does not
        // perform the SSL pre-check that the TestServer + Always policy would trigger.
        await LoginAsRoot(host.Client);
        var playerId = await SeedPlayerAsync("charlie");

        // POST without the X-GameKit-Admin-CSRF header AND without the gk_admin_csrf cookie ⇒
        // AntiforgeryValidationFilter rejects with 400 + ProblemDetails carrying csrf_validation_failed.
        var resp = await host.Client.PostAsJsonAsync(
            $"/admin/api/players/{playerId}/ban",
            new { reason = "spam-test" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("csrf_validation_failed", body);
    }

    private static string ExtractNonce(string csp)
    {
        // Captures the base64-encoded nonce inside `script-src 'self' 'nonce-...';`
        var m = Regex.Match(csp, @"nonce-([A-Za-z0-9+/=]+)");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    // ---- helpers (mirror PlayerSearchEndpointTests for cookie plumbing + player seeding) ----

    private static async Task LoginAsRoot(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/admin/api/login",
            new { username = "root", password = "hunter2hunter2", rememberMe = false });
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"LoginAsRoot failed: {resp.StatusCode} / {body}");
        }
        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var raw in setCookies)
            {
                var head = raw.Split(';', 2)[0];
                if (head.StartsWith("gk_admin_session=", StringComparison.Ordinal))
                {
                    client.DefaultRequestHeaders.Remove("Cookie");
                    client.DefaultRequestHeaders.Add("Cookie", head);
                    return;
                }
            }
        }
        throw new InvalidOperationException("Login response did not include gk_admin_session cookie.");
    }

    private async Task<Guid> SeedPlayerAsync(string displayName)
    {
        var id = Guid.CreateVersion7();
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO gamekit.players " +
            "(\"Id\", \"DisplayName\", \"CreatedAt\", \"IsBanned\") " +
            "VALUES ($1, $2, $3, $4)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter { Value = displayName });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        cmd.Parameters.Add(new NpgsqlParameter { Value = false });
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static void ResetTables(string connectionString)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "TRUNCATE TABLE gamekit.admin_audit_log; " +
                "TRUNCATE TABLE gamekit.admin_users; " +
                "DELETE FROM gamekit.player_identities; " +
                "DELETE FROM gamekit.players";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Migrations run on first AdminTestHost construction.
        }
    }
}
