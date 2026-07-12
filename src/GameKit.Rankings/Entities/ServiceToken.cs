// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Rankings.Entities;

/// <summary>
/// Hashed service-account bearer token. Raw token is issued to the operator exactly once
/// (stdout at mint time); only the SHA-256 hex digest is persisted here — mirroring the
/// Phase-2 refresh-token storage discipline (D-06).
/// </summary>
/// <remarks>
/// <para>
/// Service tokens are minted via <c>dotnet gamekit service-token issue --name &lt;name&gt;</c>
/// and are used by the game's authoritative server to call
/// <c>POST /api/sessions/{id}/complete</c> (D-05).
/// </para>
/// <para>
/// <c>Name</c> uses <c>citext</c> for case-insensitive uniqueness (same pattern as
/// <c>PlayerCredential.Username</c>). <c>TokenHash</c> is the 64-char lower-hex SHA-256
/// of the raw bearer token; lookups go through this hash, never the raw value.
/// </para>
/// </remarks>
public sealed class ServiceToken
{
    /// <summary>Row id — UUIDv7 from <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Operator-supplied label for this token (e.g. <c>"game-server-prod"</c>).
    /// Case-insensitive uniqueness enforced via <c>citext</c> column type.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// SHA-256 hex (64 chars, lower-case) of the raw bearer token.
    /// Raw value is never stored — it is printed to stdout once at mint time (D-06).
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>UTC timestamp at which this token was minted.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp at which this token expires. Null means the token never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>UTC timestamp at which this token was revoked via <c>dotnet gamekit service-token revoke</c>. Null while active.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>UTC timestamp of the most recent successful authenticated request. Updated by <c>ServiceTokenAuthenticationHandler</c>.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
