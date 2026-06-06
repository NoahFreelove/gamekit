// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameKit.Admin.UI;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Builder;
using GameKit.Admin.UI.Data;
using GameKit.Admin.UI.Data.Configurations;
using GameKit.Admin.UI.Entities;
using GameKit.Auth;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Auth.Data.Configurations;
using GameKit.Auth.Entities;
using GameKit.Auth.Services;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Auth.AccountMerge.Integration.Tests;

/// <summary>
/// SC#5 endpoint-level integration proofs: POST /admin/api/players/merge authz + response shape.
/// Uses Testcontainers Postgres + Redis. Admin host is stood up with an in-process TestServer.
/// </summary>
[Collection("AccountMerge")]
[Trait("Category", "Integration")]
public sealed class AccountMergeEndpointTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public AccountMergeEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact(DisplayName = "SC#5: POST /players/merge with admin-role (non-superadmin) cookie returns 403")]
    public async Task SC5_AuthZ_NonSuperadmin_Returns403()
    {
        await using var host = await MergeTestHost.StartAsync(_pg, _redis);

        // Login as an admin-role (non-superadmin) user.
        await host.Client.LoginAsAdminAsync("mergeadmin", "hunter2hunter2");
        var csrf = await host.Client.HarvestAntiforgeryTokenAsync();

        var (sourceId, targetId) = await host.SeedTwoPlayersAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/api/players/merge")
        {
            Content = JsonContent.Create(new { sourcePlayerId = sourceId, targetPlayerId = targetId }),
        };
        req.Headers.Add("X-GameKit-Admin-CSRF", csrf);

        var resp = await host.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact(DisplayName = "SC#5: POST /players/merge with superadmin cookie returns 200")]
    public async Task SC5_Superadmin_Returns200()
    {
        await using var host = await MergeTestHost.StartAsync(_pg, _redis);

        // Login as superadmin.
        await host.Client.LoginAsAdminAsync("superadmin", "hunter2hunter2");
        var csrf = await host.Client.HarvestAntiforgeryTokenAsync();

        var (sourceId, targetId) = await host.SeedTwoPlayersAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/api/players/merge")
        {
            Content = JsonContent.Create(new { sourcePlayerId = sourceId, targetPlayerId = targetId }),
        };
        req.Headers.Add("X-GameKit-Admin-CSRF", csrf);

        var resp = await host.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ─── CR-01: MergePlayersRequestValidator registration ──────────────────────────────────────

    [Fact(DisplayName = "CR-01: POST /players/merge with Guid.Empty sourcePlayerId returns 400 (validator is registered)")]
    public async Task CR01_EmptySourceGuid_Returns400()
    {
        await using var host = await MergeTestHost.StartAsync(_pg, _redis);

        await host.Client.LoginAsAdminAsync("superadmin", "hunter2hunter2");
        var csrf = await host.Client.HarvestAntiforgeryTokenAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/api/players/merge")
        {
            // sourcePlayerId is Guid.Empty — MergePlayersRequestValidator must reject this.
            Content = JsonContent.Create(new { sourcePlayerId = Guid.Empty, targetPlayerId = Guid.NewGuid() }),
        };
        req.Headers.Add("X-GameKit-Admin-CSRF", csrf);

        var resp = await host.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact(DisplayName = "CR-01: POST /players/merge with self-merge (source == target) returns 400 (validator is registered)")]
    public async Task CR01_SelfMerge_Returns400()
    {
        await using var host = await MergeTestHost.StartAsync(_pg, _redis);

        await host.Client.LoginAsAdminAsync("superadmin", "hunter2hunter2");
        var csrf = await host.Client.HarvestAntiforgeryTokenAsync();

        var sameId = Guid.NewGuid();
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/api/players/merge")
        {
            // source == target — MergePlayersRequestValidator must reject this.
            Content = JsonContent.Create(new { sourcePlayerId = sameId, targetPlayerId = sameId }),
        };
        req.Headers.Add("X-GameKit-Admin-CSRF", csrf);

        var resp = await host.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact(DisplayName = "SC#5: Response JSON body does NOT contain the source player_id")]
    public async Task SC5_ResponseShape_NoSourceId_InBody()
    {
        await using var host = await MergeTestHost.StartAsync(_pg, _redis);

        await host.Client.LoginAsAdminAsync("superadmin", "hunter2hunter2");
        var csrf = await host.Client.HarvestAntiforgeryTokenAsync();

        var (sourceId, targetId) = await host.SeedTwoPlayersAsync();
        var sourceIdString = sourceId.ToString();

        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/api/players/merge")
        {
            Content = JsonContent.Create(new { sourcePlayerId = sourceId, targetPlayerId = targetId }),
        };
        req.Headers.Add("X-GameKit-Admin-CSRF", csrf);

        var resp = await host.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();

        // SC#5 / T-10-04-03: the response body must NOT contain the source player_id.
        Assert.DoesNotContain(sourceIdString, body, StringComparison.OrdinalIgnoreCase);

        // The response must contain the target player id.
        Assert.Contains(targetId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    // ─── LOCAL ADMIN TEST HOST ──────────────────────────────────────────────────────────────────

    private sealed class MergeTestHost : IAsyncDisposable
    {
        private readonly string _keyDir;
        private IHost? _host;
        private string? _connectionString;

        public HttpClient Client { get; private set; } = default!;

        private MergeTestHost()
        {
            _keyDir = Path.Combine(Path.GetTempPath(), $"gk-merge-ep-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_keyDir);
            var privPath = Path.Combine(_keyDir, "priv.pem");
            var pubPath = Path.Combine(_keyDir, "pub.pem");
            using var rsa = RSA.Create(2048);
            File.WriteAllText(privPath, rsa.ExportRSAPrivateKeyPem());
            File.WriteAllText(pubPath, rsa.ExportRSAPublicKeyPem());
            PrivPath = privPath;
            PubPath = pubPath;
        }

        private string PrivPath { get; }
        private string PubPath { get; }

        public static async Task<MergeTestHost> StartAsync(PostgresFixture pg, RedisFixture redis)
        {
            var h = new MergeTestHost();
            await h.InitializeAsync(pg, redis).ConfigureAwait(false);
            return h;
        }

        private async Task InitializeAsync(PostgresFixture pg, RedisFixture redis)
        {
            _connectionString = pg.OwnerConnectionString;

            // Apply Core + Auth + Admin migrations.
            await MigrateAsync(_connectionString).ConfigureAwait(false);

            // Seed: one superadmin + one regular admin.
            await SeedAdminsAsync(_connectionString).ConfigureAwait(false);

            _host = await Host.CreateDefaultBuilder()
                .UseEnvironment("Production")
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        var b = services.AddGameKit(o =>
                        {
                            o.ConnectionString = _connectionString!;
                            o.RedisConnectionString = redis.ConnectionString;
                            o.AutoMigrate = false;
                        });
                        b.AddAuth(o =>
                        {
                            o.Jwt.Issuer = "gk-merge-ep-test";
                            o.Jwt.Audience = "gk-merge-ep-test";
                            o.Jwt.PrivateKeyPemPath = PrivPath;
                            o.Jwt.PublicKeyPemPath = PubPath;
                            o.Jwt.Kid = "merge-ep-test-kid";
                        });
                        b.AddGameKitAdmin();

                        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                            dbOpts.UseNpgsql(_connectionString!, npg =>
                            {
                                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                                npg.MigrationsHistoryTable(
                                    GameKitMigrationConstants.MigrationsHistoryTable,
                                    GameKitMigrationConstants.SchemaName);
                            }).ReplaceService<IModelCustomizer, MergeEndpointRuntimeQueryCustomizer>());

                        services.AddSingleton<IConnectionMultiplexer>(
                            ConnectionMultiplexer.Connect(redis.ConnectionString));
                    });
                    web.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseRateLimiter();
                        app.UseGameKitAuth();
                        app.UseGameKit();
                        app.UseGameKitAdmin();
                        app.UseEndpoints(e =>
                        {
                            e.MapAuth();
                            e.MapGameKit();
                            e.MapGameKitAdmin();
                        });
                    });
                })
                .StartAsync()
                .ConfigureAwait(false);

            Client = _host.GetTestServer().CreateClient();
        }

        /// <summary>Seeds two players and returns (sourceId, targetId) for endpoint merge testing.</summary>
        public async Task<(Guid sourceId, Guid targetId)> SeedTwoPlayersAsync()
        {
            if (_connectionString is null) throw new InvalidOperationException("Not initialized.");

            var sourceId = Guid.CreateVersion7();
            var targetId = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;

            await using var conn = new Npgsql.NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(
                """
                INSERT INTO gamekit.players ("Id", "DisplayName", "CreatedAt", "IsBanned")
                VALUES (@sid, @sdn, @now, FALSE),
                       (@tid, @tdn, @now2, FALSE)
                """, conn);
            cmd.Parameters.AddWithValue("@sid", sourceId);
            cmd.Parameters.AddWithValue("@sdn", $"ep-source-{sourceId:N}"[..24]);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@tid", targetId);
            cmd.Parameters.AddWithValue("@tdn", $"ep-target-{targetId:N}"[..24]);
            cmd.Parameters.AddWithValue("@now2", now);
            await cmd.ExecuteNonQueryAsync();

            return (sourceId, targetId);
        }

        private static async Task MigrateAsync(string connectionString)
        {
            // Core — suppress PendingModelChangesWarning (mirrors TestHelpers.ApplyMigrations:
            // Core snapshot differs from Auth runtime model once Auth entities are registered, per
            // per-package migration boundary PITFALLS #3).
            var coreServices = new ServiceCollection();
            coreServices.AddGameKit(o => { o.ConnectionString = connectionString; o.AutoMigrate = false; });
            coreServices.AddDbContext<GameKitDbContext>((sp, dbOpts) =>
                dbOpts.UseNpgsql(connectionString, npg =>
                {
                    npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                    npg.MigrationsHistoryTable(
                        GameKitMigrationConstants.MigrationsHistoryTable,
                        GameKitMigrationConstants.SchemaName);
                })
                .UseApplicationServiceProvider(sp)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
            await using (var coreSp = coreServices.BuildServiceProvider())
            await using (var scope = coreSp.CreateAsyncScope())
            {
                await MigrationRunner
                    .MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>())
                    .ConfigureAwait(false);
            }

            // Auth.
            var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
                .UseNpgsql(connectionString, npg =>
                {
                    npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                    npg.MigrationsHistoryTable(
                        AuthMigrationConstants.MigrationsHistoryTable,
                        GameKitMigrationConstants.SchemaName);
                })
                .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;
            await using (var authCtx = new GameKitDbContext(authOpts))
                await MigrationRunner.MigrateWithLockAsync(authCtx, AuthMigrationConstants.AdvisoryLockKey)
                    .ConfigureAwait(false);

            // Admin.
            var adminOpts = new DbContextOptionsBuilder<GameKitDbContext>()
                .UseNpgsql(connectionString, npg =>
                {
                    npg.MigrationsAssembly(typeof(AdminMigrationConstants).Assembly.FullName);
                    npg.MigrationsHistoryTable(
                        AdminMigrationConstants.MigrationsHistoryTable,
                        GameKitMigrationConstants.SchemaName);
                })
                .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;
            await using (var adminCtx = new GameKitDbContext(adminOpts))
                await MigrationRunner.MigrateWithLockAsync(adminCtx, AdminMigrationConstants.AdvisoryLockKey)
                    .ConfigureAwait(false);

            // Rankings — required by AccountMergeService (player_ranks FK surgery).
            var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
                .UseNpgsql(connectionString, npg =>
                {
                    npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                    npg.MigrationsHistoryTable(
                        RankingsMigrationConstants.MigrationsHistoryTable,
                        GameKitMigrationConstants.SchemaName);
                })
                .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;
            await using (var rankingsCtx = new GameKitDbContext(rankingsOpts))
                await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey)
                    .ConfigureAwait(false);

            // Matchmaking — required by AccountMergeService (party_members same-party check).
            var matchmakingOpts = new DbContextOptionsBuilder<GameKitDbContext>()
                .UseNpgsql(connectionString, npg =>
                {
                    npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
                    npg.MigrationsHistoryTable(
                        MatchmakingMigrationConstants.MigrationsHistoryTable,
                        GameKitMigrationConstants.SchemaName);
                })
                .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;
            await using (var matchmakingCtx = new GameKitDbContext(matchmakingOpts))
                await MigrationRunner.MigrateWithLockAsync(matchmakingCtx, MatchmakingMigrationConstants.AdvisoryLockKey)
                    .ConfigureAwait(false);
        }

        private static async Task SeedAdminsAsync(string connectionString)
        {
            var authOpts = new GameKitAuthOptions();
            var hasher = new BCryptPasswordHasher(authOpts);

            // Use raw Npgsql with ON CONFLICT DO NOTHING so this is idempotent across SC#5 tests
            // that share the same Testcontainers Postgres instance (shared PostgresFixture).
            await using var conn = new Npgsql.NpgsqlConnection(connectionString);
            await conn.OpenAsync().ConfigureAwait(false);

            await using var cmd = new Npgsql.NpgsqlCommand(
                """
                INSERT INTO gamekit.admin_users ("Id", "Username", "PasswordHash", "Role", "CreatedAt")
                VALUES (@sid, @su, @sph, @srl, @now),
                       (@aid, @au, @aph, @arl, @now)
                ON CONFLICT ("Username") DO NOTHING
                """, conn);
            var now = DateTimeOffset.UtcNow;
            cmd.Parameters.AddWithValue("@sid", Guid.CreateVersion7());
            cmd.Parameters.AddWithValue("@su", "superadmin");
            cmd.Parameters.AddWithValue("@sph", hasher.Hash("hunter2hunter2"));
            cmd.Parameters.AddWithValue("@srl", AdminRoles.Superadmin);
            cmd.Parameters.AddWithValue("@aid", Guid.CreateVersion7());
            cmd.Parameters.AddWithValue("@au", "mergeadmin");
            cmd.Parameters.AddWithValue("@aph", hasher.Hash("hunter2hunter2"));
            cmd.Parameters.AddWithValue("@arl", AdminRoles.Admin);
            cmd.Parameters.AddWithValue("@now", now);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
            {
                try { await _host.StopAsync().ConfigureAwait(false); } catch { /* best-effort */ }
                _host.Dispose();
            }
            Client?.Dispose();
            if (Directory.Exists(_keyDir))
            {
                try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
            }
        }

        /// <summary>
        /// Runtime customizer that applies Core (via base), Auth, Rankings, Matchmaking, and Admin
        /// entity configurations so the test host's runtime DbContext can query all tables used by
        /// <see cref="IAccountMergeService"/>: player_ranks (Rankings), party_members (Matchmaking),
        /// refresh_tokens, player_identities, account_merges (Auth), and admin_users (Admin).
        /// </summary>
        internal sealed class MergeEndpointRuntimeQueryCustomizer : RelationalModelCustomizer
        {
            public MergeEndpointRuntimeQueryCustomizer(ModelCustomizerDependencies dependencies)
                : base(dependencies) { }

            public override void Customize(ModelBuilder modelBuilder, DbContext context)
            {
                base.Customize(modelBuilder, context);
                // Auth entities
                modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
                modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
                modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
                modelBuilder.ApplyConfiguration(new AccountMergeConfiguration());
                // Rankings entities — player_ranks FK surgery
                new GameKit.Rankings.Data.RankingsModelBuilderExtension().ApplyTo(modelBuilder);
                // Matchmaking entities — party_members same-party conflict check
                new GameKit.Matchmaking.Data.MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
                // Admin entities
                modelBuilder.ApplyConfiguration(new AdminUserConfiguration());
            }
        }
    }
}

/// <summary>
/// Helpers for admin-cookie acquisition + antiforgery-token harvesting in AccountMerge endpoint tests.
/// <para>
/// TestServer.CreateClient() uses an in-memory transport without automatic cookie-jar management.
/// These helpers manually propagate the <c>Set-Cookie</c> values from login into subsequent requests
/// via a <c>CookieContainer</c> stored on the <see cref="HttpClient"/> instance default headers.
/// </para>
/// </summary>
internal static class MergeTestWebClientExtensions
{
    // Key used to store the per-client cookie container in the client's DefaultRequestHeaders tag.
    // HttpClient doesn't have a generic "extra state" bag, so we use a thread-safe CookieContainer
    // per client instance and store the cookie string as a default header value set after login.

    public static async Task<HttpClient> LoginAsAdminAsync(
        this HttpClient client,
        string username,
        string password)
    {
        var resp = await client.PostAsJsonAsync("/admin/api/login",
            new { username, password }).ConfigureAwait(false);
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"LoginAsAdminAsync failed: status={resp.StatusCode} body={body}");
        }

        // TestServer.CreateClient() does not maintain a cookie jar.
        // Extract Set-Cookie headers from the login response and set them as the default Cookie
        // header on subsequent requests so the admin session is recognized.
        var cookies = new System.Text.StringBuilder();
        foreach (var header in resp.Headers)
        {
            if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var v in header.Value)
                {
                    // Each "Set-Cookie: name=value; Path=...; HttpOnly" → extract name=value part.
                    var nameValue = v.Split(';')[0].Trim();
                    if (cookies.Length > 0) cookies.Append("; ");
                    cookies.Append(nameValue);
                }
            }
        }
        if (cookies.Length > 0)
        {
            // Replace any previous Cookie header to avoid duplicate values.
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", cookies.ToString());
        }

        return client;
    }

    public static async Task<string> HarvestAntiforgeryTokenAsync(this HttpClient client)
    {
        // GET /admin/login returns both:
        //   (a) a Set-Cookie: gk_admin_csrf=<token> response cookie (the "cookie token")
        //   (b) a hidden <input name="__RequestVerificationToken" value="<token>"> (the "request token")
        // ASP.NET Core antiforgery validates BOTH — the cookie token from the cookie and the
        // request token from the X-GameKit-Admin-CSRF header.  We must capture and propagate
        // the CSRF cookie here or subsequent mutation requests will fail with 400.
        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/login");
        var resp = await client.SendAsync(req).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Capture the antiforgery cookie (gk_admin_csrf) from the response and add it to
        // the client's default Cookie header alongside the session cookie.
        var existingCookies = client.DefaultRequestHeaders.TryGetValues("Cookie", out var cv)
            ? cv.FirstOrDefault() ?? string.Empty
            : string.Empty;
        var cookieSb = new System.Text.StringBuilder(existingCookies);
        foreach (var header in resp.Headers)
        {
            if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var v in header.Value)
                {
                    var nameValue = v.Split(';')[0].Trim();
                    if (cookieSb.Length > 0) cookieSb.Append("; ");
                    cookieSb.Append(nameValue);
                }
            }
        }
        if (cookieSb.Length > 0)
        {
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", cookieSb.ToString());
        }

        var m = Regex.Match(page, @"name=""__RequestVerificationToken""\s+value=""([^""]+)""");
        if (!m.Success)
        {
            throw new InvalidOperationException(
                "HarvestAntiforgeryTokenAsync: no __RequestVerificationToken found in /admin/login HTML");
        }
        return m.Groups[1].Value;
    }
}
