// SPDX-License-Identifier: Apache-2.0
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

    /// <summary>
    /// Returns <c>true</c> when <paramref name="hash"/> was produced by a prior hasher and
    /// should be transparently re-hashed on the next successful login.
    /// <c>BCryptPasswordHasher</c> always returns <c>false</c> (no upgrade path from BCrypt to BCrypt).
    /// <c>Argon2idPasswordHasher</c> returns <c>true</c> for <c>$2a$</c> / <c>$2b$</c> prefixes,
    /// signalling that a BCrypt hash should be upgraded to Argon2id on the next login.
    /// </summary>
    /// <param name="hash">The previously-stored hash string to inspect.</param>
    /// <returns>
    /// <c>true</c> when the hash was produced by a different (older) hasher and transparent
    /// re-hashing should occur; <c>false</c> when no re-hash is needed.
    /// </returns>
    bool NeedsRehash(string hash);
}
