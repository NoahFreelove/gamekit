// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// In-process ASP.NET Core test host with <c>AddGameKit().AddAuth()</c> composed and
/// <c>/auth/*</c> mapped. Uses the Postgres + WireMock fixtures from the <c>Auth</c> collection.
/// Exposes an <see cref="HttpClient"/> bound to the in-memory test server.
/// </summary>
/// <remarks>
/// Mirrors the RefreshTokenServiceTests wiring: replaces the DI-registered DbContext with one
/// that uses the FOLLOW-UP-02-03-01 runtime-query customizer (so Auth entity queries succeed
/// at runtime) and swaps the Singleton IClock for a mock whose UtcNow reads <see cref="Now"/>
/// so tests can advance the refresh-grace clock deterministically.
/// </remarks>
public sealed class AuthTestHost : IAsyncDisposable
{
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private IHost? _host;

    /// <summary>The HTTP client bound to the test server (kept alive across requests).</summary>
    public HttpClient Client { get; private set; } = default!;

    /// <summary>
    /// Mutable "now" read by the injected <see cref="IClock"/> mock — move forward to simulate time.
    /// Initialized to real UtcNow so JWTs issued by <c>JwtIssuer</c> (mock clock) line up with
    /// the JwtBearer handler's lifetime validation (real clock). Without this initialization
    /// JwtBearer rejects tokens as <c>token_expired</c> because our mock clock was frozen at 2026.
    /// </summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Constructs the host; generates an ephemeral RSA PEM keypair under the temp directory.</summary>
    public AuthTestHost()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-auth-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(_privPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, rsa.ExportRSAPublicKeyPem());
    }

    /// <summary>Builds and starts the host against the supplied fixtures + default stubs.</summary>
    /// <param name="pg">Postgres fixture (provides owner + app connection strings).</param>
    /// <param name="wm">WireMock fixture (supplies Steam OpenID + Discord stub URLs).</param>
    public async Task StartAsync(PostgresFixture pg, WireMockFixture wm)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(wm);

        // Apply Core + Auth migrations out-of-band so the host can run with AutoMigrate=false.
        await MigrateAsync(pg.OwnerConnectionString).ConfigureAwait(false);

        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var b = services.AddGameKit(o =>
                    {
                        o.ConnectionString = pg.OwnerConnectionString;
                        o.AutoMigrate = false;
                    });
                    b.AddAuth(o =>
                    {
                        o.Jwt.Issuer = "gk-test";
                        o.Jwt.Audience = "gk-test";
                        o.Jwt.PrivateKeyPemPath = _privPath;
                        o.Jwt.PublicKeyPemPath = _pubPath;
                        o.Jwt.Kid = "test-kid-1";
                        o.Jwt.RefreshReuseInterval = TimeSpan.FromSeconds(45);
                        o.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(30);

                        o.Steam.OpenIdEndpoint = wm.SteamOpenIdLoginUrl;
                        o.Steam.Realm = "https://gamekit-test.example.com/";
                        o.Steam.CallbackPath = "/auth/callback/steam";

                        // Discord is reachable but unused in E2E assertions (plan 02-05 ships
                        // the scheme; the /auth/callback/discord flow proof lives in
                        // DiscordProviderTests). Supplying ClientId/Secret keeps AddDiscord()
                        // from throwing at registration time if an endpoint reaches for it.
                        o.Discord.ClientId = "mock-client-id";
                        o.Discord.ClientSecret = "mock-client-secret";
                        o.Discord.TokenEndpoint = wm.DiscordTokenUrl;
                        o.Discord.UserInfoEndpoint = wm.DiscordUserInfoUrl;

                        // WireMock base host must be allow-listed for the egress handler.
                        var wmHost = new Uri(wm.BaseUrl).Host;
                        if (!o.AllowedProviderHosts.Contains(wmHost))
                        {
                            o.AllowedProviderHosts.Add(wmHost);
                        }
                    });

                    // FOLLOW-UP-02-03-01 workaround: rewire the DbContext to use the runtime-query
                    // customizer so the scoped context can see PlayerIdentity / PlayerCredential /
                    // RefreshToken at query time.
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(pg.OwnerConnectionString)
                              .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>());

                    // Mock clock so the RefreshTokenService grace-window logic runs on a test-controlled
                    // time line. The ServiceDescriptor.Replace call ensures we do not duplicate the
                    // AddGameKit-registered SystemClock.
                    var clockMock = new Mock<IClock>();
                    clockMock.SetupGet(c => c.UtcNow).Returns(() => Now);
                    services.Replace(ServiceDescriptor.Singleton<IClock>(clockMock.Object));
                });
                web.Configure(app =>
                {
                    // Strict ordering from RESEARCH §8.12 #6: UseRouting → UseRateLimiter →
                    // UseGameKitAuth (UseAuthentication) → UseGameKit (UseAuthorization) → Map*.
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseGameKitAuth();
                    app.UseGameKit();
                    app.UseEndpoints(e =>
                    {
                        e.MapAuth();
                        e.MapGameKit();
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        Client = _host.GetTestClient();
    }

    /// <summary>
    /// Applies Core then Auth migrations against the target database. Each test gets a fresh
    /// host but the Postgres container is shared — UUIDv7 keys keep rows isolated in practice.
    /// </summary>
    private static async Task MigrateAsync(string ownerConnectionString)
    {
        // Core migration step: use a plain Core-only service provider (no Auth extension) so the
        // runtime model matches the Core snapshot exactly. Registering AuthModelBuilderExtension
        // here would add Auth entities to the model while the Core snapshot has none, triggering
        // PendingModelChangesWarning (EF Core 10). AuthMigrationModelCustomizer applies Auth
        // configs directly without DI resolution, so the Auth extension is not needed in coreSp.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = ownerConnectionString;
            o.AutoMigrate = false;
        });
        await using var coreSp = coreServices.BuildServiceProvider();
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner
                .MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>())
                .ConfigureAwait(false);
        }

        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(ownerConnectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .UseApplicationServiceProvider(coreSp)
            .Options;
        await using var authCtx = new GameKitDbContext(authOpts);
        await authCtx.Database.MigrateAsync().ConfigureAwait(false);
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
        if (Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
