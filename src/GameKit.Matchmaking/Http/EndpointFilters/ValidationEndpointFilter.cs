// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Matchmaking.Http.EndpointFilters;

/// <summary>
/// Endpoint filter that resolves <c>IValidator&lt;TRequest&gt;</c> from DI and validates the
/// first argument of type <typeparamref name="TRequest"/> on the route. Returns a 400
/// ValidationProblem (RFC 9457) on failure. DRY clone of the Core /
/// <c>GameKit.Core.Http.EndpointFilters.ValidationEndpointFilter</c> + Rankings analog —
/// the Matchmaking namespace lives here so the endpoint registrations keep their imports
/// local to the package.
/// </summary>
/// <typeparam name="TRequest">The request body DTO type.</typeparam>
public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter where TRequest : class
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
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
