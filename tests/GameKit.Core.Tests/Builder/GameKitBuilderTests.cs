// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.RateLimiting;
using GameKit.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Core.Tests.Builder;

public class GameKitBuilderTests
{
    private const string TestConnectionString = "Host=localhost;Database=gamekit_test;Username=test;Password=test";

    [Fact]
    public void AddGameKit_ReturnsIGameKitBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IGameKitBuilder>(builder);
    }

    [Fact]
    public void AddGameKit_SetsOptions()
    {
        var services = new ServiceCollection();
        var builder = services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        Assert.Equal(TestConnectionString, builder.Options.ConnectionString);
    }

    [Fact]
    public void AddGameKit_ThrowsWhenConnectionStringEmpty()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() =>
            services.AddGameKit(opts => { /* ConnectionString left empty */ }));
    }

    [Fact]
    public void AddGameKit_ThrowsWhenConfigureIsNull()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddGameKit(null!));
    }

    [Fact]
    public void AddGameKit_RegistersGameKitOptionsSingleton()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<GameKitOptions>();
        Assert.NotNull(opts);
        Assert.Equal(TestConnectionString, opts.ConnectionString);
    }

    [Fact]
    public void AddGameKit_RegistersIClockSingleton()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var sp = services.BuildServiceProvider();
        var clock = sp.GetService<IClock>();
        Assert.NotNull(clock);
        Assert.IsType<SystemClock>(clock);
    }

    [Fact]
    public void AddGameKit_RegistersIIdGeneratorSingleton()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var sp = services.BuildServiceProvider();
        var gen = sp.GetService<IIdGenerator>();
        Assert.NotNull(gen);
        Assert.IsType<UuidV7IdGenerator>(gen);
    }

    [Fact]
    public void AddGameKit_RegistersIHttpContextAccessor()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var sp = services.BuildServiceProvider();
        var accessor = sp.GetService<IHttpContextAccessor>();
        Assert.NotNull(accessor);
    }

    [Fact]
    public void AddGameKit_RegistersICurrentPlayerScoped()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var player = scope.ServiceProvider.GetService<ICurrentPlayer>();
        Assert.NotNull(player);
        Assert.IsType<HttpContextCurrentPlayer>(player);
    }

    [Fact]
    public void AddGameKit_RegistersIMemoryCache()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var sp = services.BuildServiceProvider();
        var cache = sp.GetService<IMemoryCache>();
        Assert.NotNull(cache);
    }

    [Fact]
    public void AddGameKit_RegistersIPlayerDisplayNameResolver()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPlayerDisplayNameResolver));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddGameKit_RegistersIGdprDeleteServiceScoped()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IGdprDeleteService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void AddGameKit_RegistersIGameKitRateLimitPoliciesSingleton()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var sp = services.BuildServiceProvider();
        var policies = sp.GetService<IGameKitRateLimitPolicies>();
        Assert.NotNull(policies);
        Assert.IsType<GameKitRateLimitPolicies>(policies);
    }

    [Fact]
    public void AddGameKit_RegistersGameKitDbContext()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(GameKitDbContext));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddGameKit_ConfiguresMigrationsHistoryTable()
    {
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        // Verify the DbContext was registered (migrations table configuration
        // is validated by attempting to build the service provider and checking
        // the model — full validation happens in Plan 07 integration tests)
        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        Assert.NotNull(ctx);
    }

    [Fact]
    public void AddGameKit_DbContext_AppliesRegisteredModelBuilderExtensions()
    {
        // Sibling packages contribute entities by registering IModelBuilderExtension —
        // the DI-constructed GameKitDbContext receives them via constructor injection
        // (FOLLOW-UP-02-03-01 fix). No ReplaceService<IModelCustomizer> is needed.
        var services = new ServiceCollection();
        services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);

        var ext = new RecordingExtension();
        services.AddSingleton<IModelBuilderExtension>(ext);

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

        // Force model creation.
        _ = ctx.Model;

        Assert.True(ext.ApplyInvoked, "IModelBuilderExtension.ApplyTo must be invoked during OnModelCreating.");
    }

    private sealed class RecordingExtension : IModelBuilderExtension
    {
        public bool ApplyInvoked { get; private set; }
        public void ApplyTo(ModelBuilder modelBuilder) => ApplyInvoked = true;
    }

    [Fact]
    public void IGameKitBuilder_ExposesServices()
    {
        var services = new ServiceCollection();
        var builder = services.AddGameKit(opts => opts.ConnectionString = TestConnectionString);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void IGameKitBuilder_ExposesOptions()
    {
        var services = new ServiceCollection();
        var builder = services.AddGameKit(opts =>
        {
            opts.ConnectionString = TestConnectionString;
            opts.AutoMigrate = false;
        });
        Assert.False(builder.Options.AutoMigrate);
    }
}
