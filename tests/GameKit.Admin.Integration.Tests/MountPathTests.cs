// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Net;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// ROADMAP supporting test for ADMIN-02: <see cref="GameKitAdminOptions.MountPath"/> relocates
/// only the admin HTTP API prefix (<c>/admin/api/*</c>). The Blazor admin console is served at
/// <c>/admin/*</c> by <c>MapRazorComponents&lt;App&gt;()</c> with static <c>@page</c> routes that
/// are root-relative; MudBlazor static assets at <c>_content/MudBlazor/*</c> are root-relative.
/// MountPath does NOT move the Razor pages — this is a documented v1 contract (CLAUDE.md
/// GameKit.Admin.UI block + 03-CONTEXT.md MountPath scope note).
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class MountPathTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public MountPathTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    [Fact(DisplayName = "MountPath: Custom MountPath relocates API prefix; Blazor shell stays at /admin")]
    public async Task CustomMountPath_RelocatesApiPrefix_And_LeavesBlazorShellAtAdmin()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin),
            configureAdmin: o => o.MountPath = "/custom-admin-path");

        // (1) Custom-prefixed API endpoint exists. /health requires the admin policy and the
        // request is anonymous, but the endpoint IS registered — meaning the response is one of
        // {2xx, 401, 403} depending on cookie scheme handling. We assert NOT 404 specifically:
        // a 404 here would prove the endpoint did not register at the custom prefix.
        var customApi = await host.Client.GetAsync("/custom-admin-path/api/health");
        Assert.NotEqual(HttpStatusCode.NotFound, customApi.StatusCode);

        // (2) Default API prefix is gone — /admin/api/health now returns 404. (In Development the
        // cookie middleware would rewrite to a 302 if the path matched a registered admin route,
        // but the route literally does not exist anymore so plain ASP.NET Core 404 routing applies.)
        var defaultApi = await host.Client.GetAsync("/admin/api/health");
        Assert.Equal(HttpStatusCode.NotFound, defaultApi.StatusCode);

        // (3) Blazor login page is UNCHANGED at /admin/login — MountPath does not move Razor
        // routes (they are declared via static @page directives at compile time + MudBlazor
        // static assets are root-relative). The contract is "MountPath scopes API only".
        var loginPage = await host.Client.GetAsync("/admin/login");
        Assert.True(loginPage.IsSuccessStatusCode,
            $"Expected /admin/login to remain reachable when MountPath points elsewhere; got {loginPage.StatusCode}.");
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
