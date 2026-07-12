// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Auth.Services;

namespace GameKit.Auth.Providers;

/// <summary>
/// Outcome of <see cref="IOAuthProvider.CompleteLoginAsync"/> — either success or failure with
/// an error code. Use <see cref="Ok"/> / <see cref="Fail"/> to construct instances.
/// </summary>
/// <param name="Success">True iff the login succeeded.</param>
/// <param name="PlayerId">The GameKit player id bound to the external identity on success; null on failure.</param>
/// <param name="Tokens">The issued access + refresh pair on success; null on failure.</param>
/// <param name="ErrorCode">Stable error discriminator on failure; null on success.</param>
public sealed record OAuthResult(bool Success, Guid? PlayerId, TokenPair? Tokens, string? ErrorCode)
{
    /// <summary>Builds a successful result.</summary>
    public static OAuthResult Ok(Guid playerId, TokenPair tokens) => new(true, playerId, tokens, null);

    /// <summary>Builds a failure result.</summary>
    public static OAuthResult Fail(string errorCode) => new(false, null, null, errorCode);
}
