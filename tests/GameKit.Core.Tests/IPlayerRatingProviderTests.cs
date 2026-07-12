// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using GameKit.Core.Builder;
using GameKit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Core.Tests;

public sealed class IPlayerRatingProviderTests
{
    [Fact]
    public async System.Threading.Tasks.Task NullPlayerRatingProvider_Returns_EmptyDictionary_For_Any_Players()
    {
        var provider = new NullPlayerRatingProvider();
        var result = await provider.GetRatingsAsync(
            new[] { Guid.NewGuid(), Guid.NewGuid() },
            ladderId: Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public void AddGameKit_Registers_NullPlayerRatingProvider_As_Singleton()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = "Host=localhost;Database=x;Username=gamekit_app;Password=x";
            o.AutoMigrate = false;
        });

        var descriptor = services.Single(d => d.ServiceType == typeof(IPlayerRatingProvider));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(NullPlayerRatingProvider), descriptor.ImplementationType);
    }
}
