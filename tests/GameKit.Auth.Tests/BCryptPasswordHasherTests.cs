// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Reflection;
using GameKit.Auth.Providers.Password;
using GameKit.Auth.Services;
using Xunit;

namespace GameKit.Auth.Tests;

public sealed class BCryptPasswordHasherTests
{
    // ── DummyHash regression guard (CR-01: pre-existing v1 defect surfaced by Phase 7 review) ──

    /// <summary>
    /// <see cref="PasswordOAuthProvider"/> uses a constant <c>DummyHash</c> to equalize
    /// wall-clock timing when a username is not found. The hash MUST be exactly 60 characters
    /// (the BCrypt.Net-Next required length for a valid work-factor-12 hash). A 59-char dummy
    /// causes <c>BCrypt.Verify</c> to throw <see cref="BCrypt.Net.SaltParseException"/>
    /// immediately — before any crypto work — creating a timing oracle (CR-01).
    /// </summary>
    [Fact]
    public void DummyHash_HasCorrectLength_60Chars()
    {
        // Access the private const via reflection so the test remains valid if the field
        // is ever renamed, without depending on PasswordOAuthProvider being public.
        var field = typeof(PasswordOAuthProvider).GetField(
            "DummyHash",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var dummyHash = field!.GetValue(null) as string;
        Assert.NotNull(dummyHash);
        Assert.Equal(60, dummyHash!.Length);
    }

    /// <summary>
    /// <see cref="PasswordOAuthProvider.DummyHash"/> must be a valid BCrypt hash so that
    /// <c>BCrypt.Net.BCrypt.Verify(password, DummyHash)</c> runs the full Blowfish key-setup
    /// (returning <see langword="false"/>) rather than throwing <see cref="BCrypt.Net.SaltParseException"/>
    /// immediately (which would short-circuit the timing equalization — CR-01 regression guard).
    /// </summary>
    [Fact]
    public void DummyHash_Verify_ReturnsFalse_WithoutThrowing()
    {
        var field = typeof(PasswordOAuthProvider).GetField(
            "DummyHash",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var dummyHash = field!.GetValue(null) as string;
        Assert.NotNull(dummyHash);

        // Must return false without throwing — proves the hash is syntactically valid
        // and BCrypt performs the full comparison (not a fast-path SaltParseException).
        var result = BCrypt.Net.BCrypt.Verify("anything-that-should-not-match", dummyHash!);
        Assert.False(result);
    }


    private static BCryptPasswordHasher NewHasher(int workFactor = 4)
    {
        var opts = new GameKitAuthOptions();
        opts.Password.BCryptWorkFactor = workFactor;   // 4 for test speed; production default is 12
        return new BCryptPasswordHasher(opts);
    }

    [Fact]
    public void Hash_Then_Verify_With_Same_Password_Returns_True()
    {
        var h = NewHasher();
        var hash = h.Hash("correct-horse-battery-staple");
        Assert.True(h.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void Verify_With_Wrong_Password_Returns_False()
    {
        var h = NewHasher();
        var hash = h.Hash("correct-horse-battery-staple");
        Assert.False(h.Verify("wrong-password", hash));
    }

    [Fact]
    public void Verify_With_Malformed_Hash_Returns_False_Not_Throws()
    {
        var h = NewHasher();
        Assert.False(h.Verify("anything", "this-is-not-a-bcrypt-hash"));
    }

    [Fact]
    public void Different_Hashes_For_Same_Password()
    {
        var h = NewHasher();
        Assert.NotEqual(h.Hash("x"), h.Hash("x"));   // BCrypt salts are random
    }
}
