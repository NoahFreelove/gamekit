// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
namespace GameKit.Admin.UI.Services;

/// <summary>
/// Admin audit-log action namespace constants (D-17). Every mutation through the admin
/// surface writes an <c>admin_audit_log</c> row whose <c>action</c> column matches one of
/// these literals.
/// </summary>
public static class AdminAuditActions
{
    /// <summary>A player was banned via the admin UI (reason required per D-09).</summary>
    public const string PlayerBan = "admin.player.ban";

    /// <summary>A player was unbanned via the admin UI.</summary>
    public const string PlayerUnban = "admin.player.unban";

    /// <summary>A player was GDPR-deleted through the admin UI (superadmin-only).</summary>
    public const string PlayerGdprDelete = "admin.player.gdpr_delete";

    /// <summary>A player's rating was manually adjusted (superadmin-only; Phase 4 surface).</summary>
    public const string PlayerRankAdjust = "admin.player.rank_adjust";

    /// <summary>A new admin user was created (superadmin-only).</summary>
    public const string AdminCreate = "admin.admin.create";

    /// <summary>An admin user was deleted (superadmin-only; blocked for last superadmin).</summary>
    public const string AdminDelete = "admin.admin.delete";

    /// <summary>JWT signing key was rotated (superadmin-only; Phase 2 operational surface).</summary>
    public const string SigningKeyRotate = "admin.signing_key.rotate";

    /// <summary>An admin successfully authenticated against <c>/admin/login</c>.</summary>
    public const string SessionLoginSuccess = "admin.session.login.success";

    /// <summary>An admin login attempt failed (wrong password, unknown user, or locked account).</summary>
    public const string SessionLoginFailure = "admin.session.login.failure";
}
