// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameKit.Admin.UI;
using GameKit.Admin.UI.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace GameKit.Admin.Tests;

public class AdminCspNonceMiddlewareTests
{
    private static AdminCspNonceMiddleware Build(GameKitAdminOptions opts, RequestDelegate next)
        => new(next, opts);

    /// <summary>
    /// Builds a <see cref="DefaultHttpContext"/> whose response feature fires registered
    /// <c>OnStarting</c> callbacks when <see cref="FireOnStartingAsync"/> is invoked.
    /// Production servers (Kestrel/TestServer) fire OnStarting automatically at header flush;
    /// the default feature installed by <c>DefaultHttpContext</c> does not, so we substitute one here.
    /// </summary>
    private static DefaultHttpContext MakeCtx(string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("example.com");
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();  // avoid "response has started" issues
        ctx.Features.Set<IHttpResponseFeature>(new TestResponseFeature(ctx));
        return ctx;
    }

    private static Task FireOnStartingAsync(HttpContext ctx)
    {
        var feat = (TestResponseFeature)ctx.Features.Get<IHttpResponseFeature>()!;
        return feat.FireOnStartingAsync();
    }

    [Fact]
    public async Task AdminPath_SetsNonce_And_CspHeader()
    {
        var opts = new GameKitAdminOptions();  // MountPath = /admin
        var mw = Build(opts, _ => Task.CompletedTask);
        var ctx = MakeCtx("/admin/login");

        await mw.InvokeAsync(ctx);
        await FireOnStartingAsync(ctx);

        var nonce = ctx.Items[AdminCspNonceMiddleware.NonceItemKey] as string;
        Assert.NotNull(nonce);
        Assert.True(nonce!.Length >= 20, $"nonce too short: {nonce}");

        var csp = ctx.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains($"nonce-{nonce}", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("base-uri 'self'", csp);
        Assert.Contains("form-action 'self'", csp);
        Assert.Contains("img-src 'self' data:", csp);
    }

    [Fact]
    public async Task NonAdminPath_NoNonce_NoCspHeader()
    {
        var opts = new GameKitAdminOptions();
        var mw = Build(opts, _ => Task.CompletedTask);
        var ctx = MakeCtx("/auth/login");

        await mw.InvokeAsync(ctx);
        await FireOnStartingAsync(ctx);

        Assert.Null(ctx.Items[AdminCspNonceMiddleware.NonceItemKey]);
        Assert.False(ctx.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public async Task TwoAdminRequests_ProduceDifferentNonces()
    {
        var opts = new GameKitAdminOptions();
        var mw = Build(opts, _ => Task.CompletedTask);

        var c1 = MakeCtx("/admin/players");
        await mw.InvokeAsync(c1);
        await FireOnStartingAsync(c1);
        var n1 = (string)c1.Items[AdminCspNonceMiddleware.NonceItemKey]!;

        var c2 = MakeCtx("/admin/players");
        await mw.InvokeAsync(c2);
        await FireOnStartingAsync(c2);
        var n2 = (string)c2.Items[AdminCspNonceMiddleware.NonceItemKey]!;

        Assert.NotEqual(n1, n2);
    }

    [Fact]
    public async Task CustomMountPath_Applies_To_Configured_Prefix_Only()
    {
        var opts = new GameKitAdminOptions { MountPath = "/custom-admin" };
        var mw = Build(opts, _ => Task.CompletedTask);

        var ctxMatch = MakeCtx("/custom-admin/players");
        await mw.InvokeAsync(ctxMatch);
        await FireOnStartingAsync(ctxMatch);
        Assert.True(ctxMatch.Response.Headers.ContainsKey("Content-Security-Policy"));

        var ctxMiss = MakeCtx("/admin/players");
        await mw.InvokeAsync(ctxMiss);
        await FireOnStartingAsync(ctxMiss);
        Assert.False(ctxMiss.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    /// <summary>
    /// Test-only <see cref="IHttpResponseFeature"/> that owns its own Headers / StatusCode / Body
    /// storage and captures <c>OnStarting</c> callbacks, replaying them when
    /// <see cref="FireOnStartingAsync"/> is invoked. The stock feature shipped with
    /// <see cref="DefaultHttpContext"/> is a no-op for <c>OnStarting</c>; substituting this one
    /// makes the middleware's pre-flush header emission directly unit-testable without a live
    /// Kestrel pipeline.
    /// </summary>
    private sealed class TestResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Cb, object State)> _callbacks = new();
        private bool _fired;

        public TestResponseFeature(HttpContext ctx)
        {
            Body = new MemoryStream();
        }

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; }
        public bool HasStarted => _fired;

        public void OnStarting(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) { /* no-op for tests */ }

        public async Task FireOnStartingAsync()
        {
            if (_fired) return;
            _fired = true;
            // Run in reverse registration order, matching ASP.NET Core Kestrel semantics.
            for (var i = _callbacks.Count - 1; i >= 0; i--)
            {
                var (cb, state) = _callbacks[i];
                await cb(state).ConfigureAwait(false);
            }
        }
    }
}
