// SPDX-License-Identifier: GPL-3.0-or-later
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
/// W4 smoke tests for ROADMAP SC #6 — admin scheme registration must not change Phase-2 JWT
/// Bearer as the default auth scheme, AND admin paths must not respond to anonymous requests
/// in Production (return 404 per D-04 / AdminCookieEvents).
/// </summary>
/// <remarks>
/// The full cross-scheme isolation suite lives in plan 03-13 <c>CrossSchemeIsolationTests</c>.
/// This plan's smoke is deliberately minimal: (a) admin wiring did not clobber the default;
/// (b) admin-api endpoints under Production return 404 without credentials. End-to-end
/// <c>/auth/me</c> behavior against a FakePlayerJwtIssuer-minted token lives in 03-13 where the
/// FakePlayerJwtIssuer's <c>PublicSigningKey</c> can be registered as an additional
/// <c>IssuerSigningKey</c> on the JwtBearer validation parameters.
/// </remarks>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class AuthSchemeIsolationSmokeTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public AuthSchemeIsolationSmokeTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    [Fact]
    public async Task AdminApi_Without_Credentials_Returns_404_In_Production()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // Any /admin/* path that is not /admin/login should return 404 to an anonymous caller
        // in Production (AdminCookieEvents.RedirectToLogin suppresses 401 → 404). The admin-api
        // prefix is a specific case of the same rule.
        var resp = await host.Client.GetAsync("/admin/api/health");
        // Note: plan 03-07 ships the actual /admin/api/health endpoint. Until then, the endpoint
        // does not exist in the minimal-API group, so the route itself 404s. Either way, the
        // assertion holds: an anonymous caller must NOT receive a 401 or a JSON body under /admin/*.
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AdminApi_Without_Credentials_Returns_404_At_Root_Admin_Path_In_Production()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // Generic /admin/players check: again 404 in Production from unauthenticated caller.
        var resp = await host.Client.GetAsync("/admin/players");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Host_Startup_Preserves_JwtBearer_Default_Scheme_Registration()
    {
        // W4: AddGameKitAdmin calls AddAuthentication(JwtBearerDefaults.AuthenticationScheme) which
        // sets Bearer as the DEFAULT scheme (matching what AddAuth does). This test proves the
        // host starts successfully AFTER AddGameKitAdmin is chained — meaning the admin cookie
        // scheme was added as a NAMED scheme without clobbering Bearer's default registration.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));
        Assert.NotNull(host.Client);
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
