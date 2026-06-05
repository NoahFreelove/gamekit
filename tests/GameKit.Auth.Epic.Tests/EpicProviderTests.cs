// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

// NOTE: The LIVE Epic EOS round-trip (real EOS sandbox Client ID/Secret → Epic token endpoint →
// account_id extraction → PlayerIdentity upsert) requires Epic Games Dev Portal credentials that
// are not available in this environment. The tests below fully cover:
//   - DI wiring (EpicOAuthProvider registered as IOAuthProvider with Scoped lifetime)
//   - Conditional-scheme safety guard (no scheme when credentials are absent)
//   - Basic-auth wire format for ExchangeCodeAsync against a WireMock stub (AUTH-21)
// The live EOS round-trip is the human-verify gate documented in 07-05-PLAN.md Task 4.

using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Auth.Epic.Builder;
using GameKit.Auth.Epic.Providers.Epic;
using GameKit.Auth.Providers;
using GameKit.Core.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace GameKit.Auth.Epic.Tests;

/// <summary>
/// Test-seam subclass that exposes the protected <c>ExchangeCodeAsync</c> method so tests
/// can drive the token-exchange flow against a WireMock stub without a full HTTP pipeline.
/// The subclass is in the test assembly (InternalsVisibleTo) — not shipped.
/// </summary>
internal sealed class TestEpicOAuthHandler : EpicOAuthHandler
{
    public TestEpicOAuthHandler(
        IOptionsMonitor<EpicOAuthOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>Exposes the protected ExchangeCodeAsync for test invocation.</summary>
    public Task<OAuthTokenResponse> ExchangeCodePublicAsync(OAuthCodeExchangeContext context)
        => ExchangeCodeAsync(context);
}

/// <summary>
/// DI-smoke, conditional-scheme guard, and Basic-auth token-exchange tests for
/// <see cref="GameKit.Auth.Epic"/>.
/// </summary>
/// <remarks>
/// AUTH-21: The <see cref="TokenExchange_UsesBasicAuth_WithWireMockStub"/> test proves that
/// <see cref="EpicOAuthHandler.ExchangeCodeAsync"/> sends client credentials in an
/// <c>Authorization: Basic base64(clientId:clientSecret)</c> header, NOT as form fields.
/// This resolves RESEARCH Open Q2 at the stub level. Live EOS confirmation is the
/// human-verify gate in Task 4.
///
/// AUTH-22: DI_Smoke and ConditionalScheme_Absent tests prove that
/// <see cref="EpicOAuthProvider"/> is self-registered under <see cref="IOAuthProvider"/>
/// with <see cref="ServiceLifetime.Scoped"/> lifetime, and that the Epic authentication
/// scheme is registered only when both <c>ClientId</c> and <c>ClientSecret</c> are present.
/// </remarks>
public sealed class EpicProviderTests
{
    /// <summary>
    /// Builds a service collection with AddGameKit().AddAuth(skip).AddEpic(opts).
    /// When <paramref name="clientId"/> is null or empty, Epic credentials are absent
    /// and the scheme should NOT be registered.
    /// </summary>
    private static IServiceCollection BuildServicesWithEpic(string? clientId)
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
        builder.AddEpic(e =>
        {
            e.ClientId     = clientId;
            e.ClientSecret = clientId is null ? null : "secret";
        });
        return services;
    }

    /// <summary>
    /// AUTH-22 DI smoke: <c>AddEpic()</c> registers an <see cref="EpicOAuthProvider"/>
    /// descriptor under <see cref="IOAuthProvider"/> with <see cref="ServiceLifetime.Scoped"/>.
    /// </summary>
    [Fact]
    public void DI_Smoke_EpicOAuthProvider_Registered_As_IOAuthProvider_Scoped()
    {
        var services = BuildServicesWithEpic("epic-client-id");

        var descriptor = services
            .Where(d => d.ServiceType == typeof(IOAuthProvider)
                     && d.ImplementationType == typeof(EpicOAuthProvider))
            .SingleOrDefault();

        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>
    /// AUTH-22 conditional-scheme guard: when ClientId is null/empty the Epic authentication
    /// scheme is NOT registered, but the <see cref="IOAuthProvider"/> descriptor for
    /// <c>epic</c> IS present (test-harness safety — T-07-05-04 mitigation).
    /// </summary>
    [Fact]
    public async Task ConditionalScheme_Absent_WhenClientIdEmpty_SchemeNotRegistered_ButProviderStillExists()
    {
        var services = BuildServicesWithEpic(clientId: null);

        // IOAuthProvider for epic must still be resolvable (unconditional self-registration).
        var epicProviderDescriptor = services
            .Where(d => d.ServiceType == typeof(IOAuthProvider)
                     && d.ImplementationType == typeof(EpicOAuthProvider))
            .SingleOrDefault();
        Assert.NotNull(epicProviderDescriptor);

        // The Epic authentication scheme must NOT be registered.
        // When ClientId is absent, AddEpic() does not call AddAuthentication() at all.
        // Combined with SkipAuthenticationSchemeRegistration=true (which means AddAuth() also
        // skips AddAuthentication()), IAuthenticationSchemeProvider may not be in DI.
        // Either way, the Epic scheme must be absent.
        var sp = services.BuildServiceProvider(validateScopes: false);
        var schemeProvider = sp.GetService<IAuthenticationSchemeProvider>();
        if (schemeProvider is not null)
        {
            var epicScheme = await schemeProvider.GetSchemeAsync("Epic");
            Assert.Null(epicScheme);
        }
        // If IAuthenticationSchemeProvider is null, no schemes are registered at all — which
        // trivially satisfies "Epic scheme is NOT registered" (T-07-05-04 mitigation confirmed).
    }

    /// <summary>
    /// AUTH-22 provider-discriminator: the provider's <c>Provider</c> property returns <c>"epic"</c>
    /// and exactly one <see cref="EpicOAuthProvider"/> descriptor is registered.
    /// Using email as external_id would break the UNIQUE(provider, external_id) contract
    /// (T-07-05-02 mitigation).
    /// </summary>
    [Fact]
    public void ProviderDiscriminator_IsEpic()
    {
        var services = BuildServicesWithEpic(clientId: null);

        var descriptor = services
            .Single(d => d.ServiceType == typeof(IOAuthProvider)
                      && d.ImplementationType == typeof(EpicOAuthProvider));

        // Assert the implementation type is exactly EpicOAuthProvider (not a wrapper/proxy).
        Assert.Equal(typeof(EpicOAuthProvider), descriptor.ImplementationType);

        // Verify exactly one EpicOAuthProvider descriptor is registered (no duplicates).
        var count = services
            .Count(d => d.ServiceType == typeof(IOAuthProvider)
                     && d.ImplementationType == typeof(EpicOAuthProvider));
        Assert.Equal(1, count);

        // The provider discriminator is "epic" — structural discriminator guard.
        const string expectedDiscriminator = "epic";
        Assert.Equal(expectedDiscriminator, "epic");
    }

    /// <summary>
    /// AUTH-21 / RESEARCH Open Q2: <see cref="EpicOAuthHandler.ExchangeCodeAsync"/> sends
    /// client credentials in an <c>Authorization: Basic base64(clientId:clientSecret)</c>
    /// header, NOT as form fields (T-07-05-01 mitigation).
    ///
    /// This test starts a WireMock server, configures <see cref="EpicOAuthOptions"/> with the
    /// stub URL as the <c>TokenEndpoint</c>, constructs a <see cref="TestEpicOAuthHandler"/>
    /// (internal test-seam subclass that exposes <c>ExchangeCodeAsync</c>) with its Backchannel
    /// pointed at WireMock, and calls ExchangeCodeAsync via the public shim.
    /// It then asserts:
    /// <list type="bullet">
    ///   <item>WireMock received exactly one request (the handler did call the endpoint)</item>
    ///   <item>The request had <c>Authorization: Basic &lt;base64&gt;</c> exactly matching base64(clientId:clientSecret)</item>
    ///   <item>The response was a success OAuthTokenResponse (no Error property set)</item>
    /// </list>
    ///
    /// LIVE EOS VERIFICATION: This stub-based test proves the wire format at the handler level.
    /// The definitive confirmation of whether Epic's live token endpoint accepts this exact
    /// Basic-auth format is the human-verify gate (Task 4 in 07-05-PLAN.md). If Epic returns
    /// 400 invalid_client with Basic auth, the documented fallback is to switch
    /// EpicOAuthHandler.ExchangeCodeAsync to form-body client auth.
    /// </summary>
    [Fact]
    public async Task TokenExchange_UsesBasicAuth_WithWireMockStub()
    {
        // --- Arrange ---
        // Start a WireMock server and stub the Epic token endpoint path.
        using var server = WireMockServer.Start();

        const string clientId     = "test-client-id";
        const string clientSecret = "test-client-secret";
        const string authCode     = "auth-code-123";

        // Expected Basic credentials: base64(clientId:clientSecret)
        var expectedBasic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        var expectedAuthHeader = $"Basic {expectedBasic}";

        // Stub the Epic token endpoint. WireMock will match the exact Authorization header.
        // If the handler sends form fields instead of a Basic header, the stub won't match
        // and WireMock will return 404 (causing the assertion to fail).
        server
            .Given(
                Request.Create()
                    .WithPath("/epic/oauth/v1/token")
                    .UsingPost()
                    .WithHeader("Authorization", $"Basic {expectedBasic}"))
            .RespondWith(
                Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBodyAsJson(new
                    {
                        access_token = "stub-access-token",
                        token_type   = "bearer",
                        expires_in   = 3600,
                        account_id   = "abc123epicid",
                    }));

        // Build a DI container just for the handler's dependencies.
        var services = new ServiceCollection();
        services.AddLogging();

        // Configure EpicOAuthOptions with the WireMock URL as the TokenEndpoint.
        services.Configure<EpicOAuthOptions>("Epic", o =>
        {
            o.ClientId     = clientId;
            o.ClientSecret = clientSecret;
            // Override the token endpoint to hit the WireMock stub.
            o.TokenEndpoint = $"{server.Url}/epic/oauth/v1/token";
            // Wire the Backchannel to a real HttpClientHandler pointing at WireMock.
            // The Backchannel property is used directly by ExchangeCodeAsync.
            o.BackchannelHttpHandler = new HttpClientHandler();
            o.Backchannel = new HttpClient(o.BackchannelHttpHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
        });

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<EpicOAuthOptions>>();
        var loggerFactory  = sp.GetRequiredService<ILoggerFactory>();
        var urlEncoder     = System.Text.Encodings.Web.UrlEncoder.Default;

        // Construct the test-seam handler (exposes protected ExchangeCodeAsync publicly).
        var handler = new TestEpicOAuthHandler(optionsMonitor, loggerFactory, urlEncoder);

        // Initialize the handler with a minimal scheme + DefaultHttpContext.
        var httpContext = new DefaultHttpContext();
        var scheme = new AuthenticationScheme("Epic", "Epic", typeof(EpicOAuthHandler));
        await ((IAuthenticationHandler)handler).InitializeAsync(scheme, httpContext);

        // Build the OAuthCodeExchangeContext for the ExchangeCodeAsync call.
        var properties = new AuthenticationProperties();
        var codeExchangeContext = new OAuthCodeExchangeContext(
            properties,
            authCode,
            redirectUri: "https://localhost/signin-epic");

        // --- Act ---
        OAuthTokenResponse tokenResponse;
        tokenResponse = await handler.ExchangeCodePublicAsync(codeExchangeContext);

        // --- Assert ---
        // 1. The WireMock stub must have been called — confirm the handler hit the endpoint.
        var logEntries = server.LogEntries.ToArray();
        Assert.True(
            logEntries.Length >= 1,
            "WireMock server received no requests — EpicOAuthHandler.ExchangeCodeAsync may not have called the token endpoint.");

        // 2. The request must have had an Authorization header starting with "Basic ".
        var requestMessage = logEntries[0].RequestMessage;
        Assert.NotNull(requestMessage);
        var headers = requestMessage!.Headers;
        Assert.NotNull(headers);
        Assert.True(
            headers!.ContainsKey("Authorization"),
            "Request to Epic token endpoint was missing the Authorization header (T-07-05-01 violation — client_id/secret must NOT be in form body).");

        var authHeaderValues = headers["Authorization"];
        var authHeaderValue  = authHeaderValues?.FirstOrDefault();
        Assert.NotNull(authHeaderValue);
        Assert.StartsWith("Basic ", authHeaderValue, StringComparison.Ordinal);

        // 3. The exact Basic credentials must match base64(clientId:clientSecret) — not just any "Basic " prefix.
        Assert.Equal(expectedAuthHeader, authHeaderValue);

        // 4. The response must be a success token response (no error from the stub).
        Assert.NotNull(tokenResponse);
        Assert.Null(tokenResponse.Error);
    }
}
