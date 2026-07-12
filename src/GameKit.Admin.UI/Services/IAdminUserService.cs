// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Entities;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Admin-user CRUD (superadmin-only paths). Create runs under SERIALIZABLE for username
/// collisions (PostgreSQL <c>23505</c> mapping mirrors <c>GuestUpgradeService</c>); Delete
/// blocks removal of the last remaining superadmin (T-03-06-02).
/// </summary>
public interface IAdminUserService
{
    /// <summary>
    /// Creates a new admin. Runs under SERIALIZABLE tx — if the username already exists
    /// concurrently, the Postgres <c>23505</c> unique-violation surfaces as
    /// <see cref="AdminUsernameAlreadyTakenException"/>.
    /// </summary>
    /// <param name="username">Desired username (citext uniqueness).</param>
    /// <param name="password">Plaintext password (hashed server-side).</param>
    /// <param name="role">Either <c>"admin"</c> or <c>"superadmin"</c> (CHECK-enforced).</param>
    /// <param name="actorId">Acting superadmin id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created admin id.</returns>
    Task<Guid> CreateAsync(
        string username,
        string password,
        string role,
        Guid actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an admin. Blocks deletion when the target is the last remaining superadmin
    /// (T-03-06-02) — throws <see cref="LastSuperadminException"/> in that case.
    /// </summary>
    /// <param name="adminId">Target admin id.</param>
    /// <param name="actorId">Acting superadmin id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(Guid adminId, Guid actorId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all admins, ordered by <c>CreatedAt</c> ascending. Password hashes are NOT
    /// projected — callers get a read-only view. Defense-in-depth: the DTO surface in plan
    /// 03-07 is also hash-free.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Thrown by <see cref="IAdminUserService.CreateAsync"/> when Postgres <c>23505</c> fires on
/// <c>ix_admin_users_username</c>.
/// </summary>
public sealed class AdminUsernameAlreadyTakenException : Exception
{
    /// <summary>The username that was already taken (echoed for the 409 response body).</summary>
    public string Username { get; }

    /// <summary>Constructs the exception.</summary>
    /// <param name="username">The conflicting username.</param>
    public AdminUsernameAlreadyTakenException(string username)
        : base($"Admin username '{username}' is already taken.")
    {
        Username = username;
    }
}

/// <summary>
/// Thrown by <see cref="IAdminUserService.DeleteAsync"/> when removing the target would leave
/// zero remaining superadmins — which would lock every superadmin-only path permanently
/// (T-03-06-02).
/// </summary>
public sealed class LastSuperadminException : Exception
{
    /// <summary>The admin id that could not be deleted.</summary>
    public Guid AdminId { get; }

    /// <summary>Constructs the exception.</summary>
    /// <param name="adminId">The admin id that could not be deleted.</param>
    public LastSuperadminException(Guid adminId)
        : base($"Admin {adminId} is the last remaining superadmin and cannot be deleted. " +
               "Create a second superadmin first, then delete this one.")
    {
        AdminId = adminId;
    }
}
