// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore.Design;
using Xunit;

namespace GameKit.Core.Tests.Data;

public class CoreDesignTimeFactoryTests
{
    [Fact]
    public void Factory_IsSealedClass()
    {
        Assert.True(typeof(CoreDesignTimeFactory).IsSealed);
    }

    [Fact]
    public void Factory_ImplementsIDesignTimeDbContextFactory()
    {
        Assert.True(typeof(IDesignTimeDbContextFactory<GameKitDbContext>).IsAssignableFrom(typeof(CoreDesignTimeFactory)));
    }

    [Fact]
    public void CreateDbContext_ReturnsNonNullContext()
    {
        var factory = new CoreDesignTimeFactory();
        using var ctx = factory.CreateDbContext(System.Array.Empty<string>());
        Assert.NotNull(ctx);
    }

    [Fact]
    public void CreateDbContext_ContextIsGameKitDbContext()
    {
        var factory = new CoreDesignTimeFactory();
        using var ctx = factory.CreateDbContext(System.Array.Empty<string>());
        Assert.IsType<GameKitDbContext>(ctx);
    }
}
