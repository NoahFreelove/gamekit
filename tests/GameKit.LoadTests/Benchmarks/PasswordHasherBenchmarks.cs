// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using BenchmarkDotNet.Attributes;
using GameKit.Auth;
using GameKit.Auth.Argon2.Configuration;
using GameKit.Auth.Argon2.Services;
using GameKit.Auth.Services;

namespace GameKit.LoadTests.Benchmarks;

/// <summary>
/// Benchmarks password-hash verification at production parameters:
/// <list type="bullet">
///   <item>BCrypt: work factor 12 (default, per <see cref="PasswordOptions.BCryptWorkFactor"/>).</item>
///   <item>Argon2id: m=65536 KiB, t=3, p=1 (OWASP 2025 recommended defaults,
///         per <see cref="GameKitArgon2Options"/> zero-arg ctor).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <strong>SECURITY / CORRECTNESS NOTE (19-RESEARCH.md Pitfall §2):</strong>
/// These benchmarks intentionally use <em>production</em> cost parameters — <b>NOT</b>
/// <c>AllowInsecureParametersForTesting = true</c> and not lowered cost factors.
/// The goal is to measure actual password-verification latency to inform the tuning guide
/// (PERF-05). A benchmark at test-safe params would report &lt;1 ms and be meaningless.
/// Expect each benchmark to run for ~100 ms per iteration on modern server hardware.
/// </para>
/// <para>
/// The benchmarks measure only the <c>Verify</c> path because that is the hot path on
/// every login request. Hashing (registration) is comparatively rare.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class PasswordHasherBenchmarks
{
    private const string BenchmarkPassword = "benchmarkpassword123!";

    private BCryptPasswordHasher _bcrypt = null!;
    private Argon2idPasswordHasher _argon2 = null!;
    private string _bcryptHash = null!;
    private string _argon2Hash = null!;

    /// <summary>
    /// Constructs both hashers at production parameters and pre-computes the hashes used
    /// in each benchmark iteration. Setup runs once; the hash computation cost is excluded
    /// from the measured path.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        // BCrypt work factor 12 = production default (PasswordOptions.BCryptWorkFactor = 12).
        // Do NOT use AllowInsecureParametersForTesting; do NOT lower BCryptWorkFactor.
        _bcrypt = new BCryptPasswordHasher(new GameKitAuthOptions
        {
            Password = { BCryptWorkFactor = 12 },
        });

        // Argon2id: zero-arg ctor uses production defaults (m=65536 KiB, t=3, p=1, hashLength=32).
        // Do NOT set AllowInsecureParametersForTesting = true.
        _argon2 = new Argon2idPasswordHasher(new GameKitArgon2Options());

        // Pre-compute hashes so each iteration pays only the Verify cost.
        _bcryptHash = _bcrypt.Hash(BenchmarkPassword);
        _argon2Hash = _argon2.Hash(BenchmarkPassword);
    }

    /// <summary>
    /// Verifies a BCrypt-hashed password at work factor 12. Expect ~100 ms per iteration
    /// on commodity server hardware (by design — this is the OWASP-mandated minimum latency).
    /// </summary>
    /// <returns><see langword="true"/> when the password matches the pre-computed hash.</returns>
    [Benchmark]
    public bool BCryptVerify() => _bcrypt.Verify(BenchmarkPassword, _bcryptHash);

    /// <summary>
    /// Verifies an Argon2id-hashed password at m=65536 KiB / t=3 / p=1. Expect ~100 ms per
    /// iteration on commodity server hardware (OWASP 2025 recommended target).
    /// Note: <see cref="Argon2idPasswordHasher.Verify"/> accepts the hash as the second argument,
    /// which in turn calls <c>Isopoh.Cryptography.Argon2.Argon2.Verify(hash, password)</c>
    /// (hash-first per Isopoh API — confirmed in Argon2idPasswordHasher source, line 90).
    /// </summary>
    /// <returns><see langword="true"/> when the password matches the pre-computed hash.</returns>
    [Benchmark]
    public bool Argon2idVerify() => _argon2.Verify(BenchmarkPassword, _argon2Hash);
}
