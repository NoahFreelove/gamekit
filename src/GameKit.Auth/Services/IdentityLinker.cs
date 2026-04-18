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
/// Default <see cref="IIdentityLinker"/>: SERIALIZABLE transaction with a 3-attempt retry loop
/// on Postgres <c>40001</c> serialization_failure, and a hard-fail mapping from <c>23505</c>
/// unique_violation to <see cref="LinkResultKind.AlreadyLinkedToOtherPlayer"/>.
/// </summary>
/// <remarks>
/// The SERIALIZABLE + UNIQUE(provider, external_id) pair is the belt-and-suspenders that proves
/// ROADMAP success #4: under concurrent link attempts for the same external id targeting
/// different players, exactly one transaction commits and the other either (a) serialization-retries
/// and then hits 23505 on retry, or (b) loses the 23505 race on first attempt. Either path
/// produces <see cref="LinkResult.AlreadyLinkedToOtherPlayer"/> with a SHA-256 hash (never the
/// raw external id) per T-02-10.
/// </remarks>
internal sealed class IdentityLinker : IIdentityLinker
{
    private const int MaxRetries = 3;

    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IExternalIdHasher _hasher;
    private readonly IAuthAuditWriter _audit;

    /// <summary>Constructs the linker.</summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">UUIDv7 id generator.</param>
    /// <param name="hasher">Per-tuple external-id hasher used for 409 response bodies + audit payloads.</param>
    /// <param name="audit">Audit writer for success + collision rows.</param>
    public IdentityLinker(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator ids,
        IExternalIdHasher hasher,
        IAuthAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(audit);
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
        _hasher = hasher;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<LinkResult> LinkAsync(
        Guid playerId,
        string provider,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(externalId);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            await using var tx = await _ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                // Inside SERIALIZABLE: read-then-write is serialization-safe — Postgres will
                // abort one of two concurrent txs with 40001 if they race on the same external id.
                var existing = await _ctx.Set<PlayerIdentity>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        pi => pi.Provider == provider && pi.ExternalId == externalId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (existing is not null && existing.PlayerId != playerId)
                {
                    // Cross-player collision: another player already owns this (provider, externalId).
                    // Emit the audit row inside the SERIALIZABLE tx so the collision record shares
                    // the fate of the read that saw the row.
                    var hash = _hasher.Hash(provider, externalId);
                    await _audit.WriteAsync(
                        action: "auth.identity.link_failed_collision",
                        targetType: "player",
                        targetId: playerId,
                        actorId: playerId,
                        after: new { provider, external_id_hash = hash },
                        reason: "cross_player_collision",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return LinkResult.AlreadyLinkedToOtherPlayer(hash);
                }

                if (existing is not null)
                {
                    // Idempotent: already linked to me.
                    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return LinkResult.AlreadyLinkedToSelf();
                }

                _ctx.Set<PlayerIdentity>().Add(new PlayerIdentity
                {
                    Id = _ids.NewId(),
                    PlayerId = playerId,
                    Provider = provider,
                    ExternalId = externalId,
                    CreatedAt = _clock.UtcNow,
                    UpdatedAt = _clock.UtcNow,
                });
                await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await _audit.WriteAsync(
                    action: "auth.identity.linked",
                    targetType: "player_identity",
                    targetId: playerId,
                    actorId: playerId,
                    after: new { provider, external_id_hash = _hasher.Hash(provider, externalId) },
                    reason: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return LinkResult.Linked();
            }
            catch (Exception ex) when (TryFindPostgresException(ex) is { } pg)
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);

                // Detach in-flight entities so the scoped DbContext stays usable for the next
                // attempt / the post-catch audit write.
                foreach (var entry in _ctx.ChangeTracker.Entries())
                {
                    entry.State = EntityState.Detached;
                }

                if (pg.SqlState == "23505")
                {
                    // Lost the UNIQUE(provider, external_id) race — another player's row was committed
                    // between our SELECT and INSERT. Hash + audit + return collision.
                    var hash = _hasher.Hash(provider, externalId);
                    await _audit.WriteAsync(
                        action: "auth.identity.link_failed_collision",
                        targetType: "player",
                        targetId: playerId,
                        actorId: playerId,
                        after: new { provider, external_id_hash = hash },
                        reason: "cross_player_collision",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return LinkResult.AlreadyLinkedToOtherPlayer(hash);
                }

                if (pg.SqlState == "40001" && attempt < MaxRetries - 1)
                {
                    // serialization_failure — retry.
                    continue;
                }

                throw;
            }
        }

        throw new InvalidOperationException("IdentityLinker: SERIALIZABLE retries exhausted.");
    }

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
