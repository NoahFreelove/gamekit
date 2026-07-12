// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace GameKit.Auth.Tests;

/// <summary>
/// SEC-03 — Rate-limit enumeration audit: the three public auth write endpoints
/// (<c>POST /auth/login/{provider}</c>, <c>POST /auth/refresh</c>, <c>POST /auth/register</c>)
/// must carry <see cref="EnableRateLimitingAttribute"/> in their endpoint metadata. This asserts
/// that <see cref="GameKit.Auth.Http.RateLimiting.AuthRateLimitRegistrations"/> policies are
/// wired to each endpoint via <c>RequireRateLimiting()</c>, which places an
/// <see cref="EnableRateLimitingAttribute"/> on the endpoint.
///
/// <para>
/// The test is a forward-protection gate: if a future refactor removes <c>RequireRateLimiting</c>
/// from any write endpoint, the test fails CI before the regression ships.
/// </para>
/// <para>
/// <c>POST /auth/logout</c> is asserted to have NO <see cref="EnableRateLimitingAttribute"/>.
/// Logout is intentionally unguarded — requiring a valid rate-limit bucket for logout would
/// prevent token revocation when the rate-limit window is exhausted, leaving refresh families
/// un-revoked (RFC 7009 design; see comment in <c>AuthEndpoints.cs</c> on the logout handler).
/// </para>
/// <para>
/// The test runs in the fast unit-test suite: it uses a minimal
/// <c>WebApplicationBuilder</c> with <see cref="GameKitAuthOptions.SkipAuthenticationSchemeRegistration"/>
/// = <c>true</c> so no PEM files (beyond throwaway ephemeral ones), containers, or real network
/// connections are needed.
/// </para>
/// </summary>
public sealed class AuthRateLimitAuditTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly string _tempDir;

    /// <summary>
    /// Bootstraps a minimal ASP.NET Core application that registers the auth services and maps
    /// the auth endpoints. <see cref="GameKitAuthOptions.SkipAuthenticationSchemeRegistration"/>
    /// skips RSA-key loading so the test runs without PEM files or a Postgres connection.
    ///
    /// <para>
    /// Ephemeral RSA PEM files are still written to a temp directory because
    /// <see cref="GameKitAuthOptions"/> validation checks that the paths are non-empty even when
    /// <see cref="GameKitAuthOptions.SkipAuthenticationSchemeRegistration"/> is <c>true</c>.
    /// The files are never read at runtime.
    /// </para>
    /// </summary>
    public AuthRateLimitAuditTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gk-ratelimit-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var privPath = Path.Combine(_tempDir, "priv.pem");
        var pubPath  = Path.Combine(_tempDir, "pub.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(privPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(pubPath,  rsa.ExportRSAPublicKeyPem());

        var builder = WebApplication.CreateBuilder();
        // No UseTestServer — we only need service-provider access for endpoint metadata
        // inspection, not a running HTTP server.

        var gameKitBuilder = builder.Services.AddGameKit(o =>
        {
            o.ConnectionString = "Host=localhost;Database=gamekit_test;Username=gamekit_app;Password=unused_in_unit_test";
            o.AutoMigrate = false;
        });

        gameKitBuilder.AddAuth(o =>
        {
            // SkipAuthenticationSchemeRegistration=true → no JwtBearer scheme setup, no RSA
            // key loading at startup. Auth services (validators, rate-limit registrations,
            // IRefreshTokenService, etc.) are still registered as normal.
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer   = "gk-ratelimit-test";
            o.Jwt.Audience = "gk-ratelimit-test";
            o.Jwt.PrivateKeyPemPath = privPath;
            o.Jwt.PublicKeyPemPath  = pubPath;
            o.Jwt.Kid = "test-kid-ratelimit";
        });

        _app = builder.Build();

        // Register auth endpoints — MapAuth resolves IGameKitRateLimitPolicies and calls
        // AuthEndpoints.MapAuthEndpoints, which applies RequireRateLimiting() per-endpoint.
        // RequireRateLimiting places an EnableRateLimitingAttribute on the endpoint metadata,
        // which is what our assertions check.
        _app.MapAuth();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync().ConfigureAwait(false);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    /// <summary>
    /// Returns all <see cref="RouteEndpoint"/>s registered under the <c>auth/</c> prefix.
    ///
    /// <para>
    /// Uses <see cref="IEndpointRouteBuilder.DataSources"/> on the <see cref="WebApplication"/>
    /// directly rather than <c>IServiceProvider.GetRequiredService&lt;EndpointDataSource&gt;()</c>.
    /// The DI-registered composite data source reflects the host's routing middleware and may be
    /// empty before <c>StartAsync</c>; the <c>IEndpointRouteBuilder.DataSources</c> collection
    /// is populated immediately when <c>MapXxx</c> / <c>MapGroup</c> calls are made.
    /// </para>
    /// </summary>
    private RouteEndpoint[] GetAuthEndpoints()
    {
        // _app implements IEndpointRouteBuilder; its DataSources collection reflects all
        // MapXxx calls made AFTER Build() — including the MapGroup("/auth") added by MapAuth().
        // NOTE: Route patterns from MapGroup("/auth") include a leading slash: "/auth/login/{...}".
        //       The DI-registered CompositeEndpointDataSource is empty until StartAsync runs,
        //       so we use the IEndpointRouteBuilder.DataSources directly.
        var routeBuilder = (IEndpointRouteBuilder)_app;
        return routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e =>
            {
                var raw = (e.RoutePattern.RawText ?? string.Empty).TrimStart('/');
                return raw.StartsWith("auth/", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
    }

    /// <summary>
    /// Returns the first auth endpoint whose raw pattern (normalized, no leading slash) starts
    /// with <paramref name="rawPrefix"/>.
    /// </summary>
    private RouteEndpoint? FindEndpoint(string rawPrefix) =>
        GetAuthEndpoints().FirstOrDefault(e =>
            (e.RoutePattern.RawText ?? string.Empty)
                .TrimStart('/')
                .StartsWith(rawPrefix.TrimStart('/'), StringComparison.OrdinalIgnoreCase));

    // =====================================================================================
    // SEC-03 write-endpoint assertions
    // =====================================================================================

    /// <summary>
    /// SEC-03 / T-18-04-03 — <c>POST /auth/login/{provider}</c> must carry
    /// <see cref="EnableRateLimitingAttribute"/> (placed by
    /// <c>RequireRateLimiting(policies.AuthLogin)</c> in <c>AuthEndpoints.MapAuthEndpoints</c>).
    ///
    /// <para>
    /// This test FAILS if <c>RequireRateLimiting</c> is removed from the login endpoint,
    /// ensuring CI catches a regression that would expose login to un-rate-limited brute-force.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "SEC-03: POST /auth/login/{provider} carries EnableRateLimitingAttribute (rate-limited)")]
    public void Login_Endpoint_Has_RateLimiterMetadata()
    {
        var loginEp = FindEndpoint("auth/login/");
        Assert.NotNull(loginEp);

        // RequireRateLimiting(policyName) places EnableRateLimitingAttribute on the endpoint.
        var hasRateLimit = loginEp!.Metadata.Any(m => m is EnableRateLimitingAttribute);
        Assert.True(hasRateLimit,
            $"SEC-03 FAILED: endpoint '{loginEp.RoutePattern.RawText}' does not have EnableRateLimitingAttribute. "
            + "Add .RequireRateLimiting(policies.AuthLogin) to the /auth/login endpoint in AuthEndpoints.MapAuthEndpoints.");
    }

    /// <summary>
    /// SEC-03 / T-18-04-03 — <c>POST /auth/refresh</c> must carry
    /// <see cref="EnableRateLimitingAttribute"/> (placed by
    /// <c>RequireRateLimiting(policies.AuthRefresh)</c>).
    /// </summary>
    [Fact(DisplayName = "SEC-03: POST /auth/refresh carries EnableRateLimitingAttribute (rate-limited)")]
    public void Refresh_Endpoint_Has_RateLimiterMetadata()
    {
        var refreshEp = FindEndpoint("auth/refresh");
        Assert.NotNull(refreshEp);

        var hasRateLimit = refreshEp!.Metadata.Any(m => m is EnableRateLimitingAttribute);
        Assert.True(hasRateLimit,
            $"SEC-03 FAILED: endpoint '{refreshEp.RoutePattern.RawText}' does not have EnableRateLimitingAttribute. "
            + "Add .RequireRateLimiting(policies.AuthRefresh) to the /auth/refresh endpoint in AuthEndpoints.MapAuthEndpoints.");
    }

    /// <summary>
    /// SEC-03 / T-18-04-03 — <c>POST /auth/register</c> must carry
    /// <see cref="EnableRateLimitingAttribute"/> (placed by
    /// <c>RequireRateLimiting(policies.AuthRegister)</c>).
    /// </summary>
    [Fact(DisplayName = "SEC-03: POST /auth/register carries EnableRateLimitingAttribute (rate-limited)")]
    public void Register_Endpoint_Has_RateLimiterMetadata()
    {
        var registerEp = FindEndpoint("auth/register");
        Assert.NotNull(registerEp);

        var hasRateLimit = registerEp!.Metadata.Any(m => m is EnableRateLimitingAttribute);
        Assert.True(hasRateLimit,
            $"SEC-03 FAILED: endpoint '{registerEp.RoutePattern.RawText}' does not have EnableRateLimitingAttribute. "
            + "Add .RequireRateLimiting(policies.AuthRegister) to the /auth/register endpoint in AuthEndpoints.MapAuthEndpoints.");
    }

    // =====================================================================================
    // SEC-03 logout intentional-exclusion assertion
    // =====================================================================================

    /// <summary>
    /// SEC-03 / T-18-04-03 — <c>POST /auth/logout</c> is intentionally NOT rate-limited.
    ///
    /// <para>
    /// RFC 7009 semantics: the refresh token itself is the revocation capability. Rate-limiting
    /// logout would prevent token revocation when the limit is exhausted — specifically, a player
    /// whose session was stolen and who fires many logout attempts (after detecting the compromise)
    /// could be locked out of revoking their own refresh family. The handler is idempotent and
    /// revoking an unknown/already-revoked family is a no-op 204, so DoS risk is low.
    /// </para>
    /// <para>
    /// This [Fact] documents the exclusion explicitly so the absence of rate-limit metadata on
    /// logout is deliberate and visible in CI, NOT accidental. If this assertion starts failing,
    /// someone added <c>RequireRateLimiting</c> to logout — review the RFC 7009 rationale in
    /// <c>AuthEndpoints.cs</c> before merging.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "SEC-03: POST /auth/logout has NO EnableRateLimitingAttribute — intentional RFC 7009 exclusion")]
    public void Logout_Endpoint_Has_No_RateLimiterMetadata_Intentional()
    {
        // Match the bare auth/logout route, not auth/logout/all.
        var logoutEp = GetAuthEndpoints().FirstOrDefault(e =>
            string.Equals(
                (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                "auth/logout",
                StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(logoutEp);

        // INTENTIONAL: /auth/logout must NOT have EnableRateLimitingAttribute.
        var hasRateLimit = logoutEp!.Metadata.Any(m => m is EnableRateLimitingAttribute);
        Assert.False(hasRateLimit,
            $"SEC-03 INTENTIONAL EXCLUSION CHANGED: endpoint '{logoutEp.RoutePattern.RawText}' now has EnableRateLimitingAttribute. "
            + "Logout is intentionally unguarded per RFC 7009 semantics — the refresh token is the revocation capability. "
            + "Rate-limiting logout would block token revocation when the limit is exhausted. "
            + "Review AuthEndpoints.cs and the RFC 7009 rationale before merging this change.");
    }
}
