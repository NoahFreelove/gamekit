// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Telemetry;
using GameKit.Rankings.Builder;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.LoadTests;

/// <summary>
/// Phase 5 SC#3 load-harness fixture. Owns a dedicated Testcontainer Postgres + Redis pair
/// (NOT shared with the integration-test collection fixture) and stands up the full
/// in-process ASP.NET Core host: <c>AddGameKit().AddAuth().AddRankings().AddMatchmaking()</c>
/// with the Lua atomic-claim ticker, analytics drain, reconciler, retention cleanup, and
/// proposal sweeper all running on their natural schedules.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pitfall §8 mitigation — Maximum Pool Size=25.</b> The Npgsql connection string is
/// rebuilt via <see cref="NpgsqlConnectionStringBuilder"/> with
/// <see cref="NpgsqlConnectionStringBuilder.MaxPoolSize"/> = 25. The Phase-5 design assumes
/// the drain service holds at most one connection per batch; the reconciler holds at most
/// one per sweep; the retention sweep holds at most one per nightly pass; the ticker holds
/// zero (Redis-only). 25 leaves ~20 connections of headroom for the test driver's ad-hoc
/// seed/poll Npgsql connections without ballooning the active count Postgres tracks.
/// </para>
/// <para>
/// <b>OQ-4 implicit verification.</b> The fixture does NOT pause the reconciler or retention
/// services during the run — both run their natural schedules. The SC#3 budget assertion is
/// the verification surface: if reconciler + retention contend with the drain for the
/// 25-connection pool, the ticker iteration time will exceed the 50 ms budget OR the
/// <see cref="NpgsqlPoolObserver"/> will fire pool-exhaustion events. Either failure mode is
/// caught and surfaced with a descriptive error.
/// </para>
/// <para>
/// <b>Cooldown disabled.</b> The fixture sets <c>Cooldown.Step1Minutes = 0</c> in
/// <see cref="GameKitMatchmakingOptions"/> so the sustained-load re-enqueue loop is not
/// blocked by the escalating decline-cooldown (D-08). Production defaults remain intact —
/// this is a load-test-only override.
/// </para>
/// <para>
/// <b>Observers.</b> <see cref="Budget"/> subscribes to <see cref="MatchmakingActivitySource"/>
/// via <see cref="System.Diagnostics.ActivityListener"/>; <see cref="Pool"/> subscribes to
/// the Npgsql <see cref="System.Diagnostics.Tracing.EventSource"/>. Both are constructed
/// BEFORE the host starts so every tick + every Npgsql pool event is captured.
/// </para>
/// </remarks>
public sealed class LoadTestFixture : IAsyncLifetime
{
    private PostgresFixture? _pg;
    private RedisFixture? _redis;
    private IHost? _host;
    private RSA? _signingRsa;
    private string? _keyDir;

    /// <summary>Postgres connection string with <c>Maximum Pool Size=25</c> applied (Pitfall §8).</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Redis connection string from the per-fixture Testcontainer.</summary>
    public string RedisConnectionString { get; private set; } = string.Empty;

    /// <summary>The hosted <see cref="IServiceProvider"/>; <see langword="null"/> before <see cref="InitializeAsync"/>.</summary>
    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("LoadTestFixture not initialized — call InitializeAsync first.");

    /// <summary>The ticker-budget observer subscribed to <see cref="MatchmakingActivitySource"/>.</summary>
    public TickerBudgetObserver Budget { get; private set; } = new();

    /// <summary>The Npgsql pool observer subscribed to the <c>"Npgsql"</c> EventSource.</summary>
    public NpgsqlPoolObserver Pool { get; private set; } = new();

    /// <summary>The single Ladder id the load test enqueues against.</summary>
    public Guid TestLadderId { get; private set; } = Guid.NewGuid();

    /// <summary>Ladder name registered with <c>AddLadder</c>.</summary>
    public string TestLadderName { get; } = "loadtest";

    /// <summary>JWT issuer (matches audience for the in-process host).</summary>
    public string Issuer { get; } = "gk-mm-load";

    /// <summary>JWT audience.</summary>
    public string Audience { get; } = "gk-mm-load";

    /// <summary>
    /// HTTP client bound to the in-memory test server. Re-used by the load test for the
    /// 1000-concurrent enqueue burst — backed by <see cref="TestServer"/> which is fully
    /// thread-safe for parallel calls.
    /// </summary>
    public HttpClient Client { get; private set; } = default!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // 1. Start the per-fixture Testcontainer Postgres + Redis pair. We do NOT use the
        //    shared collection-fixture pair because the load test runs for 10+ minutes and
        //    should not contend with integration tests for a shared container.
        _pg = new PostgresFixture();
        _redis = new RedisFixture();
        await _pg.InitializeAsync().ConfigureAwait(false);
        await _redis.InitializeAsync().ConfigureAwait(false);

        RedisConnectionString = _redis.ConnectionString;

        // 2. Create a fresh per-host database with all migrations applied
        //    (Core + Auth + Admin + Rankings + Matchmaking). The helpers live in
        //    LoadTestMigrationHelpers (local; mirrors IntegrationTestHelpers in the
        //    integration-tests project) so LoadTests is self-contained — no dependency on
        //    GameKit.Matchmaking.Integration.Tests, which is `internal` and would force
        //    an IVT grant.
        var freshCs = await LoadTestMigrationHelpers.CreateFreshDatabaseAsync(_pg).ConfigureAwait(false);
        await LoadTestMigrationHelpers.ApplyMatchmakingMigrationsAsync(freshCs).ConfigureAwait(false);

        // 3. Append Maximum Pool Size=25 (Pitfall §8). Also set a tight CommandTimeout so a
        //    hung query surfaces quickly during the load run rather than silently consuming
        //    a pool slot.
        var b = new NpgsqlConnectionStringBuilder(freshCs)
        {
            MaxPoolSize = 25,
            Timeout = 15,
            CommandTimeout = 30,
        };
        ConnectionString = b.ConnectionString;

        // 4. Seed the Ladder row so /api/mm/queue can resolve a real LadderId.
        TestLadderId = await LoadTestMigrationHelpers.SeedLadderAsync(ConnectionString, TestLadderName)
            .ConfigureAwait(false);

        // 5. Generate an ephemeral RSA keypair for JWT signing.
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-mm-load-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        var priv = Path.Combine(_keyDir, "priv.pem");
        var pub = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        await File.WriteAllTextAsync(priv, _signingRsa.ExportRSAPrivateKeyPem()).ConfigureAwait(false);
        await File.WriteAllTextAsync(pub, _signingRsa.ExportRSAPublicKeyPem()).ConfigureAwait(false);

        // 6. Stand up the in-process host. AddMatchmaking with cooldown disabled + analytics
        //    channel capacity left at the production default (10000) — D-15 sufficiency under
        //    sustained 1k-concurrent load is one of the SC#3 assertions.
        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var gk = services.AddGameKit(o =>
                    {
                        o.ConnectionString = ConnectionString;
                        o.AutoMigrate = false;
                    });
                    gk.AddAuth(o =>
                    {
                        o.Jwt.Issuer = Issuer;
                        o.Jwt.Audience = Audience;
                        o.Jwt.PrivateKeyPemPath = priv;
                        o.Jwt.PublicKeyPemPath = pub;
                        o.Jwt.Kid = "loadtest-kid";
                    });
                    gk.AddRankings();
                    var mm = gk.AddMatchmaking(o =>
                    {
                        // Disable escalating cooldown for the sustained re-enqueue loop —
                        // production defaults stay intact (D-08); this is a load-test-only
                        // override so the same player can re-queue after a match.
                        o.Cooldown.Step1Minutes = 0;
                        o.Cooldown.Step2Minutes = 0;
                        o.Cooldown.Step3Minutes = 0;
                        // Short long-poll for the test driver's status check.
                        o.LongPollTimeoutSeconds = 5;
                        // Production tick interval — explicit so a future option-default
                        // change does not silently change the load-test cadence.
                        o.Ticker.TickIntervalMs = 500;
                        o.Ticker.MaxIterationBudgetMs = 50;
                    });
                    mm.AddLadder(TestLadderName);

                    // Replace the Redis multiplexer with one wired to the Testcontainer.
                    var muxDesc = services.FirstOrDefault(
                        d => d.ServiceType == typeof(IConnectionMultiplexer));
                    if (muxDesc is not null) services.Remove(muxDesc);
                    services.AddSingleton<IConnectionMultiplexer>(_ =>
                        ConnectionMultiplexer.Connect(RedisConnectionString));

                    // Replace the runtime DbContext with one that applies the test customizer
                    // so sibling-package entities are visible at query time (PITFALLS §3 /
                    // FOLLOW-UP-02-03-01 — same shape as MatchmakingTestApp).
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(ConnectionString)
                              .ReplaceService<IModelCustomizer, LoadTestModelCustomizer>());

                    // The Plan 05-07 reconciler's orphan-session sweep writes audit rows via
                    // IAdminAuditWriter. AddGameKitAdmin would register this normally, but the
                    // load host does NOT compose the Admin.UI (admin is out of scope for the
                    // load harness — we don't need Blazor + cookie auth + antiforgery). Register
                    // the writer directly so the reconciler's audit path can fire. Mirrors
                    // MatchmakingChaosTests:365.
                    services.AddScoped<GameKit.Admin.UI.Services.IAdminAuditWriter,
                                       GameKit.Admin.UI.Services.AdminAuditWriter>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    // Deliberately omit app.UseRateLimiter() — the production 5/min/player
                    // MmEnqueue policy would shed ~95% of the SC#3 sustained re-enqueue
                    // loop's traffic (~2 enq/sec/player × 1000 players) and starve the
                    // budget/pool/channel-drop observers of throughput. .RequireRateLimiting()
                    // metadata on endpoints is inert without the middleware. Rate-limit
                    // correctness is covered by MatchmakingRateLimitTests (Plan 05-08 SC#5
                    // phase gate); load testing the limiter is out of scope here.
                    app.UseGameKitAuth();
                    app.UseGameKit();
                    app.UseEndpoints(e =>
                    {
                        e.MapAuth();
                        e.MapGameKit();
                        e.MapMatchmaking();
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        Client = _host.GetTestClient();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        Client?.Dispose();

        if (_host is not null)
        {
            try { await _host.StopAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            _host.Dispose();
        }

        Budget?.Dispose();
        Pool?.Dispose();

        _signingRsa?.Dispose();
        if (_keyDir is not null && Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }

        if (_redis is not null) await _redis.DisposeAsync().ConfigureAwait(false);
        if (_pg is not null) await _pg.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Mints a player JWT signed with the host's RSA keypair.</summary>
    public string MintPlayerJwt(Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(_signingRsa);
        var creds = new SigningCredentials(new RsaSecurityKey(_signingRsa), SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim("sub", playerId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, playerId.ToString()),
                new Claim("is_guest", "false"),
                new Claim("provider", "loadtest"),
            },
            notBefore: now.AddMinutes(-1),
            expires: now.AddHours(2),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Builds a per-player <see cref="HttpClient"/> with the bearer token pre-attached.
    /// Idempotently upserts the player row so Matchmaking FKs succeed.
    /// </summary>
    public HttpClient CreateClient(Guid playerId)
    {
        EnsurePlayerRow(playerId);
        var client = _host!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintPlayerJwt(playerId));
        return client;
    }

    /// <summary>Bulk-upserts player rows so the load test does not pay N round-trips on warm-up.</summary>
    public void BulkInsertPlayers(System.Collections.Generic.IEnumerable<Guid> playerIds)
    {
        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO gamekit.players
            (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES (@id, @name, NOW(), false)
            ON CONFLICT (""Id"") DO NOTHING";
        var pId = cmd.Parameters.Add("id", NpgsqlTypes.NpgsqlDbType.Uuid);
        var pName = cmd.Parameters.Add("name", NpgsqlTypes.NpgsqlDbType.Text);
        foreach (var id in playerIds)
        {
            pId.Value = id;
            pName.Value = "L_" + id.ToString("N")[..8];
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Idempotent single-player INSERT — used by <see cref="CreateClient"/>.</summary>
    public void EnsurePlayerRow(Guid playerId)
    {
        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.players
            (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES (@id, @name, NOW(), false)
            ON CONFLICT (""Id"") DO NOTHING";
        cmd.Parameters.AddWithValue("id", playerId);
        cmd.Parameters.AddWithValue("name", "L_" + playerId.ToString("N")[..8]);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns the count of <c>matchmaking_tickets</c> rows in <c>Matched</c> status (5).
    /// Matches <see cref="GameKit.Matchmaking.Entities.TicketStatus.Matched"/> = 5 (integer
    /// enum storage per CONTEXT.md §Established Patterns).
    /// </summary>
    public async Task<long> CountMatchedTicketsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.matchmaking_tickets WHERE ""Status"" = 5";
        var n = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return n is long l ? l : Convert.ToInt64(n, System.Globalization.CultureInfo.InvariantCulture);
    }
}
