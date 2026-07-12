// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Http.EndpointFilters;

/// <summary>
/// Generic <see cref="IEndpointFilter"/> that resolves <c>IValidator&lt;TRequest&gt;</c> from DI
/// and runs it against the first argument of type <typeparamref name="TRequest"/> in the
/// endpoint invocation. Returns a 400 ValidationProblem (RFC 9457 problem+json) on failure.
/// Matches the minimal-APIs + FluentValidation 12 pattern from
/// RESEARCH §14.6 — no MVC auto-validation binding (STACK.md).
/// </summary>
/// <remarks>
/// This is a generic Core primitive. Any Core or downstream-package endpoint can opt in via
/// <c>.AddEndpointFilter&lt;ValidationEndpointFilter&lt;TRequest&gt;&gt;()</c>. Concrete
/// <c>IValidator&lt;TRequest&gt;</c> implementations (e.g., Rankings's
/// <c>SessionCompleteRequestValidator</c>) are resolved from DI at runtime.
/// </remarks>
/// <typeparam name="TRequest">The request body DTO type.</typeparam>
public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter where TRequest : class
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var req = ctx.Arguments.OfType<TRequest>().FirstOrDefault();
        if (req is null) return await next(ctx).ConfigureAwait(false);

        var validator = ctx.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        if (validator is null) return await next(ctx).ConfigureAwait(false);

        var result = await validator
            .ValidateAsync(req, ctx.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return result.IsValid
            ? await next(ctx).ConfigureAwait(false)
            : Results.ValidationProblem(result.ToDictionary());
    }
}
