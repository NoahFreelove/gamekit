// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore.Design;
using Xunit;

namespace GameKit.Core.Tests.Data;

public class CoreDesignTimeFactoryTests
{
    // Tests that exercise CreateDbContext must set GAMEKIT_MIGRATIONS_CONNECTION (WR-13:
    // factories no longer fall back to a hardcoded dev password).
    private const string TestConnectionString =
        "Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=test_only_not_real";

    private static IDisposable WithMigrationsConnection()
    {
        var prior = Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION");
        Environment.SetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION", TestConnectionString);
        return new EnvVarReset("GAMEKIT_MIGRATIONS_CONNECTION", prior);
    }

    private sealed class EnvVarReset : IDisposable
    {
        private readonly string _name;
        private readonly string? _prior;
        public EnvVarReset(string name, string? prior) { _name = name; _prior = prior; }
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _prior);
    }

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
        using var _ = WithMigrationsConnection();
        var factory = new CoreDesignTimeFactory();
        using var ctx = factory.CreateDbContext(Array.Empty<string>());
        Assert.NotNull(ctx);
    }

    [Fact]
    public void CreateDbContext_ContextIsGameKitDbContext()
    {
        using var _ = WithMigrationsConnection();
        var factory = new CoreDesignTimeFactory();
        using var ctx = factory.CreateDbContext(Array.Empty<string>());
        Assert.IsType<GameKitDbContext>(ctx);
    }

    [Fact]
    public void CreateDbContext_Throws_When_EnvVar_Missing()
    {
        // Verify WR-13 behavior: no fallback, clear error.
        var prior = Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION");
        Environment.SetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION", null);
        try
        {
            var factory = new CoreDesignTimeFactory();
            var ex = Assert.Throws<InvalidOperationException>(
                () => factory.CreateDbContext(Array.Empty<string>()));
            Assert.Contains("GAMEKIT_MIGRATIONS_CONNECTION", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION", prior);
        }
    }
}
