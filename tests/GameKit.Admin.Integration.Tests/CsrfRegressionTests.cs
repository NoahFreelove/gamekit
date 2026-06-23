// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// SEC-06 CSRF regression: asserts that state-changing admin API endpoints return exactly
/// <c>400 Bad Request</c> (not 401, not 403) when called without a valid antiforgery token,
/// matching the contract of <c>AntiforgeryValidationFilter</c>.
/// </summary>
/// <remarks>
/// The existing <c>CspAndAntiforgeryTests.BanMutation_Without_Antiforgery_Returns_400_CsrfValidationFailed</c>
/// already covers this path. This class is a dedicated regression suite that focuses exclusively
/// on the SEC-06 invariant — status code EXACTLY 400 + body contains "csrf_validation_failed" —
/// across multiple mutation endpoints, ensuring future endpoint additions don't silently change the
/// error code.
/// </remarks>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class CsrfRegressionTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public CsrfRegressionTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetTables(_pg.OwnerConnectionString);
    }

    /// <summary>
    /// POST /admin/api/players/{id}/ban without antiforgery token returns exactly 400
    /// with body containing "csrf_validation_failed".
    /// </summary>
    [Fact(DisplayName = "SEC-06: Ban mutation without antiforgery token returns exactly 400 csrf_validation_failed")]
    public async Task BanMutation_Without_Antiforgery_Returns_Exactly_400()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("sec06-admin", "hunter2hunter2", AdminRoles.Superadmin));

        await LoginAsAdmin(host.Client, "sec06-admin");
        var playerId = await SeedPlayerAsync("sec06-player-1");

        // POST without the X-GameKit-Admin-CSRF header — AntiforgeryValidationFilter must reject
        // with 400. MUST NOT be 401 (authentication) or 403 (authorization).
        var resp = await host.Client.PostAsJsonAsync(
            $"/admin/api/players/{playerId}/ban",
            new { reason = "SEC-06 regression test" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("csrf_validation_failed", body);
    }

    /// <summary>
    /// POST /admin/api/players/{id}/unban without antiforgery token returns exactly 400.
    /// </summary>
    [Fact(DisplayName = "SEC-06: Unban mutation without antiforgery token returns exactly 400")]
    public async Task UnbanMutation_Without_Antiforgery_Returns_Exactly_400()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("sec06-admin2", "hunter2hunter2", AdminRoles.Superadmin));

        await LoginAsAdmin(host.Client, "sec06-admin2");
        var playerId = await SeedPlayerAsync("sec06-player-2");

        var resp = await host.Client.PostAsJsonAsync(
            $"/admin/api/players/{playerId}/unban",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("csrf_validation_failed", body);
    }

    /// <summary>
    /// DELETE /admin/api/admins/{id} without antiforgery token returns exactly 400.
    /// </summary>
    [Fact(DisplayName = "SEC-06: Delete admin mutation without antiforgery token returns exactly 400")]
    public async Task DeleteAdminMutation_Without_Antiforgery_Returns_Exactly_400()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("sec06-superadmin", "hunter2hunter2", AdminRoles.Superadmin));

        await LoginAsAdmin(host.Client, "sec06-superadmin");

        // Use a placeholder ID — the antiforgery filter runs before the handler reads the DB,
        // so the 400 is returned regardless of whether the ID resolves to a real admin user.
        var fakeAdminId = Guid.CreateVersion7();
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/admin/api/admins/{fakeAdminId}");

        var resp = await host.Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("csrf_validation_failed", body);
    }

    // ---- helpers ----

    private static async Task LoginAsAdmin(HttpClient client, string username)
    {
        var resp = await client.PostAsJsonAsync("/admin/api/login",
            new { username = username, password = "hunter2hunter2", rememberMe = false });
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"LoginAsAdmin failed: {resp.StatusCode} / {body}");
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
        throw new InvalidOperationException("Login did not return a gk_admin_session cookie.");
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
