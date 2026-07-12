// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Admin.UI.Builder;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Data;
using GameKit.Matchmaking.Strategy;
using GameKit.Presence.Builder;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Entities;
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

namespace GameKit.Tutorial.SmokeTests;

/// <summary>
/// In-process ASP.NET Core test host that exercises the matchmaking-focused subset of GameKit
/// (Core + Auth + Rankings + Matchmaking + Presence + Admin) against
/// <c>samples/TicTacToeDuel/Program.cs</c>, with the <c>"tictactoe"</c> ladder registered in
/// both Rankings and Matchmaking, an in-process matchmaking ticker (500 ms interval), and all
/// <c>Map*</c> endpoints mapped. <c>GameKit.Lobby</c> is intentionally omitted — it is not
/// under test in this smoke suite (TicTacToeDuel does not use Lobby).
/// </summary>
/// <remarks>
/// <para>
/// Pattern modelled verbatim on <c>tests/GameKit.OpenApi.Integration.Tests/OpenApiTestApp.cs</c>:
/// ephemeral RSA 2048 PEM keypair under <c>%TEMP%</c>, fresh per-host Postgres database via
/// Testcontainers, Redis multiplexer from the shared <see cref="RedisFixture"/>.
/// </para>
/// <para>
/// We construct the host manually rather than using
/// <c>WebApplicationFactory&lt;Program&gt;</c> because the sample's Program type is internal
/// by default and standing up the real sample needs on-disk JWT keys + appsettings. This
/// hand-rolled approach is more robust and matches how every other GameKit integration test
/// host boots. (Plan 20-01 adds <c>public partial class Program</c> to the sample as a
/// forward-looking affordance for future WAF-based tests; it is NOT consumed here.)
/// </para>
/// <para>
/// Unlike <c>OpenApiTestApp</c>, this host DOES register and START the in-process matchmaking
/// ticker so proposals actually form between two enqueued players during the smoke test.
/// <c>AddGameKitHealthChecks</c> is registered so <c>GET /health/ready</c> exercises the full
/// readiness chain (Postgres + Redis + migration reporters).
/// </para>
/// </remarks>
internal sealed class TutorialSmokeTestApp : IAsyncDisposable
{
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private readonly RSA _signingRsa;
    private IHost? _host;

    /// <summary>Connection string for the fresh per-host database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Redis connection string.</summary>
    public string RedisConnectionString { get; private set; } = string.Empty;

    /// <summary>The Redis multiplexer shared with the host.</summary>
    public IConnectionMultiplexer Multiplexer { get; private set; } = default!;

    /// <summary>
    /// The Guid of the <c>"tictactoe"</c> ladder seeded by
    /// <see cref="StartAsync"/>. Non-empty after the host starts.
    /// </summary>
    public Guid TicTacToeLadderId { get; private set; } = Guid.Empty;

    /// <summary>The host service provider — exposed so tests can resolve services directly if needed.</summary>
    public IServiceProvider Services => _host!.Services;

    /// <summary>
    /// Constructs the app — generates an ephemeral RSA 2048 keypair under the temp directory.
    /// Does NOT start the host; call <see cref="StartAsync"/> (or use the static factory).
    /// </summary>
    public TutorialSmokeTestApp()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-tutorial-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath  = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        File.WriteAllText(_privPath, _signingRsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath,  _signingRsa.ExportRSAPublicKeyPem());
    }

    /// <summary>
    /// Convenience factory: constructs, starts, and returns a ready-to-use <see cref="TutorialSmokeTestApp"/>.
    /// </summary>
    public static async Task<TutorialSmokeTestApp> StartAsync(PostgresFixture pg, RedisFixture redis)
    {
        var app = new TutorialSmokeTestApp();
        await app.StartInternalAsync(pg, redis).ConfigureAwait(false);
        return app;
    }

    /// <summary>Builds a per-player <see cref="HttpClient"/> with the X-GameKit-Device header pre-set.</summary>
    /// <param name="deviceId">A unique device fingerprint string (e.g. a UUID) that the auth endpoints require.</param>
    public HttpClient CreateClient(string deviceId)
    {
        var client = _host!.GetTestClient();
        client.DefaultRequestHeaders.Add("X-GameKit-Device", deviceId);
        return client;
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
        _signingRsa.Dispose();
        if (Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private async Task StartInternalAsync(PostgresFixture pg, RedisFixture redis)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(redis);

        RedisConnectionString = redis.ConnectionString;
        ConnectionString = await CreateFreshDatabaseAsync(pg).ConfigureAwait(false);
        await ApplyMigrationsAsync(ConnectionString).ConfigureAwait(false);

        Multiplexer = ConnectionMultiplexer.Connect(RedisConnectionString);

        // The "tictactoe" Rankings ladder is seeded by the StartupLadderUpserter hosted service
        // at host startup (registered by AddRankings().AddLadder("tictactoe", ...)). We resolve
        // the Guid after startup by querying the Ladder table via a scoped DbContext.

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
                        o.Jwt.Issuer            = "gk-tutorial-test";
                        o.Jwt.Audience          = "gk-tutorial-test";
                        o.Jwt.PrivateKeyPemPath = _privPath;
                        o.Jwt.PublicKeyPemPath  = _pubPath;
                        o.Jwt.Kid               = "test-kid";
                    });

                    // Rankings — mirrors TicTacToeDuel/Program.cs AddRankings().AddLadder() chain.
                    // StartupLadderUpserter will seed the "tictactoe" Ladder row on first startup.
                    b.AddRankings()
                     .AddLadder("tictactoe", c =>
                     {
                         c.DefaultRating     = 1500;
                         c.DefaultRd         = 350;
                         c.DefaultVolatility = 0.06;
                         c.RatingPeriod      = System.TimeSpan.FromHours(1);
                     });

                    // Matchmaking — ticker runs in-process so proposals form during the smoke test.
                    // 500 ms tick mirrors the sample configuration.
                    // AddLadder("tictactoe") joins on Ladder.Name (D-12 cross-package join).
                    b.AddMatchmaking(o => o.Ticker.TickIntervalMs = 500)
                     .AddLadder("tictactoe", ladder =>
                     {
                         // Generous bracket ramp — both players have the same default rating so
                         // they pair immediately (BracketStart = 100 covers rating diff = 0).
                         ladder.BracketStart          = 100;
                         ladder.BracketEnd            = 500;
                         ladder.BracketRampSeconds    = 40;
                         ladder.PartyRatingAggregator = PartyRatingAggregator.Mean;
                     });

                    b.AddPresence();

                    // Admin — required so SuperadminGateHostedService / migration reporter register.
                    b.AddGameKitAdmin();

                    // Health checks — exercises the Postgres + Redis + migration readiness chain.
                    b.AddGameKitHealthChecks();

                    // Runtime IModelCustomizer that applies all package entity configurations
                    // so StartupLadderUpserter / SuperadminGateHostedService / ticker startup
                    // paths can query their entity sets at boot.
                    // Mirrors OpenApiRuntimeModelCustomizer (FOLLOW-UP-02-03-01 workaround).
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(ConnectionString)
                              .ReplaceService<IModelCustomizer, TutorialRuntimeModelCustomizer>());
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
                        e.MapGameKitHealth();   // /health/live + /health/ready
                        e.MapGameKit();
                        e.MapAuth();
                        e.MapRankings();
                        e.MapMatchmaking();
                        e.MapPresence();
                        e.MapGameKitAdmin("/admin");
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        // Resolve the tictactoe ladder Guid from the seeded Ladder table.
        // StartupLadderUpserter runs as a hosted service during StartAsync so the row exists now.
        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var ladder = await db.Set<Ladder>()
            .Where(l => l.Name == "tictactoe")
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        TicTacToeLadderId = ladder?.Id ?? Guid.Empty;
    }

    // ---- database helpers (mirrors OpenApiTestApp) ----

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_tutorial_" + Guid.NewGuid().ToString("N")[..12];

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
        // Pass 1 — Core.
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

        // Pass 3 — Admin.
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
                RankingsMigrationConstants.AdvisoryLockKey).ConfigureAwait(false);
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
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
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
