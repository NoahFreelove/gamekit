// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Services;

/// <summary>
/// Password hashing + verification abstraction. Default implementation is
/// <see cref="BCryptPasswordHasher"/> using BCrypt.Net-Next. AUTH-16 allows a future
/// <c>Argon2idPasswordHasher</c> sibling package (AUTH-V2-01) to be a drop-in replacement.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Returns a self-contained hash string (salt + work factor + ciphertext).</summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>An opaque hash string the same hasher can later verify.</returns>
    string Hash(string password);

    /// <summary>Returns true iff <paramref name="password"/> verifies against <paramref name="hash"/>.</summary>
    /// <param name="password">The plaintext password supplied by the caller.</param>
    /// <param name="hash">The previously-stored hash string produced by <see cref="Hash(string)"/>.</param>
    /// <returns><c>true</c> when the password matches the hash; <c>false</c> for any mismatch or malformed input.</returns>
    bool Verify(string password, string hash);
}
