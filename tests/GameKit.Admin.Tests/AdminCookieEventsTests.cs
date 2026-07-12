// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
using System.Threading.Tasks;
using GameKit.Admin.UI.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace GameKit.Admin.Tests;

public class AdminCookieEventsTests
{
    private static Mock<IHostEnvironment> EnvMock(string envName)
    {
        var env = new Mock<IHostEnvironment>(MockBehavior.Strict);
        env.SetupGet(e => e.EnvironmentName).Returns(envName);
        return env;
    }

    private static RedirectContext<CookieAuthenticationOptions> BuildCtx(string requestPath)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("example.com");
        http.Request.Path = requestPath;
        var opts = new CookieAuthenticationOptions { LoginPath = "/admin/login" };
        var scheme = new AuthenticationScheme(
            AdminAuthenticationSchemeConstants.Scheme,
            AdminAuthenticationSchemeConstants.Scheme,
            typeof(CookieAuthenticationHandler));
        return new RedirectContext<CookieAuthenticationOptions>(
            http, scheme, opts, new AuthenticationProperties(), redirectUri: "/admin/login");
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/admin/players")]
    [InlineData("/admin/api/players/search")]
    public async Task Production_NonLoginPath_Returns_404(string path)
    {
        var env = EnvMock(Environments.Production);
        var sut = new AdminCookieEvents(env.Object);
        var ctx = BuildCtx(path);

        await sut.RedirectToLogin(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        // No Location header should be set — the challenge was suppressed, not redirected.
        Assert.False(ctx.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task Production_LoginPath_Falls_Through_To_Base()
    {
        var env = EnvMock(Environments.Production);
        var sut = new AdminCookieEvents(env.Object);
        var ctx = BuildCtx("/admin/login");

        await sut.RedirectToLogin(ctx);

        // Base behavior sets a 302 with Location header.
        Assert.Equal(StatusCodes.Status302Found, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Development_NonLoginPath_Falls_Through_To_Base()
    {
        var env = EnvMock(Environments.Development);
        var sut = new AdminCookieEvents(env.Object);
        var ctx = BuildCtx("/admin/players");

        await sut.RedirectToLogin(ctx);

        Assert.Equal(StatusCodes.Status302Found, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task AccessDenied_Always_Returns_403()
    {
        var env = EnvMock(Environments.Production);
        var sut = new AdminCookieEvents(env.Object);
        var ctx = BuildCtx("/admin/admins");

        await sut.RedirectToAccessDenied(ctx);

        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }
}
