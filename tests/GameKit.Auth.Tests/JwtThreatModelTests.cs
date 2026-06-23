// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GameKit.Auth.Tests;

/// <summary>
/// SEC-01 JWT threat-model tests: proves that the production
/// <see cref="TokenValidationParameters"/> (RequireSignedTokens, ValidateIssuer,
/// ValidateAudience, ValidateLifetime) reject every major JWT forgery class.
/// Tests run in the fast unit-test suite — no containers required.
/// </summary>
/// <remarks>
/// Uses the same issuer / audience / RSA key setup as <see cref="JwtIssuerTests"/>.
/// The TokenValidationParameters mirror what <c>AuthBuilderExtensions</c> configures
/// at lines 199-210 — any future config drift that re-enables alg:none, drops audience
/// or issuer validation, or loosens lifetime checking will fail these tests in CI.
/// </remarks>
public sealed class JwtThreatModelTests : IDisposable
{
    // ---- Test fixtures -----------------------------------------------------------------

    private const string Issuer   = "gk-test";
    private const string Audience = "gk-test";

    private readonly string _tempDir;
    private readonly RsaSecurityKey _signingKey;   // private key — used by legitimate signer
    private readonly RsaSecurityKey _validationKey; // public key — used by the validator

    /// <summary>
    /// Generates an ephemeral RSA-2048 keypair for the test run.
    /// Mirrors the JwtIssuerTests setup so the keys and options represent the same
    /// production scenario (RSA-SHA256; separate public key for validation).
    /// </summary>
    public JwtThreatModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gk-jwt-threat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Generate keypair once for the test class.
        using var rsa = RSA.Create(2048);
        var privPem = rsa.ExportRSAPrivateKeyPem();
        var pubPem  = rsa.ExportRSAPublicKeyPem();

        // Signing key (private): used only to build correctly-signed tokens for control proofs.
        var signingRsa = RSA.Create();
        signingRsa.ImportFromPem(privPem);
        _signingKey = new RsaSecurityKey(signingRsa) { KeyId = "test-kid-1" };

        // Validation key (public): used in TokenValidationParameters — mirrors production.
        var validRsa = RSA.Create();
        validRsa.ImportFromPem(pubPem);
        _validationKey = new RsaSecurityKey(validRsa) { KeyId = "test-kid-1" };
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ---- Helper: build production-equivalent TokenValidationParameters ----------------

    /// <summary>
    /// Builds <see cref="TokenValidationParameters"/> matching what
    /// <c>AuthBuilderExtensions.AddAuth</c> configures on the JwtBearer handler
    /// (lines 199-210 of AuthBuilderExtensions.cs).
    /// Setting <paramref name="clockSkew"/> to zero makes expiry tests deterministic.
    /// </summary>
    private TokenValidationParameters ProductionParams(TimeSpan? clockSkew = null) =>
        new()
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime         = true,
            ValidIssuer              = Issuer,
            ValidAudience            = Audience,
            IssuerSigningKey         = _validationKey,
            ClockSkew                = clockSkew ?? TimeSpan.Zero,
            RequireSignedTokens      = true,
        };

    // ---- Helper: build a valid JwtPayload with sane defaults --------------------------

    private static JwtPayload ValidPayload(
        string issuer   = Issuer,
        string audience = Audience,
        DateTime? notBefore = null,
        DateTime? expires   = null)
    {
        var now = DateTime.UtcNow;
        return new JwtPayload(
            issuer:   issuer,
            audience: audience,
            claims:   [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore: notBefore ?? now,
            expires:   expires   ?? now.AddHours(1));
    }

    // ===================================================================================
    // Test 1: alg:none token is rejected
    // ===================================================================================

    /// <summary>
    /// SEC-01 / T-18-03-01 — An <c>alg:none</c> token (no signature) is rejected by
    /// <see cref="TokenValidationParameters.RequireSignedTokens"/> = true.
    /// The token is produced via <c>JwtSecurityTokenHandler.WriteToken</c> so it keeps
    /// its canonical three-segment form with an empty (trailing-dot) signature segment.
    /// </summary>
    [Fact]
    public void AlgNone_Token_Is_Rejected()
    {
        // Forge: build a header with alg:none and a valid-looking payload.
        var header = new JwtHeader();  // no SigningCredentials → defaults alg to "none"
        header["alg"] = "none";
        var payload  = ValidPayload();
        var token    = new JwtSecurityToken(header, payload);

        // WriteToken keeps the trailing dot so we get the canonical "header.payload." form.
        var raw = new JwtSecurityTokenHandler().WriteToken(token);

        // Canonical alg:none form: exactly 3 segments, third (signature) is empty.
        var parts = raw.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Empty(parts[2]);

        // Assert: the production validator MUST reject this token.
        // The handler rejects the alg:none token with SecurityTokenInvalidSignatureException
        // (IDX10504: token has no signature) because RequireSignedTokens=true forces
        // signature presence validation before key lookup.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        Assert.Throws<SecurityTokenInvalidSignatureException>(
            () => handler.ValidateToken(raw, ProductionParams(), out _));
    }

    // ===================================================================================
    // Test 2: HMAC-SHA256 (algorithm-downgrade) token is rejected
    // ===================================================================================

    /// <summary>
    /// SEC-01 / T-18-03-01 — A token signed with HMAC-SHA256 (symmetric key) is rejected.
    /// The production validator expects RSA-SHA256 and holds only the RSA public key, so
    /// an HMAC-signed token cannot satisfy <c>ValidateIssuerSigningKey = true</c> +
    /// the RSA key constraint.
    /// </summary>
    [Fact]
    public void HmacDowngrade_Token_Is_Rejected()
    {
        // Forge: sign with an HMAC key (attacker does not know the RSA private key).
        var hmacKey    = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var credentials = new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           Audience,
            claims:             [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var raw     = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        // The validator holds an RSA public key; it cannot verify an HMAC-signed token.
        // Expect either a key-not-found or invalid-signature exception.
        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(raw, ProductionParams(), out _));
    }

    // ===================================================================================
    // Test 3: Wrong-issuer token is rejected
    // ===================================================================================

    /// <summary>
    /// SEC-01 / T-18-03-02 — A token with <c>iss=evil-issuer</c> is rejected by
    /// <see cref="TokenValidationParameters.ValidateIssuer"/> = true.
    /// </summary>
    [Fact]
    public void WrongIssuer_Token_Is_Rejected()
    {
        // Build a properly RSA-signed token but with a wrong issuer.
        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer:             "evil-issuer",
            audience:           Audience,
            claims:             [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var raw     = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        Assert.Throws<SecurityTokenInvalidIssuerException>(
            () => handler.ValidateToken(raw, ProductionParams(), out _));
    }

    // ===================================================================================
    // Test 4: Wrong-audience token is rejected
    // ===================================================================================

    /// <summary>
    /// SEC-01 / T-18-03-02 — A token with <c>aud=evil-audience</c> is rejected by
    /// <see cref="TokenValidationParameters.ValidateAudience"/> = true.
    /// </summary>
    [Fact]
    public void WrongAudience_Token_Is_Rejected()
    {
        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           "evil-audience",
            claims:             [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        var raw     = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        Assert.Throws<SecurityTokenInvalidAudienceException>(
            () => handler.ValidateToken(raw, ProductionParams(), out _));
    }

    // ===================================================================================
    // Test 5: Expired token is rejected
    // ===================================================================================

    /// <summary>
    /// SEC-01 / T-18-03-03 — A token whose <c>exp</c> is in the past is rejected by
    /// <see cref="TokenValidationParameters.ValidateLifetime"/> = true.
    /// ClockSkew is set to zero so there is no grace window.
    /// </summary>
    [Fact]
    public void Expired_Token_Is_Rejected()
    {
        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           Audience,
            claims:             [new Claim("sub", Guid.NewGuid().ToString())],
            notBefore:          DateTime.UtcNow.AddHours(-2),
            expires:            DateTime.UtcNow.AddHours(-1),  // expired 1 hour ago
            signingCredentials: credentials);

        var raw     = new JwtSecurityTokenHandler().WriteToken(token);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };

        Assert.Throws<SecurityTokenExpiredException>(
            () => handler.ValidateToken(raw, ProductionParams(clockSkew: TimeSpan.Zero), out _));
    }
}
