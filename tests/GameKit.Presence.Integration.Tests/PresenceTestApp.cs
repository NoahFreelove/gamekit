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
using GameKit.Auth.Data;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Presence.Builder;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;

namespace GameKit.Presence.Integration.Tests;

/// <summary>
/// In-process ASP.NET Core test host with <c>AddGameKit().AddAuth().AddPresence()</c>
/// composed and the heartbeat endpoint mapped. Mirrors the construction shape of
/// <c>MatchmakingTestApp</c> (Plan 05-08 Task 5) but drops Rankings + Matchmaking —
/// the Presence integration tests do not need the full ladder/queue surface.
/// </summary>
/// <remarks>
/// <para>
/// Plan 06-04 Task 3 — the host swaps the test-side <see cref="IConnectionMultiplexer"/>
/// to the shared Testcontainers Redis so the heartbeat path writes to a real Redis
/// instance the test then probes directly to assert TTL + in-match precedence.
/// </para>
/// <para>
/// JWT issuance: <see cref="MintPlayerJwt"/> signs with the same ephemeral RSA keypair
/// the host's JwtBearer middleware validates against. Tests bypass the guest-login flow
/// so they can isolate the Presence endpoint surface.
/// </para>
/// </remarks>
internal sealed class PresenceTestApp : IAsyncDisposable
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

    /// <summary>JWT issuer (the audience matches).</summary>
    public string Issuer { get; } = "gk-presence-test";

    /// <summary>JWT audience.</summary>
    public string Audience { get; } = "gk-presence-test";

    /// <summary>Redis connection string supplied to the host.</summary>
    public string RedisConnectionString { get; private set; } = string.Empty;

    /// <summary>The connection multiplexer registered in the host's DI — exposed for direct probing.</summary>
    public IConnectionMultiplexer Multiplexer { get; private set; } = default!;

    /// <summary>Constructs the host — generates an ephemeral RSA PEM keypair under the temp directory.</summary>
    public PresenceTestApp()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-presence-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        File.WriteAllText(_privPath, _signingRsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, _signingRsa.ExportRSAPublicKeyPem());
    }

    /// <summary>Builds and starts the host against the shared Postgres + Redis fixtures.</summary>
    public async Task StartAsync(PostgresFixture pg, RedisFixture redis)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(redis);

        RedisConnectionString = redis.ConnectionString;
        ConnectionString = await CreateFreshDatabaseAsync(pg);
        await ApplyAuthMigrationsAsync(ConnectionString);

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
                    b.AddPresence();

                    // Replace the Redis multiplexer registration so the Presence provider hits
                    // the shared Testcontainer instance the tests probe directly. AddPresence
                    // intentionally does NOT register a multiplexer (consumers own the lifecycle —
                    // PATTERNS Block 5 commentary).
                    var muxDescriptor = services.FirstOrDefault(
                        d => d.ServiceType == typeof(IConnectionMultiplexer));
                    if (muxDescriptor is not null) services.Remove(muxDescriptor);
                    services.AddSingleton<IConnectionMultiplexer>(Multiplexer);
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
                        e.MapPresence();
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

    /// <summary>Returns an authenticated HttpClient with a freshly-minted bearer token.</summary>
    public HttpClient CreateClient(Guid playerId)
    {
        var client = _host!.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintPlayerJwt(playerId));
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
        Client?.Dispose();
        _signingRsa.Dispose();
        if (Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_presence_" + Guid.NewGuid().ToString("N")[..12];

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

    private static async Task ApplyAuthMigrationsAsync(string cs)
    {
        // Core migrations first.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = cs;
            o.AutoMigrate = false;
        });
        await using (var coreSp = coreServices.BuildServiceProvider())
        {
            await using var scope = coreSp.CreateAsyncScope();
            await MigrationRunner.MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        // Auth migrations layered on top.
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
        await using var authCtx = new GameKitDbContext(authOpts);
        await authCtx.Database.MigrateAsync().ConfigureAwait(false);
    }
}
