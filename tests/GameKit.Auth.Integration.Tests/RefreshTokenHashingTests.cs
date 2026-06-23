// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
/// SEC-06 regression: asserts that stored refresh tokens are always SHA-256 hex hashes and
/// that the raw issued token is NEVER persisted to the <c>refresh_tokens</c> table.
/// Runs against a real Postgres container supplied by the <c>Postgres</c> collection fixture.
/// </summary>
/// <remarks>
/// CLAUDE.md invariant: "never store raw tokens — always SHA-256 hash; raw issued to client once."
/// This class proves the invariant at the integration level.
/// </remarks>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class RefreshTokenHashingTests : IDisposable
{
    private readonly PostgresFixture _pg;
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    /// <summary>Regex that matches a valid 64-character lowercase hexadecimal string (SHA-256 output).</summary>
    private static readonly Regex Sha256HexPattern = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled);

    public RefreshTokenHashingTests(PostgresFixture pg)
    {
        _pg = pg;
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(_privPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, rsa.ExportRSAPublicKeyPem());
    }

    public void Dispose() => Directory.Delete(_keyDir, recursive: true);

    /// <summary>
    /// Core assertion: after <c>IssueRootAsync</c> the stored <c>TokenHash</c> is a 64-char
    /// lowercase hex string equal to SHA-256(raw) and NOT equal to the raw token itself.
    /// </summary>
    [Fact]
    public async Task IssueRootAsync_Stores_Sha256Hex_Not_RawToken()
    {
        await ApplyMigrationsAsync();
        var playerId = await SeedPlayerAsync();
        var sp = BuildProvider();

        // Issue a root refresh token.
        string rawToken;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            var pair = await svc.IssueRootAsync(playerId, "password", fingerprint: null);
            rawToken = pair.RawRefresh!;
        }

        Assert.False(string.IsNullOrEmpty(rawToken), "IssueRootAsync must return a non-empty raw refresh token.");

        // Query the refresh_tokens table directly and verify stored hash properties.
        await using var verifyScope = sp.CreateAsyncScope();
        var ctx = verifyScope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var rows = await ctx.Set<RefreshToken>()
            .Where(r => r.PlayerId == playerId)
            .ToListAsync();

        Assert.Single(rows);
        var storedHash = rows[0].TokenHash;

        // 1. Stored value must be exactly 64 lowercase hex characters (SHA-256 output).
        Assert.Matches(Sha256HexPattern, storedHash);

        // 2. Stored hash must equal SHA-256 of the raw token returned to the caller.
        var expectedHash = ComputeSha256Hex(rawToken);
        Assert.Equal(expectedHash, storedHash);

        // 3. Stored hash must NOT equal the raw token (proves the raw value is not stored).
        Assert.NotEqual(rawToken, storedHash);
    }

    /// <summary>
    /// After rotation, the child token's <c>TokenHash</c> is also a 64-char SHA-256 hex
    /// and the raw child token is not persisted anywhere in the <c>refresh_tokens</c> table.
    /// </summary>
    [Fact]
    public async Task RotateAsync_Stores_Sha256Hex_For_Child_Token()
    {
        await ApplyMigrationsAsync();
        var playerId = await SeedPlayerAsync();
        var sp = BuildProvider();

        // Issue root then rotate.
        string rawRoot, rawChild;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            rawRoot = (await svc.IssueRootAsync(playerId, "password", null)).RawRefresh!;
        }
        _now = _now.AddMinutes(5);
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            rawChild = (await svc.RotateAsync(rawRoot, fingerprint: null)).RawRefresh!;
        }

        Assert.False(string.IsNullOrEmpty(rawChild));

        // Verify both rows: root and child.
        await using var verifyScope = sp.CreateAsyncScope();
        var ctx = verifyScope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var rows = await ctx.Set<RefreshToken>()
            .Where(r => r.PlayerId == playerId)
            .OrderBy(r => r.IssuedAt)
            .ToListAsync();

        Assert.Equal(2, rows.Count);

        // Root row: hash must be SHA-256(rawRoot), not rawRoot.
        Assert.Matches(Sha256HexPattern, rows[0].TokenHash);
        Assert.Equal(ComputeSha256Hex(rawRoot), rows[0].TokenHash);
        Assert.NotEqual(rawRoot, rows[0].TokenHash);

        // Child row: hash must be SHA-256(rawChild), not rawChild.
        Assert.Matches(Sha256HexPattern, rows[1].TokenHash);
        Assert.Equal(ComputeSha256Hex(rawChild), rows[1].TokenHash);
        Assert.NotEqual(rawChild, rows[1].TokenHash);

        // Cross-check: the root's ReplacedByTokenHash equals the child's TokenHash.
        Assert.Equal(rows[1].TokenHash, rows[0].ReplacedByTokenHash);
    }

    /// <summary>
    /// No column in any <c>refresh_tokens</c> row contains the raw issued token as a
    /// string literal — proves there is no accidental raw-token persistence path.
    /// </summary>
    [Fact]
    public async Task NoColumn_Contains_RawToken_As_Literal()
    {
        await ApplyMigrationsAsync();
        var playerId = await SeedPlayerAsync();
        var sp = BuildProvider();

        string rawToken;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            rawToken = (await svc.IssueRootAsync(playerId, "password", null)).RawRefresh!;
        }

        await using var verifyScope = sp.CreateAsyncScope();
        var ctx = verifyScope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var rows = await ctx.Set<RefreshToken>()
            .Where(r => r.PlayerId == playerId)
            .ToListAsync();

        foreach (var row in rows)
        {
            // TokenHash must NOT equal raw token.
            Assert.NotEqual(rawToken, row.TokenHash);

            // DeviceFingerprint, Provider, ReplacedByTokenHash must also not be the raw token.
            // (These are the only other string columns on the entity; the raw token should appear nowhere.)
            Assert.False(row.DeviceFingerprint == rawToken,
                "DeviceFingerprint must not contain the raw refresh token.");
            Assert.False(row.Provider == rawToken,
                "Provider must not contain the raw refresh token.");
            if (row.ReplacedByTokenHash is not null)
                Assert.NotEqual(rawToken, row.ReplacedByTokenHash);
        }
    }

    // ---- helpers ----

    private static string ComputeSha256Hex(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<Guid> SeedPlayerAsync()
    {
        var sp = BuildProvider();
        var id = Guid.CreateVersion7();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        ctx.Players.Add(new Player
        {
            Id = id,
            DisplayName = $"hash-test-{id:N}".Substring(0, 24),
            CreatedAt = _now,
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());

        // Rewire DbContext to use the runtime Auth query customizer (FOLLOW-UP-02-03-01).
        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
            dbOpts.UseNpgsql(_pg.OwnerConnectionString)
                  .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>());

        var authOpts = new GameKitAuthOptions();
        authOpts.Jwt.Issuer = "gk-test";
        authOpts.Jwt.Audience = "gk-test";
        authOpts.Jwt.PrivateKeyPemPath = _privPath;
        authOpts.Jwt.PublicKeyPemPath = _pubPath;
        authOpts.Jwt.Kid = "test-kid-1";
        authOpts.Jwt.RefreshReuseInterval = TimeSpan.FromSeconds(45);
        authOpts.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(30);
        services.AddSingleton(authOpts);

        services.AddScoped<IIsGuestResolver, IsGuestResolver>();
        services.AddScoped<IJwtIssuer, JwtIssuer>();
        services.AddScoped<IAuthAuditWriter, AuthAuditWriter>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // Mock clock so tests control time deterministically.
        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(() => _now);
        services.Replace(ServiceDescriptor.Singleton<IClock>(clockMock.Object));

        return services.BuildServiceProvider();
    }

    private async Task ApplyMigrationsAsync()
    {
        // Core migration step.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
        await using var coreSp = coreServices.BuildServiceProvider();
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        // Auth migration step.
        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(_pg.OwnerConnectionString, npg =>
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
