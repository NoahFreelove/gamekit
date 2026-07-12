// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Services;

/// <summary>Issues signed JWT access tokens. See <see cref="JwtOptions"/> for lifetime + key configuration.</summary>
public interface IJwtIssuer
{
    /// <summary>Issues a JWT carrying the D-03 claim set; resolves <c>is_guest</c> via <see cref="IIsGuestResolver"/> in the same call.</summary>
    /// <param name="playerId">The subject player id.</param>
    /// <param name="familyId">The refresh-token family (session) id emitted as the <c>sid</c> claim.</param>
    /// <param name="provider">Authentication provider discriminator — <c>steam</c>, <c>discord</c>, <c>guest</c>, or <c>password</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The serialized, RS256-signed JWT.</returns>
    Task<string> IssueAsync(Guid playerId, Guid familyId, string provider, CancellationToken cancellationToken = default);
}
