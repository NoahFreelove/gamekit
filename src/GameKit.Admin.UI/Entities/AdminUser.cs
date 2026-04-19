// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Admin.UI.Entities;

/// <summary>
/// An operator account with access to the GameKit Admin UI. Stored in the
/// <c>gamekit.admin_users</c> table under the <c>__ef_migrations_admin</c> history.
/// Identity is separate from <c>players</c> — admin accounts never overlap with player accounts.
/// </summary>
public sealed class AdminUser
{
    /// <summary>UUIDv7 primary key (assigned by <c>IIdGenerator</c>, never generated DB-side).</summary>
    public Guid Id { get; set; }

    /// <summary>Case-insensitive username (Postgres <c>citext</c>), 3–32 chars, unique.</summary>
    public required string Username { get; set; }

    /// <summary>BCrypt.Net-Next hash (≤ 60 chars for BCrypt, reserved 72 for future Argon2 sibling).</summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// Either <c>"admin"</c> or <c>"superadmin"</c>. Enforced by DB CHECK constraint
    /// (<c>ck_admin_users_role</c>) in addition to application-side validation.
    /// </summary>
    public required string Role { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC last-successful-login timestamp (null until first login).</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Defense-in-depth — consecutive failed logins since last success; zeroed on success.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// Defense-in-depth — when set, logins reject with a "locked until" message until this timestamp.
    /// Admin unlock is out-of-band (another superadmin + CLI).
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }
}
