// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using GameKit.Auth.Egress;
using GameKit.Auth.Providers.Steam;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace GameKit.Auth.Tests;

/// <summary>
/// Unit tests for <see cref="SteamOpenIdVerifier"/>. Uses the shared WireMockFixture to stub the
/// Steam OP <c>check_authentication</c> endpoint. Covers the forged-callback rejection path
/// (Success Criterion #2 at unit scope) and parser-boundary cases.
/// </summary>
public sealed class SteamOpenIdVerifierTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _wm;

    public SteamOpenIdVerifierTests(WireMockFixture wm) => _wm = wm;

    private SteamOpenIdVerifier BuildVerifier()
    {
        var services = new ServiceCollection();
        var opts = new GameKitAuthOptions();
        opts.Jwt.Issuer = "x";
        opts.Jwt.Audience = "x";
        opts.Steam.OpenIdEndpoint = _wm.SteamOpenIdLoginUrl;

        // The default allow-list contains real Steam hosts; we also need the WireMock host so
        // the egress handler lets the check_authentication POST through.
        var wmUri = new Uri(_wm.BaseUrl);
        opts.AllowedProviderHosts.Add(wmUri.Host);

        services.AddSingleton(opts);
        services.AddTransient<EgressAllowListHandler>();
        services.AddHttpClient("gamekit.auth.provider.steam")
            .AddHttpMessageHandler<EgressAllowListHandler>();

        var sp = services.BuildServiceProvider();
        return new SteamOpenIdVerifier(sp.GetRequiredService<IHttpClientFactory>(), opts);
    }

    private static IQueryCollection BuildQuery(string claimedId, string sig = "deadbeef")
    {
        return new QueryCollection(new Dictionary<string, StringValues>
        {
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"]   = claimedId,
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.response_nonce"] = "2026-04-18T00:00:00Zabc",
            ["openid.return_to"] = "https://gamekit.example.com/auth/callback/steam",
            ["openid.assoc_handle"] = "1234567890",
            ["openid.signed"] = "signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
            ["openid.sig"] = sig,
            ["openid.mode"] = "id_res",
        });
    }

    [Fact]
    public async Task Valid_Assertion_Returns_SteamID64()
    {
        _wm.ResetDefaultStubs();   // default: is_valid:true
        var verifier = BuildVerifier();

        var result = await verifier.VerifyAsync(
            BuildQuery("https://steamcommunity.com/openid/id/76561198000000001"));

        Assert.True(result.IsValid);
        Assert.Equal("76561198000000001", result.SteamId64);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task Malformed_ClaimedId_Returns_Invalid()
    {
        var verifier = BuildVerifier();

        var result = await verifier.VerifyAsync(BuildQuery("https://evil.example.com/id/1"));

        Assert.False(result.IsValid);
        Assert.Equal("claimed_id_malformed", result.ErrorCode);
    }

    [Fact]
    public async Task Forged_Assertion_IsValid_False_Returns_Invalid()
    {
        // Success Criterion #2 (unit-level): forged Steam callback (OP responds is_valid:false)
        // is rejected — an attacker with a bogus sig cannot produce is_valid:true because Steam's
        // own server is the arbiter.
        WireMockSteamStubs.StubIsValidFalse(_wm.Server);
        try
        {
            var verifier = BuildVerifier();

            var result = await verifier.VerifyAsync(
                BuildQuery("https://steamcommunity.com/openid/id/76561198000000001", sig: "forged-sig"));

            Assert.False(result.IsValid);
            Assert.Equal("is_valid_false", result.ErrorCode);
            Assert.Null(result.SteamId64);
        }
        finally
        {
            _wm.ResetDefaultStubs();
        }
    }

    [Fact]
    public async Task Empty_ClaimedId_Returns_Invalid()
    {
        var verifier = BuildVerifier();

        var result = await verifier.VerifyAsync(BuildQuery(""));

        Assert.False(result.IsValid);
        Assert.Equal("claimed_id_missing", result.ErrorCode);
    }
}
