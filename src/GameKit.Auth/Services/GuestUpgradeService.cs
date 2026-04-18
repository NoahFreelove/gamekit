// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameKit.Auth.Services;

/// <summary>
/// Default <see cref="IGuestUpgradeService"/>. Upgrades a guest player in-place by attaching
/// either a <see cref="PlayerCredential"/> (password path) or a <c>PlayerIdentity</c> (OAuth
/// path) inside a SERIALIZABLE transaction; re-issues a root token that carries
/// <c>is_guest=false</c>.
/// </summary>
internal sealed class GuestUpgradeService : IGuestUpgradeService
{
    private const int MaxRetries = 3;

    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenService _refresh;
    private readonly IAuthAuditWriter _audit;
    private readonly IIdentityLinker _identityLinker;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="hasher">Password hasher.</param>
    /// <param name="refresh">Refresh-token service used to reissue the non-guest token.</param>
    /// <param name="audit">Audit writer.</param>
    /// <param name="identityLinker">Linker delegated to by the OAuth-upgrade path.</param>
    public GuestUpgradeService(
        GameKitDbContext ctx,
        IClock clock,
        IPasswordHasher hasher,
        IRefreshTokenService refresh,
        IAuthAuditWriter audit,
        IIdentityLinker identityLinker)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(identityLinker);
        _ctx = ctx;
        _clock = clock;
        _hasher = hasher;
        _refresh = refresh;
        _audit = audit;
        _identityLinker = identityLinker;
    }

    /// <inheritdoc />
    public async Task<TokenPair> UpgradeToPasswordAsync(
        Guid playerId,
        string username,
        string password,
        string? fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            await using var tx = await _ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                _ctx.Set<PlayerCredential>().Add(new PlayerCredential
                {
                    PlayerId = playerId,
                    Username = username,
                    PasswordHash = _hasher.Hash(password),
                    UpdatedAt = _clock.UtcNow,
                });
                await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await _audit.WriteAsync(
                    action: "auth.guest.upgraded_password",
                    targetType: "player",
                    targetId: playerId,
                    actorId: playerId,
                    after: new { is_guest = false, credential_added = true },
                    reason: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

                // Issue a fresh root token. IsGuestResolver now returns false (credential row exists),
                // so the JWT carries is_guest=false (D-13).
                return await _refresh
                    .IssueRootAsync(playerId, "password", fingerprint, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (TryFindPostgresException(ex) is { } pg)
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);

                // Detach in-flight entities so the scoped DbContext stays usable on retry.
                foreach (var entry in _ctx.ChangeTracker.Entries())
                {
                    entry.State = EntityState.Detached;
                }

                if (pg.SqlState == "23505")
                {
                    throw new UsernameAlreadyTakenException(username);
                }

                if (pg.SqlState == "40001" && attempt < MaxRetries - 1)
                {
                    continue;
                }

                throw;
            }
        }

        throw new InvalidOperationException("GuestUpgradeService: SERIALIZABLE retries exhausted.");
    }

    /// <inheritdoc />
    public Task<LinkResult> UpgradeToLinkedOAuthAsync(
        Guid playerId,
        string provider,
        string externalId,
        CancellationToken cancellationToken = default) =>
        _identityLinker.LinkAsync(playerId, provider, externalId, cancellationToken);

    /// <summary>
    /// Walks an exception's InnerException chain (bounded to a small depth) looking for a
    /// <see cref="PostgresException"/>. Needed because Npgsql's default execution strategy wraps
    /// transient failures (incl. 40001 serialization_failure) in
    /// <see cref="InvalidOperationException"/>, and EF Core further wraps the underlying
    /// provider exception in <see cref="DbUpdateException"/>. A plain
    /// <c>when (ex.InnerException is PostgresException pg)</c> pattern misses both wrappings.
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
