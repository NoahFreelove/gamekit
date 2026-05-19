// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Rankings.Authentication;

/// <summary>
/// Authentication-scheme and authorization-policy name constants for the service-token scheme
/// (D-05 / D-06). The scheme name is distinct from <c>JwtBearerDefaults.AuthenticationScheme</c>
/// and <c>AdminAuthenticationSchemeConstants.Scheme</c> so player JWTs and admin cookies cannot
/// authenticate into service-token-only endpoints (T-04-04-AC).
/// </summary>
public static class ServiceTokenAuthenticationDefaults
{
    /// <summary>Name of the <c>GameKitServiceToken</c> custom authentication scheme.</summary>
    public const string SchemeName = "GameKitServiceToken";

    /// <summary>
    /// Name of the authorization policy that requires a valid, non-revoked, non-expired
    /// service token with role <c>service-account</c>.
    /// </summary>
    public const string PolicyName = "RequiresServiceToken";
}
