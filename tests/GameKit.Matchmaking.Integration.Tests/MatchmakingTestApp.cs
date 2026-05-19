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
using GameKit.Rankings.Builder;
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
/// </remarks>
internal sealed class MatchmakingTestApp : IAsyncDisposable
{
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private readonly RSA _signingRsa;
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

    /// <summary>Constructs the host — generates an ephemeral RSA PEM keypair under the temp directory.</summary>
    public MatchmakingTestApp()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-mm-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        File.WriteAllText(_privPath, _signingRsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, _signingRsa.ExportRSAPublicKeyPem());
    }

    /// <summary>Builds and starts the host against a fresh per-host database.</summary>
    public async Task StartAsync(PostgresFixture pg, RedisFixture redis)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(redis);

        ConnectionString = await IntegrationTestHelpers.CreateFreshDatabaseAsync(pg);
        await IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync(ConnectionString);

        // Seed a Rankings ladder row so /api/mm/queue can resolve a real LadderId at enqueue
        // time and the matchmaking_tickets FK is satisfied later by the drain.
        TestLadderId = await IntegrationTestHelpers.SeedLadderAsync(ConnectionString, TestLadderName);

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
                    b.AddRankings();
                    var mm = b.AddMatchmaking(o =>
                    {
                        o.LongPollTimeoutSeconds = LongPollTimeoutSeconds;
                    });
                    mm.AddLadder(TestLadderName);

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
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(ConnectionString)
                              .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>());
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
    }

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
