// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;

namespace GameKit.Auth.Epic.Providers.Epic;

/// <summary>
/// ASP.NET Core <see cref="OAuthOptions"/> for the Epic Games OAuth 2.0 provider.
/// Pre-configures the Epic Online Services (EOS) authorization, token, and userinfo endpoints,
/// the <c>basic_profile</c> scope, and the default callback path.
/// </summary>
/// <remarks>
/// <para>
/// Epic's token endpoint requires client credentials in the HTTP Basic authorization header,
/// NOT as form fields. <see cref="EpicOAuthHandler"/> overrides <c>ExchangeCodeAsync</c> to
/// enforce this (T-07-05-01 / RESEARCH §Pitfall 6 mitigation).
/// </para>
/// <para>
/// Zero new NuGet dependencies: <see cref="OAuthOptions"/> and <see cref="OAuthHandler{T}"/>
/// ship in the <c>Microsoft.AspNetCore.App</c> shared framework.
/// </para>
/// </remarks>
public class EpicOAuthOptions : OAuthOptions
{
    /// <summary>
    /// Initializes Epic Online Services (EOS) OAuth 2.0 endpoints and default scope.
    /// </summary>
    public EpicOAuthOptions()
    {
        AuthorizationEndpoint   = "https://www.epicgames.com/id/authorize";
        TokenEndpoint           = "https://api.epicgames.dev/epic/oauth/v1/token";
        UserInformationEndpoint = "https://api.epicgames.dev/epic/oauth/v1/userInfo";
        Scope.Add("basic_profile");
        CallbackPath = new PathString("/signin-epic");
        // SaveTokens is always false — GameKit issues its own JWT/refresh-token pair.
        SaveTokens = false;
    }
}
