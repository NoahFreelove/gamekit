// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Services;
using Xunit;

namespace GameKit.Auth.Tests;

public sealed class BCryptPasswordHasherTests
{
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
