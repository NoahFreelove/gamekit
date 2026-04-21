// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace GameKit.Admin.UI.Authentication;

/// <summary>
/// Cookie-auth event handler that translates the admin challenge into
/// <c>404 Not Found</c> in Production (prevents anonymous enumeration of <c>/admin/*</c>;
/// ROADMAP SC #2, D-04) and a normal 302-to-login in Development/Staging (D-05).
/// Access-denied always returns 403 regardless of environment.
/// </summary>
public sealed class AdminCookieEvents : CookieAuthenticationEvents
{
    private readonly IHostEnvironment _env;

    /// <summary>Injected at runtime via <c>.AddScoped&lt;AdminCookieEvents&gt;</c> + <c>EventsType</c>.</summary>
    public AdminCookieEvents(IHostEnvironment env) => _env = env;

    /// <inheritdoc />
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        // Suppress the challenge with 404 only when:
        // (a) environment is Production, AND
        // (b) the request path is under /admin/* but is NOT the login page itself
        //     (the login page must remain reachable for operators to authenticate).
        if (_env.IsProduction())
        {
            var path = context.Request.Path;
            var isLoginPath = path.StartsWithSegments(context.Options.LoginPath);
            if (!isLoginPath)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
        }
        return base.RedirectToLogin(context);
    }

    /// <inheritdoc />
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        // Authenticated admin with wrong role — 403 (not 404); never hides existence from a legitimate admin.
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
