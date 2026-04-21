// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using FluentValidation;
using GameKit.Admin.UI.Authentication;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Data;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Admin.UI.Http.RateLimiting;
using GameKit.Admin.UI.Http.Validators;
using GameKit.Admin.UI.Services;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace GameKit.Admin.UI.Builder;

/// <summary>
/// Fluent-builder extensions that mount <c>GameKit.Admin.UI</c> onto an existing
/// <see cref="IGameKitBuilder"/>. Call order on the consumer side:
/// <code>
/// services.AddGameKit(o =&gt; ...).AddAuth(o =&gt; ...).AddGameKitAdmin(o =&gt; ...);
/// app.UseRouting();
/// app.UseRateLimiter();
/// app.UseGameKitAuth();
/// app.UseGameKit();
/// app.UseGameKitAdmin();
/// app.MapGameKit();
/// app.MapAuth();
/// app.MapGameKitAdmin();
/// </code>
/// </summary>
public static class AdminBuilderExtensions
{
    /// <summary>
    /// Registers every <c>GameKit.Admin.UI</c> service, hosted service, auth scheme,
    /// authorization policy, rate-limit policy, antiforgery primitive, Blazor Server primitive
    /// (<c>AddRazorComponents().AddInteractiveServerComponents</c>), MudBlazor service layer,
    /// and <see cref="IHttpContextAccessor"/> required by <c>App.razor</c> (plan 03-08) to read
    /// the CSP nonce. Registration order per SP-5 — do not re-order without updating PATTERNS.
    /// </summary>
    /// <param name="builder">The GameKit builder chain.</param>
    /// <param name="configure">Optional callback to populate <see cref="GameKitAdminOptions"/>.</param>
    /// <returns><paramref name="builder"/> for chaining.</returns>
    public static IGameKitBuilder AddGameKitAdmin(
        this IGameKitBuilder builder,
        Action<GameKitAdminOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var opts = new GameKitAdminOptions();
        configure?.Invoke(opts);
        ValidateAdminOptions(opts);
        builder.Services.AddSingleton(opts);

        // 1. Admin model-builder extension — surfaces AdminUser in the shared model at runtime.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, AdminModelBuilderExtension>());

        // 2. Migration hosted service — MUST precede SuperadminGate so admin_users exists when
        //    the gate queries it. Hosted services fire in registration order.
        builder.Services.AddHostedService<AdminMigrationHostedService>();

        // 3. Superadmin gate hosted service (D-04 / D-05).
        builder.Services.AddHostedService<SuperadminGateHostedService>();

        // 4. Cookie auth scheme. W4: preserve Phase-2 JwtBearer as the DEFAULT auth scheme
        //    (AddAuthentication(JwtBearerDefaults.AuthenticationScheme)) and register the admin
        //    cookie as a NAMED scheme only. This ensures existing player-JWT endpoints (e.g.
        //    /auth/me) continue to authenticate with Bearer when GameKit.Admin.UI is added.
        //    Admin endpoints opt in to the named cookie scheme explicitly via the authorization
        //    policies below.
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddCookie(AdminAuthenticationSchemeConstants.Scheme, o =>
            {
                o.Cookie.Name = opts.Cookie.Name;
                o.Cookie.HttpOnly = true;
                // SameAsRequest: Secure flag is set only when the request is HTTPS. Production
                // deployments MUST serve the admin console over HTTPS (enforced out-of-band via
                // reverse proxy / Kestrel HTTPS redirection). This flavor lets the sample + local
                // dev run on plain HTTP without silently losing the auth cookie. If your threat
                // model requires Secure always (even in dev), override via cookie events.
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.ExpireTimeSpan = opts.Cookie.ExpireTimeSpan;
                o.SlidingExpiration = opts.Cookie.SlidingExpiration;
                // Blazor Razor pages are fixed at /admin/* regardless of MountPath (which scopes
                // only the HTTP API prefix — see GameKitAdminOptions.MountPath XML doc). Cookie
                // redirect targets must reference the Blazor login page at its real route.
                o.LoginPath = "/admin/login";
                o.LogoutPath = "/admin/logout";
                o.AccessDeniedPath = "/admin/access-denied";
                o.EventsType = typeof(AdminCookieEvents);
            });
        builder.Services.AddScoped<AdminCookieEvents>();

        // 5. Authorization policies — pin the scheme so player JWTs (Bearer) cannot satisfy
        //    admin requirements (ROADMAP SC #6).
        builder.Services.AddAuthorization(ao =>
        {
            ao.AddPolicy(AdminPolicies.Admin, p => p
                .AddAuthenticationSchemes(AdminAuthenticationSchemeConstants.Scheme)
                .RequireAuthenticatedUser()
                .RequireRole(AdminRoles.Admin, AdminRoles.Superadmin));
            ao.AddPolicy(AdminPolicies.Superadmin, p => p
                .AddAuthenticationSchemes(AdminAuthenticationSchemeConstants.Scheme)
                .RequireAuthenticatedUser()
                .RequireRole(AdminRoles.Superadmin));
        });

        // 6. Services — Scoped for anything touching the request-scoped DbContext.
        builder.Services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();
        builder.Services.AddScoped<IAdminAuthService, AdminAuthService>();
        builder.Services.AddScoped<IPlayerSearchService, PlayerSearchService>();
        builder.Services.AddScoped<IPlayerBanService, PlayerBanService>();
        builder.Services.AddScoped<IAdminUserService, AdminUserService>();
        builder.Services.AddScoped<IHealthProbeService, HealthProbeService>();

        // 7. Error-rate ring buffer + log provider — Singleton so they observe events across
        //    requests. LogErrorCounter is wired as an ILoggerProvider so every ILogger<T>
        //    created in the app feeds its Error+ events into the ring buffer.
        builder.Services.AddSingleton<ErrorRateRingBuffer>();
        builder.Services.AddSingleton<ILoggerProvider, LogErrorCounter>();

        // 8. Rate limiter — registers the gamekit:admin:login sliding-window 5/min/IP policy.
        //    Caller must have previously invoked services.AddRateLimiter(...) for this to take
        //    effect at request time.
        builder.Services.AddAdminRateLimits();

        // 9. Antiforgery (D-16) — pinned header + cookie names.
        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = AdminAuthenticationSchemeConstants.CsrfHeaderName;
            o.Cookie.Name = AdminAuthenticationSchemeConstants.CsrfCookieName;
            o.Cookie.HttpOnly = false; // JS reads and echoes via header
            o.Cookie.SameSite = SameSiteMode.Lax;
            // See admin auth cookie comment above — SameAsRequest lets dev run on HTTP.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // 10. Blazor Server primitives (plan 03-08's App.razor + MapRazorComponents depend on
        //     these; registering them here keeps AddGameKitAdmin a single entry point).
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        // 11. MudBlazor services.
        builder.Services.AddMudServices();

        // 12. IHttpContextAccessor — App.razor reads ctx.Items[AdminCspNonceMiddleware.NonceItemKey].
        builder.Services.AddHttpContextAccessor();

        // 12b. Default HttpClient for Blazor Server admin pages — BaseAddress derived from the current
        //      HttpContext so pages can issue same-origin POSTs (e.g. Login.razor → /admin/api/login).
        //      Without a BaseAddress, relative URIs throw "An invalid request URI was provided."
        builder.Services.AddScoped(sp =>
        {
            var ctx = sp.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new InvalidOperationException(
                    "HttpContext is unavailable; the admin HttpClient is only valid inside a request scope.");
            return new HttpClient
            {
                BaseAddress = new Uri($"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}/")
            };
        });

        // 13. FluentValidation validators for admin DTOs (plan 03-07). ValidationEndpointFilter<T>
        //     resolves IValidator<T> lazily; unregistered types would be a silent no-op, so we
        //     register every DTO with a validator explicitly here.
        builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        builder.Services.AddScoped<IValidator<BanPlayerRequest>, BanPlayerRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateAdminRequest>, CreateAdminRequestValidator>();
        builder.Services.AddScoped<IValidator<PlayerSearchRequest>, PlayerSearchRequestValidator>();

        return builder;
    }

    /// <summary>Fail-fast validator for <see cref="GameKitAdminOptions"/> (T-03-03-04 mitigation).</summary>
    /// <param name="opts">The options to validate.</param>
    /// <exception cref="ArgumentException">Thrown for any invariant violation.</exception>
    internal static void ValidateAdminOptions(GameKitAdminOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        if (string.IsNullOrWhiteSpace(opts.MountPath) || !opts.MountPath.StartsWith('/'))
            throw new ArgumentException(
                $"{nameof(GameKitAdminOptions)}.{nameof(GameKitAdminOptions.MountPath)} must start with '/'.",
                nameof(opts));
        if (opts.Panel.RefreshInterval <= TimeSpan.Zero)
            throw new ArgumentException(
                $"{nameof(GameKitAdminOptions)}.{nameof(GameKitAdminOptions.Panel)}.{nameof(AdminPanelOptions.RefreshInterval)} must be > 0.",
                nameof(opts));
        if (opts.Cookie.ExpireTimeSpan <= TimeSpan.Zero)
            throw new ArgumentException(
                $"{nameof(GameKitAdminOptions)}.{nameof(GameKitAdminOptions.Cookie)}.{nameof(AdminCookieOptions.ExpireTimeSpan)} must be > 0.",
                nameof(opts));
        if (opts.Panel.HealthErrorRateBucketSize <= TimeSpan.Zero)
            throw new ArgumentException(
                $"{nameof(GameKitAdminOptions)}.{nameof(GameKitAdminOptions.Panel)}.{nameof(AdminPanelOptions.HealthErrorRateBucketSize)} must be > 0.",
                nameof(opts));
        if (opts.Panel.HealthErrorRateWindow < opts.Panel.HealthErrorRateBucketSize)
            throw new ArgumentException(
                $"{nameof(GameKitAdminOptions)}.{nameof(GameKitAdminOptions.Panel)}.{nameof(AdminPanelOptions.HealthErrorRateWindow)} must be >= bucket size.",
                nameof(opts));
    }
}
