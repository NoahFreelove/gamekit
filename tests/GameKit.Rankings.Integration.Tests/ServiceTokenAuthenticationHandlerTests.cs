// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Authentication;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="ServiceTokenAuthenticationHandler"/> (D-05 / D-06 / RANK-11).
/// Exercises all five authentication paths:
/// <list type="bullet">
///   <item>Valid token returns 200 (authenticated).</item>
///   <item>Revoked token returns 401.</item>
///   <item>Expired token returns 401.</item>
///   <item>Unknown token returns 401.</item>
///   <item>Missing Authorization header returns 401 (policy challenge).</item>
/// </list>
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class ServiceTokenAuthenticationHandlerTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres + Redis fixtures.</summary>
    public ServiceTokenAuthenticationHandlerTests(PostgresFixture pg, RedisFixture redis)
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

    [Fact]
    public async Task ValidToken_Returns_200()
    {
        await using var server = await BuildTestServer(_cs, _redis.ConnectionString);
        using var client = server.CreateClient();

        var (raw, _) = await IssueTokenAsync(server, "test-valid", expiresAt: null);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", raw);

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokedToken_Returns_401()
    {
        await using var server = await BuildTestServer(_cs, _redis.ConnectionString);
        using var client = server.CreateClient();

        var (raw, _) = await IssueTokenAsync(server, "test-revoked", expiresAt: null);
        await RevokeTokenAsync(server, "test-revoked");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", raw);

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_Returns_401()
    {
        await using var server = await BuildTestServer(_cs, _redis.ConnectionString);
        using var client = server.CreateClient();

        // Issue with expiry in the past.
        var (raw, _) = await IssueTokenAsync(
            server, "test-expired", expiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", raw);

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownToken_Returns_401()
    {
        await using var server = await BuildTestServer(_cs, _redis.ConnectionString);
        using var client = server.CreateClient();

        // Never minted — any random bearer will fail the hash lookup.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "this-token-was-never-minted");

        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingAuthorizationHeader_Returns_401()
    {
        await using var server = await BuildTestServer(_cs, _redis.ConnectionString);
        using var client = server.CreateClient();

        // No header — the policy challenge produces 401.
        var response = await client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Task<GameKitTestServer> BuildTestServer(string cs, string redisCs)
        => GameKitTestServer.CreateAsync(cs, redisCs);

    private static async Task<(string Raw, Entities.ServiceToken Row)> IssueTokenAsync(
        GameKitTestServer server, string name, DateTimeOffset? expiresAt)
    {
        using var scope = server.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IServiceTokenService>();
        return await svc.IssueAsync(name, expiresAt, default);
    }

    private static async Task RevokeTokenAsync(GameKitTestServer server, string name)
    {
        using var scope = server.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IServiceTokenService>();
        await svc.RevokeAsync(name, default);
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_auth_handler_" + Guid.NewGuid().ToString("N")[..12];

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
        // Core
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // Rankings
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
}

/// <summary>
/// Minimal in-process <see cref="TestServer"/> that mounts the <c>GameKitServiceToken</c> scheme
/// and exposes a single <c>GET /protected</c> endpoint requiring the policy.
/// Wraps <see cref="IHost"/> so the test can resolve services and send HTTP requests.
/// </summary>
internal sealed class GameKitTestServer : IAsyncDisposable
{
    private readonly IHost _host;

    private GameKitTestServer(IHost host)
    {
        _host = host;
    }

    public IServiceProvider Services => _host.Services;

    public HttpClient CreateClient()
    {
        var testServer = _host.GetTestServer();
        return testServer.CreateClient();
    }

    public static async Task<GameKitTestServer> CreateAsync(string cs, string redisCs)
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
                        .AddRankings(o => { });

                    // RankingsTickerLeaseHelper (registered as a hosted service via AddRankings)
                    // requires IConnectionMultiplexer. Tests don't exercise the ticker but the
                    // host activates all hosted services on StartAsync, so the dependency must
                    // be resolvable.
                    services.AddSingleton<IConnectionMultiplexer>(_ =>
                        ConnectionMultiplexer.Connect(redisCs));

                    // Override DbContext to include Rankings entities (bypass global EF model cache).
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts
                            .UseNpgsql(cs)
                            .ReplaceService<IModelCustomizer, ServiceTokenTestModelCustomizer>()
                            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/protected", () => "OK")
                            .RequireAuthorization(ServiceTokenAuthenticationDefaults.PolicyName);
                    });
                });
            });

        var host = builder.Build();
        await host.StartAsync();
        return new GameKitTestServer(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>
/// Test-only model customizer that includes Rankings entities in the EF model
/// (bypasses global cache — Pitfall 3).
/// </summary>
internal sealed class ServiceTokenTestModelCustomizer : RelationalModelCustomizer
{
    public ServiceTokenTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
