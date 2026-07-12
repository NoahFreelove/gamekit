// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Http.EndpointFilters;

/// <summary>
/// Endpoint filter that validates the antiforgery token on the <c>POST /admin/api/ladders/{id}/end-season</c>
/// mutation (T-04-07-CS / Open Q4). DRY clone of
/// <c>GameKit.Admin.UI.Http.EndpointFilters.AntiforgeryValidationFilter</c> — identical logic,
/// separate namespace to preserve the package boundary (Rankings does NOT reference Admin.UI;
/// Admin.UI references Rankings for the IEndSeasonService dialog injection, not the reverse).
/// </summary>
/// <remarks>
/// Source of truth for the cookie scheme name and the Superadmin policy constant is
/// <c>GameKit.Admin.UI.Authentication.AdminAuthenticationSchemeConstants</c> and
/// <c>GameKit.Admin.UI.Authorization.AdminPolicies</c>. These are referenced as string literals
/// in <see cref="RankingsAdminEndpoints"/> because Rankings does not project-reference Admin.UI.
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
