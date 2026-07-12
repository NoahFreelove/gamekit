// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Apple.Builder;
using GameKit.Auth.Egress;
using GameKit.Auth.Google.Builder;
using Xunit;

namespace GameKit.Auth.Tests;

/// <summary>
/// SEC-05 egress audit tests. Asserts that:
/// <list type="bullet">
///   <item><see cref="EgressAllowListHandler"/> throws <see cref="EgressViolationException"/>
///   for any host NOT on the allow-list (negative case).</item>
///   <item>The default Steam + Discord hosts pass through (baseline).</item>
///   <item>The Apple provider host (<c>appleid.apple.com</c>) passes through
///   when allowlisted by <see cref="AppleBuilderExtensions.AppleProviderHosts"/>.</item>
///   <item>The Google provider hosts (<c>oauth2.googleapis.com</c>,
///   <c>www.googleapis.com</c>, <c>accounts.google.com</c>) pass through
///   when allowlisted by <see cref="GoogleBuilderExtensions.GoogleProviderHosts"/>.</item>
/// </list>
/// Uses a recording <see cref="StubTerminalHandler"/> as the inner handler so "allowed" means
/// the request was forwarded (HTTP 200) and "blocked" means <see cref="EgressViolationException"/>
/// was thrown before it reached the inner handler.
/// </summary>
public sealed class EgressAuditTests
{
    // ---- allow-list: Steam + Discord (DefaultAllowedHosts.All) + Apple + Google ----

    /// <summary>Builds a client with the full Apple+Google-augmented allow-list.</summary>
    private static HttpClient BuildClientWithAllProviders()
    {
        var opts = BuildOpts();
        // Apple hosts (mirrors AddApple approach-b registration)
        foreach (var h in AppleBuilderExtensions.AppleProviderHosts)
            if (!opts.AllowedProviderHosts.Contains(h))
                opts.AllowedProviderHosts.Add(h);
        // Google hosts (mirrors AddGoogle approach-b registration)
        foreach (var h in GoogleBuilderExtensions.GoogleProviderHosts)
            if (!opts.AllowedProviderHosts.Contains(h))
                opts.AllowedProviderHosts.Add(h);
        return BuildClient(opts);
    }

    private static HttpClient BuildClient(GameKitAuthOptions opts)
    {
        var inner = new StubTerminalHandler();
        var handler = new EgressAllowListHandler(opts) { InnerHandler = inner };
        return new HttpClient(handler);
    }

    private static GameKitAuthOptions BuildOpts()
    {
        var o = new GameKitAuthOptions();
        o.Jwt.Issuer = "gamekit-test";
        o.Jwt.Audience = "gamekit-test";
        o.SkipAuthenticationSchemeRegistration = true;
        return o;
    }

    // ---- DENY tests ----

    [Theory]
    [InlineData("https://evil.example.com/oauth")]
    [InlineData("https://attacker.internal/")]
    [InlineData("http://steamcommunity.com.attacker.test/")]
    [InlineData("https://appleid.apple.com.evil.org/auth/token")]
    [InlineData("https://googleapis.com.evil.org/token")]
    public async Task NonAllowlisted_Host_Throws_EgressViolationException(string url)
    {
        using var client = BuildClientWithAllProviders();
        await Assert.ThrowsAsync<EgressViolationException>(() => client.GetAsync(url));
    }

    // ---- ALLOW tests: default providers (Steam + Discord) ----

    [Theory]
    [InlineData("https://steamcommunity.com/openid/login")]
    [InlineData("https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/")]
    [InlineData("https://discord.com/api/oauth2/token")]
    [InlineData("https://discordapp.com/api/users/@me")]
    public async Task DefaultProvider_Host_Passes_Through(string url)
    {
        // Uses only DefaultAllowedHosts (Steam + Discord)
        using var client = BuildClient(BuildOpts());
        using var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ---- ALLOW tests: Apple provider host ----

    [Theory]
    [InlineData("https://appleid.apple.com/auth/token")]
    public async Task Apple_Provider_Host_Passes_Through_When_Allowlisted(string url)
    {
        using var client = BuildClientWithAllProviders();
        using var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Apple_Provider_Host_Is_Blocked_Without_Allowlist()
    {
        // Without Apple hosts in the options, appleid.apple.com must be rejected.
        using var client = BuildClient(BuildOpts()); // only Steam + Discord defaults
        await Assert.ThrowsAsync<EgressViolationException>(
            () => client.GetAsync("https://appleid.apple.com/auth/token"));
    }

    // ---- ALLOW tests: Google provider hosts ----

    [Theory]
    [InlineData("https://oauth2.googleapis.com/token")]
    [InlineData("https://www.googleapis.com/oauth2/v1/userinfo")]
    [InlineData("https://accounts.google.com/.well-known/openid-configuration")]
    public async Task Google_Provider_Host_Passes_Through_When_Allowlisted(string url)
    {
        using var client = BuildClientWithAllProviders();
        using var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Theory]
    [InlineData("https://oauth2.googleapis.com/token")]
    [InlineData("https://www.googleapis.com/oauth2/v1/userinfo")]
    [InlineData("https://accounts.google.com/.well-known/openid-configuration")]
    public async Task Google_Provider_Host_Is_Blocked_Without_Allowlist(string url)
    {
        // Without Google hosts in the options, googleapis.com must be rejected.
        using var client = BuildClient(BuildOpts()); // only Steam + Discord defaults
        await Assert.ThrowsAsync<EgressViolationException>(() => client.GetAsync(url));
    }

    // ---- Structural: verify the host constant arrays are non-empty ----

    [Fact]
    public void AppleProviderHosts_Contains_Expected_Hosts()
    {
        Assert.Contains("appleid.apple.com", AppleBuilderExtensions.AppleProviderHosts);
    }

    [Fact]
    public void GoogleProviderHosts_Contains_Expected_Hosts()
    {
        Assert.Contains("oauth2.googleapis.com", GoogleBuilderExtensions.GoogleProviderHosts);
        Assert.Contains("www.googleapis.com", GoogleBuilderExtensions.GoogleProviderHosts);
        Assert.Contains("accounts.google.com", GoogleBuilderExtensions.GoogleProviderHosts);
    }

    // ---- Inner stub handler ----

    private sealed class StubTerminalHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
