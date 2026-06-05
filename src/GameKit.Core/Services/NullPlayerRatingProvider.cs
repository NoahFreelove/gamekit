// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;

/// <summary>
/// Null-object default for <see cref="IPlayerRatingProvider"/>. Returns an empty dictionary for every
/// query so Core-only and Matchmaking-without-Rankings installs operate in zero-rated (v1) mode
/// without throwing. Registered via <c>TryAddSingleton</c> in <c>AddGameKit()</c> (CORE-18) so
/// <c>GameKit.Rankings</c> (Phase 8) can override it by registering its own provider after
/// <c>AddGameKit()</c> completes.
/// </summary>
internal sealed class NullPlayerRatingProvider : IPlayerRatingProvider
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<Guid, PlayerRatingValue>> GetRatingsAsync(
        IReadOnlyCollection<Guid> playerIds,
        Guid ladderId,
        CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyDictionary<Guid, PlayerRatingValue>>(
            ImmutableDictionary<Guid, PlayerRatingValue>.Empty);
}
