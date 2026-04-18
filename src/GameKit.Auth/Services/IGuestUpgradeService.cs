// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Services;

/// <summary>
/// Guest-account upgrade service (AUTH-13, CONTEXT D-12). In-place upgrade: same
/// <c>Player.Id</c>, a new <c>PlayerCredential</c> (password path) or <c>PlayerIdentity</c>
/// (OAuth path). Because <see cref="IIsGuestResolver"/> returns false once any credential or
/// identity exists, the re-issued JWT no longer carries <c>is_guest=true</c> (D-13).
/// </summary>
public interface IGuestUpgradeService
{
    /// <summary>
    /// Attaches a username + password credential to an existing player (typically a guest).
    /// Inserts the <c>PlayerCredential</c> row inside a Postgres SERIALIZABLE transaction and
    /// issues a fresh <see cref="TokenPair"/> whose JWT no longer carries <c>is_guest=true</c>.
    /// </summary>
    /// <param name="playerId">The existing player id to upgrade.</param>
    /// <param name="username">The desired username (subject to the CITEXT UNIQUE index).</param>
    /// <param name="password">The plaintext password (hashed before persistence).</param>
    /// <param name="fingerprint">Optional client device fingerprint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A fresh <see cref="TokenPair"/> carrying a non-guest JWT.</returns>
    /// <exception cref="UsernameAlreadyTakenException">
    /// Thrown when a concurrent register won the UNIQUE(Username) race. The endpoint layer
    /// translates this to HTTP 409 (RESEARCH §15 open question #3).
    /// </exception>
    Task<TokenPair> UpgradeToPasswordAsync(
        Guid playerId,
        string username,
        string password,
        string? fingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Links an OAuth identity (Steam / Discord / etc.) to an existing player. Thin wrapper over
    /// <see cref="IIdentityLinker.LinkAsync"/>; caller maps the returned <see cref="LinkResult"/>
    /// to HTTP codes at the endpoint layer.
    /// </summary>
    /// <param name="playerId">The existing player id to upgrade.</param>
    /// <param name="provider">Provider discriminator (<c>steam</c>, <c>discord</c>, etc.).</param>
    /// <param name="externalId">Provider external id (verified by the endpoint layer before this call).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="LinkResult"/> carrying the outcome.</returns>
    Task<LinkResult> UpgradeToLinkedOAuthAsync(
        Guid playerId,
        string provider,
        string externalId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown by <see cref="IGuestUpgradeService.UpgradeToPasswordAsync"/> when a concurrent register
/// won the UNIQUE(Username) race — the endpoint layer maps this to HTTP 409 <c>username_taken</c>.
/// </summary>
public sealed class UsernameAlreadyTakenException : Exception
{
    /// <summary>The username that was contested.</summary>
    public string Username { get; }

    /// <summary>Constructs the exception.</summary>
    /// <param name="username">The contested username.</param>
    public UsernameAlreadyTakenException(string username)
        : base($"Username '{username}' is already taken.")
    {
        Username = username;
    }
}
