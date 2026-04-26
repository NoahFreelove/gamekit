// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using GameKit.Admin.Integration.Tests.Mocks;
using GameKit.Admin.UI.Authorization;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// ROADMAP Phase 3 Success Criterion #6: a valid player JWT cannot authenticate into any
/// admin endpoint. The admin cookie scheme (<c>GameKitAdmin</c>) and the player Bearer scheme
/// (<c>JwtBearerDefaults.AuthenticationScheme</c>) are deliberately disjoint (D-02): admin
/// authorization policies pin the admin scheme via <c>AddAuthenticationSchemes</c>, so a Bearer
/// header carrying a valid player JWT cannot satisfy the admin requirement. In Production,
/// <see cref="GameKit.Admin.UI.Authentication.AdminCookieEvents"/> short-circuits the cookie
/// challenge with a <c>404</c> rather than a <c>401</c> — making admin paths indistinguishable
/// from non-mounted paths to an attacker holding only player credentials.
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class CrossSchemeIsolationTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public CrossSchemeIsolationTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    [Fact(DisplayName = "SC#6: Player JWT in Bearer header cannot access /admin/api/* in Production (returns 404)")]
    public async Task PlayerJwt_InBearerHeader_CannotAccessAdminEndpoints_InProduction()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        using var issuer = new FakePlayerJwtIssuer();
        var jwt = issuer.IssueValidPlayerJwt(Guid.NewGuid(), Guid.NewGuid());

        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/api/health");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var resp = await host.Client.SendAsync(req);

        // The admin authorization policy pins the GameKitAdmin cookie scheme; Bearer cannot
        // satisfy it. The cookie challenge that fires in its place is intercepted by
        // AdminCookieEvents.RedirectToLogin which returns 404 in Production (D-04). An attacker
        // holding only a player JWT thus cannot distinguish admin-mounted from admin-not-mounted.
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact(DisplayName = "SC#6: Player JWT cannot satisfy admin policy even on Development /admin/api/* (no 200)")]
    public async Task PlayerJwt_Cannot_Authenticate_Even_On_NonProduction_Admin_Endpoints()
    {
        // In Development the cookie redirect surfaces as 302 instead of 404, but the contract
        // that matters for SC#6 is identical: player Bearer must NEVER yield 200 on /admin/*.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        using var issuer = new FakePlayerJwtIssuer();
        var jwt = issuer.IssueValidPlayerJwt(Guid.NewGuid(), Guid.NewGuid());

        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/api/health");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var resp = await host.Client.SendAsync(req);

        // Bearer handler is not in the admin policy's scheme list; the policy fails authentication
        // and the cookie scheme's RedirectToLogin runs (302 in Dev). The negative-space contract
        // is "never 200" — admin endpoints never honor a Bearer token.
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
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
            // Migrations run on first AdminTestHost construction.
        }
    }
}
