// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using GameKit.Core.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace GameKit.Core.Tests.Services;

public class HttpContextCurrentPlayerTests
{
    [Fact]
    public void PlayerId_ReturnsNull_WhenNoHttpContext()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var player = new HttpContextCurrentPlayer(accessor.Object);
        Assert.Null(player.PlayerId);
    }

    [Fact]
    public void PlayerId_ReturnsNull_WhenNotAuthenticated()
    {
        var context = new DefaultHttpContext();
        // Identity is not authenticated by default
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);

        var player = new HttpContextCurrentPlayer(accessor.Object);
        Assert.Null(player.PlayerId);
    }

    [Fact]
    public void PlayerId_ReadsGameKitPlayerIdClaim()
    {
        var id = Guid.NewGuid();
        var claims = new[] { new Claim("gamekit_player_id", id.ToString()) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);

        var player = new HttpContextCurrentPlayer(accessor.Object);
        Assert.Equal(id, player.PlayerId);
    }

    [Fact]
    public void PlayerId_FallsBackToNameIdentifierClaim()
    {
        var id = Guid.NewGuid();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);

        var player = new HttpContextCurrentPlayer(accessor.Object);
        Assert.Equal(id, player.PlayerId);
    }

    [Fact]
    public void PlayerId_PrefersGameKitClaim_OverNameIdentifier()
    {
        var gameKitId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var claims = new[]
        {
            new Claim("gamekit_player_id", gameKitId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, otherId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);

        var player = new HttpContextCurrentPlayer(accessor.Object);
        Assert.Equal(gameKitId, player.PlayerId);
    }

    [Fact]
    public void PlayerId_ReturnsNull_WhenClaimIsNotGuid()
    {
        var claims = new[] { new Claim("gamekit_player_id", "not-a-guid") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);

        var player = new HttpContextCurrentPlayer(accessor.Object);
        Assert.Null(player.PlayerId);
    }
}
