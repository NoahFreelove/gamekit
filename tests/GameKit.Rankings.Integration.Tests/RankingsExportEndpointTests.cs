// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Auth.Entities;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Http;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// HTTP-layer integration tests for the two GDPR-export endpoints (RANK-13 / D-15 / D-16),
/// closing 04-HUMAN-UAT item 5. Exercises <c>GET /api/players/{id}/export</c> (player path,
/// D-16 sub-claim mismatch → 403) and <c>GET /admin/api/players/{id}/export</c> (admin path,
/// superadmin-gated + audit row per D-16 / T-04-08-AT) at the real HTTP layer against
/// Testcontainers Postgres + Redis.
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class RankingsExportEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres + Redis fixtures.</summary>
    public RankingsExportEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Player path: sub-claim mismatch -> 403
    // -------------------------------------------------------------------------

    /// <summary>
    /// D-16: <c>GET /api/players/{B}/export</c> with an authenticated principal whose sub claim
    /// != B returns 403 Forbidden — the endpoint never even reaches the export service.
    /// </summary>
    [Fact]
    public async Task PlayerSubMismatch_Returns_403()
    {
        await using var server = await BuildServerAsync(_cs, _redis.ConnectionString);
        using var client = server.CreateClient();

        var playerB = Guid.NewGuid();
        var callerA = Guid.NewGuid(); // deliberately != playerB

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/players/{playerB}/export");
        request.Headers.Add("X-Test-Sub", callerA.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Admin path: superadmin -> 200 + exactly one audit row
    // -------------------------------------------------------------------------

    /// <summary>
    /// D-16 / T-04-08-AT: <c>GET /admin/api/players/{id}/export</c> as a superadmin principal
    /// returns 200 OK and writes exactly one <c>admin.player.gdpr_export</c> audit row keyed to
    /// the target player and the acting admin.
    /// </summary>
    [Fact]
    public async Task AdminPath_Requires_Superadmin_And_Writes_Audit()
    {
        await using var server = await BuildServerAsync(_cs, _redis.ConnectionString);
        using var client = server.CreateClient();

        var playerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, playerId, "ExportAdminPathPlayer");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/admin/api/players/{playerId}/export");
        request.Headers.Add("X-Test-Sub", adminId.ToString());
        request.Headers.Add("X-Test-Role", "superadmin");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auditCount = await QueryScalarAsync(_cs, $@"
            SELECT COUNT(*) FROM gamekit.admin_audit_log
            WHERE ""Action"" = 'admin.player.gdpr_export'
              AND ""TargetId"" = '{playerId}'
              AND ""ActorId"" = '{adminId}'");

        Assert.Equal(1L, auditCount);
    }

    // -------------------------------------------------------------------------
    // Admin path: non-superadmin -> 403, zero audit rows
    // -------------------------------------------------------------------------

    /// <summary>
    /// D-16: <c>GET /admin/api/players/{id}/export</c> as an authenticated "admin"-role
    /// (non-superadmin) principal returns 403 Forbidden and writes NO audit rows — proves
    /// the endpoint is superadmin-gated, not merely authenticated-gated.
    /// </summary>
    [Fact]
    public async Task AdminPath_NonSuperadmin_Returns_403_NoAudit()
    {
        await using var server = await BuildServerAsync(_cs, _redis.ConnectionString);
        using var client = server.CreateClient();

        var playerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await SeedPlayerAsync(_cs, playerId, "ExportNonSuperadminPlayer");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/admin/api/players/{playerId}/export");
        request.Headers.Add("X-Test-Sub", adminId.ToString());
        request.Headers.Add("X-Test-Role", "admin");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var auditCount = await QueryScalarAsync(_cs, $@"
            SELECT COUNT(*) FROM gamekit.admin_audit_log
            WHERE ""Action"" = 'admin.player.gdpr_export'
              AND ""TargetId"" = '{playerId}'");

        Assert.Equal(0L, auditCount);
    }

    // -------------------------------------------------------------------------
    // Test host
    // -------------------------------------------------------------------------

    private static async Task<RankingsExportEndpointTestServer> BuildServerAsync(string cs, string redisCs)
        => await RankingsExportEndpointTestServer.CreateAsync(cs, redisCs);

    // -------------------------------------------------------------------------
    // Helpers (mirrors GdprExportContractTests / SessionsStartEndpointTests patterns)
    // -------------------------------------------------------------------------

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_export_" + Guid.NewGuid().ToString("N")[..12];
        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync();
        }
        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var freshCs = builder.ConnectionString;
        await using (var freshConn = new NpgsqlConnection(freshCs))
        {
            await freshConn.OpenAsync();
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync();
        }
        return freshCs;
    }

    private static async Task ApplyMigrationsAsync(string cs)
    {
        // 1. Core migrations.
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // 2. Auth migrations — required for player_identities / player_credentials tables
        //    (IGdprExportService reads them even though this test doesn't seed identities).
        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
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
        {
            await MigrationRunner.MigrateWithLockAsync(authCtx, AuthMigrationConstants.AdvisoryLockKey);
        }

        // 3. Rankings migrations.
        var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var rankingsCtx = new GameKitDbContext(rankingsOpts);
        await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);
    }

    private static async Task SeedPlayerAsync(string cs, Guid id, string displayName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var now = DateTimeOffset.UtcNow;
        cmd.CommandText = $@"
            INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES ('{id}', '{displayName}', '{now:O}', false)
            ON CONFLICT DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> QueryScalarAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : Convert.ToInt64(result);
    }
}

/// <summary>
/// In-process <see cref="TestServer"/> mounting <c>MapRankingsPlayer</c> + <c>MapRankingsAdmin</c>
/// behind a minimal in-test authentication scheme that reads <c>X-Test-Sub</c> / <c>X-Test-Role</c>
/// request headers, plus the two admin authorization policies the mapped admin group requires
/// (<c>gamekit.admin.superadmin</c> / <c>gamekit.admin.admin</c>) bound to that scheme.
/// </summary>
internal sealed class RankingsExportEndpointTestServer : IAsyncDisposable
{
    private readonly IHost _host;

    private RankingsExportEndpointTestServer(IHost host) => _host = host;

    public HttpClient CreateClient() => _host.GetTestServer().CreateClient();

    public static async Task<RankingsExportEndpointTestServer> CreateAsync(string cs, string redisCs)
    {
        var builder = new HostBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();

                    services
                        .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
                        .AddRankings(o => { })
                        .AddLadder("export-ladder");

                    services.AddLogging();

                    // AddRankings' ticker hosted service resolves IConnectionMultiplexer at startup.
                    services.AddSingleton<IConnectionMultiplexer>(_ =>
                        ConnectionMultiplexer.Connect(redisCs));

                    // Override DbContext to include Rankings entities (bypass global EF model cache).
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts
                            .UseNpgsql(cs)
                            .ReplaceService<IModelCustomizer, RankingsExportEndpointTestModelCustomizer>()
                            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

                    // In-test auth scheme, set as BOTH default authenticate + default challenge
                    // scheme — no AddAuth() call in this host, so there is no competing default.
                    services
                        .AddAuthentication(RankingsExportEndpointTests_TestScheme.Name)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            RankingsExportEndpointTests_TestScheme.Name, _ => { });

                    services.AddAuthorization(o =>
                    {
                        o.AddPolicy("gamekit.admin.superadmin", p => p
                            .AddAuthenticationSchemes(RankingsExportEndpointTests_TestScheme.Name)
                            .RequireAuthenticatedUser()
                            .RequireRole("superadmin"));
                        o.AddPolicy("gamekit.admin.admin", p => p
                            .AddAuthenticationSchemes(RankingsExportEndpointTests_TestScheme.Name)
                            .RequireAuthenticatedUser()
                            .RequireRole("admin", "superadmin"));
                    });

                    services.AddAntiforgery();
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapRankingsPlayer();
                        endpoints.MapRankingsAdmin();
                    });
                });
            });

        var host = builder.Build();
        await host.StartAsync();
        return new RankingsExportEndpointTestServer(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>Scheme-name holder to avoid a magic-string mismatch between registration and policy binding.</summary>
internal static class RankingsExportEndpointTests_TestScheme
{
    public const string Name = "TestScheme";
}

/// <summary>
/// Minimal in-test authentication handler. Reads <c>X-Test-Sub</c> into
/// <see cref="ClaimTypes.NameIdentifier"/> and (optionally) <c>X-Test-Role</c> into
/// <see cref="ClaimTypes.Role"/>. Absent <c>X-Test-Sub</c> → <see cref="AuthenticateResult.NoResult"/>
/// so anonymous requests are challenged normally.
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
#pragma warning disable CS0618 // ISystemClock ctor overload retained for AuthenticationHandler back-compat across ASP.NET Core versions.
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }
#pragma warning restore CS0618

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Sub", out var subValues) || subValues.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new System.Collections.Generic.List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subValues[0]!),
        };

        if (Request.Headers.TryGetValue("X-Test-Role", out var roleValues) && roleValues.Count > 0)
            claims.Add(new Claim(ClaimTypes.Role, roleValues[0]!));

        var identity = new ClaimsIdentity(claims, RankingsExportEndpointTests_TestScheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, RankingsExportEndpointTests_TestScheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Test-only EF model customizer including Core + Auth + Rankings entities — mirrors
/// <c>GdprTestModelCustomizer</c> from <see cref="GdprExportContractTests"/>. Auth entity
/// configurations are applied inline because Auth's own configurations are internal.
/// </summary>
internal sealed class RankingsExportEndpointTestModelCustomizer : RelationalModelCustomizer
{
    public RankingsExportEndpointTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder.Entity<PlayerIdentity>(b =>
        {
            b.ToTable("player_identities", "gamekit");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Provider).IsRequired().HasMaxLength(16);
            b.Property(p => p.ExternalId).IsRequired().HasMaxLength(64);
            b.Property(p => p.DisplayName).HasMaxLength(64);
            b.Property(p => p.AvatarUrl).HasMaxLength(512);
            b.Property(p => p.Metadata).HasColumnType("jsonb");
            b.Property(p => p.CreatedAt).IsRequired();
            b.Property(p => p.UpdatedAt).IsRequired();
            b.HasIndex(p => new { p.Provider, p.ExternalId }).IsUnique();
            b.HasIndex(p => p.PlayerId);
            b.HasOne<GameKit.Core.Entities.Player>().WithMany()
                .HasForeignKey(p => p.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlayerCredential>(b =>
        {
            b.ToTable("player_credentials", "gamekit");
            b.HasKey(c => c.PlayerId);
            b.Property(c => c.PlayerId).ValueGeneratedNever();
            b.Property(c => c.Username).IsRequired().HasMaxLength(32).HasColumnType("citext");
            b.Property(c => c.PasswordHash).IsRequired().HasMaxLength(72);
            b.Property(c => c.UpdatedAt).IsRequired();
            b.HasIndex(c => c.Username).IsUnique();
            b.HasOne<GameKit.Core.Entities.Player>().WithMany()
                .HasForeignKey(c => c.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
