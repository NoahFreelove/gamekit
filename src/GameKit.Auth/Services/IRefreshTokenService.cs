// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Services;

/// <summary>
/// Rotates refresh tokens using Pattern 3 (reuse-interval with fingerprint gate). The service owns:
/// <list type="bullet">
///   <item>Happy-path rotation: parent → child (Guid-keyed), parent marked revoked + replaced_by.</item>
///   <item>Grace window: within 45 s of parent's <c>UsedAt</c> AND fingerprint matches → re-issue already-created child (idempotent).</item>
///   <item>Family revoke: reuse outside grace OR fingerprint mismatch → UPDATE all family rows to revoked.</item>
///   <item>Issuing the initial root token on login (no parent).</item>
/// </list>
/// Also exposes family-wide revocation for <c>/auth/logout</c> and <c>/auth/logout/all</c>.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Issues the initial refresh-token family root for a newly-logged-in player; returns a fresh <see cref="TokenPair"/>.</summary>
    /// <param name="playerId">The subject player id.</param>
    /// <param name="provider">Provider discriminator (<c>steam</c>, <c>discord</c>, <c>guest</c>, <c>password</c>).</param>
    /// <param name="fingerprint">Optional client device fingerprint (X-GameKit-Device header value).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The issued <see cref="TokenPair"/> with non-null <c>RawRefresh</c>.</returns>
    Task<TokenPair> IssueRootAsync(Guid playerId, string provider, string? fingerprint, CancellationToken cancellationToken = default);

    /// <summary>Rotates an existing refresh token per Pattern 3.</summary>
    /// <param name="rawRefreshToken">The raw refresh token the client presented.</param>
    /// <param name="fingerprint">Client fingerprint gating grace-window replay.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="TokenPair"/>. <c>RawRefresh</c> is null when the server returned the already-issued child (idempotent replay).</returns>
    /// <exception cref="UnauthorizedException">Thrown when the token is unknown, expired, or the family was revoked.</exception>
    Task<TokenPair> RotateAsync(string rawRefreshToken, string? fingerprint, CancellationToken cancellationToken = default);

    /// <summary>Revokes the family containing <paramref name="rawRefreshToken"/>. Used by <c>/auth/logout</c>.</summary>
    /// <param name="rawRefreshToken">The raw refresh token whose family should be revoked.</param>
    /// <param name="reason">Stable reason string (e.g. <c>manual_logout</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeFamilyAsync(string rawRefreshToken, string reason, CancellationToken cancellationToken = default);

    /// <summary>Revokes every family belonging to <paramref name="playerId"/>. Used by <c>/auth/logout/all</c>.</summary>
    /// <param name="playerId">The player whose refresh families should be revoked.</param>
    /// <param name="reason">Stable reason string (e.g. <c>logout_all</c>, <c>admin_ban</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeAllForPlayerAsync(Guid playerId, string reason, CancellationToken cancellationToken = default);
}
