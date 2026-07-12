// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Argon2.Builder;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Auth.Entities;
using GameKit.Auth.Providers;
using GameKit.Auth.Providers.Password;
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
/// Testcontainers Postgres integration tests proving AUTH-18: BCrypt→Argon2 rehash-on-verify.
///
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Rehash case:</b> a player seeded with a BCrypt hash logs in under an
///       Argon2-configured host and ends up with a durable <c>$argon2id$</c> hash.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Control case (no rehash):</b> a player seeded with a BCrypt hash logs in under
///       the default BCrypt-configured host and the stored hash is unchanged.
///     </description>
///   </item>
/// </list>
///
/// Both cases re-read the stored hash from a FRESH <see cref="GameKitDbContext"/> scope
/// to prove that the UPDATE was durable and not merely an in-memory change-tracker artifact
/// (RESEARCH §Pitfall 3 — T-07-06-01 mitigation).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class ArgonRehashOnVerifyTests
{
    private readonly PostgresFixture _pg;

    /// <summary>xUnit-injected Testcontainers Postgres fixture.</summary>
    public ArgonRehashOnVerifyTests(PostgresFixture pg) => _pg = pg;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a DI provider with <c>AddGameKit().AddAuth().UseArgon2(fast params)</c>.
    /// Low <c>TimeCost=1</c> / <c>MemoryCost=1024</c> keep test latency manageable.
    /// </summary>
    private static TestContext BuildArgon2Provider(string connectionString)
    {
        var keyDir = Path.Combine(Path.GetTempPath(), $"gk-argon2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDir);
        var privPath = Path.Combine(keyDir, "priv.pem");
        var pubPath  = Path.Combine(keyDir, "pub.pem");
        using (var rsa = RSA.Create(2048))
        {
            File.WriteAllText(privPath, rsa.ExportRSAPrivateKeyPem());
            File.WriteAllText(pubPath,  rsa.ExportRSAPublicKeyPem());
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
            o.Jwt.Issuer   = "gk-test";
            o.Jwt.Audience = "gk-test";
            o.Jwt.PrivateKeyPemPath  = privPath;
            o.Jwt.PublicKeyPemPath   = pubPath;
            o.Jwt.Kid                = "test-kid-1";
            o.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(30);
        });

        // UseArgon2 with test-safe parameters so the hash completes quickly.
        // AllowInsecureParametersForTesting bypasses the OWASP minimum-parameter guards;
        // low values are intentional here to keep integration test latency manageable.
        gkBuilder.UseArgon2(o =>
        {
            o.TimeCost   = 1;
            o.MemoryCost = 1024;
            o.AllowInsecureParametersForTesting = true;
        });

        // FOLLOW-UP-02-03-01 runtime query customizer — same workaround as TestHelpers.BuildProvider.
        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
            dbOpts.UseNpgsql(connectionString)
                  .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>());

        var sp = services.BuildServiceProvider();
        return new TestContext(sp, keyDir);
    }

    /// <summary>
    /// Seeds a <see cref="PlayerCredential"/> row with a BCrypt-format hash directly so the
    /// test can control which hasher algorithm produced the stored credential — bypassing the
    /// registration path (which would use whatever hasher is active in DI).
    /// </summary>
    private static async Task SeedBcryptCredential(
        IServiceProvider sp,
        Guid playerId,
        string username,
        string bcryptHash,
        string connectionString)
    {
        // Seed the Player and PlayerCredential rows using a raw DbContext.
        // We reuse the existing scoped DbContext from DI (which has the AuthRuntimeQueryCustomizer)
        // to write through EF so the entities land in the same schema that migrations created.
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

        ctx.Players.Add(new GameKit.Core.Entities.Player
        {
            Id          = playerId,
            DisplayName = username,
            CreatedAt   = DateTimeOffset.UtcNow,
        });
        ctx.Set<PlayerCredential>().Add(new PlayerCredential
        {
            PlayerId     = playerId,
            Username     = username,
            PasswordHash = bcryptHash,
            UpdatedAt    = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Opens a fresh <see cref="GameKitDbContext"/> to re-read a stored hash.</summary>
    private static async Task<string> ReadStoredHash(string connectionString, Guid playerId)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(connectionString)
            .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>()
            .Options;
        await using var freshCtx = new GameKitDbContext(opts);
        var cred = await freshCtx.Set<PlayerCredential>()
            .AsNoTracking()
            .FirstAsync(c => c.PlayerId == playerId);
        return cred.PasswordHash;
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rehash case (AUTH-18 core test): a player whose credential was seeded with a BCrypt hash
    /// logs in under a host configured with <c>UseArgon2()</c>. After login the stored hash must
    /// start with <c>$argon2id$</c> (proven by re-reading from a fresh DbContext scope).
    /// </summary>
    [Fact]
    public async Task Argon2_Host_Migrates_BcryptHash_To_Argon2id_On_Login()
    {
        const string password = "correct-horse-battery-argon2";
        var playerId = Guid.NewGuid();
        var username = $"argon2user-{playerId:N}"[..20];

        // Produce a real BCrypt hash (work factor 4 for test speed) to seed the credential.
        var bcryptHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4);
        Assert.StartsWith("$2a$", bcryptHash, StringComparison.Ordinal);

        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = BuildArgon2Provider(_pg.OwnerConnectionString);

        // Arrange: seed with BCrypt hash.
        await SeedBcryptCredential(tc.Services, playerId, username, bcryptHash, _pg.OwnerConnectionString);

        // Act: login via PasswordOAuthProvider under the Argon2-configured host.
        await using (var scope = tc.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "password");
            var result = await provider.CompleteLoginAsync(username, password, null, null);
            Assert.True(result.Success, $"Login should succeed; ErrorCode={result.ErrorCode}");
        }

        // Assert: re-read from a FRESH DbContext scope to prove durability.
        var storedHash = await ReadStoredHash(_pg.OwnerConnectionString, playerId);
        Assert.StartsWith("$argon2id$", storedHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// Control case (AUTH-18 no-rehash): a player whose credential was seeded with a BCrypt hash
    /// logs in under the default <c>BCryptPasswordHasher</c> host (no <c>UseArgon2()</c>).
    /// <c>BCryptPasswordHasher.NeedsRehash</c> always returns <c>false</c>, so the stored hash
    /// must be byte-identical to the originally-seeded BCrypt value after login.
    /// </summary>
    [Fact]
    public async Task Bcrypt_Host_Does_Not_Rehash_BcryptHash_On_Login()
    {
        const string password = "correct-horse-battery-bcrypt";
        var playerId = Guid.NewGuid();
        var username = $"bcryptctrl-{playerId:N}"[..20];

        // Produce a real BCrypt hash (work factor 4 for test speed) to seed the credential.
        var bcryptHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4);
        Assert.StartsWith("$2a$", bcryptHash, StringComparison.Ordinal);

        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        // Default BCrypt provider — no UseArgon2().
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        // Arrange: seed with BCrypt hash.
        await SeedBcryptCredential(tc.Services, playerId, username, bcryptHash, _pg.OwnerConnectionString);

        // Act: login via PasswordOAuthProvider under the default BCrypt host.
        await using (var scope = tc.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "password");
            var result = await provider.CompleteLoginAsync(username, password, null, null);
            Assert.True(result.Success, $"Login should succeed; ErrorCode={result.ErrorCode}");
        }

        // Assert: stored hash must be unchanged ($2a$ prefix, identical to seeded value).
        var storedHash = await ReadStoredHash(_pg.OwnerConnectionString, playerId);
        Assert.Equal(bcryptHash, storedHash);
    }
}
