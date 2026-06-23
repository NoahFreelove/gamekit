// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Services;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// In-process ASP.NET Core test host with <c>AddGameKit().AddAuth().AddRankings().AddMatchmaking()</c>
/// composed and the full <c>/api/parties/*</c> + <c>/api/mm/*</c> route surface mapped. The host
/// exposes a <see cref="MintPlayerJwt"/> helper that issues a JWT signed with the same
/// ephemeral RSA keypair the host's JwtBearer middleware validates against — bypasses the
/// guest-login flow so endpoint tests can focus on the matchmaking layer.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the construction shape of <c>tests/GameKit.Auth.Integration.Tests/AuthTestHost.cs</c>
/// (Plan 02-07) but composes the Matchmaking pipeline on top: Auth supplies the JwtBearer
/// scheme + IssuerSigningKey; Rankings supplies the <c>Ladder</c> entity Matchmaking joins
/// against; Matchmaking supplies the endpoint surface under test.
/// </para>
/// <para>
/// <see cref="LongPollTimeoutSeconds"/> may be set before <see cref="StartAsync"/> to shorten
/// the long-poll wait for the LongPollStatusTests; default is 30 s (production).
/// </para>
/// <para>
/// <b>Two-replica shared-DB mode (SCALE-04):</b> pass <paramref name="connectionString"/> to
/// <see cref="StartAsync(PostgresFixture, RedisFixture, string?, Action{IServiceCollection}?)"/>
/// so a second instance shares the same Postgres database. The caller is responsible for
/// running migrations exactly once (via the first app's startup) before starting the second
/// instance; the shared-DB path does NOT re-migrate. See RESEARCH §Pitfall 3.
/// </para>
/// </remarks>
internal sealed class MatchmakingTestApp : IAsyncDisposable
{
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private readonly RSA _signingRsa;
    private readonly bool _withRankingsRatingSource;
    private readonly Action<MatchmakingLadderConfig>? _configureLadder;
    private readonly int? _lockTtlSeconds;
    private IHost? _host;
    private string _databaseSuffix = string.Empty;

    /// <summary>HTTP client bound to the in-memory test server.</summary>
    public HttpClient Client { get; private set; } = default!;

    /// <summary>Connection string for the fresh per-host database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>JWT issuer (the audience matches).</summary>
    public string Issuer { get; } = "gk-matchmaking-test";

    /// <summary>JWT audience.</summary>
    public string Audience { get; } = "gk-matchmaking-test";

    /// <summary>
    /// Per-host long-poll timeout (seconds). Tests may shorten this (e.g. to 2 s) for
    /// deterministic timeout assertions. Mutable before <see cref="StartAsync"/>.
    /// </summary>
    public int LongPollTimeoutSeconds { get; set; } = 30;

    /// <summary>The ladder id registered by <see cref="StartAsync"/> — exposed for tests.</summary>
    public Guid TestLadderId { get; private set; } = Guid.NewGuid();

    /// <summary>The ladder name registered by <see cref="StartAsync"/> — exposed for tests.</summary>
    public string TestLadderName { get; } = "default";

    /// <summary>
    /// The Redis key used by this host's matchmaker leader-election lock. Populated after
    /// <see cref="StartAsync"/> completes. Reads the lock key that the host's
    /// <see cref="GameKitMatchmakingTickerOptions.LockKey"/> resolves to after DI build —
    /// which is the default <c>gamekit:matchmaking:matcher:lock</c> unless the caller's
    /// <paramref name="serviceOverrides"/> or the options configuration changes it.
    /// Used by <c>GracefulDrainTests</c> to assert the lease was released after host stop.
    /// </summary>
    public string MatcherLockKey { get; private set; } = string.Empty;

    /// <summary>
    /// Constructs the host — generates an ephemeral RSA PEM keypair under the temp directory.
    /// </summary>
    /// <param name="withRankingsRatingSource">
    /// When <see langword="true"/>, registers <see cref="RankingsRatingSource"/> as
    /// <c>IPlayerRatingProvider</c> via <c>WithRatingsFrom&lt;RankingsRatingSource&gt;()</c>
    /// (MATCH-16 cross-package SC#3 proof). Default <see langword="false"/> — v1 zero-rating fallback.
    /// </param>
    /// <param name="configureLadder">
    /// Optional callback to further configure the test ladder (e.g. set
    /// <see cref="MatchmakingLadderConfig.AllowedRegions"/> for MATCH-18 regional pool tests).
    /// Invoked after the ladder name is set; the name is locked to <see cref="TestLadderName"/>
    /// regardless of any <c>Name</c> assignment inside the callback.
    /// </param>
    /// <param name="lockTtlSeconds">
    /// Optional override for <see cref="GameKitMatchmakingTickerOptions.LockTtlSeconds"/>.
    /// When set, the ticker uses this short TTL — required by the SCALE-04 split-brain test
    /// to make lease expiry observable within a deterministic test window (e.g. 2 s).
    /// When <see langword="null"/>, the production default (90 s) is used.
    /// </param>
    public MatchmakingTestApp(
        bool withRankingsRatingSource = false,
        Action<MatchmakingLadderConfig>? configureLadder = null,
        int? lockTtlSeconds = null)
    {
        _withRankingsRatingSource = withRankingsRatingSource;
        _configureLadder = configureLadder;
        _lockTtlSeconds = lockTtlSeconds;
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-mm-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        File.WriteAllText(_privPath, _signingRsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, _signingRsa.ExportRSAPublicKeyPem());
    }

    /// <summary>
    /// Builds and starts the host against a fresh per-host database. Delegates to the full
    /// <see cref="StartAsync(PostgresFixture, RedisFixture, string?, Action{IServiceCollection}?)"/>
    /// overload with <c>connectionString: null</c> and <c>serviceOverrides: null</c> — preserves
    /// the two-argument call shape used by all existing integration tests.
    /// </summary>
    public Task StartAsync(PostgresFixture pg, RedisFixture redis)
        => StartAsync(pg, redis, connectionString: null, serviceOverrides: null);

    /// <summary>
    /// Builds and starts the host, optionally sharing a pre-migrated database and injecting
    /// test-specific service overrides.
    /// </summary>
    /// <param name="pg">Postgres fixture providing Testcontainers admin + owner connection strings.</param>
    /// <param name="redis">Redis fixture providing the Testcontainers connection string.</param>
    /// <param name="connectionString">
    /// When non-<see langword="null"/>, the host connects to this existing database and does
    /// NOT create a fresh database or re-apply migrations (RESEARCH Pitfall 3 — migrate once).
    /// Pass <c>_appA.ConnectionString</c> when starting <c>_appB</c> in two-replica tests so
    /// both replicas share one <c>game_sessions</c> table. The shared-DB path still registers
    /// the <see cref="MatchmakingTestModelCustomizer"/> so all <c>DbSet&lt;T&gt;</c> paths resolve.
    /// When <see langword="null"/>, a fresh database is created and full migrations are applied.
    /// </param>
    /// <param name="serviceOverrides">
    /// Optional callback applied AFTER all standard GameKit services are registered and BEFORE
    /// the host is built. Use this to replace services (e.g. inject
    /// <c>DelayingChaosInterceptor</c> for the SCALE-04 split-brain test) without forking
    /// <see cref="MatchmakingTestApp"/>. Mirrors <c>LobbyTestApp.StartAsync</c>.
    /// </param>
    public async Task StartAsync(
        PostgresFixture pg,
        RedisFixture redis,
        string? connectionString = null,
        Action<IServiceCollection>? serviceOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(redis);

        if (connectionString is not null)
        {
            // Shared-DB path: use the supplied connection string directly.
            // The caller already applied migrations via the first app's StartAsync call.
            // Do NOT create a fresh database or re-apply migrations (RESEARCH Pitfall 3).
            ConnectionString = connectionString;
            // Seed a ladder in the shared DB only if not already present.
            TestLadderId = await IntegrationTestHelpers.SeedLadderAsync(ConnectionString, TestLadderName + "_b");
        }
        else
        {
            // Fresh-DB path: create a new database, apply all migrations, seed the ladder.
            ConnectionString = await IntegrationTestHelpers.CreateFreshDatabaseAsync(pg);
            await IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync(ConnectionString);
            // Seed a Rankings ladder row so /api/mm/queue can resolve a real LadderId at
            // enqueue time and the matchmaking_tickets FK is satisfied later by the drain.
            TestLadderId = await IntegrationTestHelpers.SeedLadderAsync(ConnectionString, TestLadderName);
        }

        // Capture the short TTL (if any) for use in the options callback below.
        var ttlSeconds = _lockTtlSeconds;

        // Build the host.
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
                    var rankings = b.AddRankings();
                    // MATCH-16 SC#3 cross-package proof: wire RankingsRatingSource so real
                    // Glicko-2 ratings flow into the Redis ticket hash at enqueue time.
                    if (_withRankingsRatingSource)
                        rankings.WithRatingsFrom<RankingsRatingSource>();
                    var mm = b.AddMatchmaking(o =>
                    {
                        o.LongPollTimeoutSeconds = LongPollTimeoutSeconds;
                        // SCALE-04: when a short TTL was requested, configure the ticker
                        // to use it so lease expiry is observable within the test window.
                        if (ttlSeconds.HasValue)
                            o.Ticker.LockTtlSeconds = ttlSeconds.Value;
                    });
                    mm.AddLadder(TestLadderName, _configureLadder);

                    // Replace the Redis connection so all matchmaking Redis ops hit the
                    // shared Testcontainer multiplexer.
                    var muxDescriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer));
                    if (muxDescriptor is not null) services.Remove(muxDescriptor);
                    services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
                        StackExchange.Redis.ConnectionMultiplexer.Connect(redis.ConnectionString));

                    // FOLLOW-UP-02-03-01 / Plan 05-01 MatchmakingTestModelCustomizer:
                    // the runtime DbContext model must see Matchmaking + Rankings entities at
                    // query time. Replace the scoped DbContext registration with one that
                    // applies the test customizer (re-binds the model with both packages'
                    // configurations applied so DbSet<Party>/<PartyMember>/<Ladder> succeed).
                    // Also applied for the shared-DB path so DbSet<GameSession> resolves.
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(ConnectionString)
                              .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>());

                    // Apply optional test-specific service overrides (e.g. inject
                    // DelayingChaosInterceptor for the SCALE-04 split-brain simulation).
                    // Mirrors LobbyTestApp.StartAsync serviceOverrides pattern.
                    serviceOverrides?.Invoke(services);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
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

        // Read the configured lock key back from the built host's options so MatcherLockKey
        // reflects whatever value the host actually resolves (default or serviceOverrides-changed).
        // Uses IOptions<GameKitMatchmakingOptions> rather than the constant so operator
        // overrides to Ticker.LockKey are captured correctly.
        var opts = _host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<GameKitMatchmakingOptions>>();
        MatcherLockKey = opts.Value.Ticker.LockKey;
    }

    /// <summary>
    /// Resolves <see cref="IMatchmakerTicker"/> from the test host's DI container.
    /// Used by integration tests that drive a single deterministic ticker tick
    /// (e.g. <c>RegionalPoolTests.SC2_TickerGlob_PicksUpBothRegionalAndDefaultKeys</c>).
    /// </summary>
    public IMatchmakerTicker GetTicker() =>
        _host!.Services.GetRequiredService<IMatchmakerTicker>();

    /// <summary>
    /// Mints a valid player JWT signed with the host's RSA private key. The
    /// <c>sub</c>/<c>NameIdentifier</c> claim is set to <paramref name="playerId"/>; the
    /// resulting token validates against the host's JwtBearer middleware without further
    /// configuration.
    /// </summary>
    /// <param name="playerId">Canonical player id to place in the <c>sub</c> claim.</param>
    /// <returns>The serialized JWT string.</returns>
    public string MintPlayerJwt(Guid playerId)
    {
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
                new Claim("provider", "test"),
            },
            notBefore: now.AddMinutes(-1),
            expires: now.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> with the bearer header pre-attached for the given
    /// player id. The player row is upserted into the <c>players</c> table so any FK from
    /// Matchmaking entities (Party.OwnerPlayerId, PartyMember.PlayerId) is satisfied.
    /// </summary>
    public HttpClient CreateClient(Guid playerId)
    {
        EnsurePlayerRow(playerId);
        var client = _host!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintPlayerJwt(playerId));
        return client;
    }

    /// <summary>Idempotent INSERT of a player row so Matchmaking-side FKs succeed.</summary>
    public void EnsurePlayerRow(Guid playerId)
    {
        using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.players
            (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES (@id, @name, NOW(), false)
            ON CONFLICT (""Id"") DO NOTHING";
        cmd.Parameters.AddWithValue("id", playerId);
        cmd.Parameters.AddWithValue("name", "P_" + playerId.ToString("N")[..8]);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            try { await _host.StopAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            _host.Dispose();
        }
        Client?.Dispose();
        _signingRsa.Dispose();
        if (Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
