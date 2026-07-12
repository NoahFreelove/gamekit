// SPDX-License-Identifier: Apache-2.0
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
/// ROADMAP Phase 3 Success Criterion #1 end-to-end coverage: an operator can
/// <c>app.MapGameKitAdmin("/admin")</c>, bootstrap an admin (here: via the seed helper that
/// mirrors the CLI path covered in plan 03-11), log in with the admin scheme, and search for a
/// player by id, display name, and identity provider+external_id.
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class RoadmapScenarioTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public RoadmapScenarioTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetTables(_pg.OwnerConnectionString);
    }

    [Fact(DisplayName = "SC#1: Mount /admin, bootstrap admin via service, login, search by id + identity + displayname")]
    public async Task Sc1_EndToEnd_Mount_Bootstrap_Login_Search()
    {
        // Arrange — bootstrap a superadmin (seed mirrors the plan 03-11 CLI path) and a player
        // with one Steam identity. Development env so the admin login API is exercised through
        // the same code path as Production except for the cookie-events 404 fallback (which the
        // search calls do not trip — they happen post-login).
        var alicePlayerId = Guid.Parse("0196f000-0000-7000-8000-000000000001");
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));
        await SeedPlayerAsync(alicePlayerId, "alice");
        await SeedIdentityAsync(alicePlayerId, "steam", "76561198012345678");

        // Act + Assert (1) — Login with the seeded admin. Establishes the gk_admin_session cookie.
        await LoginAsRoot(host.Client);

        // (2) Search by UUID — direct id lookup via PlayerSearchService.ClassifyInput.
        var byId = await host.Client.GetAsync(
            $"/admin/api/players/search?Query={alicePlayerId}&PageSize=50");
        byId.EnsureSuccessStatusCode();
        var byIdBody = await byId.Content.ReadAsStringAsync();
        Assert.Contains(alicePlayerId.ToString(), byIdBody);

        // (3) Search by display name — case-insensitive citext prefix match.
        var byName = await host.Client.GetAsync("/admin/api/players/search?Query=alice&PageSize=50");
        byName.EnsureSuccessStatusCode();
        var byNameBody = await byName.Content.ReadAsStringAsync();
        Assert.Contains(alicePlayerId.ToString(), byNameBody);
        Assert.Contains("alice", byNameBody);

        // (4) Search by identity — provider:external_id form (the ':' is URL-escaped). Resolves
        // through the player_identities lookup branch of ClassifyInput.
        var byIdentity = await host.Client.GetAsync(
            "/admin/api/players/search?Query=steam%3A76561198012345678&PageSize=50");
        byIdentity.EnsureSuccessStatusCode();
        var byIdentityBody = await byIdentity.Content.ReadAsStringAsync();
        Assert.Contains(alicePlayerId.ToString(), byIdentityBody);

        // SC#1 contract: all three modes resolve the SAME player id.
        Assert.Contains(alicePlayerId.ToString(), byIdBody);
        Assert.Contains(alicePlayerId.ToString(), byNameBody);
        Assert.Contains(alicePlayerId.ToString(), byIdentityBody);
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
        // TestServer's default handler does not auto-persist cookies — manually plumb the
        // session cookie back into the client's default Cookie header (matches the pattern
        // used in PlayerSearchEndpointTests).
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

    private async Task SeedPlayerAsync(Guid id, string displayName)
    {
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
