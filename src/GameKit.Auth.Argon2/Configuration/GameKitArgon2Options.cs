// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Argon2.Configuration;

/// <summary>
/// Configuration options for <see cref="GameKit.Auth.Argon2.Services.Argon2idPasswordHasher"/>.
/// All defaults meet or exceed OWASP 2025 minimums for Argon2id (m≥19456 KiB, t≥2 iterations).
/// </summary>
/// <remarks>
/// <para>
/// Tuning guidance: the default <see cref="MemoryCost"/> of 65536 KiB (64 MiB) and
/// <see cref="TimeCost"/> of 3 iterations are calibrated for a single core on commodity server
/// hardware (~100 ms per hash). Hosts expecting more than ~50 concurrent logins should consider
/// reducing <see cref="Lanes"/> / <see cref="Threads"/> or increasing worker processes instead
/// of lowering <see cref="MemoryCost"/> below the OWASP minimum (19456 KiB).
/// </para>
/// <para>
/// The BCrypt→Argon2 migration window (AUTH-18) is enabled by default: existing
/// <c>$2a$</c>/<c>$2b$</c>-prefixed hashes are verified by BCrypt.Verify and transparently
/// re-hashed with Argon2id on the next successful login without requiring a forced password reset.
/// </para>
/// </remarks>
public sealed class GameKitArgon2Options
{
    /// <summary>
    /// Argon2 memory cost in KiB (m parameter). Must be ≥ 19456 KiB (OWASP 2025 minimum).
    /// Default: 65536 (64 MiB) — OWASP recommended starting point.
    /// </summary>
    public int MemoryCost { get; set; } = 65536;

    /// <summary>
    /// Argon2 time cost — number of iterations (t parameter). Must be ≥ 2 (OWASP 2025 minimum).
    /// Default: 3.
    /// </summary>
    public int TimeCost { get; set; } = 3;

    /// <summary>
    /// Degree of parallelism — number of lanes (p parameter).
    /// Default: 1. Increase on multi-core hosts to amortize memory cost across concurrent hashes.
    /// </summary>
    public int Lanes { get; set; } = 1;

    /// <summary>
    /// Number of threads used for hashing. Should be ≤ <see cref="Lanes"/>.
    /// Default: 1.
    /// </summary>
    public int Threads { get; set; } = 1;

    /// <summary>
    /// Length of the derived hash output in bytes.
    /// Default: 32 (256-bit output — exceeds 128-bit minimum).
    /// </summary>
    public int HashLength { get; set; } = 32;
}
