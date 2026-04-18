// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Egress;
using Xunit;

namespace GameKit.Auth.Tests;

public sealed class EgressAllowListHandlerTests
{
    private static HttpClient BuildClient(GameKitAuthOptions opts)
    {
        var inner = new StubTerminalHandler();
        var handler = new EgressAllowListHandler(opts) { InnerHandler = inner };
        return new HttpClient(handler);
    }

    private static GameKitAuthOptions DefaultOpts()
    {
        var o = new GameKitAuthOptions();
        o.Jwt.Issuer = "gamekit-test";
        o.Jwt.Audience = "gamekit-test";
        o.SkipAuthenticationSchemeRegistration = true;
        return o;
    }

    [Theory]
    [InlineData("https://steamcommunity.com/openid/login")]
    [InlineData("https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/")]
    [InlineData("https://discord.com/api/oauth2/token")]
    [InlineData("https://discordapp.com/api/users/@me")]
    public async Task Allowed_Host_Passes_Through(string url)
    {
        using var client = BuildClient(DefaultOpts());
        using var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.com/oauth")]
    [InlineData("https://attacker.internal/")]
    [InlineData("http://steamcommunity.com.attacker.test/")]
    public async Task OffList_Host_Throws_EgressViolationException(string url)
    {
        using var client = BuildClient(DefaultOpts());
        await Assert.ThrowsAsync<EgressViolationException>(() => client.GetAsync(url));
    }

    [Fact]
    public async Task Host_Comparison_Is_Case_Insensitive()
    {
        using var client = BuildClient(DefaultOpts());
        using var resp = await client.GetAsync("https://STEAMCOMMUNITY.COM/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Additional_Allowed_Host_From_Options_Passes()
    {
        var opts = DefaultOpts();
        opts.AllowedProviderHosts.Add("localhost");
        using var client = BuildClient(opts);
        using var resp = await client.GetAsync("http://localhost:7777/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private sealed class StubTerminalHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
