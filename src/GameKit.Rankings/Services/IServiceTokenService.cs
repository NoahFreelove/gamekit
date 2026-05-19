// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Rankings.Entities;

namespace GameKit.Rankings.Services;

/// <summary>
/// Service-token DTO for list operations. Never includes <c>TokenHash</c> or the raw token.
/// </summary>
/// <param name="Id">Row id.</param>
/// <param name="Name">Operator-supplied label.</param>
/// <param name="CreatedAt">UTC timestamp at which the token was minted.</param>
/// <param name="ExpiresAt">UTC expiry timestamp, or <see langword="null"/> if the token never expires.</param>
/// <param name="RevokedAt">UTC revocation timestamp, or <see langword="null"/> if the token is still active.</param>
/// <param name="LastUsedAt">UTC timestamp of the last successful authentication, or <see langword="null"/> if never used.</param>
public sealed record ServiceTokenSummaryDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Manages service-account bearer tokens used to authenticate calls to
/// <c>POST /api/sessions/{id}/complete</c> (D-05 / D-06). Tokens are minted via
/// <c>dotnet gamekit service-token issue</c>; raw bearer is printed once to stdout;
/// only the SHA-256 hex digest is stored in <c>service_tokens</c>.
/// </summary>
public interface IServiceTokenService
{
    /// <summary>
    /// Mints a new service token. Generates a 32-byte cryptographically random raw bearer,
    /// SHA-256-hashes it, inserts the <see cref="ServiceToken"/> row, and returns the
    /// raw bearer (printed exactly once by the CLI verb).
    /// </summary>
    /// <param name="name">Operator label for the token. Must be unique (case-insensitive).</param>
    /// <param name="expiresAt">Optional UTC expiry. <see langword="null"/> means the token never expires.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of the raw bearer string (never stored) and the persisted <see cref="ServiceToken"/> row.</returns>
    /// <exception cref="ServiceTokenNameAlreadyExistsException">Thrown when <paramref name="name"/> is already taken.</exception>
    Task<(string Raw, ServiceToken Row)> IssueAsync(string name, DateTimeOffset? expiresAt, CancellationToken ct);

    /// <summary>
    /// Revokes the named token by setting its <c>RevokedAt</c> timestamp. Idempotent —
    /// calling revoke on an already-revoked token returns <see langword="true"/>.
    /// </summary>
    /// <param name="name">Operator label of the token to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the token was found; <see langword="false"/> if no matching token exists.</returns>
    Task<bool> RevokeAsync(string name, CancellationToken ct);

    /// <summary>
    /// Returns a summary of all service tokens. Never includes <c>TokenHash</c> or the raw token.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ServiceTokenSummaryDto>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Looks up a <see cref="ServiceToken"/> by SHA-256-hashing the supplied raw bearer value.
    /// Used by <c>ServiceTokenAuthenticationHandler</c> on every authenticated request (Pitfall 10 —
    /// DB hot-read accepted for v1; v2 may add <c>IMemoryCache</c> TTL optimization).
    /// </summary>
    /// <param name="raw">The raw bearer token extracted from the <c>Authorization</c> header.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching row, or <see langword="null"/> if the hash is not found.</returns>
    Task<ServiceToken?> FindByRawAsync(string raw, CancellationToken ct);
}

/// <summary>
/// Thrown by <see cref="IServiceTokenService.IssueAsync"/> when the requested token name is
/// already registered in <c>service_tokens</c>.
/// </summary>
public sealed class ServiceTokenNameAlreadyExistsException : Exception
{
    /// <summary>Constructs the exception with the duplicate name.</summary>
    public ServiceTokenNameAlreadyExistsException(string name)
        : base($"A service token with name '{name}' already exists. Token names must be unique (case-insensitive).") { }
}
