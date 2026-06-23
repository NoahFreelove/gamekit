// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;

namespace GameKit.Core.Services;

/// <summary>
/// Contract that sibling GameKit packages implement to delete rows in tables whose FK to
/// <c>players</c> uses <c>ON DELETE RESTRICT</c> (or any package-owned data the package
/// must erase as part of a GDPR right-to-erasure request).
/// </summary>
/// <remarks>
/// <para>
/// This mirrors the <see cref="GameKit.Core.Data.IModelBuilderExtension"/> pattern (SEC-04 Option A,
/// <c>.planning/phases/18-security-audit/18-RESEARCH.md §SEC-04</c>): Core defines the hook; sibling
/// packages register implementations; the Core service iterates them without knowing which packages
/// are installed.
/// </para>
/// <para>
/// <b>Transaction contract:</b> Every implementation <b>MUST NOT</b> open its own database
/// transaction or call <c>CommitAsync</c>. <see cref="GdprDeleteService"/> invokes each
/// implementation inside the existing <c>SERIALIZABLE</c> transaction so all pre-delete cleanup
/// and the final player-row delete are committed atomically. Opening a nested transaction would
/// produce a partially-erased player on rollback — a GDPR violation.
/// </para>
/// <para>
/// <b>Registration:</b> Register at startup via:
/// <code>
/// services.TryAddEnumerable(ServiceDescriptor.Scoped&lt;IGdprDeleteExtension, MyGdprDeleteExtension&gt;());
/// </code>
/// Using <c>TryAddEnumerable</c> (rather than plain <c>AddScoped</c>) prevents duplicate
/// registrations if <c>AddMyPackage</c> is called multiple times.
/// </para>
/// <para>
/// When no implementations are registered, <see cref="GdprDeleteService"/> resolves an empty
/// <c>IEnumerable&lt;IGdprDeleteExtension&gt;</c> and the deletion proceeds with only the
/// Core-owned cascade rules — the same behavior as before SEC-04.
/// </para>
/// </remarks>
public interface IGdprDeleteExtension
{
    /// <summary>
    /// Performs package-owned pre-delete cleanup for the player identified by
    /// <paramref name="playerId"/>, running inside the caller's <c>SERIALIZABLE</c> transaction.
    /// </summary>
    /// <param name="ctx">
    /// The <see cref="GameKitDbContext"/> already enlisted in the ambient transaction.
    /// Do <b>not</b> call <c>BeginTransactionAsync</c> or <c>CommitAsync</c> on this context.
    /// </param>
    /// <param name="playerId">The player being erased.</param>
    /// <param name="cancellationToken">Propagated from the caller.</param>
    /// <returns>A task that completes when the package-owned rows have been removed.</returns>
    Task DeletePlayerDataAsync(GameKitDbContext ctx, Guid playerId, CancellationToken cancellationToken);
}
