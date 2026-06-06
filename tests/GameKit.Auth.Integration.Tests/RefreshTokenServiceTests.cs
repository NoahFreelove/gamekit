// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Auth.Entities;
using GameKit.Auth.Services;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// End-to-end integration tests for the refresh-token rotation state machine against a real
/// Postgres container. Success criterion #3 (concurrent refresh inside grace with matching
/// fingerprint → stays logged in; mismatch → family revoked) is proven here at the service layer.
/// An <see cref="IClock"/> mock is swapped in so tests can advance time deterministically.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class RefreshTokenServiceTests : IDisposable
{
    private readonly PostgresFixture _pg;
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddYears(56);

    public RefreshTokenServiceTests(PostgresFixture pg)
    {
        _pg = pg;
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-refresh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(_privPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, rsa.ExportRSAPublicKeyPem());
    }

    public void Dispose() => Directory.Delete(_keyDir, recursive: true);

    [Fact]
    public async Task IssueRoot_Creates_Family_Row_And_Returns_TokenPair()
    {
        await ApplyMigrations();
        var playerId = await SeedPlayer();
        var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var pair = await svc.IssueRootAsync(playerId, "guest", "device-alpha");

        Assert.False(string.IsNullOrEmpty(pair.AccessJwt));
        Assert.False(string.IsNullOrEmpty(pair.RawRefresh));

        await using var verifyScope = sp.CreateAsyncScope();
        var ctx = verifyScope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var row = await ctx.Set<RefreshToken>().SingleAsync(r => r.PlayerId == playerId);
        Assert.Null(row.RevokedAt);
        Assert.Null(row.UsedAt);
        Assert.Equal("device-alpha", row.DeviceFingerprint);
        Assert.Equal("guest", row.Provider);
    }

    [Fact]
    public async Task Rotate_Happy_Path_Marks_Parent_Revoked_And_Issues_Child()
    {
        await ApplyMigrations();
        var playerId = await SeedPlayer();
        var sp = BuildProvider();

        string raw0;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            var root = await svc.IssueRootAsync(playerId, "guest", "device-alpha");
            raw0 = root.RawRefresh!;
        }

        _now = _now.AddMinutes(5);   // simulate 5 min later

        TokenPair rotated;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            rotated = await svc.RotateAsync(raw0, "device-alpha");
        }

        Assert.False(string.IsNullOrEmpty(rotated.AccessJwt));
        Assert.False(string.IsNullOrEmpty(rotated.RawRefresh));
        Assert.NotEqual(raw0, rotated.RawRefresh);

        await using var verify = sp.CreateAsyncScope();
        var ctx = verify.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var rows = await ctx.Set<RefreshToken>().Where(r => r.PlayerId == playerId).OrderBy(r => r.IssuedAt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].RevokedAt);
        Assert.NotNull(rows[0].UsedAt);
        Assert.Equal(rows[1].TokenHash, rows[0].ReplacedByTokenHash);
        Assert.Null(rows[1].RevokedAt);
    }

    [Fact]
    public async Task RefreshInsideGraceWithMatchingFingerprint_ReturnsChildToken()
    {
        await ApplyMigrations();
        var playerId = await SeedPlayer();
        var sp = BuildProvider();

        string raw0;
        await using (var scope = sp.CreateAsyncScope())
        {
            raw0 = (await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .IssueRootAsync(playerId, "guest", "device-alpha")).RawRefresh!;
        }

        _now = _now.AddMinutes(5);

        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .RotateAsync(raw0, "device-alpha");
        }

        // Within 45 s of the parent's UsedAt, retry with SAME raw0, SAME fingerprint →
        // expect the already-issued child's access token, RawRefresh = null.
        _now = _now.AddSeconds(20);
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            var replay = await svc.RotateAsync(raw0, "device-alpha");
            Assert.Null(replay.RawRefresh);   // server says: you already have it
            Assert.False(string.IsNullOrEmpty(replay.AccessJwt));
        }

        // Family must NOT be revoked — the live child's RevokedAt stays null.
        await using var verify = sp.CreateAsyncScope();
        var ctx = verify.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var live = await ctx.Set<RefreshToken>().CountAsync(r => r.PlayerId == playerId && r.RevokedAt == null);
        Assert.Equal(1, live);
    }

    [Fact]
    public async Task RefreshInsideGraceWithMismatchedFingerprint_RevokesFamily()
    {
        await ApplyMigrations();
        var playerId = await SeedPlayer();
        var sp = BuildProvider();

        string raw0;
        await using (var scope = sp.CreateAsyncScope())
        {
            raw0 = (await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .IssueRootAsync(playerId, "guest", "device-alpha")).RawRefresh!;
        }

        _now = _now.AddMinutes(5);
        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .RotateAsync(raw0, "device-alpha");
        }

        _now = _now.AddSeconds(20);   // inside 45s grace but wrong fingerprint
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => svc.RotateAsync(raw0, "device-BETA"));
            Assert.Equal("refresh_revoked", ex.Code);
        }

        await using var verify = sp.CreateAsyncScope();
        var ctx = verify.ServiceProvider.GetRequiredService<GameKitDbContext>();
        Assert.Equal(0, await ctx.Set<RefreshToken>().CountAsync(r => r.PlayerId == playerId && r.RevokedAt == null));

        // Audit row with reason=refresh_fingerprint_mismatch exists.
        Assert.True(await ctx.AdminAuditLog.AnyAsync(
            a => a.Action == "auth.refresh.family_revoked" && a.Reason == "refresh_fingerprint_mismatch"));
    }

    [Fact]
    public async Task ReuseOutsideGrace_RevokesFamily()
    {
        await ApplyMigrations();
        var playerId = await SeedPlayer();
        var sp = BuildProvider();

        string raw0;
        await using (var scope = sp.CreateAsyncScope())
        {
            raw0 = (await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .IssueRootAsync(playerId, "guest", "device-alpha")).RawRefresh!;
        }

        _now = _now.AddMinutes(5);
        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .RotateAsync(raw0, "device-alpha");
        }

        _now = _now.AddMinutes(10);   // well outside 45s grace
        await using (var scope = sp.CreateAsyncScope())
        {
            var ex = await Assert.ThrowsAsync<UnauthorizedException>(
                () => scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                    .RotateAsync(raw0, "device-alpha"));
            Assert.Equal("refresh_revoked", ex.Code);
        }

        await using var verify = sp.CreateAsyncScope();
        var ctx = verify.ServiceProvider.GetRequiredService<GameKitDbContext>();
        Assert.True(await ctx.AdminAuditLog.AnyAsync(
            a => a.Action == "auth.refresh.family_revoked" && a.Reason == "refresh_reuse_outside_grace"));
    }

    [Fact]
    public async Task Unknown_Token_Throws_UnknownRefresh()
    {
        await ApplyMigrations();
        await SeedPlayer();
        var sp = BuildProvider();

        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(
            () => svc.RotateAsync("deadbeef-fake-token", "device-alpha"));
        Assert.Equal("unknown_refresh", ex.Code);
    }

    // ---------- Fixture helpers ----------

    private async Task<Guid> SeedPlayer()
    {
        var sp = BuildProvider();
        var id = Guid.CreateVersion7();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        ctx.Players.Add(new Player { Id = id, DisplayName = $"p-{id:N}".Substring(0, 20), CreatedAt = _now });
        await ctx.SaveChangesAsync();
        return id;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());

        // Rewire the DbContext to use the Auth-runtime-query customizer so Auth entities
        // appear in the model at query time (mirrors IsGuestResolverTests / 02-03 FOLLOW-UP-02-03-01).
        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
            dbOpts.UseNpgsql(_pg.OwnerConnectionString)
                  .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>());

        // Root Auth options: RefreshReuseInterval = 45s (default), real PEM keys so JwtIssuer can load.
        var opts = new GameKitAuthOptions();
        opts.Jwt.Issuer = "gk-test";
        opts.Jwt.Audience = "gk-test";
        opts.Jwt.PrivateKeyPemPath = _privPath;
        opts.Jwt.PublicKeyPemPath = _pubPath;
        opts.Jwt.Kid = "test-kid-1";
        opts.Jwt.RefreshReuseInterval = TimeSpan.FromSeconds(45);
        opts.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(30);
        services.AddSingleton(opts);

        services.AddScoped<IIsGuestResolver, IsGuestResolver>();
        services.AddScoped<IJwtIssuer, JwtIssuer>();
        services.AddScoped<IAuthAuditWriter, AuthAuditWriter>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // Mock clock so tests control time. Replace the AddGameKit-registered SystemClock.
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(() => _now);
        services.Replace(ServiceDescriptor.Singleton<IClock>(clockMock.Object));

        return services.BuildServiceProvider();
    }

    private async Task ApplyMigrations()
    {
        // Core migration step: use a plain Core-only service provider (no Auth extension) so the
        // runtime model matches the Core snapshot exactly. Registering AuthModelBuilderExtension
        // here would add Auth entities to the model while the Core snapshot has none, triggering
        // PendingModelChangesWarning (EF Core 10). The Auth extension is not needed here because
        // AuthMigrationModelCustomizer applies Auth configs directly without DI resolution.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
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
    /// Runtime customizer for refresh-token query-side tests — applies Core's OnModelCreating AND
    /// Auth's three entity configurations directly. Mirrors PlayerIdentityUniqueTests /
    /// IsGuestResolverTests (FOLLOW-UP-02-03-01 workaround).
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
