// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Microsoft.IdentityModel.Tokens;

namespace GameKit.LoadTests.Benchmarks;

/// <summary>
/// Benchmarks <see cref="JwtSecurityTokenHandler.ValidateToken"/> at production parameters —
/// RSA-SHA256, ValidateIssuer/Audience/Lifetime/SigningKey all <see langword="true"/>,
/// ClockSkew = 30 s. Mirrors <c>AuthBuilderExtensions.TokenValidationParameters</c> exactly.
/// </summary>
/// <remarks>
/// A valid JWT is pre-issued in <see cref="Setup"/> so the [Benchmark] measures only the
/// validation hot-path, not token issuance (which lives in JwtIssuer, a separate seam).
/// </remarks>
[MemoryDiagnoser]
public class JwtValidationBenchmarks
{
    private JwtSecurityTokenHandler _handler = null!;
    private TokenValidationParameters _params = null!;
    private string _token = null!;

    /// <summary>
    /// Creates an in-process RSA-2048 key, issues a valid 1-hour JWT, and builds
    /// <see cref="TokenValidationParameters"/> mirroring <c>AuthBuilderExtensions</c>.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        // Generate ephemeral RSA-2048 key (same approach as JwtIssuer + AuthBuilderExtensions).
        // In production the private key is loaded from a PEM file at startup; here we generate
        // in-process to keep the benchmark self-contained and free of file I/O.
        var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "bench-kid" };
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        {
            // Disable CryptoProvider caching so the benchmark exercises the full signing path
            // without short-circuiting via a cached signer — mirrors LoadTestFixture.MintPlayerJwt.
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };

        _handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        // Pre-issue a valid token with a 1-hour expiry so ValidateLifetime passes throughout
        // the benchmark run (BDN runs warm-up + many iterations, which can exceed seconds).
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer             = "gk-bench",
            Audience           = "gk-bench",
            Expires            = DateTime.UtcNow.AddHours(1),
            SigningCredentials  = creds,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("is_guest", "false"),
                new Claim("provider", "bench"),
            }),
        };
        _token = _handler.WriteToken(_handler.CreateToken(descriptor));

        // Build TokenValidationParameters mirroring AuthBuilderExtensions (line 199).
        // ValidateIssuerSigningKey = true, RequireSignedTokens = true are both explicitly set
        // so the benchmark exercises the full verification stack.
        _params = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = "gk-bench",
            ValidateAudience         = true,
            ValidAudience            = "gk-bench",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = signingKey,
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
            RequireSignedTokens      = true,
        };
    }

    /// <summary>
    /// Validates the pre-issued JWT token. This is the hot path exercised on every authenticated
    /// HTTP request — RSA-SHA256 signature verification + claims extraction.
    /// </summary>
    /// <returns>The <see cref="ClaimsPrincipal"/> extracted from the token.</returns>
    [Benchmark]
    public ClaimsPrincipal ValidateToken()
        => _handler.ValidateToken(_token, _params, out _);
}
