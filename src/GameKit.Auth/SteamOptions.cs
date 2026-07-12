// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth;

/// <summary>Steam OpenID 2.0 provider options. Server-side check_authentication is mandatory (CONTEXT D-09).</summary>
public sealed class SteamOptions
{
    /// <summary>OpenID OP endpoint URL — overridable only for testing (default: https://steamcommunity.com/openid/login).</summary>
    public string OpenIdEndpoint { get; set; } = "https://steamcommunity.com/openid/login";

    /// <summary>Relative callback path handled by <c>/auth/callback/steam</c>. Default: <c>/auth/callback/steam</c>.</summary>
    public string CallbackPath { get; set; } = "/auth/callback/steam";

    /// <summary>OpenID realm (absolute URL, trailing slash). Required.</summary>
    public string Realm { get; set; } = string.Empty;

    /// <summary>Optional Steam Web API key (only needed for user-profile enrichment — v1 does not use).</summary>
    public string? ApiKey { get; set; }
}
