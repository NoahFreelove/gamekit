// SPDX-License-Identifier: Apache-2.0
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
using GameKit.Lobby.Builder;
using GameKit.Matchmaking.Builder;
using GameKit.Rankings.Builder;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// In-process ASP.NET Core test host with the full Lobby pipeline
/// (<c>AddGameKit().AddAuth().AddRankings().AddMatchmaking().AddLobby()</c>) and the
/// complete endpoint surface mapped. Provides:
/// <list type="bullet">
///   <item><see cref="MintPlayerJwt(Guid)"/> — JWT signed with the host's ephemeral RSA
///         keypair, accepted by the host's JwtBearer middleware (SC#2 auth tests).</item>
///   <item><see cref="ConnectLobbyHubAsync(Guid)"/> — builds a <see cref="HubConnection"/>
///         routed through <c>Server.CreateHandler()</c> so WebSocket traffic stays in-process
///         (SC#2/SC#3/SC#4/SC#5 hub tests).</item>
///   <item><see cref="EnsurePlayerRow(Guid)"/> — idempotent player row upsert for FK
///         satisfaction.</item>
///   <item>Shared-Redis backplane: <see cref="StartAsync"/> replaces
///         <c>IConnectionMultiplexer</c> with a multiplexer to the shared Testcontainers
///         Redis connection string — two <see cref="LobbyTestApp"/> instances pointing to the
///         same <see cref="RedisFixture"/> share the Redis backplane (SC#5).</item>
/// </list>
/// </summary>
/// <remarks>
/// Mirrors <c>tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs</c>.
/// The pipeline adds <c>app.UseWebSockets()</c> BEFORE <c>app.UseRouting()</c> per
/// RESEARCH Pitfall 7 — TestServer requires explicit WebSocket middleware to honour the
/// WebSocket transport path in <see cref="HubConnectionBuilder"/>.
/// </remarks>
internal sealed class LobbyTestApp : IAsyncDisposable
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

    /// <summary>JWT issuer (audience matches).</summary>
    public string Issuer { get; } = "gk-lobby-test";

    /// <summary>JWT audience.</summary>
    public string Audience { get; } = "gk-lobby-test";

    /// <summary>The in-process test server — exposes <c>CreateHandler()</c> for <see cref="HubConnectionBuilder"/>.</summary>
    public TestServer Server => _host!.GetTestServer();

    /// <summary>The ladder id seeded at startup — exposed for tests that need to reference it.</summary>
    public Guid TestLadderId { get; private set; } = Guid.NewGuid();

    /// <summary>The default ladder name seeded at startup.</summary>
    public string TestLadderName { get; } = "default";

    /// <summary>
    /// Constructs the test app — generates an ephemeral RSA PEM keypair under a temp directory.
    /// </summary>
    public LobbyTestApp()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-lobby-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        File.WriteAllText(_privPath, _signingRsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, _signingRsa.ExportRSAPublicKeyPem());
    }

    /// <summary>
    /// Builds and starts the host against a fresh per-host database. Replaces the registered
    /// <c>IConnectionMultiplexer</c> with one connected to <paramref name="redis"/>'s
    /// connection string — when two <see cref="LobbyTestApp"/> instances share the same
    /// <see cref="RedisFixture"/> they share the Redis backplane (SC#5).
    /// </summary>
    /// <param name="pg">Postgres fixture providing the Testcontainers connection string.</param>
    /// <param name="redis">Redis fixture providing the Testcontainers connection string.</param>
    /// <param name="serviceOverrides">
    /// Optional callback applied AFTER all standard GameKit services are registered.
    /// Use this to replace services (e.g. inject a broken <c>IPartyService</c> stub for
    /// failure-path testing) without forking <see cref="LobbyTestApp"/>.
    /// </param>
    public Task StartAsync(PostgresFixture pg, RedisFixture redis,
        Action<IServiceCollection>? serviceOverrides = null)
        => StartCoreAsync(pg, redis, serviceOverrides);

    private async Task StartCoreAsync(PostgresFixture pg, RedisFixture redis,
        Action<IServiceCollection>? serviceOverrides)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(redis);

        ConnectionString = await IntegrationTestHelpers.CreateFreshDatabaseAsync(pg);
        await IntegrationTestHelpers.ApplyLobbyMigrationsAsync(ConnectionString);

        // Seed a Rankings ladder row so /api/mm/queue + lobby.LadderId FK is satisfied.
        TestLadderId = await SeedLadderAsync(ConnectionString, TestLadderName);

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
                    b.AddRankings();
                    var mm = b.AddMatchmaking();
                    mm.AddLadder(TestLadderName);
                    b.AddLobby();

                    // Replace the Redis connection so all SignalR backplane + matchmaking
                    // Redis ops hit the shared Testcontainer multiplexer.
                    // When two LobbyTestApp instances share the same RedisFixture they share
                    // the Redis backplane — this is the SC#5 two-TestServer mechanism.
                    var muxDescriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(StackExchange.Redis.IConnectionMultiplexer));
                    if (muxDescriptor is not null) services.Remove(muxDescriptor);
                    services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
                        StackExchange.Redis.ConnectionMultiplexer.Connect(redis.ConnectionString));

                    // FOLLOW-UP-02-03-01: the runtime DbContext model must see Lobby + Matchmaking
                    // + Rankings entities at query time.
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(ConnectionString)
                              .ReplaceService<IModelCustomizer, LobbyTestModelCustomizer>());

                    // Apply optional test-specific service overrides (e.g. broken IPartyService
                    // for CR-02 failure-path tests).
                    serviceOverrides?.Invoke(services);
                });
                web.Configure(app =>
                {
                    // UseWebSockets MUST come before UseRouting for TestServer WebSocket transport
                    // to function correctly (RESEARCH Pitfall 7 / SC#2/SC#5 hub tests).
                    app.UseWebSockets();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseGameKitAuth();
                    app.UseGameKit();
                    app.UseEndpoints(e =>
                    {
                        e.MapAuth();
                        e.MapGameKit();
                        e.MapMatchmaking();
                        e.MapLobby();
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        Client = _host.GetTestClient();
    }

    /// <summary>
    /// Mints a valid player JWT signed with the host's RSA private key. The <c>sub</c> /
    /// <c>NameIdentifier</c> claim is set to <paramref name="playerId"/>; the token validates
    /// against the host's JwtBearer middleware without further configuration.
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
    /// player id. Upserts the player row so FK constraints are satisfied.
    /// </summary>
    /// <param name="playerId">Canonical player id.</param>
    public HttpClient CreateClient(Guid playerId)
    {
        EnsurePlayerRow(playerId);
        var client = _host!.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintPlayerJwt(playerId));
        return client;
    }

    /// <summary>
    /// Builds a <see cref="HubConnection"/> to <c>/hubs/lobby</c> routed through the in-process
    /// test server. The JWT for <paramref name="playerId"/> is supplied via
    /// <c>AccessTokenProvider</c> so the JwtBearer <c>OnMessageReceived</c> hook picks it up
    /// from the query string (SC#2 / T-11-03-01).
    /// </summary>
    /// <param name="playerId">Canonical player id whose JWT is placed in <c>access_token</c>.</param>
    /// <returns>
    /// A configured but NOT yet started <see cref="HubConnection"/>. Call
    /// <see cref="HubConnection.StartAsync()"/> on the returned connection.
    /// </returns>
    public HubConnection ConnectLobbyHubAsync(Guid playerId)
    {
        var jwt = MintPlayerJwt(playerId);
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/lobby", o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(jwt);
            })
            .Build();
    }

    /// <summary>Idempotent INSERT of a player row so Lobby / Matchmaking-side FKs succeed.</summary>
    /// <param name="playerId">Player id to upsert.</param>
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

    /// <summary>
    /// Seeds a lobby row with all members in <c>ReadyChecking</c> state in the test database.
    /// Returns the seeded lobby id.
    /// </summary>
    /// <param name="memberPlayerIds">All member player ids (first element is the owner).</param>
    /// <param name="ladderId">The ladder id to assign to the lobby.</param>
    /// <returns>The seeded lobby id.</returns>
    public async Task<Guid> SeedLobbyAsync(IReadOnlyList<Guid> memberPlayerIds, Guid ladderId)
    {
        ArgumentNullException.ThrowIfNull(memberPlayerIds);
        if (memberPlayerIds.Count == 0)
            throw new ArgumentException("At least one member required.", nameof(memberPlayerIds));

        var lobbyId = Guid.NewGuid();
        var ownerId = memberPlayerIds[0];
        var now = DateTimeOffset.UtcNow;

        await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Insert the lobby in ReadyChecking state.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"INSERT INTO gamekit.lobbies
                (""Id"", ""OwnerId"", ""LadderId"", ""State"", ""MaxMembers"", ""CreatedAt"", ""UpdatedAt"")
                VALUES (@id, @ownerId, @ladderId, 1, 8, @now, @now)";
            cmd.Parameters.AddWithValue("id", lobbyId);
            cmd.Parameters.AddWithValue("ownerId", ownerId);
            cmd.Parameters.AddWithValue("ladderId", ladderId);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert all members.
        foreach (var playerId in memberPlayerIds)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO gamekit.lobby_members
                (""Id"", ""LobbyId"", ""PlayerId"", ""Ready"", ""JoinedAt"")
                VALUES (@id, @lobbyId, @playerId, false, @now)";
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("lobbyId", lobbyId);
            cmd.Parameters.AddWithValue("playerId", playerId);
            cmd.Parameters.AddWithValue("now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        return lobbyId;
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

    // ---- static helpers ----

    private static async Task<Guid> SeedLadderAsync(string cs, string name)
    {
        var ladderId = Guid.NewGuid();

        await using var conn = new Npgsql.NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.ladders
            (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"", ""Config"")
            VALUES (@id, @name, 'Glicko2', true, NOW(), '{}'::jsonb)";
        cmd.Parameters.AddWithValue("id", ladderId);
        cmd.Parameters.AddWithValue("name", name);
        await cmd.ExecuteNonQueryAsync();
        return ladderId;
    }
}
