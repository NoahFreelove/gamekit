// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace GameKit.Admin.Integration.Tests.Mocks;

/// <summary>
/// Mints a valid GameKit player JWT signed with a test-only RSA keypair. Used ONLY by the
/// Success Criterion #6 isolation test (plan 03-13) — proves that a perfectly valid player
/// JWT cannot authenticate into any <c>/admin/*</c> route because the admin cookie scheme
/// and the player JWT scheme are strictly disjoint (D-02).
/// <para>
/// Claim shape follows D-03: <c>sub</c> = player id, <c>provider</c> = login provider
/// (<c>guest</c> by default for the isolation test), <c>sid</c> = refresh-token family id.
/// <c>MapInboundClaims = false</c> semantics are preserved — no Microsoft claim-type remapping.
/// </para>
/// </summary>
public sealed class FakePlayerJwtIssuer : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly string _issuer;
    private readonly string _audience;

    /// <summary>
    /// Constructs a fresh issuer with a throwaway RSA keypair. Issuer + audience default to
    /// <c>gamekit.test</c> so the resulting token validates against the test harness' JWT
    /// validation parameters without extra configuration.
    /// </summary>
    /// <param name="issuer">Value to place in the <c>iss</c> claim.</param>
    /// <param name="audience">Value to place in the <c>aud</c> claim.</param>
    public FakePlayerJwtIssuer(string issuer = "gamekit.test", string audience = "gamekit.test")
    {
        _issuer = issuer;
        _audience = audience;
    }

    /// <summary>
    /// Public half of the throwaway RSA keypair — register it in the test harness'
    /// <c>TokenValidationParameters.IssuerSigningKey</c> to make the minted JWT pass
    /// signature validation. Never returned to production code.
    /// </summary>
    public RsaSecurityKey PublicSigningKey => new(_rsa.ExportParameters(false));

    /// <summary>
    /// Mints a valid player JWT with the D-03 claim shape. Default lifetime = 15 minutes
    /// (matches the GameKit access-token TTL from <c>GameKitAuthOptions.Jwt.AccessTokenLifetime</c>).
    /// </summary>
    /// <param name="playerId">Player id to place in the <c>sub</c> claim.</param>
    /// <param name="sessionId">Refresh-token family id to place in the <c>sid</c> claim.</param>
    /// <param name="lifetime">Optional token lifetime; defaults to 15 minutes.</param>
    /// <returns>The serialized JWT string ready for an <c>Authorization: Bearer</c> header.</returns>
    public string IssueValidPlayerJwt(Guid playerId, Guid sessionId, TimeSpan? lifetime = null)
    {
        var creds = new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256);
        var now = DateTime.UtcNow;
        var exp = now.Add(lifetime ?? TimeSpan.FromMinutes(15));
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: new[]
            {
                new Claim("sub", playerId.ToString()),
                new Claim("provider", "guest"),
                new Claim("sid", sessionId.ToString()),
            },
            notBefore: now,
            expires: exp,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Scrubs the throwaway RSA keypair from memory.</summary>
    public void Dispose() => _rsa.Dispose();
}
