// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Admin.UI.Http;
using GameKit.Admin.UI.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Admin.UI.Builder;

/// <summary>
/// Extension methods that mount <c>GameKit.Admin.UI</c> middleware + endpoints onto the host
/// pipeline. Consumer ordering contract: <c>UseRouting → UseRateLimiter → UseGameKitAuth →
/// UseGameKit → UseGameKitAdmin → MapGameKit + MapAuth + MapGameKitAdmin</c>. Deviating causes
/// admin CSP / antiforgery to fire on non-admin paths or to miss admin paths entirely.
/// </summary>
public static class AdminApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the admin-scoped middleware: <see cref="AdminCspNonceMiddleware"/> (per-request
    /// nonce + strict CSP header under <c>/admin/*</c>) and <c>UseAntiforgery</c>. Call AFTER
    /// <c>UseGameKit</c> and BEFORE any <c>MapGameKitAdmin</c>.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns><paramref name="app"/> for chaining.</returns>
    public static IApplicationBuilder UseGameKitAdmin(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<AdminCspNonceMiddleware>();
        app.UseAntiforgery();
        return app;
    }

    /// <summary>
    /// Mounts the admin HTTP-API endpoint group at <c>{MountPath}/api</c> (default
    /// <c>/admin/api</c>). The Blazor admin console itself is served at root-relative
    /// <c>/admin/*</c> by <c>MapRazorComponents&lt;App&gt;()</c> (plan 03-08); this method
    /// does NOT mount the Razor component routes — plan 03-08 layers them on top.
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <param name="prefix">Optional prefix override; falls back to <see cref="GameKitAdminOptions.MountPath"/>.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapGameKitAdmin(
        this IEndpointRouteBuilder routes,
        string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        // SCOPE NOTE: `mount` relocates ONLY the admin HTTP API prefix (/admin/api/*). The
        // Blazor admin console is served at /admin/* by MapRazorComponents<App>() with static
        // @page directives + root-relative MudBlazor static asset paths — those ARE NOT
        // affected by MountPath. Dynamic Blazor-route rewriting is a potential v2 feature.
        var opts = routes.ServiceProvider.GetRequiredService<GameKitAdminOptions>();
        var mount = prefix ?? opts.MountPath;

        var apiGroup = routes.MapGroup($"{mount}/api");
        AdminEndpoints.Map(apiGroup);

        // HTML-route surface for cookie-mutating actions invoked by static-SSR pages
        // (currently POST /admin/login from the static Login.razor form). Distinct from the
        // /admin/api/* JSON group above — see AdminFormEndpoints for rationale.
        routes.MapAdminFormEndpoints(mount);

        // Mount the Blazor Server admin console (plan 03-08). Page @page routes inside
        // GameKit.Admin.UI/Components/**/*.razor are rooted under /admin/* (see UI-SPEC
        // §Route scope note). WithStaticAssets exposes _content/GameKit.Admin.UI/* and
        // _content/MudBlazor/* for the consumer app's static-file pipeline.
        routes.MapRazorComponents<Components.App>()
              .AddInteractiveServerRenderMode()
              .WithStaticAssets();

        return routes;
    }
}
