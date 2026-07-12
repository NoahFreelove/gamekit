// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Services;
using Xunit;

namespace GameKit.Auth.Tests;

public sealed class ExternalIdHasherTests
{
    private readonly ExternalIdHasher _h = new();

    [Fact]
    public void Hash_Is_Deterministic()
    {
        Assert.Equal(_h.Hash("steam", "76561198000000001"), _h.Hash("steam", "76561198000000001"));
    }

    [Fact]
    public void Hash_Differs_By_Provider()
    {
        Assert.NotEqual(_h.Hash("steam", "x"), _h.Hash("discord", "x"));
    }

    [Fact]
    public void Hash_Differs_By_ExternalId()
    {
        Assert.NotEqual(_h.Hash("steam", "a"), _h.Hash("steam", "b"));
    }

    [Fact]
    public void Hash_Is_64_Char_Hex_Lowercase()
    {
        var hash = _h.Hash("steam", "x");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}
