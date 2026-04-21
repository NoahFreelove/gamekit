// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Entities;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Default <see cref="IAdminAuthService"/>. Runs <see cref="IPasswordHasher.Verify(string, string)"/>
/// against a canned BCrypt dummy hash on the user-not-found branch so wall-clock response time
/// matches the hit path (T-03-06-03; mirrors Phase-2 <c>PasswordOAuthProvider.DummyHash</c>).
/// </summary>
public sealed class AdminAuthService : IAdminAuthService
{
    // Canned BCrypt hash (work-factor 12) for an unknowable password. Distinct from
    // PasswordOAuthProvider.DummyHash so a leak of one does not compromise the other. The
    // literal below is 60 chars ($2a$12$ + 22-char salt + 31-char ciphertext) and is a real
    // BCrypt.Net-Next 4.1.0 output for the password "admin-dummy-never-matches" at work
    // factor 12. Paste VERBATIM — do not regenerate; deterministic source of the dummy hash
    // must survive CI re-runs so timing parity is reproducible across runs. BCryptPasswordHasher
    // will run its full work-factor-12 comparison against this literal, equalizing wall-clock
    // time between user-not-found and wrong-password branches (T-03-06-03).
    private const string DummyHash = "$2a$12$IqEI8DJ7RlcRdaL03LoJo.JbZ1kR.Ao4S3xPGk7XQdhaPfwmAyv2q";

    private readonly GameKitDbContext _ctx;
    private readonly IPasswordHasher _hasher;
    private readonly IAdminAuditWriter _audit;
    private readonly IClock _clock;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="hasher">Password hasher (reuses Phase-2 <see cref="BCryptPasswordHasher"/>).</param>
    /// <param name="audit">Audit writer for login success + failure rows.</param>
    /// <param name="clock">Clock abstraction.</param>
    public AdminAuthService(
        GameKitDbContext ctx,
        IPasswordHasher hasher,
        IAdminAuditWriter audit,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        _ctx = ctx;
        _hasher = hasher;
        _audit = audit;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<(Guid AdminId, string Role)?> VerifyPasswordAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var admin = await _ctx.Set<AdminUser>()
            .AsTracking()
            .FirstOrDefaultAsync(a => a.Username == username, cancellationToken)
            .ConfigureAwait(false);

        if (admin is null)
        {
            // Timing parity — run BCrypt.Verify against the canned dummy hash so response wall-clock
            // time matches the hit path. BCryptPasswordHasher.Verify swallows SaltParseException, so
            // the dummy literal just needs to be BCrypt-parseable; correctness of the boolean return
            // is irrelevant (we always return null here).
            _hasher.Verify(password, DummyHash);
            return null;
        }

        if (admin.LockedUntil is { } lu && lu > _clock.UtcNow)
        {
            await _audit.WriteAsync(
                action: AdminAuditActions.SessionLoginFailure,
                targetType: "admin",
                targetId: admin.Id,
                actorId: admin.Id,
                before: null,
                after: new { reason = "locked", locked_until = lu },
                reason: null,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        if (!_hasher.Verify(password, admin.PasswordHash))
        {
            admin.FailedLoginCount += 1;
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                action: AdminAuditActions.SessionLoginFailure,
                targetType: "admin",
                targetId: admin.Id,
                actorId: admin.Id,
                before: null,
                after: new { failed_count = admin.FailedLoginCount },
                reason: null,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return null;
        }

        admin.FailedLoginCount = 0;
        admin.LastLoginAt = _clock.UtcNow;
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            action: AdminAuditActions.SessionLoginSuccess,
            targetType: "admin",
            targetId: admin.Id,
            actorId: admin.Id,
            before: null,
            after: new { last_login_at = admin.LastLoginAt },
            reason: null,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (admin.Id, admin.Role);
    }
}
