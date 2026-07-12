// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Services;
using GameKit.Auth;
using GameKit.Auth.Entities;
using GameKit.Auth.Providers;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// End-to-end coverage of D-03 ban enforcement at the login and refresh paths for all four
/// providers. Verifies that (a) <see cref="BannedCheckHelper"/> produces a stable 16-char
/// lowercase hex reason hash, (b) every <see cref="IOAuthProvider"/> returns
/// <c>OAuthResult.Fail("banned:&lt;hash&gt;")</c> when the target player is banned, (c) the
/// HTTP layer translates that shape to 403 Forbidden with <c>error = "banned"</c> +
/// <c>externalIdHash = &lt;hash&gt;</c> (reusing the existing <c>AuthErrorResponse</c>
/// envelope's hash field as the reason hash carrier), and (d) the refresh rotation path
/// revokes the entire family + throws <see cref="UnauthorizedException"/> with code
/// <c>player_banned</c> when the player is banned between refresh cycles.
/// </summary>
/// <remarks>
/// The test harness is <see cref="AdminTestHost"/> rather than the Phase-2 <c>AuthTestHost</c>
/// so ban rows can be written via <see cref="IPlayerBanService"/> — the production admin service
/// that updates <c>players.IsBanned</c> + <c>BannedAt</c> + <c>BanReason</c> atomically with the
/// audit row (T-03-06-01). Using the admin service validates the full flow: admin ban → next
/// provider interaction rejects → family revoke on refresh.
/// </remarks>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class BanEnforcementTests
{
    private static readonly Regex ReasonHashPattern = new("^[0-9a-f]{16}$", RegexOptions.Compiled);

    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public BanEnforcementTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetTables(_pg.OwnerConnectionString);
    }

    // ---------- HTTP-path tests ----------

    [Fact]
    public async Task PasswordProvider_BannedPlayer_Login_Returns_403_With_ReasonHash()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // Register via /auth/register → PasswordOAuthProvider.RegisterAsync mints Player + Credential.
        var username = $"banme-{Guid.NewGuid():N}"[..16];
        var registerResp = await host.Client.PostAsJsonAsync("/auth/register",
            new { username, password = "correct-horse-battery", displayName = "BanTarget" });
        registerResp.EnsureSuccessStatusCode();

        var playerId = await GetPlayerIdByUsernameAsync(username);

        // Ban via production admin service.
        var actorId = await GetSeededAdminIdAsync();
        var (banScope, banSvc) = host.Resolve<IPlayerBanService>();
        try
        {
            await banSvc.BanAsync(playerId, actorId, "cheating-signature", default);
        }
        finally { banScope.Dispose(); }

        // Login → 403 + error=banned + externalIdHash=<16hex>.
        var loginResp = await host.Client.PostAsJsonAsync("/auth/login/password",
            new { username, password = "correct-horse-battery" });

        Assert.Equal(HttpStatusCode.Forbidden, loginResp.StatusCode);

        var body = await loginResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("banned", GetStringLenient(doc.RootElement, "error"));
        var reasonHash = GetStringLenient(doc.RootElement, "externalIdHash");
        Assert.NotNull(reasonHash);
        Assert.Matches(ReasonHashPattern, reasonHash!);
    }

    [Fact]
    public async Task RefreshAfterBan_Revokes_Family_And_Returns_401_PlayerBanned()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var username = $"fresh-{Guid.NewGuid():N}"[..16];
        var registerResp = await host.Client.PostAsJsonAsync("/auth/register",
            new { username, password = "correct-horse-battery", displayName = "R" });
        registerResp.EnsureSuccessStatusCode();
        var tokens = await registerResp.Content.ReadFromJsonAsync<TokenResponseShape>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrEmpty(tokens!.AccessToken));
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));

        var playerId = await GetPlayerIdByUsernameAsync(username);
        var actorId = await GetSeededAdminIdAsync();

        var (banScope, banSvc) = host.Resolve<IPlayerBanService>();
        try
        {
            await banSvc.BanAsync(playerId, actorId, "reason-x", default);
        }
        finally { banScope.Dispose(); }

        // Attempt refresh — expect 401 with error=player_banned.
        var refreshResp = await host.Client.PostAsJsonAsync("/auth/refresh",
            new { refreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResp.StatusCode);
        var body = await refreshResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("player_banned", GetStringLenient(doc.RootElement, "error"));

        // Every refresh-token row for the player has RevokedAt populated.
        var (dbScope, ctx) = host.CreateDbScope();
        try
        {
            var families = await ctx.Set<RefreshToken>().AsNoTracking()
                .Where(r => r.PlayerId == playerId).ToListAsync();
            Assert.NotEmpty(families);
            Assert.All(families, r => Assert.NotNull(r.RevokedAt));
        }
        finally { dbScope.Dispose(); }
    }

    // ---------- Service-layer tests (exercise the shared helper across 4 providers) ----------

    [Fact]
    public async Task BannedCheckHelper_BannedPlayer_Returns_BannedErrorCode()
    {
        // Direct service-level check: this is the code path GuestOAuthProvider.CompleteLoginAsync
        // now invokes; validating the helper against a banned seeded player proves the guest path
        // (and every other provider path) would reject.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var playerId = await SeedBannedPlayerAsync("banned-guest", "guest-reason");

        var (scope, ctx) = host.CreateDbScope();
        try
        {
            var result = await BannedCheckHelper.CheckAsync(ctx, playerId, default);
            Assert.NotNull(result);
            Assert.False(result!.Success);
            Assert.NotNull(result.ErrorCode);
            Assert.StartsWith("banned:", result.ErrorCode);
            var hash = result.ErrorCode!["banned:".Length..];
            Assert.Matches(ReasonHashPattern, hash);
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task SteamProvider_BannedPlayer_Returns_BannedErrorCode_And_IssuesNoToken()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var steamId = "76561199000000042";
        var playerId = await SeedPlayerWithIdentityAsync("banned-steamer", "steam", steamId, banned: true, banReason: "vac");

        var scope = host.Services().CreateScope();
        try
        {
            var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "steam");
            var result = await provider.CompleteLoginAsync(steamId, "Updated Name", null, "dev-1", default);
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorCode);
            Assert.StartsWith("banned:", result.ErrorCode);
            Assert.Matches(ReasonHashPattern, result.ErrorCode!["banned:".Length..]);
        }
        finally { scope.Dispose(); }

        // No refresh-token row issued.
        var (dbScope, ctx) = host.CreateDbScope();
        try
        {
            Assert.Equal(0, await ctx.Set<RefreshToken>().AsNoTracking()
                .CountAsync(r => r.PlayerId == playerId));
        }
        finally { dbScope.Dispose(); }
    }

    [Fact]
    public async Task DiscordProvider_BannedPlayer_Returns_BannedErrorCode_And_IssuesNoToken()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var discordId = "111122223333444455";
        var playerId = await SeedPlayerWithIdentityAsync("banned-discorder", "discord", discordId, banned: true, banReason: "tos");

        var scope = host.Services().CreateScope();
        try
        {
            var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "discord");
            var result = await provider.CompleteLoginAsync(discordId, "NewName", null, "dev-1", default);
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorCode);
            Assert.StartsWith("banned:", result.ErrorCode);
            Assert.Matches(ReasonHashPattern, result.ErrorCode!["banned:".Length..]);
        }
        finally { scope.Dispose(); }

        var (dbScope, ctx) = host.CreateDbScope();
        try
        {
            Assert.Equal(0, await ctx.Set<RefreshToken>().AsNoTracking()
                .CountAsync(r => r.PlayerId == playerId));
        }
        finally { dbScope.Dispose(); }
    }

    [Fact]
    public async Task PasswordProvider_ServiceLayer_BannedPlayer_Returns_BannedErrorCode()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var username = $"bpwd-{Guid.NewGuid():N}"[..16];
        var playerId = await SeedPlayerWithPasswordCredentialAsync(username, "password-12", "BanTarget");
        await FlipBanAsync(playerId, banned: true, reason: "test-reason");

        var scope = host.Services().CreateScope();
        try
        {
            var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "password");
            var result = await provider.CompleteLoginAsync(username, "password-12", null, "dev-1", default);
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorCode);
            Assert.StartsWith("banned:", result.ErrorCode);
            Assert.Matches(ReasonHashPattern, result.ErrorCode!["banned:".Length..]);
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task BannedCheckHelper_UnbannedPlayer_Returns_Null()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var playerId = await SeedPlayerAsync("clean-player");

        var (scope, ctx) = host.CreateDbScope();
        try
        {
            var result = await BannedCheckHelper.CheckAsync(ctx, playerId, default);
            Assert.Null(result);
        }
        finally { scope.Dispose(); }
    }

    [Fact]
    public async Task BannedCheckHelper_SameReason_ProducesStableHash()
    {
        // Admins correlate an audit row (with the full reason) to a player's 403 response by
        // hashing SHA-256(reason)[..8] themselves. Prove the hash is deterministic: same reason
        // on two distinct players gives the SAME 16-char hex.
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Development",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var p1 = await SeedBannedPlayerAsync("p1", "same-reason");
        var p2 = await SeedBannedPlayerAsync("p2", "same-reason");

        string h1;
        var (s1, c1) = host.CreateDbScope();
        try
        {
            var r = await BannedCheckHelper.CheckAsync(c1, p1, default);
            Assert.NotNull(r);
            h1 = r!.ErrorCode!["banned:".Length..];
        }
        finally { s1.Dispose(); }

        string h2;
        var (s2, c2) = host.CreateDbScope();
        try
        {
            var r = await BannedCheckHelper.CheckAsync(c2, p2, default);
            Assert.NotNull(r);
            h2 = r!.ErrorCode!["banned:".Length..];
        }
        finally { s2.Dispose(); }

        Assert.Equal(h1, h2);
        Assert.Matches(ReasonHashPattern, h1);
    }

    // ---------- Seed helpers ----------

    private async Task<Guid> SeedPlayerAsync(string displayName)
    {
        var id = Guid.CreateVersion7();
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO gamekit.players (\"Id\", \"DisplayName\", \"CreatedAt\", \"IsBanned\") " +
            "VALUES ($1, $2, $3, false)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter { Value = displayName });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedBannedPlayerAsync(string displayName, string reason)
    {
        var id = Guid.CreateVersion7();
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO gamekit.players " +
            "(\"Id\", \"DisplayName\", \"CreatedAt\", \"IsBanned\", \"BannedAt\", \"BanReason\") " +
            "VALUES ($1, $2, $3, true, $4, $5)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter { Value = displayName });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        cmd.Parameters.Add(new NpgsqlParameter { Value = reason });
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedPlayerWithIdentityAsync(
        string displayName, string provider, string externalId, bool banned, string? banReason)
    {
        var id = banned
            ? await SeedBannedPlayerAsync(displayName, banReason ?? string.Empty)
            : await SeedPlayerAsync(displayName);

        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO gamekit.player_identities " +
            "(\"Id\", \"PlayerId\", \"Provider\", \"ExternalId\", \"DisplayName\", \"AvatarUrl\", \"CreatedAt\", \"UpdatedAt\") " +
            "VALUES ($1, $2, $3, $4, $5, NULL, $6, $7)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = Guid.CreateVersion7() });
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter { Value = provider });
        cmd.Parameters.Add(new NpgsqlParameter { Value = externalId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = displayName });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedPlayerWithPasswordCredentialAsync(string username, string password, string displayName)
    {
        var id = await SeedPlayerAsync(displayName);
        var hasher = new BCryptPasswordHasher(new GameKitAuthOptions());
        var hash = hasher.Hash(password);

        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO gamekit.player_credentials " +
            "(\"PlayerId\", \"Username\", \"PasswordHash\", \"UpdatedAt\") " +
            "VALUES ($1, $2, $3, $4)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter { Value = username });
        cmd.Parameters.Add(new NpgsqlParameter { Value = hash });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task FlipBanAsync(Guid playerId, bool banned, string? reason)
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE gamekit.players SET " +
            "\"IsBanned\" = $1, " +
            "\"BannedAt\" = CASE WHEN $1 THEN $2 ELSE NULL END, " +
            "\"BanReason\" = CASE WHEN $1 THEN $3 ELSE NULL END " +
            "WHERE \"Id\" = $4";
        cmd.Parameters.Add(new NpgsqlParameter { Value = banned });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        cmd.Parameters.Add(new NpgsqlParameter { Value = reason ?? (object)DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { Value = playerId });
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> GetPlayerIdByUsernameAsync(string username)
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"PlayerId\" FROM gamekit.player_credentials WHERE \"Username\" = $1";
        cmd.Parameters.Add(new NpgsqlParameter { Value = username });
        var v = await cmd.ExecuteScalarAsync();
        if (v is null || v is DBNull)
            throw new InvalidOperationException($"No credential for username {username}");
        return (Guid)v;
    }

    private async Task<Guid> GetSeededAdminIdAsync()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Id\" FROM gamekit.admin_users LIMIT 1";
        var id = (Guid)(await cmd.ExecuteScalarAsync() ?? Guid.Empty);
        if (id == Guid.Empty)
            throw new InvalidOperationException("No admin seeded.");
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
                "TRUNCATE TABLE gamekit.refresh_tokens; " +
                "TRUNCATE TABLE gamekit.player_credentials; " +
                "TRUNCATE TABLE gamekit.player_identities; " +
                "TRUNCATE TABLE gamekit.admin_users; " +
                "DELETE FROM gamekit.players";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Tables not yet materialized — first test-host construction runs migrations.
        }
    }

    private static string? GetStringLenient(JsonElement root, string propertyName)
    {
        // JSON casing from Results.Json uses camelCase by default in ASP.NET Core; fall back
        // to PascalCase just in case an upstream serializer option changes.
        if (root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString();
        var pascal = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        if (root.TryGetProperty(pascal, out var el2) && el2.ValueKind == JsonValueKind.String)
            return el2.GetString();
        return null;
    }

    private sealed record TokenResponseShape(string AccessToken, string? RefreshToken, string TokenType);
}

/// <summary>
/// Extension: expose the private <c>_host.Services</c> as a readable entry point so tests can
/// create arbitrary DI scopes (beyond the <see cref="AdminTestHost.Resolve{T}"/> single-service
/// shape). Reaches into the host via reflection since the property is intentionally
/// non-public; encapsulated here to keep the test body clean.
/// </summary>
internal static class AdminTestHostServicesExtensions
{
    public static IServiceProvider Services(this AdminTestHost host)
    {
        // AdminTestHost.Resolve<T>() + CreateDbScope() both go through the inner IHost.Services;
        // we need a generic accessor to iterate IEnumerable<IOAuthProvider>. Use the private
        // field via reflection since making it public would bloat the harness surface.
        var field = typeof(AdminTestHost).GetField("_host",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var innerHost = field!.GetValue(host)
            ?? throw new InvalidOperationException("AdminTestHost not initialized.");
        var servicesProp = innerHost.GetType().GetProperty("Services")
            ?? throw new InvalidOperationException("IHost.Services missing.");
        return (IServiceProvider)servicesProp.GetValue(innerHost)!;
    }
}
