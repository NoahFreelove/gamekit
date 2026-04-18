// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth;

/// <summary>Discord OAuth2 provider options. Scope is locked to <c>identify</c> (AUTH-07 / D-10).</summary>
public sealed class DiscordOptions
{
    /// <summary>Discord application client id. Required.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Discord application client secret. Required.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Callback path registered with Discord. Default: <c>/auth/callback/discord</c>.</summary>
    public string CallbackPath { get; set; } = "/auth/callback/discord";

    /// <summary>Authorization endpoint — overridable only for testing.</summary>
    public string AuthorizationEndpoint { get; set; } = "https://discord.com/api/oauth2/authorize";

    /// <summary>Token endpoint — overridable only for testing.</summary>
    public string TokenEndpoint { get; set; } = "https://discord.com/api/oauth2/token";

    /// <summary>UserInfo endpoint — overridable only for testing.</summary>
    public string UserInfoEndpoint { get; set; } = "https://discord.com/api/users/@me";
}
