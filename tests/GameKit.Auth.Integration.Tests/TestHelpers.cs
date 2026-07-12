// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// Shared Plan 02-06 test scaffolding: applies Core + Auth migrations and builds a fully-wired
/// <see cref="ServiceProvider"/> configured for provider/service integration tests (Guest, Password,
/// IdentityLinker, GuestUpgrade). Extracted from the ApplyMigrations + BuildProvider patterns in
/// <c>RefreshTokenServiceTests</c> and <c>SteamProviderTests</c> so the four new Plan 02-06 test
/// classes can share a single code path.
/// </summary>
/// <remarks>
/// The DI graph deliberately re-uses the production <c>AddAuth</c> extension — including the
/// Scrutor scan that auto-registers all four <c>IOAuthProvider</c> implementations (steam,
/// discord, guest, password). It also installs the FOLLOW-UP-02-03-01 runtime query customizer
/// so the scoped <see cref="GameKitDbContext"/> can query <c>PlayerIdentity</c> /
/// <c>PlayerCredential</c> directly (the split-model-view gap is tracked for a dedicated plan).
/// </remarks>
internal static class TestHelpers
{
    /// <summary>
    /// Builds a DI provider configured with <c>AddGameKit(...).AddAuth(...)</c>,
    /// <c>SkipAuthenticationSchemeRegistration = true</c>, fresh ephemeral RSA PEM keys written
    /// to a unique temp directory, and the <see cref="AuthRuntimeQueryCustomizer"/> wired onto
    /// the scoped <see cref="GameKitDbContext"/>.
    /// </summary>
    /// <param name="connectionString">Postgres connection string (typically <c>PostgresFixture.OwnerConnectionString</c>).</param>
    /// <returns>A disposable <see cref="TestContext"/>; dispose deletes the temp PEM directory.</returns>
    public static TestContext BuildProvider(string connectionString)
    {
        var keyDir = Path.Combine(Path.GetTempPath(), $"gk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDir);
        var privPath = Path.Combine(keyDir, "priv.pem");
        var pubPath = Path.Combine(keyDir, "pub.pem");
        using (var rsa = RSA.Create(2048))
        {
            File.WriteAllText(privPath, rsa.ExportRSAPrivateKeyPem());
            File.WriteAllText(pubPath, rsa.ExportRSAPublicKeyPem());
        }

        var services = new ServiceCollection();
        var gkBuilder = services.AddGameKit(o =>
        {
            o.ConnectionString = connectionString;
            o.AutoMigrate = false;
        });
        gkBuilder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "gk-test";
            o.Jwt.Audience = "gk-test";
            o.Jwt.PrivateKeyPemPath = privPath;
            o.Jwt.PublicKeyPemPath = pubPath;
            o.Jwt.Kid = "test-kid-1";
            o.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(30);
        });

        // Re-register the DbContext with the runtime-query customizer so queries see
        // PlayerIdentity + PlayerCredential + RefreshToken. Mirrors the FOLLOW-UP-02-03-01
        // workaround from RefreshTokenServiceTests / SteamProviderTests.
        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
            dbOpts.UseNpgsql(connectionString)
                  .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>());

        var sp = services.BuildServiceProvider();
        return new TestContext(sp, keyDir);
    }

    /// <summary>
    /// Applies Core migrations then Auth migrations against the target database. Called once
    /// per test to keep each test hermetic — the Testcontainers Postgres instance is shared by
    /// the class-scoped fixture but tests create/reuse tables by UUIDv7 keys so collisions are
    /// avoided in practice.
    /// </summary>
    /// <param name="connectionString">Postgres connection string.</param>
    public static async Task ApplyMigrations(string connectionString)
    {
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o => { o.ConnectionString = connectionString; o.AutoMigrate = false; });
        coreServices.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());
        // Rule-1 fix: EF Core 10 introduced PendingModelChangesWarning, which fires here because
        // AuthModelBuilderExtension adds Auth entities to the runtime model while the Core snapshot
        // only knows Core entities (per-package migration boundary, PITFALLS.md #3). The warning
        // is intentional and expected — suppress it so the Core migration step can proceed normally.
        // The Auth entities are applied in the separate authCtx migration step below.
        coreServices.AddDbContext<GameKitDbContext>((sp, dbOpts) =>
            dbOpts.UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKitMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .UseApplicationServiceProvider(sp)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        await using var coreSp = coreServices.BuildServiceProvider();
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(
                scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString, npg =>
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
        await authCtx.Database.MigrateAsync();
    }
}

/// <summary>
/// Holds the DI container + its on-disk PEM directory. Dispose cleans the temp files.
/// </summary>
internal sealed class TestContext : IAsyncDisposable
{
    private readonly ServiceProvider _sp;
    private readonly string _keyDir;

    /// <summary>The DI container.</summary>
    public IServiceProvider Services => _sp;

    /// <summary>Constructs a test context; caller owns disposal.</summary>
    public TestContext(ServiceProvider sp, string keyDir)
    {
        _sp = sp;
        _keyDir = keyDir;
    }

    /// <summary>Creates a new async scope on the underlying container.</summary>
    public AsyncServiceScope CreateAsyncScope() => _sp.CreateAsyncScope();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _sp.DisposeAsync().ConfigureAwait(false);
        if (Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}

/// <summary>
/// Runtime <see cref="IModelCustomizer"/> that applies Core's OnModelCreating (via the base
/// implementation) AND <see cref="AuthModelBuilderExtension"/> directly, so the scoped DbContext
/// can query PlayerIdentity, PlayerCredential, and RefreshToken at runtime. Mirrors the
/// FOLLOW-UP-02-03-01 workaround used by every existing Auth integration test class.
/// </summary>
internal sealed class AuthRuntimeQueryCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public AuthRuntimeQueryCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new AuthModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
