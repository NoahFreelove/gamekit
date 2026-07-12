// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Services;
using GameKit.Core.Services;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace GameKit.Auth.Tests;

public sealed class JwtIssuerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _privPath;
    private readonly string _pubPath;

    public JwtIssuerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gamekit-jwt-issuer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _privPath = Path.Combine(_tempDir, "key.pem");
        _pubPath = Path.Combine(_tempDir, "key.pub.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(_privPath, rsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, rsa.ExportRSAPublicKeyPem());
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private (JwtIssuer issuer, GameKitAuthOptions opts) Build(bool isGuest)
    {
        var opts = new GameKitAuthOptions();
        opts.Jwt.Issuer = "gk-test";
        opts.Jwt.Audience = "gk-test";
        opts.Jwt.PrivateKeyPemPath = _privPath;
        opts.Jwt.PublicKeyPemPath = _pubPath;
        opts.Jwt.Kid = "test-kid-1";
        opts.Jwt.AccessTokenLifetime = TimeSpan.FromMinutes(15);

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UnixEpoch.AddYears(56));
        var ids = new Mock<IIdGenerator>();
        ids.Setup(i => i.NewId()).Returns(Guid.CreateVersion7());
        var guest = new Mock<IIsGuestResolver>();
        guest.Setup(g => g.IsGuestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(isGuest);

        return (new JwtIssuer(opts, clock.Object, ids.Object, guest.Object), opts);
    }

    [Theory]
    [InlineData(true, "guest")]
    [InlineData(false, "steam")]
    [InlineData(false, "discord")]
    [InlineData(false, "password")]
    public async Task Issued_Token_Contains_All_D03_Claims(bool isGuest, string provider)
    {
        var (issuer, opts) = Build(isGuest);
        var playerId = Guid.CreateVersion7();
        var familyId = Guid.CreateVersion7();

        var jwt = await issuer.IssueAsync(playerId, familyId, provider);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        Assert.Equal(opts.Jwt.Issuer, token.Issuer);
        Assert.Contains(opts.Jwt.Audience, token.Audiences);
        Assert.Equal(playerId.ToString(), token.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal(familyId.ToString(), token.Claims.First(c => c.Type == "sid").Value);
        Assert.Equal(provider, token.Claims.First(c => c.Type == "provider").Value);
        Assert.Equal(isGuest ? "true" : "false", token.Claims.First(c => c.Type == "is_guest").Value);
        Assert.NotNull(token.Claims.FirstOrDefault(c => c.Type == "jti"));
        Assert.Equal(opts.Jwt.Kid, token.Header.Kid);
        Assert.Equal("RS256", token.SignatureAlgorithm);
    }

    [Fact]
    public async Task Issued_Token_Validates_With_Matching_Public_Key()
    {
        var (issuer, opts) = Build(isGuest: false);
        var jwt = await issuer.IssueAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "steam");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(_pubPath));
        var key = new RsaSecurityKey(rsa) { KeyId = opts.Jwt.Kid };
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        handler.ValidateToken(jwt, new TokenValidationParameters
        {
            ValidIssuer = opts.Jwt.Issuer,
            ValidAudience = opts.Jwt.Audience,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromMinutes(1),
            RequireSignedTokens = true,
            ValidateLifetime = false,   // clock is fixed; lifetime validation is tangential here
        }, out _);
    }
}
