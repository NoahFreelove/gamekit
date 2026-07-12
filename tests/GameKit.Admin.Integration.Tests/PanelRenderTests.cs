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
/// ROADMAP Phase 3 Success Criterion #4: the match-history, health (Postgres / Redis
/// connectivity, recent error rate), rank-adjust, and queue-depth panels all render without
/// error. The rank-adjust + queue-depth panels render a clear "requires GameKit.Rankings /
/// GameKit.Matchmaking" placeholder when those packages are absent (UI-SPEC §11 / §12).
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class PanelRenderTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public PanelRenderTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetTables(_pg.OwnerConnectionString);
    }

    [Fact(DisplayName = "SC#4: /admin/matchmaking renders MissingPackageAlert when GameKit.Matchmaking is not registered")]
    public async Task QueueDepthPanel_Renders_MissingPackageAlert_WhenMatchmakingNotInstalled()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));
        await LoginAsRoot(host.Client);

        var html = await host.Client.GetStringAsync("/admin/matchmaking");
        // Copy contract from MissingPackageAlert.razor: "Install GameKit.{PackageName} and add
        // .Add{PackageName}(…) to your service registration to enable {Feature}."
        Assert.Contains("Install GameKit.Matchmaking", html);
    }

    [Fact(DisplayName = "SC#4: /admin/rankings/adjust renders MissingPackageAlert when GameKit.Rankings is not registered (superadmin)")]
    public async Task RankAdjustPanel_Renders_MissingPackageAlert_WhenRankingsNotInstalled_ForSuperadmin()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));
        await LoginAsRoot(host.Client);

        var html = await host.Client.GetStringAsync("/admin/rankings/adjust");
        Assert.Contains("Install GameKit.Rankings", html);
    }

    [Fact(DisplayName = "SC#4: /admin/api/health returns 3-probe HealthReport (Postgres + Redis + ErrorRate)")]
    public async Task HealthPanel_Returns_ThreeProbeReport()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));
        await LoginAsRoot(host.Client);

        var resp = await host.Client.GetAsync("/admin/api/health");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        // System.Text.Json default property naming is camelCase, so the HealthReport record
        // surfaces as { postgres, redis, errorRate, checkedAt }.
        Assert.Contains("postgres", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redis", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("errorRate", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "SC#4: /admin/api/match-history returns completed sessions for the queried player")]
    public async Task MatchHistoryPanel_Returns_CompletedSessions_ForPlayer()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));
        await LoginAsRoot(host.Client);

        var playerId = await SeedPlayerAsync("history-target");
        var sessionId = Guid.CreateVersion7();
        await SeedCompletedSessionAsync(sessionId);
        await SeedSessionParticipantAsync(sessionId, playerId, team: 0);

        var resp = await host.Client.GetAsync($"/admin/api/match-history?playerId={playerId}&pageSize=50");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        // The MatchHistoryRow projection includes the session id in the response — sufficient
        // proof the join-and-filter path returned the seeded completed session.
        Assert.Contains(sessionId.ToString(), body, StringComparison.OrdinalIgnoreCase);
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

    private async Task SeedCompletedSessionAsync(Guid sessionId)
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        // GameSession.State is stored as a string column via HasConversion<string>(); the literal
        // "Completed" matches GameSessionState.Completed.
        cmd.CommandText =
            "INSERT INTO gamekit.game_sessions " +
            "(\"Id\", \"State\", \"LadderId\", \"CreatedAt\", \"StartedAt\", \"CompletedAt\", \"Metadata\") " +
            "VALUES ($1, $2, NULL, $3, $4, $5, NULL)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = sessionId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = "Completed" });
        cmd.Parameters.Add(new NpgsqlParameter { Value = now.AddMinutes(-30) });
        cmd.Parameters.Add(new NpgsqlParameter { Value = now.AddMinutes(-25) });
        cmd.Parameters.Add(new NpgsqlParameter { Value = now });
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedSessionParticipantAsync(Guid sessionId, Guid playerId, int team)
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO gamekit.session_participants " +
            "(\"Id\", \"SessionId\", \"PlayerId\", \"Team\", \"Result\", \"Score\", " +
            " \"RatingBefore\", \"RatingAfter\", \"RatingDelta\") " +
            "VALUES ($1, $2, $3, $4, NULL, NULL, NULL, NULL, NULL)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = Guid.CreateVersion7() });
        cmd.Parameters.Add(new NpgsqlParameter { Value = sessionId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = playerId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = team });
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
                "DELETE FROM gamekit.session_participants; " +
                "DELETE FROM gamekit.game_sessions; " +
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
