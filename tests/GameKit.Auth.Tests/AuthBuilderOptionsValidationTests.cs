// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using GameKit.Auth.Builder;
using GameKit.Auth.Egress;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Auth.Tests;

public sealed class AuthBuilderOptionsValidationTests
{
    private static IGameKitBuilder NewBuilder()
    {
        var services = new ServiceCollection();
        return services.AddGameKit(o =>
        {
            o.ConnectionString = "Host=localhost;Database=gamekit_test;Username=gamekit_app;Password=gamekit_app_dev";
            o.AutoMigrate = false;
        });
    }

    [Fact]
    public void AddAuth_Missing_Issuer_Throws()
    {
        var builder = NewBuilder();
        var ex = Assert.Throws<ArgumentException>(() => builder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Audience = "x";
        }));
        Assert.Contains("Issuer", ex.Message);
    }

    [Fact]
    public void AddAuth_Missing_Audience_Throws()
    {
        var builder = NewBuilder();
        var ex = Assert.Throws<ArgumentException>(() => builder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "x";
        }));
        Assert.Contains("Audience", ex.Message);
    }

    [Fact]
    public void AddAuth_Missing_PrivateKey_Throws_When_Scheme_Registration_Not_Skipped()
    {
        var builder = NewBuilder();
        var ex = Assert.Throws<ArgumentException>(() => builder.AddAuth(o =>
        {
            // scheme registration default = not skipped
            o.Jwt.Issuer = "x";
            o.Jwt.Audience = "x";
            o.Jwt.PrivateKeyPemPath = "/nonexistent/path/key.pem";
            o.Jwt.PublicKeyPemPath = "/nonexistent/path/key.pub.pem";
        }));
        Assert.Contains("PrivateKeyPemPath", ex.Message);
    }

    [Fact]
    public void AddAuth_Cleared_AllowedHosts_Throws()
    {
        var builder = NewBuilder();
        var ex = Assert.Throws<ArgumentException>(() => builder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "x";
            o.Jwt.Audience = "x";
            o.AllowedProviderHosts.Clear();
        }));
        Assert.Contains("AllowedProviderHosts", ex.Message);
    }

    [Fact]
    public void AddAuth_Happy_Path_With_Skip_Registers_Options_And_HttpClients()
    {
        var builder = NewBuilder();
        builder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "gamekit-test";
            o.Jwt.Audience = "gamekit-test";
        });

        var sp = builder.Services.BuildServiceProvider();

        // GameKitAuthOptions registered as singleton.
        var opts = sp.GetRequiredService<GameKitAuthOptions>();
        Assert.NotNull(opts);

        // EgressAllowListHandler resolvable (transient).
        var handler = sp.GetRequiredService<EgressAllowListHandler>();
        Assert.NotNull(handler);

        // Default allow-list populated from DefaultAllowedHosts.
        Assert.Contains("steamcommunity.com", opts.AllowedProviderHosts);
        Assert.Contains("discord.com", opts.AllowedProviderHosts);

        // Named HttpClients registered.
        var factory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
        using var steam = factory.CreateClient("gamekit.auth.provider.steam");
        using var discord = factory.CreateClient("gamekit.auth.provider.discord");
        Assert.NotNull(steam);
        Assert.NotNull(discord);

        // AuthModelBuilderExtension registered via TryAddEnumerable.
        var descriptor = System.Linq.Enumerable.FirstOrDefault(
            (System.Collections.Generic.IEnumerable<ServiceDescriptor>)builder.Services,
            d => d.ServiceType == typeof(IModelBuilderExtension)
                 && d.ImplementationType?.Name == "AuthModelBuilderExtension");
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact]
    public void AddAuth_Happy_Path_With_Real_Keys_Does_Not_Throw()
    {
        // Create a throwaway RSA PEM pair so the validator can pass.
        var dir = Path.Combine(Path.GetTempPath(), $"gamekit-auth-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var privPath = Path.Combine(dir, "key.pem");
        var pubPath = Path.Combine(dir, "key.pub.pem");
        using (var rsa = System.Security.Cryptography.RSA.Create(2048))
        {
            File.WriteAllText(privPath, rsa.ExportRSAPrivateKeyPem());
            File.WriteAllText(pubPath, rsa.ExportRSAPublicKeyPem());
        }

        try
        {
            var builder = NewBuilder();
            builder.AddAuth(o =>
            {
                o.Jwt.Issuer = "x";
                o.Jwt.Audience = "x";
                o.Jwt.PrivateKeyPemPath = privPath;
                o.Jwt.PublicKeyPemPath = pubPath;
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
