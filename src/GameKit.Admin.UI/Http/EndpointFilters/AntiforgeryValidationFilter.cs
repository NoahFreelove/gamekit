// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Admin.UI.Http.EndpointFilters;

/// <summary>
/// Endpoint filter that validates the antiforgery token on every admin mutation (D-16 / ADMIN-12).
/// Resolves <see cref="IAntiforgery"/> from the request services (not a constructor parameter —
/// the filter is stateless and can be added as a type, not an instance). Failure returns
/// <c>400 BadRequest</c> with a plain-minimal-APIs-consumable JSON body of
/// <c>{ "error": "csrf_validation_failed" }</c>.
/// </summary>
/// <remarks>
/// Intended registration on mutation endpoints is
/// <c>.AddEndpointFilter&lt;AntiforgeryValidationFilter&gt;()</c> placed BEFORE the
/// <c>ValidationEndpointFilter&lt;TRequest&gt;</c> chain so CSRF fails before body deserialization.
/// Pairs with <c>AddAntiforgery(...)</c> and <c>UseAntiforgery()</c> wired by
/// <c>UseGameKitAdmin</c>; see <c>.planning/phases/03-admin-ui/03-RESEARCH.md</c> §Antiforgery on
/// mutations lines 660-725.
/// </remarks>
public sealed class AntiforgeryValidationFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { error = "csrf_validation_failed" });
        }
        return await next(context).ConfigureAwait(false);
    }
}
