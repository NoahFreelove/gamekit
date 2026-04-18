// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Xunit;

namespace GameKit.Auth.Tests;

/// <summary>Smoke test — exists so <c>dotnet test</c> has at least one passing test until plan 02-02 lands real tests.</summary>
[Trait("Category", "Smoke")]
public sealed class SmokeTests
{
    [Fact]
    public void Assembly_Loads()
    {
        // If this test fails, the Directory.Packages.props pins or test-project setup is broken
        // and every downstream Phase-2 plan will fail to compile.
        var asm = typeof(GameKit.Auth.AuthMarker).Assembly;
        Assert.NotNull(asm);
        Assert.Equal("GameKit.Auth", asm.GetName().Name);
    }
}
