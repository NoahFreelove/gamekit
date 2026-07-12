// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace GameKit.TestFixtures;

/// <summary>Default WireMock stubs for Discord's OAuth2 token + /users/@me endpoints.</summary>
public static class WireMockDiscordStubs
{
    /// <summary>Canonical Discord snowflake used in the default /users/@me stub.</summary>
    public const string DefaultDiscordId = "123456789012345678";

    /// <summary>Canonical username used in the default /users/@me stub.</summary>
    public const string DefaultUsername = "mock_user";

    /// <summary>Applies the default positive-path stubs (token + identify-only user info).</summary>
    public static void ApplyDefaults(WireMockServer server)
    {
        server
            .Given(Request.Create().WithPath("/discord-mock/api/oauth2/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new
                {
                    access_token = "mock-access",
                    token_type = "Bearer",
                    expires_in = 3600,
                    refresh_token = "mock-refresh",
                    scope = "identify",
                }));

        server
            .Given(Request.Create().WithPath("/discord-mock/api/users/@me").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new
                {
                    id = DefaultDiscordId,
                    username = DefaultUsername,
                    discriminator = "0001",
                }));
    }
}
