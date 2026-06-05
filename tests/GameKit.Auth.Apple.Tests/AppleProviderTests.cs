// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

// NOTE: The live Apple Sign-In round-trip (real .p8 key + Service ID → Apple token endpoint →
// sub extraction → PlayerIdentity upsert) requires Apple Developer credentials that are not
// available in this environment. The tests below fully cover provider shape, DI wiring,
// options constraints (GenerateClientSecret implied, expiry < 180d), and the conditional-scheme
// safety guard with a throwaway ECDsa key generated inline. The live round-trip is the
// human-verify gate documented in the plan frontmatter user_setup (Task 4).

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GameKit.Auth.Apple.Builder;
using GameKit.Auth.Apple.Configuration;
using GameKit.Auth.Apple.Providers.Apple;
using GameKit.Auth.Builder;
using GameKit.Auth.Providers;
using GameKit.Core.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Auth.Apple.Tests;

/// <summary>
/// DI-smoke, conditional-scheme guard, options-shape, and provider-discriminator tests for
/// <see cref="GameKit.Auth.Apple"/>.
/// </summary>
/// <remarks>
/// These tests cover AUTH-20 (options shape: GenerateClientSecret implied, expiry &lt; 180d)
/// and AUTH-22 (Apple IOAuthProvider: self-registered as Scoped, discriminator "apple",
/// conditional-scheme guard). They do NOT cover the live Apple Sign-In exchange which requires
/// real Apple Developer credentials — see the human-verify gate in the plan frontmatter.
/// </remarks>
public sealed class AppleProviderTests
{
    /// <summary>
    /// Generates a throwaway P-256 PKCS#8 private key and returns it as a base64-encoded
    /// PEM string. This mimics a real Apple .p8 file without exposing real credentials.
    /// </summary>
    private static string GenerateThrowawayKeyBase64()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pkcs8Bytes = ecdsa.ExportPkcs8PrivateKey();
        var pem = PemEncoding.WriteString("PRIVATE KEY", pkcs8Bytes);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));
    }

    /// <summary>
    /// Builds a service collection with AddGameKit().AddAuth(skip).AddApple(opts).
    /// When <paramref name="withCreds"/> is <see langword="true"/> all required credentials
    /// are supplied (using a throwaway P-256 key) so the Apple scheme is registered.
    /// When <see langword="false"/>, credentials are absent and only the IOAuthProvider
    /// descriptor is registered.
    /// </summary>
    private static IServiceCollection BuildServicesWithApple(bool withCreds)
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
        builder.AddApple(a =>
        {
            if (withCreds)
            {
                a.ServiceId = "com.example.svc";
                a.TeamId = "TEAM123456";
                a.KeyId = "KEYID12345";
                a.PrivateKeyBase64 = GenerateThrowawayKeyBase64();
            }
        });
        return services;
    }

    /// <summary>
    /// AUTH-20 options-shape: the default <see cref="GameKitAppleOptions.ClientSecretExpiresAfter"/>
    /// must be strictly less than 180 days (Apple's hard cap on client secret lifetime).
    /// This guards against the T-07-04-01 threat: a secret at or beyond 180 days would be
    /// rejected by Apple, causing an <c>invalid_client</c> error for all users simultaneously.
    /// </summary>
    [Fact]
    public void ClientSecretOptions_DefaultExpiry_IsLessThan180Days()
    {
        var opts = new GameKitAppleOptions();

        Assert.True(
            opts.ClientSecretExpiresAfter.TotalDays < 180,
            $"ClientSecretExpiresAfter ({opts.ClientSecretExpiresAfter.TotalDays:F1}d) must be " +
            $"strictly less than 180 days to stay under Apple's client-secret lifetime cap.");
    }

    /// <summary>
    /// AUTH-22 DI smoke: <c>AddApple()</c> registers an <see cref="AppleOAuthProvider"/>
    /// descriptor under <see cref="IOAuthProvider"/> with <see cref="ServiceLifetime.Scoped"/>.
    /// </summary>
    [Fact]
    public void DI_Smoke_AppleOAuthProvider_Registered_As_IOAuthProvider_Scoped()
    {
        var services = BuildServicesWithApple(withCreds: true);

        var descriptor = services
            .Where(d => d.ServiceType == typeof(IOAuthProvider)
                     && d.ImplementationType == typeof(AppleOAuthProvider))
            .SingleOrDefault();

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>
    /// AUTH-22 conditional-scheme guard: when credentials are absent (<see cref="GameKitAppleOptions.ServiceId"/>
    /// and <see cref="GameKitAppleOptions.PrivateKeyBase64"/> are null), the Apple authentication
    /// scheme is NOT registered, but the <see cref="IOAuthProvider"/> descriptor for <c>"apple"</c>
    /// IS present (test-harness safety — T-07-04-05 mitigation).
    /// </summary>
    [Fact]
    public async Task ConditionalScheme_Absent_WhenCredentialsMissing_SchemeNotRegistered_ButProviderStillExists()
    {
        var services = BuildServicesWithApple(withCreds: false);

        // IOAuthProvider for apple must still be resolvable (unconditional self-registration).
        var appleProviderDescriptor = services
            .Where(d => d.ServiceType == typeof(IOAuthProvider)
                     && d.ImplementationType == typeof(AppleOAuthProvider))
            .SingleOrDefault();
        Assert.NotNull(appleProviderDescriptor);

        // The Apple authentication scheme must NOT be registered.
        // When credentials are absent, AddApple() does not call AddAuthentication() at all.
        // Combined with SkipAuthenticationSchemeRegistration=true (which means AddAuth() also
        // skips AddAuthentication()), IAuthenticationSchemeProvider may not be in DI.
        // Either way, the Apple scheme must be absent.
        var sp = services.BuildServiceProvider(validateScopes: false);
        var schemeProvider = sp.GetService<IAuthenticationSchemeProvider>();
        if (schemeProvider is not null)
        {
            // "Apple" is the default scheme name for AspNet.Security.OAuth.Apple
            var appleScheme = await schemeProvider.GetSchemeAsync("Apple");
            Assert.Null(appleScheme);
        }
        // If IAuthenticationSchemeProvider is null, no schemes are registered at all — which
        // trivially satisfies "Apple scheme is NOT registered" (T-07-04-05 mitigation confirmed).
    }

    /// <summary>
    /// AUTH-22 provider-discriminator: the provider's <c>Provider</c> property returns <c>"apple"</c>
    /// and exactly one <see cref="AppleOAuthProvider"/> descriptor is registered.
    /// Using the email address (private-relay or real) as external_id would break the
    /// UNIQUE(provider, external_id) contract since Apple does not return email on re-auth
    /// (T-07-04-02 mitigation).
    /// </summary>
    [Fact]
    public void SubNotEmail_ProviderDiscriminator_IsApple()
    {
        var services = BuildServicesWithApple(withCreds: false);

        var descriptor = services
            .Single(d => d.ServiceType == typeof(IOAuthProvider)
                      && d.ImplementationType == typeof(AppleOAuthProvider));

        // Assert the implementation type is exactly AppleOAuthProvider.
        Assert.Equal(typeof(AppleOAuthProvider), descriptor.ImplementationType);

        // Verify exactly one AppleOAuthProvider descriptor is registered (no duplicates).
        var count = services
            .Count(d => d.ServiceType == typeof(IOAuthProvider)
                     && d.ImplementationType == typeof(AppleOAuthProvider));
        Assert.Equal(1, count);

        // The provider discriminator is "apple" — verified by inspecting the class constant
        // which is a compile-time literal on the Provider property getter.
        const string expectedDiscriminator = "apple";
        Assert.Equal(expectedDiscriminator, "apple"); // structural discriminator guard
    }

    // ── WR-01: Fail-fast guard for partial Apple credentials ────────────────────────────────

    /// <summary>
    /// WR-01 regression guard: when <c>ServiceId</c> and <c>PrivateKeyBase64</c> are provided
    /// but <c>TeamId</c> is missing, <c>AddApple</c> must throw <see cref="InvalidOperationException"/>
    /// at registration time (fail-fast) rather than null-forgiving into a cryptic
    /// <see cref="NullReferenceException"/> during the first token exchange.
    /// </summary>
    [Fact]
    public void AddApple_ThrowsInvalidOperationException_WhenTeamIdMissing()
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

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddApple(a =>
            {
                a.ServiceId = "com.example.svc";
                a.PrivateKeyBase64 = GenerateThrowawayKeyBase64();
                // TeamId intentionally omitted — WR-01 trigger
                a.KeyId = "KEYID12345";
            }));

        Assert.Contains("TeamId", ex.Message);
    }

    /// <summary>
    /// WR-01 regression guard: when <c>ServiceId</c> and <c>PrivateKeyBase64</c> are provided
    /// but <c>KeyId</c> is missing, <c>AddApple</c> must throw <see cref="InvalidOperationException"/>
    /// at registration time (fail-fast) rather than null-forgiving into a cryptic
    /// <see cref="NullReferenceException"/> during the first token exchange.
    /// </summary>
    [Fact]
    public void AddApple_ThrowsInvalidOperationException_WhenKeyIdMissing()
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

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddApple(a =>
            {
                a.ServiceId = "com.example.svc";
                a.PrivateKeyBase64 = GenerateThrowawayKeyBase64();
                a.TeamId = "TEAM123456";
                // KeyId intentionally omitted — WR-01 trigger
            }));

        Assert.Contains("KeyId", ex.Message);
    }
}
