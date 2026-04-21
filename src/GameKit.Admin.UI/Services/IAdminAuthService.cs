// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Admin-side password verification (T-03-06-03 timing-parity mitigation). Mirrors the
/// Phase-2 <c>PasswordOAuthProvider</c> dummy-hash pattern so a user-not-found branch still
/// runs a full BCrypt work-factor-12 comparison, equalizing wall-clock response time with
/// the hit path.
/// </summary>
public interface IAdminAuthService
{
    /// <summary>
    /// Verifies <paramref name="username"/> + <paramref name="password"/> against
    /// <c>admin_users</c>. Returns the admin's id + role on success, <c>null</c> on any failure
    /// (unknown user, wrong password, locked account). Audit rows are written for every path
    /// except unknown-user (to avoid username-enumeration via audit-log visibility).
    /// </summary>
    /// <param name="username">Admin username (case-insensitive via citext column).</param>
    /// <param name="password">Plaintext password to verify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Admin id + role tuple on success, <c>null</c> otherwise.</returns>
    Task<(Guid AdminId, string Role)?> VerifyPasswordAsync(
        string username,
        string password,
        CancellationToken cancellationToken);
}
