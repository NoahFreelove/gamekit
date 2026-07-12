// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Integration tests for <c>GET /admin/api/players/search</c> (unified classifier via
/// <c>PlayerSearchService.ClassifyInput</c>) and antiforgery enforcement on
/// <c>POST /admin/api/players/{id}/ban</c>. The admin cookie is acquired via the login endpoint
/// before each authenticated call.
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class PlayerSearchEndpointTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public PlayerSearchEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetTables(_pg.OwnerConnectionString);
    }

    [Fact]
    public async Task Search_ByUuid_Returns_That_Player()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var alice = await SeedPlayerAsync("alice");
        await SeedPlayerAsync("bob");
        await LoginAsRoot(host.Client);

        var resp = await host.Client.GetAsync($"/admin/api/players/search?Query={alice}&PageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(alice.ToString(), body);
        Assert.DoesNotContain("bob", body);
    }

    [Fact]
    public async Task Search_ByIdentity_Returns_Linked_Player()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var alice = await SeedPlayerAsync("alice");
        await SeedIdentityAsync(alice, "steam", "76561199000000001");
        await LoginAsRoot(host.Client);

        var resp = await host.Client.GetAsync(
            "/admin/api/players/search?Query=steam%3A76561199000000001&PageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(alice.ToString(), body);
    }

    [Fact]
    public async Task Search_ByDisplayNamePrefix_Returns_Matching_Players()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var alice = await SeedPlayerAsync("alice");
        await SeedPlayerAsync("alec");
        await SeedPlayerAsync("zack");
        await LoginAsRoot(host.Client);

        var resp = await host.Client.GetAsync("/admin/api/players/search?Query=ali&PageSize=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("alice", body);
        Assert.Contains(alice.ToString(), body);
        Assert.DoesNotContain("zack", body);
    }

    [Fact]
    public async Task BanPlayer_WithoutAntiforgeryToken_Returns400CsrfValidationFailed()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // Force the TestServer client to emit HTTPS-scheme requests so AntiforgeryOptions.Cookie.
        // SecurePolicy = Always (configured by AddGameKitAdmin) does not reject the request at the
        // SSL pre-check. In production the host serves the admin UI over HTTPS; TestServer defaults
        // to http://localhost/ unless BaseAddress is overridden.
        host.Client.BaseAddress = new Uri("https://localhost/");

        var playerId = await SeedPlayerAsync("charlie");
        await LoginAsRoot(host.Client);

        // No CSRF token attached — AntiforgeryValidationFilter must reject with 400 + csrf_validation_failed.
        var resp = await host.Client.PostAsJsonAsync(
            $"/admin/api/players/{playerId}/ban",
            new { reason = "spam" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("csrf_validation_failed", body);
    }

    // ---- helpers ----

    private static async Task LoginAsRoot(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/admin/api/login",
            new { username = "root", password = "hunter2hunter2", rememberMe = false });
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"LoginAsRoot failed: {resp.StatusCode} / {body}");
        }
        // Manually plumb the session cookie back into the client's default headers because the
        // TestServer's default handler does NOT auto-persist cookies across calls.
        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var raw in setCookies)
            {
                // Take the "name=value" head before the first ';'.
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

    private async Task SeedIdentityAsync(Guid playerId, string provider, string externalId)
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        cmd.CommandText =
            "INSERT INTO gamekit.player_identities " +
            "(\"Id\", \"PlayerId\", \"Provider\", \"ExternalId\", \"CreatedAt\", \"UpdatedAt\") " +
            "VALUES ($1, $2, $3, $4, $5, $6)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = Guid.CreateVersion7() });
        cmd.Parameters.Add(new NpgsqlParameter { Value = playerId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = provider });
        cmd.Parameters.Add(new NpgsqlParameter { Value = externalId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = now });
        cmd.Parameters.Add(new NpgsqlParameter { Value = now });
        await cmd.ExecuteNonQueryAsync();
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
