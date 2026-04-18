// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace GameKit.TestFixtures;

/// <summary>Default and builder-overridable WireMock stubs for the Steam OpenID 2.0 OP.</summary>
public static class WireMockSteamStubs
{
    /// <summary>Exact Key-Value form response body that signals assertion valid (OpenID 2.0 §11.4.2).</summary>
    public const string IsValidTrueBody = "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n";

    /// <summary>Exact Key-Value form response body that signals assertion invalid (forgery).</summary>
    public const string IsValidFalseBody = "ns:http://specs.openid.net/auth/2.0\nis_valid:false\n";

    /// <summary>Applies the default positive-path stub (is_valid:true) to the server.</summary>
    public static void ApplyDefaults(WireMockServer server)
    {
        server
            .Given(Request.Create().WithPath("/steam-mock/openid/login").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/plain")
                .WithBody(IsValidTrueBody));
    }

    /// <summary>Overrides the stub to respond with is_valid:false (forgery rejection test — success criterion #2).</summary>
    public static void StubIsValidFalse(WireMockServer server)
    {
        server.ResetMappings();
        server
            .Given(Request.Create().WithPath("/steam-mock/openid/login").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/plain")
                .WithBody(IsValidFalseBody));
    }
}
