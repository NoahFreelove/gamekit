// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Admin.UI;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Builder;
using GameKit.Admin.UI.Data;
using GameKit.Admin.UI.Entities;
using GameKit.Auth;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Auth.Services;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Microsoft.AspNetCore.SignalR.Client;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// In-process ASP.NET Core test host for <c>GameKit.Admin.UI</c> integration tests. Applies
/// Core + Auth + Admin migrations out-of-band, then boots a <see cref="TestServer"/> whose
/// pipeline composes <c>AddGameKit → AddAuth → AddGameKitAdmin</c> with the
/// <c>SuperadminGateHostedService</c> gate enabled.
/// </summary>
/// <remarks>
/// <para>
/// Three migration passes run in order (Core → Auth → Admin). <see cref="SuperadminGateHostedService"/>
/// queries <c>admin_users</c> during host start, so any <c>seed</c> callback must execute BEFORE
/// <see cref="IHost.StartAsync"/>. <see cref="StartAsync(PostgresFixture, RedisFixture, string, System.Func{AdminTestHost, Task}?)"/>
/// handles this ordering internally: migrations first → seed (via a separate service provider) →
/// host start (which triggers the gate).
/// </para>
/// </remarks>
public sealed class AdminTestHost : IAsyncDisposable
{
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private IHost? _host;
    private string? _connectionString;
    private readonly ConcurrentQueue<string> _logMessages = new();

    /// <summary>The HTTP client bound to the in-memory test server.</summary>
    public HttpClient Client { get; private set; } = default!;

    /// <summary>The in-process TestServer — exposes <c>CreateHandler()</c> for <see cref="HubConnectionBuilder"/>.</summary>
    public TestServer Server => _host!.GetTestServer();

    /// <summary>The MountPath configured for this host (default <c>/admin</c>).</summary>
    public string MountPath { get; private set; } = "/admin";

    /// <summary>All log messages captured by the in-memory log provider (for warning-assertion tests).</summary>
    public ConcurrentQueue<string> LogMessages => _logMessages;

    /// <summary>Constructs the host; generates an ephemeral RSA PEM keypair under the temp directory.</summary>
    public AdminTestHost()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-admin-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(_privPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, rsa.ExportRSAPublicKeyPem());
    }

    /// <summary>
    /// Convenience factory: builds the host, applies migrations, optionally seeds admin rows,
    /// and starts the in-memory server. If the superadmin gate fires in Production without a
    /// seeded superadmin, <see cref="IHost.StartAsync"/> throws and the exception propagates.
    /// </summary>
    /// <param name="pg">Postgres fixture.</param>
    /// <param name="redis">Redis fixture.</param>
    /// <param name="env">Hosting environment name (<c>Production</c> / <c>Development</c> / <c>Staging</c>).</param>
    /// <param name="seed">Optional async seed callback executed AFTER migrations but BEFORE host start.</param>
    /// <param name="configureAdmin">Optional <see cref="GameKitAdminOptions"/> override (e.g. custom <c>MountPath</c>).</param>
    /// <param name="configureExtraServices">
    /// Optional hook to register additional services into the web host's
    /// <see cref="IServiceCollection"/> AFTER the standard
    /// <c>AddGameKit</c> / <c>AddAuth</c> / <c>AddGameKitAdmin</c> chain runs but BEFORE the host
    /// starts. Plan 06-07 Task 2 uses this to register a mock
    /// <see cref="GameKit.Core.Services.IPresenceProvider"/> so the PresencePanel renders the
    /// happy path (table-with-rows) — the production registration would normally come from
    /// <c>GameKit.Presence.AddPresence()</c>, which is intentionally NOT called in these tests.
    /// </param>
    public static async Task<AdminTestHost> StartAsync(
        PostgresFixture pg,
        RedisFixture redis,
        string env = "Production",
        Func<AdminTestHost, Task>? seed = null,
        Action<GameKitAdminOptions>? configureAdmin = null,
        Action<IServiceCollection>? configureExtraServices = null)
    {
        var host = new AdminTestHost();
        await host.InitializeAsync(pg, redis, env, seed, configureAdmin, configureExtraServices).ConfigureAwait(false);
        return host;
    }

    /// <summary>
    /// Single-instance (no-Redis) variant: builds and starts a host with NO
    /// <see cref="IConnectionMultiplexer"/> registered, exercising the CR-01 regression path.
    /// <see cref="AddGameKitAdmin"/> sees no <c>IConnectionMultiplexer</c> in the service
    /// collection and skips <c>AddStackExchangeRedis</c>, leaving SignalR's default in-process
    /// backplane intact.
    /// </summary>
    /// <param name="pg">Postgres fixture (still required — admin schema lives in Postgres).</param>
    /// <param name="env">Hosting environment name.</param>
    /// <param name="seed">Optional async seed callback executed AFTER migrations but BEFORE host start.</param>
    /// <param name="configureAdmin">Optional <see cref="GameKitAdminOptions"/> override.</param>
    public static async Task<AdminTestHost> StartNoRedisAsync(
        PostgresFixture pg,
        string env = "Development",
        Func<AdminTestHost, Task>? seed = null,
        Action<GameKitAdminOptions>? configureAdmin = null)
    {
        var host = new AdminTestHost();
        await host.InitializeNoRedisAsync(pg, env, seed, configureAdmin).ConfigureAwait(false);
        return host;
    }

    private async Task InitializeNoRedisAsync(
        PostgresFixture pg,
        string env,
        Func<AdminTestHost, Task>? seed,
        Action<GameKitAdminOptions>? configureAdmin = null)
    {
        ArgumentNullException.ThrowIfNull(pg);
        _connectionString = pg.OwnerConnectionString;

        await MigrateAsync(_connectionString).ConfigureAwait(false);

        if (seed is not null)
            await seed(this).ConfigureAwait(false);

        _host = await Host.CreateDefaultBuilder()
            .UseEnvironment(env)
            .ConfigureAppConfiguration((_, cfg) =>
            {
                foreach (var src in cfg.Sources.OfType<JsonConfigurationSource>().ToList())
                    cfg.Sources.Remove(src);
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    // No IConnectionMultiplexer registration — single-instance no-Redis path.
                    // AddGameKitAdmin must detect the absence and skip AddStackExchangeRedis (CR-01).
                    var b = services.AddGameKit(o =>
                    {
                        o.ConnectionString = _connectionString!;
                        // RedisConnectionString intentionally omitted.
                        o.AutoMigrate = false;
                    });
                    b.AddAuth(o =>
                    {
                        o.Jwt.Issuer = "gk-test";
                        o.Jwt.Audience = "gk-test";
                        o.Jwt.PrivateKeyPemPath = _privPath;
                        o.Jwt.PublicKeyPemPath = _pubPath;
                        o.Jwt.Kid = "test-kid-1";
                    });
                    b.AddGameKitAdmin(o =>
                    {
                        configureAdmin?.Invoke(o);
                        MountPath = o.MountPath;
                    });

                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(_connectionString!, npg =>
                        {
                            npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                            npg.MigrationsHistoryTable(
                                GameKit.Core.Data.GameKitMigrationConstants.MigrationsHistoryTable,
                                GameKit.Core.Data.GameKitMigrationConstants.SchemaName);
                        }).ReplaceService<IModelCustomizer, AdminRuntimeQueryCustomizer>());

                    services.AddLogging(log =>
                    {
                        log.ClearProviders();
                        log.SetMinimumLevel(LogLevel.Debug);
                        log.AddProvider(new TestLoggerProvider(_logMessages));
                    });
                });
                web.Configure(app =>
                {
                    app.UseWebSockets();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseGameKitAuth();
                    app.UseGameKit();
                    app.UseGameKitAdmin();
                    app.UseEndpoints(e =>
                    {
                        e.MapAuth();
                        e.MapGameKit();
                        e.MapGameKitAdmin();
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        Client = _host.GetTestServer().CreateClient();
    }

    private async Task InitializeAsync(
        PostgresFixture pg,
        RedisFixture redis,
        string env,
        Func<AdminTestHost, Task>? seed,
        Action<GameKitAdminOptions>? configureAdmin = null,
        Action<IServiceCollection>? configureExtraServices = null)
    {
        ArgumentNullException.ThrowIfNull(pg);
        ArgumentNullException.ThrowIfNull(redis);
        _connectionString = pg.OwnerConnectionString;

        // Apply all three migrations out-of-band so the host can start with AutoMigrate=false.
        await MigrateAsync(_connectionString).ConfigureAwait(false);

        // Run caller-supplied seed (e.g. inserting a bootstrap superadmin) BEFORE the host starts,
        // so SuperadminGateHostedService sees the seeded row.
        if (seed is not null)
            await seed(this).ConfigureAwait(false);

        _host = await Host.CreateDefaultBuilder()
            .UseEnvironment(env)
            .ConfigureAppConfiguration((_, cfg) =>
            {
                // Remove all JsonConfigurationSource entries that Host.CreateDefaultBuilder adds
                // (appsettings.json, appsettings.{Env}.json — both with reloadOnChange:true).
                // Each source creates a FileSystemWatcher, which consumes an inotify instance.
                // The Linux default max_user_instances is 128; the full 60-test Admin suite spins
                // up enough hosts to exhaust that limit, causing later StartAsync calls to throw
                // IOException instead of the expected InvalidOperationException or success.
                // Tests configure everything programmatically so appsettings.json is not needed.
                foreach (var src in cfg.Sources.OfType<JsonConfigurationSource>().ToList())
                    cfg.Sources.Remove(src);
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var b = services.AddGameKit(o =>
                    {
                        o.ConnectionString = _connectionString!;
                        o.RedisConnectionString = redis.ConnectionString;
                        o.AutoMigrate = false;
                    });
                    b.AddAuth(o =>
                    {
                        o.Jwt.Issuer = "gk-test";
                        o.Jwt.Audience = "gk-test";
                        o.Jwt.PrivateKeyPemPath = _privPath;
                        o.Jwt.PublicKeyPemPath = _pubPath;
                        o.Jwt.Kid = "test-kid-1";
                    });
                    b.AddGameKitAdmin(o =>
                    {
                        configureAdmin?.Invoke(o);
                        MountPath = o.MountPath;
                    });

                    // Test-host DbContext override: the runtime FOLLOW-UP-02-03-01 path relies on
                    // CoreOptionsExtension.ApplicationServiceProvider to resolve IModelBuilderExtension
                    // at OnModelCreating time. Under the Host.CreateDefaultBuilder +
                    // ConfigureWebHostDefaults pattern, the factory lambda is invoked twice (once
                    // with the generic-host scope, which doesn't yet see Auth+Admin registrations;
                    // once with the web-host scope, which does) — EF caches the model from the first
                    // invocation, skipping the second. Result: AdminUser never lands in the runtime
                    // model. Bypass via ReplaceService<IModelCustomizer, AdminRuntimeQueryCustomizer>
                    // which applies Core + Auth + Admin entity configurations directly on the model.
                    // Matches the AuthTestHost pattern.
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(_connectionString!, npg =>
                        {
                            npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                            npg.MigrationsHistoryTable(
                                GameKit.Core.Data.GameKitMigrationConstants.MigrationsHistoryTable,
                                GameKit.Core.Data.GameKitMigrationConstants.SchemaName);
                        }).ReplaceService<IModelCustomizer, AdminRuntimeQueryCustomizer>());

                    // Redis multiplexer (HealthProbeService resolves it).
                    services.AddSingleton<IConnectionMultiplexer>(
                        ConnectionMultiplexer.Connect(redis.ConnectionString));

                    // Register the GameKit health checks (postgres + conditional redis) exactly as
                    // the real apps do (samples/*/Program.cs call AddGameKitHealthChecks). Without
                    // this, HealthProbeService finds no "postgres"/"redis" entries and every tile
                    // reports "Down"/"not configured" — so HealthProbeTests can never pass. Must run
                    // AFTER the IConnectionMultiplexer registration above so the conditional redis
                    // check is included (D-09 conditional-on-multiplexer contract; call-order Pitfall 1).
                    b.AddGameKitHealthChecks();

                    // Capture log messages so tests can assert warnings (Development path of SuperadminGate).
                    services.AddLogging(log =>
                    {
                        log.ClearProviders();
                        log.SetMinimumLevel(LogLevel.Debug);
                        log.AddProvider(new TestLoggerProvider(_logMessages));
                    });

                    // Plan 06-07: allow the test to inject additional services (e.g. a mocked
                    // IPresenceProvider) AFTER the standard GameKit registration chain so the
                    // PresencePanel happy-path test can render the populated table without booting
                    // GameKit.Presence + Redis-with-seeded-keys.
                    configureExtraServices?.Invoke(services);
                });
                web.Configure(app =>
                {
                    // Admin-UI friendly pipeline: Routing → RateLimiter → UseGameKitAuth → UseGameKit →
                    // UseGameKitAdmin → Map* (per plan 03-06 SP-6).
                    // UseWebSockets MUST come before UseRouting for TestServer WebSocket transport to
                    // function correctly (RESEARCH Pitfall 7 / SC#2 AdminEventHub hub tests).
                    app.UseWebSockets();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseGameKitAuth();
                    app.UseGameKit();
                    app.UseGameKitAdmin();
                    app.UseEndpoints(e =>
                    {
                        e.MapAuth();
                        e.MapGameKit();
                        e.MapGameKitAdmin();
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        // TestServer's HttpClient follows redirects by default; disable so assertions see the
        // raw 302/404 from AdminCookieEvents.
        Client = _host.GetTestServer().CreateClient();
    }

    /// <summary>Seeds an admin row directly via EF. Callable from a <c>seed</c> callback passed to <see cref="StartAsync"/>.</summary>
    /// <param name="username">Username.</param>
    /// <param name="password">Plaintext password (hashed server-side via <see cref="BCryptPasswordHasher"/>).</param>
    /// <param name="role"><see cref="AdminRoles.Admin"/> or <see cref="AdminRoles.Superadmin"/>.</param>
    public async Task SeedAdminAsync(string username, string password, string role)
    {
        if (_connectionString is null)
            throw new InvalidOperationException("AdminTestHost.SeedAdminAsync: connection string not initialized.");

        // Build an AuthOptions shell whose only job is to supply a work-factor to the hasher.
        var authOpts = new GameKitAuthOptions();
        var hasher = new BCryptPasswordHasher(authOpts);

        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(_connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AdminMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>()
            .Options;

        await using var ctx = new GameKitDbContext(opts);
        ctx.Set<AdminUser>().Add(new AdminUser
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            PasswordHash = hasher.Hash(password),
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Applies Core → Auth → Admin migrations in order. Each pass uses a dedicated context with
    /// the package's <c>MigrationsAssembly</c> + <c>MigrationsHistoryTable</c> + customizer.
    /// </summary>
    private static async Task MigrateAsync(string ownerConnectionString)
    {
        // Pass 1 — Core.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = ownerConnectionString;
            o.AutoMigrate = false;
        });
        await using (var coreSp = coreServices.BuildServiceProvider())
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner
                .MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>())
                .ConfigureAwait(false);
        }

        // Pass 2 — Auth.
        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(ownerConnectionString, npg =>
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
            await MigrationRunner
                .MigrateWithLockAsync(authCtx, AuthMigrationConstants.AdvisoryLockKey)
                .ConfigureAwait(false);
        }

        // Pass 3 — Admin.
        var adminOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(ownerConnectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AdminMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>()
            .Options;
        await using (var adminCtx = new GameKitDbContext(adminOpts))
        {
            await MigrationRunner
                .MigrateWithLockAsync(adminCtx, AdminMigrationConstants.AdvisoryLockKey)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns a fresh scoped <see cref="GameKitDbContext"/> from the host service provider.
    /// Caller owns disposal.
    /// </summary>
    public (IServiceScope Scope, GameKitDbContext Ctx) CreateDbScope()
    {
        if (_host is null) throw new InvalidOperationException("Host not started.");
        var scope = _host.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
    }

    /// <summary>
    /// Resolves a service from the host service provider inside a fresh scope. Returns both the
    /// scope (so the caller can dispose it) and the resolved service.
    /// </summary>
    public (IServiceScope Scope, T Service) Resolve<T>() where T : notnull
    {
        if (_host is null) throw new InvalidOperationException("Host not started.");
        var scope = _host.Services.CreateScope();
        return (scope, scope.ServiceProvider.GetRequiredService<T>());
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

    /// <summary>
    /// Runtime <see cref="IModelCustomizer"/> for the admin integration-test DbContext. Applies
    /// Core entity configurations (via <see cref="RelationalModelCustomizer"/>'s base call —
    /// which invokes <c>GameKitDbContext.OnModelCreating</c> and its
    /// <c>ApplyConfigurationsFromAssembly</c>) and then Auth + Admin entity configurations
    /// directly, so queries against <c>Player</c> / <c>PlayerIdentity</c> / <c>AdminUser</c>
    /// succeed at runtime. Bypasses the FOLLOW-UP-02-03-01 broken
    /// <c>ApplicationServiceProvider</c> path (which captures the generic-host's service
    /// provider under <c>Host.CreateDefaultBuilder + ConfigureWebHostDefaults</c> and doesn't
    /// see Auth/Admin <c>IModelBuilderExtension</c> registrations on the web host's collection).
    /// Mirrors <c>AuthRuntimeQueryCustomizer</c> from Phase 2 integration tests. Requires
    /// <c>InternalsVisibleTo("GameKit.Admin.Integration.Tests")</c> in <c>GameKit.Auth</c>
    /// (granted in plan 03-06) to reach the internal Auth entity configurations.
    /// </summary>
    internal sealed class AdminRuntimeQueryCustomizer : RelationalModelCustomizer
    {
        public AdminRuntimeQueryCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies) { }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.PlayerIdentityConfiguration());
            modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.PlayerCredentialConfiguration());
            modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.RefreshTokenConfiguration());
            modelBuilder.ApplyConfiguration(new GameKit.Admin.UI.Data.Configurations.AdminUserConfiguration());
        }
    }

    /// <summary>In-memory logger provider that appends every formatted message to a concurrent queue.</summary>
    private sealed class TestLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _sink;
        public TestLoggerProvider(ConcurrentQueue<string> sink) => _sink = sink;
        public ILogger CreateLogger(string categoryName) => new TestLogger(_sink, categoryName);
        public void Dispose() { }

        private sealed class TestLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _sink;
            private readonly string _category;
            public TestLogger(ConcurrentQueue<string> sink, string category)
            {
                _sink = sink;
                _category = category;
            }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
                Func<TState, Exception?, string> fmt)
            {
                _sink.Enqueue($"[{level}] {_category}: {fmt(state, ex)}");
            }
        }
    }
}
