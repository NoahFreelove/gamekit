// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using WireMock.Server;
using Xunit;

namespace GameKit.TestFixtures;

/// <summary>
/// Starts a WireMock.Net server once per xUnit collection; exposes stub URLs for the
/// Steam OpenID 2.0 <c>check_authentication</c> endpoint and the Discord OAuth2 token +
/// <c>/users/@me</c> endpoints. Used by GameKit.Auth.Integration.Tests to mock the two
/// provider roundtrips without real external egress (CONTEXT D-09).
/// </summary>
public sealed class WireMockFixture : IAsyncLifetime
{
    /// <summary>The underlying WireMock server (tests may reset/override stubs).</summary>
    public WireMockServer Server { get; private set; } = default!;

    /// <summary>Full base URL (e.g. http://localhost:54321).</summary>
    public string BaseUrl => Server.Url!;

    /// <summary>Steam OpenID 2.0 login / check_authentication endpoint stub URL.</summary>
    public string SteamOpenIdLoginUrl => $"{BaseUrl}/steam-mock/openid/login";

    /// <summary>Discord OAuth2 token-exchange endpoint stub URL.</summary>
    public string DiscordTokenUrl => $"{BaseUrl}/discord-mock/api/oauth2/token";

    /// <summary>Discord /users/@me user-info stub URL.</summary>
    public string DiscordUserInfoUrl => $"{BaseUrl}/discord-mock/api/users/@me";

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        Server = WireMockServer.Start();
        WireMockSteamStubs.ApplyDefaults(Server);
        WireMockDiscordStubs.ApplyDefaults(Server);
        return Task.CompletedTask;
    }

    /// <summary>Resets mappings to the default stubs (for tests that overrode them).</summary>
    public void ResetDefaultStubs()
    {
        Server.ResetMappings();
        WireMockSteamStubs.ApplyDefaults(Server);
        WireMockDiscordStubs.ApplyDefaults(Server);
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        Server?.Stop();
        Server?.Dispose();
        return Task.CompletedTask;
    }
}
