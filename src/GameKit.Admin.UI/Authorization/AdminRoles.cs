// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Authorization;

/// <summary>Role identifier constants for <c>admin_users.role</c> (D-06).</summary>
public static class AdminRoles
{
    /// <summary>Baseline admin role — can ban/unban, view audit + matches + health.</summary>
    public const string Admin = "admin";

    /// <summary>Elevated admin — can additionally create/delete admins, GDPR-delete, rank-adjust, rotate keys.</summary>
    public const string Superadmin = "superadmin";
}
