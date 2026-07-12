// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using Microsoft.IdentityModel.Tokens;

namespace GameKit.Auth.Services;

/// <summary>
/// Default <see cref="IJwtIssuer"/> — signs with RSA-SHA256 using the configured PEM key.
/// Emits the D-03 claim set (<c>sub</c>, <c>jti</c>, <c>iat</c>, <c>iss</c>, <c>aud</c>,
/// <c>exp</c>, <c>nbf</c>, <c>is_guest</c>, <c>provider</c>, <c>sid</c>).
/// </summary>
internal sealed class JwtIssuer : IJwtIssuer
{
    private readonly GameKitAuthOptions _opts;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IIsGuestResolver _guestResolver;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _handler = new();

    /// <summary>Constructs the issuer; loads the RSA private key from the configured PEM path once.</summary>
    /// <param name="opts">Root auth options (JWT section supplies issuer/audience/kid/lifetime/PEM path).</param>
    /// <param name="clock">Clock abstraction — token <c>iat</c>/<c>exp</c>/<c>nbf</c> derive from <see cref="IClock.UtcNow"/>.</param>
    /// <param name="ids">Id generator used for the <c>jti</c> correlation claim (UUIDv7).</param>
    /// <param name="guestResolver">Resolves the <c>is_guest</c> claim freshly from the database on each issue.</param>
    public JwtIssuer(
        GameKitAuthOptions opts,
        IClock clock,
        IIdGenerator ids,
        IIsGuestResolver guestResolver)
    {
        _opts = opts;
        _clock = clock;
        _ids = ids;
        _guestResolver = guestResolver;

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(opts.Jwt.PrivateKeyPemPath));
        var key = new RsaSecurityKey(rsa) { KeyId = opts.Jwt.Kid };
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    /// <inheritdoc />
    public async Task<string> IssueAsync(Guid playerId, Guid familyId, string provider, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var isGuest = await _guestResolver.IsGuestAsync(playerId, cancellationToken).ConfigureAwait(false);

        var claims = new List<Claim>
        {
            new("sub", playerId.ToString()),
            new("jti", _ids.NewId().ToString()),
            new(
                "iat",
                ((long)(now - DateTimeOffset.UnixEpoch).TotalSeconds).ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new("is_guest", isGuest ? "true" : "false", ClaimValueTypes.Boolean),
            new("provider", provider),
            new("sid", familyId.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _opts.Jwt.Issuer,
            audience: _opts.Jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.Add(_opts.Jwt.AccessTokenLifetime).UtcDateTime,
            signingCredentials: _signingCredentials);

        return _handler.WriteToken(token);
    }
}
