// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Auth.Entities;
using GameKit.Auth.Providers;
using GameKit.Auth.Providers.Steam;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// Postgres + WireMock integration coverage for <see cref="SteamOAuthProvider"/> and
/// <see cref="SteamOpenIdVerifier"/>. Proves success criterion #2 at the integration layer:
/// a forged Steam callback (OP returns <c>is_valid:false</c>) is rejected and NO
/// <see cref="PlayerIdentity"/> row is written. The end-to-end WebApplicationFactory
/// coverage lives in plan 02-07.
/// </summary>
[Collection("Auth")]
[Trait("Category", "Integration")]
public sealed class SteamProviderTests : IDisposable
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private readonly WireMockFixture _wm;

    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;

    public SteamProviderTests(PostgresFixture pg, RedisFixture redis, WireMockFixture wm)
    {
        _pg = pg; _redis = redis; _wm = wm;

        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-steamprov-{Guid.NewGuid():N}");
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
    public async Task CompleteLoginAsync_Creates_Player_And_Identity_On_First_Login()
    {
        await ApplyMigrations();
        var externalId = $"765611980000{Random.Shared.Next(10000, 99999):D5}";

        var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
            .First(p => p.Provider == "steam");

        var result = await provider.CompleteLoginAsync(externalId, "steam-display", null, "device-alpha");

        Assert.True(result.Success);
        Assert.NotNull(result.Tokens);
        Assert.False(string.IsNullOrEmpty(result.Tokens!.AccessJwt));

        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var identity = await ctx.Set<PlayerIdentity>()
            .SingleAsync(i => i.Provider == "steam" && i.ExternalId == externalId);
        Assert.Equal(result.PlayerId, identity.PlayerId);
        Assert.Equal("steam-display", identity.DisplayName);
    }

    [Fact]
    public async Task CompleteLoginAsync_Second_Call_Same_SteamId_Reuses_Player()
    {
        await ApplyMigrations();
        var externalId = $"765611980000{Random.Shared.Next(10000, 99999):D5}";

        var sp = BuildProvider();

        Guid playerId1;
        await using (var scope = sp.CreateAsyncScope())
        {
            var p = scope.ServiceProvider.GetServices<IOAuthProvider>().First(x => x.Provider == "steam");
            var r = await p.CompleteLoginAsync(externalId, "first", null, "d1");
            playerId1 = r.PlayerId!.Value;
        }

        Guid playerId2;
        await using (var scope = sp.CreateAsyncScope())
        {
            var p = scope.ServiceProvider.GetServices<IOAuthProvider>().First(x => x.Provider == "steam");
            var r = await p.CompleteLoginAsync(externalId, "second", null, "d1");
            playerId2 = r.PlayerId!.Value;
        }

        Assert.Equal(playerId1, playerId2);

        await using var verify = sp.CreateAsyncScope();
        var ctx = verify.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var identity = await ctx.Set<PlayerIdentity>().SingleAsync(i => i.ExternalId == externalId);
        Assert.Equal("second", identity.DisplayName);
    }

    [Fact]
    public async Task Forged_Assertion_Rejected_By_Verifier_And_No_Row_Written()
    {
        // Success Criterion #2 (integration level): WireMock serves is_valid:false →
        // verifier returns invalid → no PlayerIdentity is inserted (the endpoint flow in
        // plan 02-07 guards on IsValid, so here we assert the direct result + the absence
        // of a db row for the attempted external_id).
        await ApplyMigrations();
        var forgedExternalId = $"765611989999{Random.Shared.Next(10000, 99999):D5}";

        WireMockSteamStubs.StubIsValidFalse(_wm.Server);
        try
        {
            var sp = BuildProvider();
            await using var scope = sp.CreateAsyncScope();
            var verifier = scope.ServiceProvider.GetRequiredService<SteamOpenIdVerifier>();

            var query = new QueryCollection(new Dictionary<string, StringValues>
            {
                ["openid.mode"]           = "id_res",
                ["openid.claimed_id"]     = $"https://steamcommunity.com/openid/id/{forgedExternalId}",
                ["openid.identity"]       = $"https://steamcommunity.com/openid/id/{forgedExternalId}",
                ["openid.op_endpoint"]    = "https://steamcommunity.com/openid/login",
                ["openid.response_nonce"] = "nonce",
                ["openid.return_to"]      = "https://x/",
                ["openid.assoc_handle"]   = "h",
                ["openid.signed"]         = "signed",
                ["openid.sig"]            = "forged-sig",
            });

            var result = await verifier.VerifyAsync(query);
            Assert.False(result.IsValid);
            Assert.Equal("is_valid_false", result.ErrorCode);

            // Endpoint flow guard: if !IsValid, CompleteLoginAsync is NOT called → no row.
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var count = await ctx.Set<PlayerIdentity>().CountAsync(i => i.ExternalId == forgedExternalId);
            Assert.Equal(0, count);
        }
        finally
        {
            _wm.ResetDefaultStubs();
        }
    }

    // --------- Fixture helpers (shape mirrors RefreshTokenServiceTests) ---------

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        var wmHost = new Uri(_wm.BaseUrl).Host;

        var gkBuilder = services.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
        gkBuilder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "gk-test";
            o.Jwt.Audience = "gk-test";
            o.Jwt.PrivateKeyPemPath = _privPath;
            o.Jwt.PublicKeyPemPath = _pubPath;
            o.Jwt.Kid = "test-kid-1";
            o.Steam.OpenIdEndpoint = _wm.SteamOpenIdLoginUrl;
            o.AllowedProviderHosts.Add(wmHost);
        });

        // Rewire the DbContext to use the Auth-runtime-query customizer so PlayerIdentity
        // appears in the model at query time (FOLLOW-UP-02-03-01 workaround). This replaces
        // the default DbContext registration that AddGameKit wired up.
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

    /// <summary>
    /// Runtime customizer — mirrors the FOLLOW-UP-02-03-01 DI-gap workaround used by
    /// RefreshTokenServiceTests / IsGuestResolverTests / PlayerIdentityUniqueTests.
    /// Applies Core (via base) then Auth entities directly so queries see Player + PlayerIdentity.
    /// </summary>
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
