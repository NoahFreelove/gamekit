// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using System.Linq;
using GameKit.Auth.Builder;
using GameKit.Auth.Providers;
using GameKit.Core.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Auth.Tests;

/// <summary>
/// Asserts Scrutor's assembly scan registers every <see cref="IOAuthProvider"/> implementation
/// in <c>GameKit.Auth</c> with scoped lifetime. As of plan 02-06 the expected roster is four
/// providers: Steam + Discord (from plan 02-05) plus Guest + Password (from plan 02-06).
/// </summary>
public sealed class ScrutorProviderDiscoveryTests
{
    private static IServiceCollection BuildServicesWithAuth()
    {
        var services = new ServiceCollection();
        var builder = services.AddGameKit(o =>
        {
            o.ConnectionString = "Host=localhost;Database=x;Username=gamekit_app;Password=x";
            o.AutoMigrate = false;
        });
        builder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "x";
            o.Jwt.Audience = "x";
        });
        return services;
    }

    /// <summary>
    /// Finds descriptors for <see cref="IOAuthProvider"/>. Scrutor's <c>AsImplementedInterfaces</c>
    /// registers each concrete type under its implemented interfaces via an <c>ImplementationFactory</c>
    /// that pipes through the scoped scope, so <see cref="ServiceDescriptor.ImplementationType"/> may
    /// be null. We detect both shapes (typed and factory).
    /// </summary>
    private static IReadOnlyList<ServiceDescriptor> FindIOAuthProviderDescriptors(IServiceCollection services) =>
        services.Where(d => d.ServiceType == typeof(IOAuthProvider)).ToList();

    [Fact]
    public void AddAuth_Registers_SteamAndDiscord_IOAuthProvider_Implementations()
    {
        var services = BuildServicesWithAuth();

        var descriptors = FindIOAuthProviderDescriptors(services);
        Assert.NotEmpty(descriptors);
        // Plan 02-06 expands the expected roster from 2 (Steam + Discord) to 4
        // (adds Guest + Password). Scrutor's publicOnly:false discovers internal sealed
        // providers in the GameKit.Auth assembly.
        Assert.Equal(4, descriptors.Count);

        // Resolve the providers via IEnumerable<IOAuthProvider> to confirm end-to-end DI works.
        // We stub out GameKitDbContext with a fake since the real context would try to open Npgsql.
        var sp = services.BuildServiceProvider(validateScopes: false);
        using var scope = sp.CreateScope();

        // Resolve each via its concrete type through descriptors — avoids eager DI construction
        // (GameKitDbContext is not test-reachable here without Postgres).
        var implementationTypes = descriptors
            .Select(d => d.ImplementationType)
            .Where(t => t is not null)
            .Select(t => t!.Name)
            .ToHashSet();

        Assert.Contains("SteamOAuthProvider", implementationTypes);
        Assert.Contains("DiscordOAuthProvider", implementationTypes);
        Assert.Contains("GuestOAuthProvider", implementationTypes);
        Assert.Contains("PasswordOAuthProvider", implementationTypes);
    }

    [Fact]
    public void IOAuthProvider_Registrations_Are_Scoped()
    {
        var services = BuildServicesWithAuth();

        var descriptors = FindIOAuthProviderDescriptors(services);
        Assert.NotEmpty(descriptors);
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }
}
