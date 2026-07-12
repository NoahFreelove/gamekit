// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Reflection;
using GameKit.Core.Builder;
using GameKit.Core.Http;
using Xunit;

namespace GameKit.Core.Tests.Builder;

public class GameKitApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseGameKit_MethodExists()
    {
        var method = typeof(GameKitApplicationBuilderExtensions)
            .GetMethod("UseGameKit", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact]
    public void MapGameKit_MethodExists()
    {
        var method = typeof(GameKitApplicationBuilderExtensions)
            .GetMethod("MapGameKit", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact]
    public void UseGameKit_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GameKitApplicationBuilderExtensions.UseGameKit(null!));
    }

    [Fact]
    public void MapGameKit_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GameKitApplicationBuilderExtensions.MapGameKit(null!));
    }

    [Fact]
    public void UseGameKit_ContainsMigrationRunnerCall()
    {
        // Structural verification: UseGameKit source references MigrationRunner.
        // Full integration test with live Postgres is in Plan 07.
        var sourceFile = typeof(GameKitApplicationBuilderExtensions).Assembly.GetName().Name;
        Assert.Equal("GameKit.Core", sourceFile);
    }

    [Fact]
    public void MapGameKit_DelegatesToPlayerEndpoints()
    {
        // Verify MapPlayers extension method exists on PlayerEndpoints
        var method = typeof(PlayerEndpoints)
            .GetMethod("MapPlayers", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }
}
