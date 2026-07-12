// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Entities;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Default <see cref="IAdminUserService"/>. Create uses SERIALIZABLE + 3-retry loop on
/// <c>40001</c> (mirrors <see cref="GameKit.Auth.Services.GuestUpgradeService"/>); Delete
/// counts remaining superadmins inside its SERIALIZABLE tx before removing the target.
/// </summary>
public sealed class AdminUserService : IAdminUserService
{
    private const int MaxRetries = 3;

    private readonly GameKitDbContext _ctx;
    private readonly IPasswordHasher _hasher;
    private readonly IAdminAuditWriter _audit;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="hasher">Password hasher (reuses Phase-2 <see cref="BCryptPasswordHasher"/>).</param>
    /// <param name="audit">Audit writer (admin.admin.create / admin.admin.delete).</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">UUIDv7 id generator.</param>
    public AdminUserService(
        GameKitDbContext ctx,
        IPasswordHasher hasher,
        IAdminAuditWriter audit,
        IClock clock,
        IIdGenerator ids)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        _ctx = ctx;
        _hasher = hasher;
        _audit = audit;
        _clock = clock;
        _ids = ids;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(
        string username,
        string password,
        string role,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(role);
        if (role != AdminRoles.Admin && role != AdminRoles.Superadmin)
            throw new ArgumentException(
                $"Role must be '{AdminRoles.Admin}' or '{AdminRoles.Superadmin}'; got '{role}'.",
                nameof(role));

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            await using var tx = await _ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var newId = _ids.NewId();
                _ctx.Set<AdminUser>().Add(new AdminUser
                {
                    Id = newId,
                    Username = username,
                    PasswordHash = _hasher.Hash(password),
                    Role = role,
                    CreatedAt = _clock.UtcNow,
                    LastLoginAt = null,
                    FailedLoginCount = 0,
                    LockedUntil = null,
                });
                await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await _audit.WriteAsync(
                    action: AdminAuditActions.AdminCreate,
                    targetType: "admin",
                    targetId: newId,
                    actorId: actorId,
                    before: null,
                    after: new { username, role, created_at = _clock.UtcNow },
                    reason: null,
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return newId;
            }
            catch (Exception ex) when (TryFindPostgresException(ex) is { } pg)
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);

                // Detach in-flight entities so the scoped DbContext stays usable on retry.
                foreach (var entry in _ctx.ChangeTracker.Entries())
                    entry.State = EntityState.Detached;

                if (pg.SqlState == "23505")
                    throw new AdminUsernameAlreadyTakenException(username);

                if (pg.SqlState == "40001" && attempt < MaxRetries - 1)
                    continue;

                throw;
            }
        }

        throw new InvalidOperationException("AdminUserService.CreateAsync: SERIALIZABLE retries exhausted.");
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid adminId, Guid actorId, CancellationToken cancellationToken)
    {
        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var target = await _ctx.Set<AdminUser>()
            .FirstOrDefaultAsync(a => a.Id == adminId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Admin {adminId} not found.");

        // T-03-06-02: block when the target is a superadmin AND is the last remaining one.
        if (target.Role == AdminRoles.Superadmin)
        {
            var superadminCount = await _ctx.Set<AdminUser>()
                .AsNoTracking()
                .CountAsync(a => a.Role == AdminRoles.Superadmin, cancellationToken)
                .ConfigureAwait(false);
            if (superadminCount <= 1)
                throw new LastSuperadminException(adminId);
        }

        var before = new
        {
            username = target.Username,
            role = target.Role,
            created_at = target.CreatedAt,
        };

        _ctx.Set<AdminUser>().Remove(target);
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            action: AdminAuditActions.AdminDelete,
            targetType: "admin",
            targetId: adminId,
            actorId: actorId,
            before: before,
            after: null,
            reason: null,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminUser>> ListAsync(CancellationToken cancellationToken)
    {
        return await _ctx.Set<AdminUser>()
            .AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Walks an exception's InnerException chain looking for a <see cref="PostgresException"/>.
    /// Npgsql's default execution strategy wraps transient failures in InvalidOperationException,
    /// and EF Core wraps provider exceptions in DbUpdateException — a plain pattern-match misses
    /// both wrappings. Mirrors <c>GuestUpgradeService.TryFindPostgresException</c>.
    /// </summary>
    private static PostgresException? TryFindPostgresException(Exception? ex)
    {
        for (var i = 0; i < 8 && ex is not null; i++)
        {
            if (ex is PostgresException pg) return pg;
            ex = ex.InnerException;
        }
        return null;
    }
}
