// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Services;

/// <summary>
/// CONTEXT D-13 computed-property check: a player is a guest iff they have no identities
/// AND no credentials. Called by <see cref="IJwtIssuer"/> in the same request scope so the
/// <c>is_guest</c> claim cannot drift from the database state.
/// </summary>
public interface IIsGuestResolver
{
    /// <summary>Returns true if the player has no identities and no credentials.</summary>
    /// <param name="playerId">Player primary key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the player has no <c>PlayerIdentity</c> rows and no <c>PlayerCredential</c> row.</returns>
    Task<bool> IsGuestAsync(Guid playerId, CancellationToken cancellationToken = default);
}
