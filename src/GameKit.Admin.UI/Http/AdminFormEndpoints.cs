// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using FluentValidation;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Admin.UI.Http.RateLimiting;
using GameKit.Admin.UI.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Admin.UI.Http;

/// <summary>
/// HTML-route surface for the admin UI: form-encoded POST endpoints that the static-SSR
/// Blazor pages submit to directly from the browser. Distinct from <see cref="AdminEndpoints"/>,
/// which serves <c>/admin/api/*</c> as a JSON HTTP API for SPA / programmatic clients.
///
/// Why split: cookie-mutating actions (login, logout, password change, …) cannot be safely
/// invoked from a Blazor INTERACTIVE circuit via HttpClient — the Set-Cookie header lands on
/// the server-side HttpClient, never reaching the browser. The browser must make the request
/// itself. A static-SSR HTML form post is the canonical pattern for this in ASP.NET Core.
/// </summary>
public static class AdminFormEndpoints
{
    /// <summary>
    /// Maps the admin HTML-route surface (form POST handlers) onto <paramref name="routes"/>.
    /// Currently a single route, <c>POST {mountPath}/login</c>; future cookie-mutating actions
    /// (logout, change-password, etc.) belong here.
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <param name="mountPath">The admin mount path (e.g. <c>/admin</c>).</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapAdminFormEndpoints(
        this IEndpointRouteBuilder routes,
        string mountPath)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(mountPath);

        // POST {mount}/login — form-encoded; returns 302 Redirect on both success and failure.
        // Shares the AdminLoginPolicy (5/min/IP sliding window) with the JSON /admin/api/login
        // endpoint — the IP-keyed partition means the buckets coalesce across both surfaces, so a
        // brute-force attacker can't double their budget by spraying both endpoints.
        routes.MapPost($"{mountPath}/login/submit", LoginFormAsync)
              .RequireRateLimiting(AdminRateLimitRegistrations.AdminLoginPolicy)
              .AllowAnonymous();

        return routes;
    }

    private static async Task<IResult> LoginFormAsync(
        HttpContext http,
        GameKitAdminOptions opts,
        IAdminAuthService authSvc,
        IValidator<LoginRequest> validator,
        IAntiforgery antiforgery)
    {
        var ct = http.RequestAborted;

        if (!http.Request.HasFormContentType)
        {
            // Programmatic clients should hit the JSON /admin/api/login endpoint; redirecting
            // here would mask a misconfigured client. 415 is the most accurate signal.
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        // Antiforgery (login-CSRF protection): Login.razor renders <AntiforgeryToken /> inside
        // the form, and UseAntiforgery on the consumer pipeline issues the cookie. Validating
        // here keeps the protection live for every login attempt regardless of authenticated state.
        try
        {
            await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Redirect($"{opts.MountPath}/login?error=unavailable");
        }

        var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        var req = new LoginRequest(
            Username: form["Username"].ToString(),
            Password: form["Password"].ToString(),
            RememberMe: bool.TryParse(form["RememberMe"], out var rm) && rm);

        // Preserve ReturnUrl across the failure-redirect so the user lands back on their
        // intended destination after a corrected attempt. SafeReturnUrl rejects open-redirect
        // attempts (absolute URLs, protocol-relative URLs).
        var returnUrl = SafeReturnUrl(form["ReturnUrl"].ToString());
        var preservedReturn = returnUrl is null
            ? string.Empty
            : "&ReturnUrl=" + Uri.EscapeDataString(returnUrl);

        var validation = await validator.ValidateAsync(req, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return Results.Redirect($"{opts.MountPath}/login?error=invalid{preservedReturn}");
        }

        var ok = await AdminEndpoints.SignInCoreAsync(http, opts, authSvc, req, ct).ConfigureAwait(false);
        return ok
            ? Results.Redirect(returnUrl ?? opts.MountPath)
            : Results.Redirect($"{opts.MountPath}/login?error=invalid{preservedReturn}");
    }

    /// <summary>
    /// Open-redirect guard for form-supplied ReturnUrl values. Accepts only same-origin,
    /// non-protocol-relative absolute paths.
    /// </summary>
    private static string? SafeReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!url.StartsWith('/')) return null;
        if (url.StartsWith("//", StringComparison.Ordinal)) return null;
        return url;
    }
}
