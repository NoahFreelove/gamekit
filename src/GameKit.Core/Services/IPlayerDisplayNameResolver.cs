// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;

/// <summary>
/// Resolves a display name for any <see cref="Guid"/>? player id, returning the configured
/// <c>DeletedPlayerDisplayName</c> tombstone when the id is null (post-GDPR-delete) or missing.
/// Single source of truth per design decision D-11 — sibling packages must not hand-render deleted-player names.
/// </summary>
public interface IPlayerDisplayNameResolver
{
    /// <summary>Returns the live display name, or the configured tombstone when <paramref name="playerId"/> is null or missing.</summary>
    /// <param name="playerId">Player id to resolve; null returns the tombstone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<string> ResolveAsync(Guid? playerId, CancellationToken cancellationToken = default);
}
