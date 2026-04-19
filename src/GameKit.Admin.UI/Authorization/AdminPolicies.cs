// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Authorization;

/// <summary>Authorization-policy name constants for admin endpoints (<c>.RequireAuthorization(AdminPolicies.Admin)</c>).</summary>
public static class AdminPolicies
{
    /// <summary>Requires authenticated admin via the <c>GameKitAdmin</c> scheme with role <c>admin</c> or <c>superadmin</c>.</summary>
    public const string Admin = "gamekit.admin.admin";

    /// <summary>Requires authenticated admin via the <c>GameKitAdmin</c> scheme with role <c>superadmin</c>.</summary>
    public const string Superadmin = "gamekit.admin.superadmin";
}
