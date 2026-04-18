// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Auth.Entities;

/// <summary>
/// Username + password credential for a GameKit <c>Player</c>. One row per player at most.
/// Separate from <c>PlayerIdentity</c> so a player may hold both (e.g. Discord identity + fallback
/// username/password). Password hashing uses <c>IPasswordHasher</c> (BCrypt by default; Argon2 via
/// the v2 sibling package per AUTH-V2-01).
/// </summary>
public sealed class PlayerCredential
{
    /// <summary>PK (and FK to <c>players.id</c> — ON DELETE CASCADE). Exactly one credential per player.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Case-insensitive-unique username (RFC-lite, 3-32 chars). Enforced by Postgres CITEXT-shaped UNIQUE.</summary>
    public required string Username { get; set; }

    /// <summary>BCrypt-format password hash (includes salt + work factor). Never store plaintext or pepper.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>UTC timestamp at which <c>PasswordHash</c> was last set / rotated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
