// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Builder;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Data;
using GameKit.Matchmaking.Services;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace GameKit.LoadTests.Infrastructure;

/// <summary>
/// Lean host that owns the Testcontainers Redis + Postgres lifecycle for
/// <see cref="GameKit.LoadTests.Benchmarks.MatchmakingTicketBenchmarks"/>.
/// </summary>
/// <remarks>
/// <para>
/// This helper replicates the core wiring of
/// <c>tests/GameKit.Matchmaking.LoadTests/LoadTestFixture.cs</c> in a BenchmarkDotNet-safe
/// form: containers are started in the BDN <c>[GlobalSetup]</c> (once per benchmark class),
/// and the <c>IMatchmakingService</c> is resolved from the running host's
/// <see cref="IServiceProvider"/>.
/// </para>
/// <para>
/// Migration approach: applies Core → Admin → Rankings → Matchmaking migrations in order
/// using public <see cref="MigrationRunner.MigrateWithLockAsync"/> overloads with the
/// package-specific advisory-lock keys and <c>IModelCustomizer</c> replacements — the same
/// pattern used by <c>LoadTestMigrationHelpers</c> in the sibling sustain-load project.
/// </para>
/// <para>
/// Postgres setup: mounts the repo's <c>docker/postgres/init</c> scripts (role creation,
/// extensions) exactly as <c>PostgresFixture</c> does, so <c>gamekit_owner</c>,
/// <c>gamekit_app</c>, and the <c>gamekit</c> schema are available for EF migrations.
/// </para>
/// </remarks>
public sealed class MatchmakingBenchmarkHost : IAsyncDisposable
{
    private PostgreSqlContainer? _pg;
    private RedisContainer? _redis;
    private IHost? _host;
    private string? _keyDir;

    /// <summary>The benchmark's target ladder id — seeded once during setup.</summary>
    public Guid TestLadderId { get; private set; }

    /// <summary>The benchmark's test player id — a placeholder guid seeded during setup.</summary>
    public Guid TestPlayerId { get; } = Guid.NewGuid();

    /// <summary>
    /// The <see cref="IMatchmakingService"/> resolved from the running host.
    /// Available after <see cref="InitializeAsync"/> returns.
    /// </summary>
    public IMatchmakingService MatchmakingService { get; private set; } = null!;

    /// <summary>
    /// Starts Testcontainers Redis + Postgres, applies migrations, seeds a ladder + player,
    /// and starts the in-process GameKit host. Intended to be called from BDN
    /// <c>[GlobalSetup]</c> (once per benchmark run; container boot cost ~1-3s is not measured).
    /// </summary>
    public async Task InitializeAsync()
    {
        // ── 1. Start containers ──────────────────────────────────────────────────────
        var repoRoot = FindRepoRoot();
        var initDir  = Path.Combine(repoRoot, "docker", "postgres", "init");

        _pg = new PostgreSqlBuilder("postgres:17.9")
            .WithUsername("postgres")
            .WithPassword("postgres_test")
            .WithDatabase("postgres")
            .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
            .Build();

        _redis = new RedisBuilder("redis:8.6.2").Build();

        // Start both containers in parallel to shorten setup time.
        await Task.WhenAll(_pg.StartAsync(), _redis.StartAsync());

        var pgHost = _pg.Hostname;
        var pgPort = _pg.GetMappedPublicPort(5432);
        var ownerCs = $"Host={pgHost};Port={pgPort};Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";
        var appCs   = $"Host={pgHost};Port={pgPort};Database=gamekit;Username=gamekit_app;Password=gamekit_app_dev";
        var adminCs = $"Host={pgHost};Port={pgPort};Database=gamekit;Username=postgres;Password=postgres_test";
        var redisCs = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)}";

        // ── 2. Apply GameKit migrations in dependency order ──────────────────────────
        //       Core → Admin → Rankings → Matchmaking  (mirrors LoadTestMigrationHelpers)
        await using (var sp = BuildServiceProviderForMigrations(ownerCs))
        {
            await using var scope = sp.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        await using (var adminCtx = BuildMigrationContext<AdminMigrationModelCustomizer>(
            ownerCs,
            typeof(AdminMigrationConstants).Assembly.FullName!,
            AdminMigrationConstants.MigrationsHistoryTable))
        {
            await MigrationRunner.MigrateWithLockAsync(adminCtx, AdminMigrationConstants.AdvisoryLockKey);
        }

        await using (var rankingsCtx = BuildMigrationContext<RankingsMigrationModelCustomizer>(
            ownerCs,
            typeof(RankingsMigrationConstants).Assembly.FullName!,
            RankingsMigrationConstants.MigrationsHistoryTable))
        {
            await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);
        }

        await using (var matchCtx = BuildMigrationContext<MatchmakingMigrationModelCustomizer>(
            ownerCs,
            typeof(MatchmakingMigrationConstants).Assembly.FullName!,
            MatchmakingMigrationConstants.MigrationsHistoryTable))
        {
            await MigrationRunner.MigrateWithLockAsync(matchCtx, MatchmakingMigrationConstants.AdvisoryLockKey);
        }

        // ── 3. Seed a Ladder row ─────────────────────────────────────────────────────
        TestLadderId = await SeedLadderAsync(ownerCs, "bench");

        // ── 4. Seed a Player row ─────────────────────────────────────────────────────
        await SeedPlayerAsync(appCs, TestPlayerId);

        // ── 5. Generate ephemeral RSA keypair for JWT options required by AddAuth ────
        //       AddMatchmaking does not use JWT directly; AddAuth needs it to resolve
        //       without throwing at host start.
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        var priv = Path.Combine(_keyDir, "priv.pem");
        var pub  = Path.Combine(_keyDir, "pub.pem");
        using var signingRsa = System.Security.Cryptography.RSA.Create(2048);
        await File.WriteAllTextAsync(priv, signingRsa.ExportRSAPrivateKeyPem());
        await File.WriteAllTextAsync(pub, signingRsa.ExportRSAPublicKeyPem());

        // ── 6. Build and start the in-process host ───────────────────────────────────
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                var gk = services.AddGameKit(o =>
                {
                    o.ConnectionString = appCs;
                    o.MigrationsConnectionString = ownerCs;
                    o.AutoMigrate = false; // migrations already applied above
                });

                // Override the DbContext registration to use GameKitModelCacheKeyFactory so the
                // full-runtime model (Rankings + Matchmaking entities) is cached separately from
                // the Core-only migration model built in BuildServiceProviderForMigrations above.
                // This prevents the "Cannot create a DbSet for 'SessionCompleteIdempotency'" crash
                // that occurs when the migration SP pollutes the shared EF model cache.
                services.AddDbContext<GameKitDbContext>((sp, dbOpts) =>
                    dbOpts.UseNpgsql(appCs, npg =>
                    {
                        npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                        npg.MigrationsHistoryTable(
                            GameKitMigrationConstants.MigrationsHistoryTable,
                            GameKitMigrationConstants.SchemaName);
                    })
                    .UseApplicationServiceProvider(sp)
                    .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                                    GameKitModelCacheKeyFactory>());

                gk.AddAuth(o =>
                {
                    o.Jwt.Issuer          = "gk-bench";
                    o.Jwt.Audience        = "gk-bench";
                    o.Jwt.PrivateKeyPemPath = priv;
                    o.Jwt.PublicKeyPemPath  = pub;
                    o.Jwt.Kid             = "bench-kid";
                });

                gk.AddRankings();

                var mm = gk.AddMatchmaking(o =>
                {
                    // Disable escalating cooldown so the same player can re-queue between
                    // benchmark iterations without being throttled.
                    o.Cooldown.Step1Minutes = 0;
                    o.Cooldown.Step2Minutes = 0;
                    o.Cooldown.Step3Minutes = 0;
                    // Production tick interval (500 ms) is fine; ticker runs in background.
                    o.Ticker.TickIntervalMs = 500;
                });
                mm.AddLadder("bench");

                // Replace the default IConnectionMultiplexer with the Testcontainers Redis.
                var muxDesc = services.FirstOrDefault(d => d.ServiceType == typeof(IConnectionMultiplexer));
                if (muxDesc is not null) services.Remove(muxDesc);
                services.AddSingleton<IConnectionMultiplexer>(
                    _ => ConnectionMultiplexer.Connect(redisCs));

                // Register audit writer (needed by matchmaking reconciler orphan-session sweep).
                services.AddScoped<GameKit.Admin.UI.Services.IAdminAuditWriter,
                                   GameKit.Admin.UI.Services.AdminAuditWriter>();
            })
            .Build();

        await _host.StartAsync().ConfigureAwait(false);

        // Create a scope for the benchmark — IMatchmakingService is Scoped.
        // We create one persistent scope so iterations don't pay scope-creation overhead.
        MatchmakingService = _host.Services
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<IMatchmakingService>();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            try { await _host.StopAsync(); } catch { /* best-effort */ }
            _host.Dispose();
        }

        if (_keyDir is not null && Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }

        if (_redis is not null) await _redis.DisposeAsync();
        if (_pg    is not null) await _pg.DisposeAsync();
    }

    // ── private helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal <see cref="IServiceProvider"/> containing <see cref="GameKitDbContext"/>
    /// registered via <c>AddGameKit</c>, used for running Core migrations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers <see cref="GameKitModelCacheKeyFactory"/> so that this Core-only migration
    /// service provider's EF model (no Rankings/Matchmaking entities) does NOT pollute the
    /// shared EF model cache and collide with the full-runtime host's model
    /// (which includes all sibling-package entities via <see cref="IModelBuilderExtension"/>).
    /// Without this, the Core-only migration SP builds and caches the model under key
    /// <c>(GameKitDbContext, GameKitModelCustomizer, false)</c>, and the runtime host
    /// retrieves the same cache entry — causing "Cannot create DbSet for X" errors.
    /// The <see cref="GameKitModelCacheKeyFactory"/> appends the extension-type list to the
    /// cache key, producing distinct entries for Core-only and full-runtime contexts.
    /// See <see cref="GameKitModelCacheKeyFactory"/> XML doc for full rationale.
    /// </para>
    /// </remarks>
    private static ServiceProvider BuildServiceProviderForMigrations(string cs)
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = cs;
            o.MigrationsConnectionString = cs;
            o.AutoMigrate = false;
        });

        // Override the DbContext registration to include the model cache key factory.
        // We must re-register GameKitDbContext with the factory so the migration model
        // doesn't pollute the cache key shared with the runtime host.
        services.AddDbContext<GameKitDbContext>((sp, dbOpts) =>
            dbOpts.UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKitMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .UseApplicationServiceProvider(sp)
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory,
                            GameKitModelCacheKeyFactory>());

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a standalone <see cref="GameKitDbContext"/> wired for a single package's
    /// migration run (replaces <see cref="IModelCustomizer"/> with the package-specific
    /// migration customizer and sets the correct migrations assembly + history table).
    /// </summary>
    private static GameKitDbContext BuildMigrationContext<TCustomizer>(
        string cs,
        string migrationsAssemblyName,
        string migrationsHistoryTable)
        where TCustomizer : class, IModelCustomizer
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(migrationsAssemblyName);
                npg.MigrationsHistoryTable(migrationsHistoryTable, GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, TCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }

    /// <summary>Inserts a <c>ladders</c> row and returns its id.</summary>
    private static async Task<Guid> SeedLadderAsync(string cs, string name)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.ladders
            (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"", ""Config"")
            VALUES (@id, @n, 'Glicko2', true, NOW(), '{}'::jsonb)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("n", name);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Inserts a minimal <c>players</c> row so Matchmaking FKs resolve.</summary>
    private static async Task SeedPlayerAsync(string cs, Guid playerId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.players
            (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES (@id, @name, NOW(), false)
            ON CONFLICT (""Id"") DO NOTHING";
        cmd.Parameters.AddWithValue("id", playerId);
        cmd.Parameters.AddWithValue("name", "bench_" + playerId.ToString("N")[..8]);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Walks parent directories until the <c>.git</c> directory is found.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Cannot locate repo root (no .git directory found).");
    }
}
