// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using GameKit.Auth.Egress;

namespace GameKit.Auth;

/// <summary>
/// Root options for GameKit.Auth. Populated via <c>services.AddGameKit(...).AddAuth(opts =&gt; ...)</c>.
/// </summary>
public sealed class GameKitAuthOptions
{
    /// <summary>JWT issuance options (issuer / audience / RSA PEM paths / lifetimes / kid).</summary>
    public JwtOptions Jwt { get; } = new();

    /// <summary>Steam OpenID 2.0 options (return URL, realm, optional API key).</summary>
    public SteamOptions Steam { get; } = new();

    /// <summary>Discord OAuth2 options (client id/secret, callback path, identify-only scope).</summary>
    public DiscordOptions Discord { get; } = new();

    /// <summary>Username/password policy (BCrypt work factor, username regex).</summary>
    public PasswordOptions Password { get; } = new();

    /// <summary>
    /// Host allow-list enforced by <see cref="EgressAllowListHandler"/>. Default-populated with the four
    /// hosts from <see cref="DefaultAllowedHosts.All"/> (<c>steamcommunity.com</c>, <c>api.steampowered.com</c>,
    /// <c>discord.com</c>, <c>discordapp.com</c>). Operators may add hosts (tests mocking provider endpoints
    /// append their WireMock host) but the defaults are literal code — a misconfigured appsettings.json can
    /// never clear the list silently.
    /// </summary>
    public List<string> AllowedProviderHosts { get; } = new(DefaultAllowedHosts.All);

    /// <summary>
    /// When true, <c>AddAuth</c> skips <c>AddAuthentication().AddJwtBearer()</c> wiring. Used by unit
    /// tests that do not want to load a real RSA PEM file. Production apps leave this <c>false</c>.
    /// Plan 02-05 flips this to <c>false</c> once Steam/Discord scheme registration is complete.
    /// </summary>
    public bool SkipAuthenticationSchemeRegistration { get; set; }
}
