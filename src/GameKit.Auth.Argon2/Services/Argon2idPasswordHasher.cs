// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Auth.Argon2.Configuration;
using GameKit.Auth.Services;
using Isopoh.Cryptography.Argon2;

namespace GameKit.Auth.Argon2.Services;

/// <summary>
/// <see cref="IPasswordHasher"/> backed by Argon2id (Isopoh.Cryptography.Argon2 2.0.0, CC0).
/// </summary>
/// <remarks>
/// <para>
/// Default parameters (m=65536 KiB, t=3, p=1) meet OWASP 2025 Argon2id minimums. Parameters
/// are configurable via <see cref="GameKitArgon2Options"/> passed to <c>UseArgon2()</c>.
/// </para>
/// <para>
/// <strong>Live migration (AUTH-18):</strong> <see cref="Verify"/> accepts both Argon2id hashes
/// (<c>$argon2id$</c> prefix) and legacy BCrypt hashes (<c>$2a$</c>/<c>$2b$</c> prefix).
/// <see cref="NeedsRehash"/> returns <c>true</c> for BCrypt prefixes so
/// <c>PasswordOAuthProvider</c> can transparently upgrade hashes on the next successful login
/// without a forced password reset or schema migration.
/// </para>
/// <para>
/// <strong>Diamond-dependency note:</strong> This package references <c>BCrypt.Net-Next</c>
/// solely for the live migration verify path. Once all active users have been migrated,
/// BCrypt.Verify calls will never be reached (NeedsRehash returns false for Argon2id hashes).
/// </para>
/// </remarks>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private readonly GameKitArgon2Options _opts;

    /// <summary>
    /// Constructs the hasher from the provided Argon2 options.
    /// </summary>
    /// <param name="opts">Argon2 parameters (memory cost, time cost, lanes, threads, hash length).</param>
    public Argon2idPasswordHasher(GameKitArgon2Options opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        _opts = opts;
    }

    /// <inheritdoc />
    /// <remarks>Returns a self-contained <c>$argon2id$v=19$m=...,t=...,p=...$...</c> encoded string.</remarks>
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        // Use the static string-overload which handles random salt generation internally.
        // Argon2Config.Salt=null requires Argon2 to generate a random salt, but the
        // Hash(Argon2Config) static overload only produces a complete encoded string when
        // provided an explicit salt. The simple string overload handles this correctly.
        return Isopoh.Cryptography.Argon2.Argon2.Hash(
            password:    password,
            timeCost:    _opts.TimeCost,
            memoryCost:  _opts.MemoryCost,
            parallelism: _opts.Lanes,
            type:        Argon2Type.HybridAddressing,
            hashLength:  _opts.HashLength,
            secureArrayCall: Isopoh.Cryptography.SecureArray.SecureArray.DefaultCall)
            ?? throw new InvalidOperationException("Argon2 hash produced a null encoded string.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Dispatches on hash prefix: <c>$2a$</c>/<c>$2b$</c> → BCrypt.Verify (live migration window);
    /// otherwise → Argon2.Verify (encoded hash is the first argument per Isopoh API).
    /// Malformed hashes return <c>false</c> rather than throwing.
    /// </remarks>
    public bool Verify(string password, string hash)
    {
        if (hash.StartsWith("$2a$", StringComparison.Ordinal) ||
            hash.StartsWith("$2b$", StringComparison.Ordinal))
        {
            // BCrypt hash — verify with BCrypt so live migration can proceed.
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
        }

        // Argon2id hash — Isopoh API: encoded hash is the FIRST argument (proven by round-trip test).
        return Isopoh.Cryptography.Argon2.Argon2.Verify(hash, password);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <c>true</c> for BCrypt prefixes (<c>$2a$</c>/<c>$2b$</c>), signalling to
    /// <c>PasswordOAuthProvider</c> that the credential should be transparently upgraded to
    /// Argon2id on the next successful login. Returns <c>false</c> for Argon2id hashes.
    /// </remarks>
    public bool NeedsRehash(string hash)
        => hash.StartsWith("$2a$", StringComparison.Ordinal) ||
           hash.StartsWith("$2b$", StringComparison.Ordinal);
}
