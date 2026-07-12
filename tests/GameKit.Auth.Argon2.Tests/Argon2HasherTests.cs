// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Argon2.Builder;
using GameKit.Auth.Argon2.Configuration;
using GameKit.Auth.Argon2.Services;
using GameKit.Auth.Builder;
using GameKit.Auth.Services;
using GameKit.Core.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GameKit.Auth.Argon2.Tests;

/// <summary>
/// Unit tests for <see cref="Argon2idPasswordHasher"/>.
///
/// Wave 0 purpose: these tests prove the Isopoh <c>Argon2.Verify(hash, password)</c>
/// argument order (encoded hash is the FIRST argument — RESEARCH open question A3).
/// All tests use low-cost parameters (m=1024/t=1/p=1) for speed.
/// </summary>
public sealed class Argon2HasherTests
{
    // Low-cost parameters for test speed. Production defaults are m=65536/t=3/p=1.
    private static Argon2idPasswordHasher NewHasher()
        => new Argon2idPasswordHasher(new GameKitArgon2Options
        {
            MemoryCost = 1024,
            TimeCost   = 1,
            Lanes      = 1,
            Threads    = 1,
            HashLength = 32,
        });

    // ── Hash prefix ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Hash_Returns_Argon2id_Prefix()
    {
        var h    = NewHasher();
        var hash = h.Hash("test-password");
        Assert.StartsWith("$argon2id$", hash);
    }

    // ── Round-trip (proves Isopoh Argon2.Verify argument order — RESEARCH A3) ─────────

    [Fact]
    public void Hash_Then_Verify_With_Same_Password_Returns_True()
    {
        var h    = NewHasher();
        var hash = h.Hash("correct-horse-battery-staple");
        // If arg order were wrong (password first), this would always return false.
        Assert.True(h.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void Verify_With_Wrong_Password_Returns_False()
    {
        var h    = NewHasher();
        var hash = h.Hash("correct-horse-battery-staple");
        Assert.False(h.Verify("wrong-password", hash));
    }

    // ── Salt randomness ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Different_Hashes_For_Same_Password()
    {
        var h = NewHasher();
        // Argon2 incorporates a random salt; two hashes of the same password must differ.
        Assert.NotEqual(h.Hash("x"), h.Hash("x"));
    }

    // ── Malformed input ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_With_Malformed_Hash_Returns_False_Not_Throws()
    {
        var h = NewHasher();
        Assert.False(h.Verify("anything", "this-is-not-any-valid-hash"));
    }

    // ── NeedsRehash discriminator ───────────────────────────────────────────────────────

    [Fact]
    public void NeedsRehash_BcryptHash_2a_Prefix_ReturnsTrue()
    {
        var h = NewHasher();
        // Syntactically valid BCrypt $2a$ format (work factor 12, truncated salt for brevity).
        Assert.True(h.NeedsRehash("$2a$12$somehashabcdefghijklmn"));
    }

    [Fact]
    public void NeedsRehash_BcryptHash_2b_Prefix_ReturnsTrue()
    {
        var h = NewHasher();
        Assert.True(h.NeedsRehash("$2b$12$somehashabcdefghijklmn"));
    }

    [Fact]
    public void NeedsRehash_Argon2idHash_ReturnsFalse()
    {
        var h    = NewHasher();
        var hash = h.Hash("x");
        // Our own Argon2id output must NOT trigger a rehash.
        Assert.False(h.NeedsRehash(hash));
    }

    // ── OWASP param floor guard (threat T-07-02-01) ─────────────────────────────────────

    [Fact]
    public void DefaultOptions_MeetOwasp2025Minimums()
    {
        var defaults = new GameKitArgon2Options();
        Assert.True(defaults.MemoryCost >= 19456,
            $"MemoryCost {defaults.MemoryCost} KiB is below OWASP 2025 minimum of 19456 KiB.");
        Assert.True(defaults.TimeCost >= 2,
            $"TimeCost {defaults.TimeCost} is below OWASP 2025 minimum of 2.");
    }

    // ── Legacy BCrypt compatibility (live migration path — AUTH-18) ─────────────────────

    [Fact]
    public void Verify_BcryptHash_CorrectPassword_ReturnsTrue()
    {
        // Generate a real BCrypt hash at work factor 4 (minimal cost for test speed).
        var bcryptHash = BCrypt.Net.BCrypt.HashPassword("migration-test-pw", 4);

        var h = NewHasher();
        // Argon2idPasswordHasher must be able to verify the BCrypt hash so that
        // users migrated from BCrypt can still log in before their hash is re-hashed.
        Assert.True(h.Verify("migration-test-pw", bcryptHash));
    }

    [Fact]
    public void Verify_BcryptHash_WrongPassword_ReturnsFalse()
    {
        var bcryptHash = BCrypt.Net.BCrypt.HashPassword("correct-pw", 4);

        var h = NewHasher();
        Assert.False(h.Verify("wrong-pw", bcryptHash));
    }

    // ── IPasswordHasher contract ────────────────────────────────────────────────────────

    [Fact]
    public void Implements_IPasswordHasher()
    {
        var h = NewHasher();
        Assert.IsAssignableFrom<IPasswordHasher>(h);
    }

    // ── WR-02: OWASP floor enforcement at UseArgon2() registration time ─────────────────

    /// <summary>
    /// Helper to build a minimal service collection up to the point of calling UseArgon2.
    /// </summary>
    private static IGameKitBuilder BuildBaseBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGameKit(o =>
        {
            o.ConnectionString = "Host=localhost;Database=x;Username=gamekit_app;Password=x";
            o.AutoMigrate = false;
        });
        builder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "x";
            o.Jwt.Audience = "x";
        });
        return builder;
    }

    /// <summary>
    /// WR-02: UseArgon2 must throw <see cref="ArgumentOutOfRangeException"/> when
    /// <c>MemoryCost</c> is below the OWASP 2025 minimum of 19456 KiB. Isopoh has no
    /// floor of its own — without this guard, silently under-protected hashes would be produced.
    /// </summary>
    [Fact]
    public void UseArgon2_ThrowsArgumentOutOfRangeException_WhenMemoryCostBelowOwaspMinimum()
    {
        var builder = BuildBaseBuilder();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.UseArgon2(o =>
            {
                o.MemoryCost = 1024;   // far below 19456 KiB OWASP minimum
                o.TimeCost   = 3;
                o.Lanes      = 1;
            }));

        Assert.Contains("MemoryCost", ex.Message);
        Assert.Contains("19456", ex.Message);
    }

    /// <summary>
    /// WR-02: UseArgon2 must throw <see cref="ArgumentOutOfRangeException"/> when
    /// <c>TimeCost</c> is below the OWASP 2025 minimum of 2 iterations.
    /// </summary>
    [Fact]
    public void UseArgon2_ThrowsArgumentOutOfRangeException_WhenTimeCostBelowOwaspMinimum()
    {
        var builder = BuildBaseBuilder();

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.UseArgon2(o =>
            {
                o.MemoryCost = 65536;
                o.TimeCost   = 1;   // below 2-iteration minimum
                o.Lanes      = 1;
            }));

        Assert.Contains("TimeCost", ex.Message);
    }

    /// <summary>
    /// WR-02: UseArgon2 with default options (all meeting OWASP floors) must NOT throw.
    /// </summary>
    [Fact]
    public void UseArgon2_DefaultOptions_DoNotThrow()
    {
        var builder = BuildBaseBuilder();
        // Must not throw — default options meet OWASP minimums.
        builder.UseArgon2();
    }

    // ── WR-01: AllowInsecureParametersForTesting environment guard ──────────────────────

    /// <summary>
    /// WR-01: <see cref="Argon2InsecureParamGuardHostedService"/> must throw
    /// <see cref="InvalidOperationException"/> at startup when
    /// <see cref="GameKitArgon2Options.AllowInsecureParametersForTesting"/> is <see langword="true"/>
    /// and the host environment is not Development.
    /// </summary>
    [Fact]
    public async Task Argon2InsecureParamGuard_ThrowsInvalidOperationException_WhenFlagSetOutsideDevelopment()
    {
        var opts = new GameKitArgon2Options { AllowInsecureParametersForTesting = true };
        var env = new StubHostEnvironment("Production");
        var svc = new Argon2InsecureParamGuardHostedService(opts, env);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.StartAsync(CancellationToken.None));

        Assert.Contains("AllowInsecureParametersForTesting", ex.Message);
        Assert.Contains("Development", ex.Message);
    }

    /// <summary>
    /// WR-01: <see cref="Argon2InsecureParamGuardHostedService"/> must NOT throw when
    /// <see cref="GameKitArgon2Options.AllowInsecureParametersForTesting"/> is <see langword="true"/>
    /// and the host environment IS Development (the flag's intended usage).
    /// </summary>
    [Fact]
    public async Task Argon2InsecureParamGuard_DoesNotThrow_WhenFlagSetInDevelopment()
    {
        var opts = new GameKitArgon2Options { AllowInsecureParametersForTesting = true };
        var env = new StubHostEnvironment("Development");
        var svc = new Argon2InsecureParamGuardHostedService(opts, env);

        // Must not throw — flag is permitted in Development.
        await svc.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// WR-01: <see cref="Argon2InsecureParamGuardHostedService"/> must NOT throw when the flag is
    /// <see langword="false"/> regardless of environment.
    /// </summary>
    [Fact]
    public async Task Argon2InsecureParamGuard_DoesNotThrow_WhenFlagNotSet_AnyEnvironment()
    {
        var opts = new GameKitArgon2Options { AllowInsecureParametersForTesting = false };
        var env = new StubHostEnvironment("Production");
        var svc = new Argon2InsecureParamGuardHostedService(opts, env);

        // Must not throw — the flag is off, no guard needed.
        await svc.StartAsync(CancellationToken.None);
    }

    /// <summary>Minimal <see cref="IHostEnvironment"/> implementation for WR-01 unit tests.</summary>
    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName)
            => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "TestApp";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
