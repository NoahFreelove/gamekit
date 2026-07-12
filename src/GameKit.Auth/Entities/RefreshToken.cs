// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Auth.Entities;

/// <summary>
/// Hashed refresh-token row with a parent→child rotation chain. <c>TokenHash</c> is SHA-256(raw);
/// raw token is issued to the client exactly once (AUTH-V2-04 pitfall mitigation). <c>FamilyId</c>
/// stays constant across the rotation chain so reuse-detection revokes the whole family atomically
/// (AUTH-11). <c>DeviceFingerprint</c> is the X-GameKit-Device UUID (CONTEXT D-05).
/// </summary>
public sealed class RefreshToken
{
    /// <summary>Row id — UUIDv7 from <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <c>players.id</c> (ON DELETE CASCADE).</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Family (session) id — stable across a parent→child rotation chain; used for family-wide revocation.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>SHA-256 hex (64 chars) of the raw refresh token. Raw value is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>SHA-256 of the child token that replaced this row at rotation (nullable until rotated).</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Client-supplied device fingerprint (X-GameKit-Device header) stored at first login / register.</summary>
    public string? DeviceFingerprint { get; set; }

    /// <summary>Provider that issued the root of this family — <c>steam</c>, <c>discord</c>, <c>guest</c>, <c>password</c>.</summary>
    public required string Provider { get; set; }

    /// <summary>UTC timestamp at which this row was issued.</summary>
    public DateTimeOffset IssuedAt { get; set; }

    /// <summary>UTC timestamp at which this row expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>First time the row was redeemed by <c>/auth/refresh</c> (set on rotation).</summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>Set at rotation OR at family revocation (reuse detection / manual logout).</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
