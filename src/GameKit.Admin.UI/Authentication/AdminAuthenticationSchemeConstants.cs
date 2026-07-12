// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Authentication;

/// <summary>
/// Authentication scheme + cookie + CSRF constants (D-02 / D-15 / D-16).
/// The scheme name is distinct from <c>JwtBearerDefaults.AuthenticationScheme</c> so
/// a player JWT cannot authenticate into any admin endpoint (ROADMAP SC #6).
/// </summary>
public static class AdminAuthenticationSchemeConstants
{
    /// <summary>Admin cookie auth scheme name.</summary>
    public const string Scheme = "GameKitAdmin";

    /// <summary>Session cookie name (also the default of <see cref="AdminCookieOptions.Name"/>).</summary>
    public const string CookieName = "gk_admin_session";

    /// <summary>CSRF request header name — <c>IAntiforgery</c> looks for the token here.</summary>
    public const string CsrfHeaderName = "X-GameKit-Admin-CSRF";

    /// <summary>CSRF cookie name — read by Blazor / JS and echoed via the header.</summary>
    public const string CsrfCookieName = "gk_admin_csrf";
}
