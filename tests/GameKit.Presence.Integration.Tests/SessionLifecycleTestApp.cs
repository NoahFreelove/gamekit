// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Presence.Builder;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Entities;
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
using Npgsql;
using StackExchange.Redis;

namespace GameKit.Presence.Integration.Tests;

/// <summary>
/// In-process ASP.NET Core test host with <c>AddGameKit().AddAuth().AddRankings().AddPresence()</c>
/// composed and the full GameKit endpoint surface mapped. Adds the
/// service-token authentication handler so the test can call
/// <c>POST /api/sessions/{id}/start|complete|abandon</c> with a freshly-minted token.
/// </summary>
/// <remarks>
/// <para>
/// Used exclusively by <see cref="SessionsLifecycleObserverTests"/> (Plan 06-05 Task 3) to
/// empirically validate the cross-package ISessionLifecycleObserver wire-up — the
/// game-server's POST to /start sets <c>presence:{playerId}=in_match</c> via
/// <c>PresenceSessionObserver</c>; /complete + /abandon clear it back to <c>online</c>.
/// </para>
/// <para>
/// Why a separate class from <see cref="PresenceTestApp"/>: PresenceTestApp deliberately
/// omits Rankings (PATTERNS Block 12 — Presence does not depend on Rankings at runtime).
/// SessionsLifecycleObserverTests is the one test that needs the hybrid (Core + Auth +
/// Rankings + Presence) and gets its own host class to keep that test-only coupling
/// contained.
/// </para>
/// </remarks>
internal sealed class SessionLifecycleTestApp : IAsyncDisposable
{
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private readonly RSA _signingRsa;
    private readonly string _ladderName;
    private IHost? _host;

    public HttpClient Client { get; private set; } = default!;
    public string ConnectionString { get; private set; } = string.Empty;
    public string Issuer { get; } = "gk-lifecycle-test";
    public string Audience { get; } = "gk-lifecycle-test";
    public string RedisConnectionString { get; private set; } = string.Empty;
    public IConnectionMultiplexer Multiplexer { get; private set; } = default!;
    public IServiceProvider Services => _host!.Services;

    public SessionLifecycleTestApp(string ladderName = "lifecycle-ladder")
    {
        _ladderName = ladderName;
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-lifecycle-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        File.WriteAllText(_privPath, _signingRsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, _signingRsa.ExportRSAPublicKeyPem());
    }

    public async Task StartAsync(PostgresFixture pg, RedisFixture redis)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(redis);

        RedisConnectionString = redis.ConnectionString;
        ConnectionString = await CreateFreshDatabaseAsync(pg);
        await ApplyMigrationsAsync(ConnectionString);

        Multiplexer = ConnectionMultiplexer.Connect(RedisConnectionString);

        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var b = services.AddGameKit(o =>
                    {
                        o.ConnectionString = ConnectionString;
                        o.AutoMigrate = false;
                    });
                    b.AddAuth(o =>
                    {
                        o.Jwt.Issuer = Issuer;
                        o.Jwt.Audience = Audience;
                        o.Jwt.PrivateKeyPemPath = _privPath;
                        o.Jwt.PublicKeyPemPath = _pubPath;
                        o.Jwt.Kid = "test-kid";
                    });
                    b.AddRankings(_ => { }).AddLadder(_ladderName);
                    b.AddPresence();

                    // Inject the Redis multiplexer the Presence + Rankings layers share.
                    var muxDescriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(IConnectionMultiplexer));
                    if (muxDescriptor is not null) services.Remove(muxDescriptor);
                    services.AddSingleton<IConnectionMultiplexer>(Multiplexer);

                    // Override DbContext to include all three packages' entities (Auth +
                    // Rankings + Core) — bypasses the global EF Core model cache (PITFALLS #3).
                    services.AddDbContext<GameKitDbContext>((_, opts) =>
                        opts.UseNpgsql(ConnectionString)
                            .ReplaceService<IModelCustomizer, LifecycleHostModelCustomizer>()
                            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseGameKitAuth();
                    app.UseGameKit();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapAuth();
                        e.MapGameKit();
                        e.MapPresence();
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        Client = _host.GetTestClient();
    }

    /// <summary>Issues a fresh service-token (game-server) JWT via the in-host
    /// <see cref="IServiceTokenService"/>.</summary>
    public async Task<string> IssueServiceTokenAsync(string name = "test-game-server")
    {
        using var scope = _host!.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IServiceTokenService>();
        var (raw, _) = await svc.IssueAsync(name, expiresAt: null, default);
        return raw;
    }

    /// <summary>Returns an HttpClient pre-loaded with a service-token Authorization header.</summary>
    public async Task<HttpClient> CreateServiceTokenClient(string name = "test-game-server")
    {
        var raw = await IssueServiceTokenAsync(name);
        var client = _host!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", raw);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            try { await _host.StopAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            _host.Dispose();
        }
        try { Multiplexer?.Dispose(); } catch { /* best-effort */ }
        Client?.Dispose();
        _signingRsa.Dispose();
        if (Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_lifecycle_" + Guid.NewGuid().ToString("N")[..12];

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
        // Core migrations.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; });
        await using (var coreSp = coreServices.BuildServiceProvider())
        {
            await using var scope = coreSp.CreateAsyncScope();
            await MigrationRunner.MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        // Auth migrations.
        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .Options;
        await using (var authCtx = new GameKitDbContext(authOpts))
        {
            await authCtx.Database.MigrateAsync().ConfigureAwait(false);
        }

        // Rankings migrations.
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
/// Runtime DbContext model customizer that composes Core + Auth + Rankings entities into
/// a single <c>GameKitDbContext</c> model for the in-process test host.
/// Mirrors the per-package model-customizer pattern (PITFALLS #3 — bypasses the global EF
/// Core model cache so tests are not poisoned by per-package isolated contexts).
/// </summary>
internal sealed class LifecycleHostModelCustomizer : RelationalModelCustomizer
{
    public LifecycleHostModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new AuthModelBuilderExtension().ApplyTo(modelBuilder);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
