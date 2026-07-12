// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// ROADMAP Phase 3 Success Criterion #2 coverage: an unauthenticated request to <c>/admin/*</c>
/// in <c>Production</c> receives a <c>404</c> (not <c>401</c>); a startup assertion fails fast
/// when the admin module is mounted with no superadmin row in Production.
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class ProductionGateTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public ProductionGateTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    [Fact(DisplayName = "SC#2: Production unauthenticated GET /admin/players returns 404 (not 401, not 302)")]
    public async Task Production_UnauthenticatedGET_AdminPath_Returns404()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // No admin cookie attached. AdminCookieEvents.RedirectToLogin must short-circuit with 404.
        var resp = await host.Client.GetAsync("/admin/players");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact(DisplayName = "SC#2: Development unauthenticated GET /admin/players redirects to /admin/login")]
    public async Task Development_UnauthenticatedGET_AdminPath_RedirectsToLogin()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // TestServer's default HttpClient does NOT follow redirects so the raw 302 surfaces here.
        var resp = await host.Client.GetAsync("/admin/players");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        var location = resp.Headers.Location?.ToString() ?? string.Empty;
        Assert.Contains("/admin/login", location, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "SC#2: Production with no superadmin throws InvalidOperationException at host startup")]
    public async Task Production_NoSuperadmin_HostStartAsync_Throws()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var _ = await AdminTestHost.StartAsync(_pg, _redis, env: "Production");
        });
        // Operator-friendly message must point at the bootstrap CLI command (D-04).
        Assert.Contains("dotnet gamekit admin create", ex.Message);
    }

    [Fact(DisplayName = "SC#2: Production /admin/login is reachable anonymously (operator must be able to authenticate)")]
    public async Task Production_LoginPath_Reachable_Anonymously()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // The login page must remain reachable even in Production — without it, an operator
        // could never authenticate (AdminCookieEvents whitelists the LoginPath).
        var resp = await host.Client.GetAsync("/admin/login");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
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
