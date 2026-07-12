// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Core.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Phase 6 / Plan 06-07 — PRES-06 SC#2 empirical anchor. Asserts the two load-bearing
/// behaviors of <c>PresencePanel.razor</c>:
/// <list type="number">
/// <item>When <see cref="IPresenceProvider"/> is NOT registered (consumer omitted
/// <c>GameKit.Presence</c>), the panel renders <c>MissingPackageAlert</c> whose body emits the
/// two required literal substrings per UI-SPEC §9 substring contract:
/// <c>Install GameKit.Presence</c> AND <c>AddPresence(…)</c>. Mirrors the equivalent
/// Phase 3 assertions for Matchmaking / Rankings in <see cref="PanelRenderTests"/>.</item>
/// <item>When <see cref="IPresenceProvider"/> IS registered (mock returns three player ids),
/// the panel renders the happy-path <c>&lt;table class="t"&gt;</c> populated with one row per
/// id. The presence of <c>&lt;table class="t"&gt;</c> together with three rows (each carrying
/// the truncated-hex player-id prefix produced by the page's <c>TruncatePlayerId</c> helper) is
/// sufficient proof that the render path executed.</item>
/// </list>
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class PresencePanelRenderTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public PresencePanelRenderTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        // Match the PanelRenderTests pattern: reset admin_users + admin_audit_log so a
        // re-run inside the same xUnit collection session does not fight a stale "root"
        // row left over by another [Collection("Admin")] test.
        ResetAdminTables(_pg.OwnerConnectionString);
    }

    private static void ResetAdminTables(string connectionString)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "TRUNCATE TABLE gamekit.admin_audit_log; " +
                "TRUNCATE TABLE gamekit.admin_users";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Migrations run on first AdminTestHost construction.
        }
    }

    [Fact(DisplayName = "PRES-06 SC#2: /admin/presence renders MissingPackageAlert with 'Install GameKit.Presence' + 'AddPresence(…)' substrings when GameKit.Presence is not registered")]
    public async Task MissingPackage_RendersInstallPresenceAndAddPresenceSubstrings()
    {
        // Boot the host WITHOUT calling AddPresence() — Sp.GetService<IPresenceProvider>() returns null.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));
        await LoginAsRoot(host.Client);

        var html = await host.Client.GetStringAsync("/admin/presence");

        // UI-SPEC §9 — both substrings MUST appear (the page's MissingPackageAlert callsite +
        // template line 20 emit them naturally). The "…" is U+2026 horizontal ellipsis, not three
        // ASCII dots — matches the literal in MissingPackageAlert.razor:20.
        Assert.Contains("Install GameKit.Presence", html);
        Assert.Contains("AddPresence(…)", html);
    }

    [Fact(DisplayName = "PRES-06 SC#2: /admin/presence renders <table class=\"t\"> with rows when IPresenceProvider is registered")]
    public async Task PresenceRegistered_RendersTableWithRows()
    {
        var p1 = Guid.Parse("a3f9c1d2-0000-7000-8000-000000000001");
        var p2 = Guid.Parse("b4faa2e3-0000-7000-8000-000000000002");
        var p3 = Guid.Parse("c5fbb3f4-0000-7000-8000-000000000003");

        var mockProvider = new Mock<IPresenceProvider>(MockBehavior.Strict);
        mockProvider
            .Setup(p => p.GetOnlinePlayerIdsAsync(25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)new[] { p1, p2, p3 });

        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin),
            configureExtraServices: services =>
            {
                // Late binding: registered AFTER GameKit core/auth/admin so the AddGameKit
                // chain does not stomp on it. PresencePanel resolves it via
                // sp.GetService<IPresenceProvider>() at OnInitializedAsync.
                services.AddSingleton<IPresenceProvider>(mockProvider.Object);
            });
        await LoginAsRoot(host.Client);

        var html = await host.Client.GetStringAsync("/admin/presence");

        // The happy path renders the sketch <table class="t"> primitive (PATTERNS warning #8 —
        // NOT MudDataGrid). Existence of the class together with three truncated player-id
        // prefixes is sufficient proof that the row-render loop executed.
        Assert.Contains("<table class=\"t\">", html);
        // PresencePanel.TruncatePlayerId formats Guid as "N" (32 lowercase hex chars, no dashes)
        // and slices the first 8 chars. Assert each seeded player's prefix appears in the body.
        Assert.Contains("a3f9c1d2", html);
        Assert.Contains("b4faa2e3", html);
        Assert.Contains("c5fbb3f4", html);
        // And the table should NOT degrade into the MissingPackageAlert branch.
        Assert.DoesNotContain("Install GameKit.Presence", html);
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
}
