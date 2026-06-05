// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Services;

/// <summary>
/// <see cref="IPasswordHasher"/> backed by BCrypt.Net-Next 4.1.0. Work factor is configurable
/// via <see cref="PasswordOptions.BCryptWorkFactor"/> (default 12 per CONTEXT discretion).
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    /// <summary>Constructs the hasher; reads work factor from <see cref="GameKitAuthOptions.Password"/>.</summary>
    /// <param name="opts">The root auth options (work factor read from <see cref="PasswordOptions.BCryptWorkFactor"/>).</param>
    public BCryptPasswordHasher(GameKitAuthOptions opts)
    {
        _workFactor = opts.Password.BCryptWorkFactor;
    }

    /// <inheritdoc />
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Malformed / non-BCrypt hash — treat as a failed verification rather than bubbling.
            return false;
        }
    }

    /// <inheritdoc />
    // BCrypt is the default hasher; it never needs re-hash by a newer BCrypt hasher.
    // Returns false unconditionally — Argon2idPasswordHasher overrides this to return
    // true for $2a$/$2b$ prefixes.
    public bool NeedsRehash(string hash) => false;
}
