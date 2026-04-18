// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Auth.Entities;
using GameKit.Auth.Providers;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// Postgres + WireMock integration coverage for <see cref="DiscordOAuthProvider"/>. Covers the
/// Player + PlayerIdentity upsert path given a verified Discord snowflake + username (the kind of
/// input the aspnet-contrib handler's <c>OnCreatingTicket</c> event supplies to the provider).
/// The end-to-end handler coverage (302 redirect flow, identify-scope assertion, backchannel egress)
/// lives in plan 02-07 via WebApplicationFactory.
/// </summary>
[Collection("Auth")]
[Trait("Category", "Integration")]
public sealed class DiscordProviderTests : IDisposable
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private readonly WireMockFixture _wm;

    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;

    public DiscordProviderTests(PostgresFixture pg, RedisFixture redis, WireMockFixture wm)
    {
        _pg = pg; _redis = redis; _wm = wm;

        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-discordprov-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(_privPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, rsa.ExportRSAPublicKeyPem());
    }

    public void Dispose()
    {
        if (Directory.Exists(_keyDir)) Directory.Delete(_keyDir, recursive: true);
    }

    [Fact]
    public async Task DiscordProvider_CompleteLoginAsync_Creates_Row()
    {
        await ApplyMigrations();
        var externalId = Random.Shared.NextInt64(100_000_000_000_000_000L, 999_999_999_999_999_999L)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
            .First(p => p.Provider == "discord");

        var result = await provider.CompleteLoginAsync(externalId, "mock_user", null, "device-alpha");

        Assert.True(result.Success);
        Assert.NotNull(result.Tokens);
        Assert.False(string.IsNullOrEmpty(result.Tokens!.AccessJwt));

        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var count = await ctx.Set<PlayerIdentity>()
            .CountAsync(i => i.Provider == "discord" && i.ExternalId == externalId);
        Assert.Equal(1, count);
    }

    // --------- Fixture helpers (parity with SteamProviderTests) ---------

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        var gkBuilder = services.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
        gkBuilder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "gk-test";
            o.Jwt.Audience = "gk-test";
            o.Jwt.PrivateKeyPemPath = _privPath;
            o.Jwt.PublicKeyPemPath = _pubPath;
            o.Jwt.Kid = "test-kid-1";
        });

        // Rewire to the Auth-runtime-query customizer so PlayerIdentity is in the model.
        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
            dbOpts.UseNpgsql(_pg.OwnerConnectionString)
                  .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>());

        return services.BuildServiceProvider();
    }

    private async Task ApplyMigrations()
    {
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
        coreServices.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());
        await using var coreSp = coreServices.BuildServiceProvider();
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(_pg.OwnerConnectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(AuthMigrationConstants.MigrationsHistoryTable, GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .UseApplicationServiceProvider(coreSp)
            .Options;
        await using var authCtx = new GameKitDbContext(authOpts);
        await authCtx.Database.MigrateAsync();
    }

    internal sealed class AuthRuntimeQueryCustomizer : RelationalModelCustomizer
    {
        public AuthRuntimeQueryCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            new AuthModelBuilderExtension().ApplyTo(modelBuilder);
        }
    }
}
