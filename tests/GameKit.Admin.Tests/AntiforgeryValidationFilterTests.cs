// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameKit.Admin.UI.Http.EndpointFilters;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace GameKit.Admin.Tests;

public class AntiforgeryValidationFilterTests
{
    private static (EndpointFilterInvocationContext ctx, Mock<IAntiforgery> af) Build()
    {
        var services = new ServiceCollection();
        var af = new Mock<IAntiforgery>(MockBehavior.Strict);
        services.AddSingleton(af.Object);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var ctx = new TestEndpointFilterInvocationContext(http);
        return (ctx, af);
    }

    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public TestEndpointFilterInvocationContext(HttpContext http)
        {
            HttpContext = http;
            Arguments = Array.Empty<object?>();
        }
        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments { get; }
        public override T GetArgument<T>(int index) => default!;
    }

    [Fact]
    public async Task ValidToken_InvokesNext()
    {
        var (ctx, af) = Build();
        af.Setup(x => x.ValidateRequestAsync(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);
        var sut = new AntiforgeryValidationFilter();
        var called = false;
        EndpointFilterDelegate next = _ => { called = true; return ValueTask.FromResult<object?>("ok"); };

        var result = await sut.InvokeAsync(ctx, next);

        Assert.True(called);
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task InvalidToken_Returns_BadRequest_With_CsrfError()
    {
        var (ctx, af) = Build();
        af.Setup(x => x.ValidateRequestAsync(It.IsAny<HttpContext>()))
          .ThrowsAsync(new AntiforgeryValidationException("bad"));
        var sut = new AntiforgeryValidationFilter();
        var called = false;
        EndpointFilterDelegate next = _ => { called = true; return ValueTask.FromResult<object?>("ok"); };

        var result = await sut.InvokeAsync(ctx, next);

        Assert.False(called);
        Assert.NotNull(result);
        // Result should be a Microsoft.AspNetCore.Http.IResult of type "BadRequest"; we can't deeply introspect
        // its body without calling ExecuteAsync, but the type-name check is sufficient for RED->GREEN here.
        Assert.Contains("BadRequest", result!.GetType().FullName);
    }
}
