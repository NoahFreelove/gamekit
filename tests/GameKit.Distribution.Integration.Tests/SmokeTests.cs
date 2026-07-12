// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Reflection;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// Wave 0 smoke test (Phase 6, Plan 06-03 Task 3): proves the Distribution
/// integration-test project loads and that ALL 7 GameKit src assemblies are
/// resolvable. This is a Wave-0 sentinel — if a future ProjectReference is
/// dropped from this csproj by mistake, this test fails loudly before
/// OPS-04 / DIST-02 / OPS-06 reach for the missing assembly.
/// </summary>
public sealed class SmokeTests
{
    private static readonly string[] AllGameKitPackages =
    {
        "GameKit.Core",
        "GameKit.Auth",
        "GameKit.Rankings",
        "GameKit.Matchmaking",
        "GameKit.Admin.UI",
        "GameKit.Presence",
        "GameKit.OpenApi",
    };

    /// <summary>
    /// Asserts every GameKit src package (the 7 of D-22) is resolvable from
    /// the Distribution test project. A regression of this test indicates a
    /// missing ProjectReference in GameKit.Distribution.Integration.Tests.csproj.
    /// </summary>
    [Fact]
    public void TestProject_Loads_AllSevenGameKitPackages()
    {
        foreach (var name in AllGameKitPackages)
        {
            var asm = Assembly.Load(name);
            Assert.NotNull(asm);
            Assert.Equal(name, asm.GetName().Name);
        }
    }
}
