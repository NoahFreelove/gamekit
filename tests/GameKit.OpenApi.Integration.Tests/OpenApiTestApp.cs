// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Admin.UI.Builder;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Data;
using GameKit.OpenApi.Builder;
using GameKit.Presence.Builder;
using GameKit.Rankings.Builder;
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

namespace GameKit.OpenApi.Integration.Tests;

/// <summary>
/// In-process ASP.NET Core test host composing the FULL GameKit chain
/// (Core + Auth + Rankings + Matchmaking + Presence + Admin + OpenApi) +
/// mapping every player-facing endpoint and the admin surface. The shape
/// mirrors <c>samples/TicTacToeDuel/Program.cs</c> verbatim so the D-09
/// contract test in <c>OpenApiCoverageTests</c> enumerates the same
/// endpoint set the sample exposes.
/// </summary>
/// <remarks>
/// <para>
/// Pattern modelled on <c>PresenceTestApp</c> (Plan 06-04) — the same
/// per-test fresh-database + auth-migration sequence is reused so the
/// host boots cleanly without bleeding test state across cases. Admin
/// migrations are also applied so the admin endpoint surface is
/// observable in <see cref="OpenApiAdminRouteExclusionTests"/> (proves
/// the D-19 filter is non-vacuous).
/// </para>
/// <para>
/// We construct the host manually rather than using
/// <c>WebApplicationFactory&lt;Program&gt;</c> because the sample's
/// top-level Program type is internal by default and standing up the
/// real sample requires its on-disk JWT keys + appsettings — the
/// in-test host pattern avoids both concerns while preserving the
/// identical endpoint surface.
/// </para>
/// </remarks>
internal sealed class OpenApiTestApp : IAsyncDisposable
{
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private readonly RSA _signingRsa;
    private IHost? _host;

    /// <summary>HTTP client bound to the in-memory test server.</summary>
    public HttpClient Client { get; private set; } = default!;

    /// <summary>Connection string for the fresh per-host database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Redis connection string supplied to the host.</summary>
    public string RedisConnectionString { get; private set; } = string.Empty;

    /// <summary>The Redis multiplexer the host shares with the test for direct probing.</summary>
    public IConnectionMultiplexer Multiplexer { get; private set; } = default!;

    /// <summary>The host service provider — exposed so tests can resolve EndpointDataSource for D-09.</summary>
    public IServiceProvider Services => _host!.Services;

    /// <summary>Constructs the host — generates an ephemeral RSA PEM keypair under the temp directory.</summary>
    public OpenApiTestApp()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-openapi-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath  = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        File.WriteAllText(_privPath, _signingRsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath,  _signingRsa.ExportRSAPublicKeyPem());
    }

    /// <summary>Builds and starts the host against the shared Postgres + Redis fixtures.</summary>
    public async Task StartAsync(PostgresFixture pg, RedisFixture redis)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(redis);

        RedisConnectionString = redis.ConnectionString;
        ConnectionString = await CreateFreshDatabaseAsync(pg).ConfigureAwait(false);
        await ApplyMigrationsAsync(ConnectionString).ConfigureAwait(false);

        Multiplexer = ConnectionMultiplexer.Connect(RedisConnectionString);

        _host = await Host.CreateDefaultBuilder()
            .UseEnvironment("Development")  // Skips SuperadminGateHostedService's Production-throw path.
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    // Redis multiplexer first — Rankings + Matchmaking + Presence all expect it.
                    services.AddSingleton<IConnectionMultiplexer>(Multiplexer);

                    var b = services.AddGameKit(o =>
                    {
                        o.ConnectionString = ConnectionString;
                        o.RedisConnectionString = RedisConnectionString;
                        o.AutoMigrate = false;
                    });
                    b.AddAuth(o =>
                    {
                        o.Jwt.Issuer            = "gk-openapi-test";
                        o.Jwt.Audience          = "gk-openapi-test";
                        o.Jwt.PrivateKeyPemPath = _privPath;
                        o.Jwt.PublicKeyPemPath  = _pubPath;
                        o.Jwt.Kid               = "test-kid";
                    });
                    // Rankings + Matchmaking are added WITHOUT AddLadder so the StartupLadderUpserter
                    // returns early ("no ladders registered") — it would otherwise need a runtime
                    // IModelCustomizer to apply Rankings entity configurations, which is out of
                    // scope for the OpenAPI contract test. The endpoint surface (MapRankings /
                    // MapMatchmaking) is unaffected by whether ladders are seeded.
                    b.AddRankings();
                    b.AddMatchmaking(o => o.Ticker.TickIntervalMs = 500);
                    b.AddPresence();
                    b.AddGameKitAdmin();

                    // Plan 06-06 under test — the OpenApi runtime.
                    services.AddGameKitOpenApi();

                    // Runtime IModelCustomizer that applies Auth + Admin + Rankings + Matchmaking
                    // entity configurations onto the DbContext model so SuperadminGateHostedService
                    // / StartupLadderUpserter / Matchmaking startup paths can query their entity sets
                    // at boot. Mirrors AdminTestHost's AdminRuntimeQueryCustomizer (FOLLOW-UP-02-03-01
                    // ApplicationServiceProvider workaround).
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(ConnectionString)
                              .ReplaceService<IModelCustomizer, OpenApiRuntimeModelCustomizer>());
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
                        e.MapGameKit();
                        e.MapAuth();
                        e.MapRankings();
                        e.MapMatchmaking();
                        e.MapPresence();
                        e.MapGameKitOpenApi();
                        e.MapGameKitAdmin("/admin");
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        Client = _host.GetTestClient();
    }

    /// <inheritdoc />
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
        var dbName = "gamekit_openapi_" + Guid.NewGuid().ToString("N")[..12];

        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync().ConfigureAwait(false);
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var freshCs = builder.ConnectionString;

        await using (var freshConn = new NpgsqlConnection(freshCs))
        {
            await freshConn.OpenAsync().ConfigureAwait(false);
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return freshCs;
    }

    private static async Task ApplyMigrationsAsync(string cs)
    {
        // Pass 1 — Core. Runs MigrationRunner against the AddGameKit() service provider.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = cs;
            o.AutoMigrate = false;
        });
        await using (var coreSp = coreServices.BuildServiceProvider())
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(
                scope.ServiceProvider.GetRequiredService<GameKitDbContext>()).ConfigureAwait(false);
        }

        // Pass 2 — Auth.
        await using (var authCtx = BuildAuthMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                authCtx, AuthMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }

        // Pass 3 — Admin (MapGameKitAdmin needs admin_users + admin_audit_log to start clean).
        await using (var adminCtx = BuildAdminMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                adminCtx,
                GameKit.Admin.UI.Data.AdminMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }

        // Pass 4 — Rankings.
        await using (var rCtx = BuildRankingsMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                rCtx,
                GameKit.Rankings.Data.RankingsMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }

        // Pass 5 — Matchmaking.
        await using (var mmCtx = BuildMatchmakingMigrationContext(cs))
        {
            await MigrationRunner.MigrateWithLockAsync(
                mmCtx, MatchmakingMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
        }
    }

    private static GameKitDbContext BuildAuthMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
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
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildAdminMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKit.Admin.UI.Data.AdminMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKit.Admin.UI.Data.AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Admin.UI.Data.AdminMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildRankingsMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKit.Rankings.Data.RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKit.Rankings.Data.RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Rankings.Data.RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildMatchmakingMigrationContext(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }
}
