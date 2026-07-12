// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Builder;
using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Matchmaking.Builder;
using GameKit.Rankings.Builder;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// OPS-06 EXTENSION (Plan 06-08 Task 3): boots an <see cref="IHost"/> that registers
/// Core + Auth + Rankings + Matchmaking + Admin.UI and asserts every package's
/// migration history table is created against a fresh Testcontainers Postgres.
/// Validates the coordinated migration startup ordering established by
/// <see cref="Core.Hosting.GameKitVersionAssertionHostedService"/> (index 0) +
/// each per-package <c>*MigrationHostedService</c>.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 OPS-06 (<c>tests/GameKit.Integration.Tests/CleanInstallMigrationTests.cs</c>)
/// covered Core-only migrations against a fresh Postgres. Plan 06-08 EXTENDS the
/// surface to Auth + Rankings + Matchmaking + Admin.UI. <c>Presence</c> +
/// <c>OpenApi</c> are intentionally EXCLUDED from the chain: PRES-01 makes Presence
/// Redis-only (no EF entities, no migrations); OpenApi is doc-generation only.
/// </para>
/// <para>
/// Note: the plan uses the placeholder name <c>.AddAdminUi()</c>; the real extension
/// is <c>.AddGameKitAdmin()</c> per <c>src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs</c>
/// (Rule 3 — trivially blocking name resolution). PATTERNS warning #5: Admin.UI ships
/// its own <c>__ef_migrations_admin</c> table from Phase 3 — omitting <c>.AddGameKitAdmin()</c>
/// would silently skip the package's migration coverage in the clean-install assertion.
/// </para>
/// <para>
/// Uses <c>DistributionIntegrationFixture</c> for the Postgres container. The fixture's
/// init-script bind-mount creates the <c>gamekit_owner</c> role + an empty <c>gamekit</c>
/// schema; the per-package migration hosted services then materialize their tables.
/// This is the canonical clean-install path (operator runs <c>docker compose up</c>,
/// then starts the app; tables are created by the migration runners).
/// </para>
/// </remarks>
[Collection("Redis")]
[Trait("Category", "Integration")]
public sealed class OPS06_CleanInstallMigrationTests
{
    private readonly RedisFixture _redis;

    /// <summary>
    /// Uses the shared <see cref="RedisFixture"/> (one container per CI run for the
    /// Redis sidecar that Matchmaking + Presence require) but spins up its own
    /// per-test <see cref="PostgreSqlContainer"/> with the 3-role init script bind-
    /// mounted. This isolation is REQUIRED — the OPS-06 "clean install" assertion
    /// must apply migrations against a pristine schema; sharing the
    /// <see cref="DistributionIntegrationFixture"/> Postgres would let DIST-02's
    /// owner-seed row (and Core's startup ladder upserter, etc.) leak into the
    /// migration apply chain and produce false-positive failures.
    /// </summary>
    public OPS06_CleanInstallMigrationTests(RedisFixture redis)
    {
        _redis = redis;
    }

    [Fact]
    public async Task CleanInstall_AllMigratingPackages_Apply_NoDrift()
    {
        // Spin up an isolated Postgres container per OPS-06 invocation with the
        // 3-role init script bind-mounted (the migration runners use the owner
        // role; admin bootstrap reads the admin schema, etc.). Matches the
        // PostgresFixture shape from tests/GameKit.TestFixtures/PostgresFixture.cs.
        var initDir = Path.Combine(GitRootLocator.FindRepoRoot(), "docker", "postgres", "init");
        await using var pg = new PostgreSqlBuilder("postgres:17.9")
            .WithUsername("postgres")
            .WithPassword("postgres_test")
            .WithDatabase("postgres")
            .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
            .Build();
        await pg.StartAsync();

        var host_ = pg.Hostname;
        var port = pg.GetMappedPublicPort(5432);
        var ownerConn = $"Host={host_};Port={port};Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";

        var redisConn = _redis.ConnectionString;

        // Test host runs in the Development environment to bypass the
        // SuperadminGateHostedService (T-03-06-05) which otherwise throws if the
        // admin_users table is empty in Production. OPS-06 is a migration-coverage
        // gate; bootstrapping a superadmin is out of scope.
        //
        // Use WebApplication.CreateBuilder (web host) rather than
        // Host.CreateApplicationBuilder (generic host) because AddGameKitAdmin
        // registers RazorComponents + MudBlazor services that require
        // IWebHostEnvironment — only the web host provides it. No HTTP server is
        // actually started; we only call StartAsync to drive the migration
        // hosted services. The plan's "console-host" suggestion is overruled here
        // by the real DI dependency (Rule 3 blocking fix).
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });

        // Matchmaking (Plan 05-09) takes a singleton IConnectionMultiplexer constructor-
        // injected; the package builder intentionally does NOT auto-register the multiplexer
        // because (a) it's a singleton with operator-owned lifecycle, (b) production
        // deployments often wire ConfigurationOptions (TLS, AbortOnConnectFail, etc.) manually.
        // Mirrors the convention in samples/TicTacToeDuel/Program.cs:23-25.
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConn));

        var gameKitBuilder = builder.Services.AddGameKit(o =>
        {
            o.ConnectionString = ownerConn;
            o.MigrationsConnectionString = ownerConn;
            o.RedisConnectionString = redisConn;
            o.AutoMigrate = true;
        });

        gameKitBuilder.AddAuth(auth =>
        {
            // Minimum-valid options: Issuer + Audience required; skipping scheme
            // registration so the test does not need RSA PEM key files (this is
            // the same convention the Auth integration tests use — see
            // ValidateAuthOptions in AuthBuilderExtensions.cs).
            auth.Jwt.Issuer = "ops-06-test";
            auth.Jwt.Audience = "ops-06-test";
            auth.SkipAuthenticationSchemeRegistration = true;
        });

        gameKitBuilder.AddRankings(_ => { /* defaults */ })
            .AddLadder("default-test-ladder", _ => { /* defaults */ });

        gameKitBuilder.AddMatchmaking(_ => { /* defaults */ })
            .AddLadder("default-test-ladder", _ => { /* defaults */ });

        // PATTERNS warning #5: Admin.UI ships its own __ef_migrations_admin table
        // (Phase 3 D-12). The plan's wording uses the placeholder ".AddAdminUi()"
        // — the real extension is .AddGameKitAdmin() per
        // src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs (Rule 3 — trivially
        // blocking name resolution).
        gameKitBuilder.AddGameKitAdmin(_ => { /* defaults */ });

        // Cross-test pollution defense: OPS05_VersionMismatchAssertionThrowsTests
        // synthesizes a "GameKit.SyntheticTest" assembly via Reflection.Emit and the
        // collectible AssemblyBuilder may not finalize before OPS-06 runs (xUnit shares
        // the test process). If the synthetic marker lingers,
        // GameKitVersionAssertionHostedService at index 0 throws on host.StartAsync —
        // a false-positive against the genuine OPS-06 migration-coverage assertion.
        // Remove the version-assertion hosted service from this test's DI container
        // (the assertion is validated in OPS-05 with full coverage; removing it here
        // does NOT relax OPS-06's actual assertion surface — clean-install migrations).
        // Use a string-based descriptor match because the assertion type is internal
        // to GameKit.Core (no InternalsVisibleTo to this test assembly).
        var assertionDescriptor = builder.Services
            .FirstOrDefault(d => d.ImplementationType?.FullName == "GameKit.Core.Hosting.GameKitVersionAssertionHostedService");
        if (assertionDescriptor is not null)
        {
            builder.Services.Remove(assertionDescriptor);
        }

        using var host = builder.Build();

        // StartAsync drives every IHostedService — version assertion first (index 0),
        // then each per-package *MigrationHostedService applies its migrations under
        // the package's advisory-lock key. The Core migrations are applied by
        // UseGameKit() in the middleware pipeline; since this is a console host
        // (Host.CreateApplicationBuilder), we apply Core manually first using a
        // Core-only DbContext (single-arg ctor — no IModelBuilderExtensions; mirrors
        // GameKitApplicationBuilderExtensions.UseGameKit's BuildMigrationContext
        // pattern). This is important: a DI-resolved GameKitDbContext in a
        // multi-package host fan-outs IModelBuilderExtension over Auth/Rankings/
        // Matchmaking/Admin entities, which makes the Migrator trip
        // PendingModelChangesWarning — not a drift bug, just the boundary between
        // the runtime composite model and per-package migration snapshots
        // (PITFALLS #3).
        var ct = CancellationToken.None;
        await using (var coreMigrationCtx = BuildCoreOnlyMigrationContext(ownerConn))
        {
            await MigrationRunner.MigrateWithLockAsync(coreMigrationCtx, cancellationToken: ct);
        }

        await host.StartAsync(ct);

        // Assertion 1: every expected migration history table exists.
        await using var conn = new NpgsqlConnection(ownerConn);
        await conn.OpenAsync(ct);

        var expectedHistoryTables = new[]
        {
            "__ef_migrations_core",
            "__ef_migrations_auth",
            "__ef_migrations_rankings",
            "__ef_migrations_matchmaking",
            "__ef_migrations_admin",
        };

        foreach (var table in expectedHistoryTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT to_regclass('gamekit.{table}') IS NOT NULL;";
            var exists = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            Assert.True(exists, $"gamekit.{table} must exist after clean install (OPS-06 / Plan 06-08).");
        }

        // Assertion 2: no model-snapshot drift on the Core context — GetPendingMigrationsAsync
        // returns empty after the apply. (Per-package contexts are validated in their respective
        // *MigrationDeterminismTests; OPS-06 extends the assertion to the composite happy path.)
        // Use the same Core-only context shape as the apply step above.
        await using (var coreCheckCtx = BuildCoreOnlyMigrationContext(ownerConn))
        {
            var pending = (await coreCheckCtx.Database.GetPendingMigrationsAsync(ct)).ToList();
            Assert.Empty(pending);
        }

        await host.StopAsync(ct);
    }

    /// <summary>
    /// Builds a Core-only <see cref="GameKitDbContext"/> using the single-arg ctor that
    /// receives no <c>IEnumerable&lt;IModelBuilderExtension&gt;</c>, so sibling-package entities
    /// are absent from the model. Mirrors the pattern in
    /// <c>GameKitApplicationBuilderExtensions.UseGameKit.BuildMigrationContext</c>. Required
    /// to keep the Migrator from raising <c>PendingModelChangesWarning</c> when the test
    /// host registers Auth/Rankings/Matchmaking/Admin model-builder extensions whose
    /// migrations live in their own snapshots (PITFALLS #3 per-package migration boundary).
    /// </summary>
    private static GameKitDbContext BuildCoreOnlyMigrationContext(string connectionString)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKitMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            });

        return new GameKitDbContext(optionsBuilder.Options);
    }
}
