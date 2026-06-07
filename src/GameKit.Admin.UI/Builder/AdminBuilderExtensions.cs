// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
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
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using StackExchange.Redis;

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

        // 4. Cookie auth scheme + path-based default scheme.
        //
        //    W4 originally pinned JwtBearer as the default scheme so player API endpoints stayed
        //    on Bearer tokens, with the admin cookie as a NAMED-only scheme that endpoints opted
        //    into via .AddAuthenticationSchemes on each policy. That works for minimal API
        //    endpoints — RequireAuthorization(policy) re-authenticates against the policy's
        //    schemes — but it breaks Blazor's [Authorize] attribute on Razor components, because
        //    AuthorizeRouteView reads HttpContext.User which is built from the DEFAULT scheme.
        //    With JwtBearer as default and no Bearer header in the browser request, HttpContext.User
        //    is anonymous regardless of the gk_admin_session cookie, so every admin page renders
        //    its NotAuthorized branch.
        //
        //    Fix: a path-based policy scheme as the default. /admin/* requests forward to the
        //    cookie scheme; everything else forwards to JwtBearer. This preserves Phase-2
        //    behavior on player endpoints AND populates HttpContext.User with the admin claims
        //    on admin paths so Blazor's authorization sees the role claim.
        const string DefaultByPathScheme = "GameKit:DefaultByPath";
        builder.Services.AddAuthentication(DefaultByPathScheme)
            .AddPolicyScheme(DefaultByPathScheme, "GameKit default (path-based)", o =>
            {
                // Admin paths (/admin/*) AND the Blazor Server transport endpoints (/_blazor/*)
                // both forward to the cookie scheme. The SignalR negotiate at /_blazor/negotiate
                // captures the principal that becomes the interactive circuit's identity — if
                // we left /_blazor on the JwtBearer fallback, the circuit would boot anonymous
                // and AuthorizeRouteView on every admin page would render NotAuthorized after
                // prerender (prerender uses HttpContext.User from the original /admin/* GET).
                // ASSUMPTION: this build of the admin UI is the only consumer of /_blazor on
                // this host. If a consumer mounts their own Blazor app on the same Kestrel,
                // they should override this scheme selector.
                o.ForwardDefaultSelector = ctx =>
                {
                    var path = ctx.Request.Path;
                    var isAdminOrBlazorTransport =
                        path.StartsWithSegments(opts.MountPath, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase);
                    return isAdminOrBlazorTransport
                        ? AdminAuthenticationSchemeConstants.Scheme
                        : JwtBearerDefaults.AuthenticationScheme;
                };
            })
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

        // 8. ADMIN-14: opt-in Redis error counter for cross-replica aggregation. Uses
        //    TryAddSingleton with a factory that returns null! when no IConnectionMultiplexer
        //    is registered — HealthProbeService and LogErrorCounter both inject it as an
        //    optional nullable param and fall back to the in-memory ErrorRateRingBuffer.
        //    Single-instance installs (no Redis) are unaffected.
        builder.Services.TryAddSingleton<IRedisErrorRateCounter>(sp =>
        {
            var mux = sp.GetService<IConnectionMultiplexer>();
            if (mux is null) return null!;  // single-instance install — in-memory only
            return new RedisErrorRateCounter(mux, sp.GetRequiredService<GameKitAdminOptions>());
        });

        // 9. ADMIN-13: SignalR Redis backplane + AdminEventHub + live-broadcast relay.
        //    AddSignalR() is idempotent (called earlier by AddRazorComponents). AddStackExchangeRedis
        //    chains off ISignalRServerBuilder to register the StackExchange.Redis backplane ONLY
        //    when IConnectionMultiplexer has already been registered in the service collection
        //    (CR-01 fix — single-instance installs that do not register IConnectionMultiplexer use
        //    the in-process SignalR backplane; calling AddStackExchangeRedis without a connection
        //    factory causes a default-localhost connection attempt on the first hub use).
        //    ChannelPrefix "GameKit" matches AddLobby() — hub-type isolation (IHubContext<T>)
        //    prevents cross-delivery between AdminEventHub and LobbyHub (RESEARCH A4).
        //    TryAddEnumerable: if AddLobby() already registered LobbyRedisBackplanePostConfigure,
        //    the Admin one stacks on top — both set ConnectionFactory to the same IConnectionMultiplexer
        //    instance (idempotent, Pitfall 1 mitigation).
        var hasMux = builder.Services.Any(
            sd => sd.ServiceType == typeof(IConnectionMultiplexer));
        var signalRBuilder = builder.Services.AddSignalR();
        if (hasMux)
        {
            signalRBuilder.AddStackExchangeRedis(options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit");
            });
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IPostConfigureOptions<RedisOptions>,
                    AdminBackplanePostConfigure>());
        }

        // 10. ADMIN-13: background relay service — registered unconditionally; the service
        //     injects IConnectionMultiplexer? as nullable and short-circuits ExecuteAsync when
        //     null (Pitfall 4), so single-instance installs without Redis start cleanly.
        builder.Services.AddHostedService<AdminLiveBroadcastService>();

        // 11. Rate limiter — registers the gamekit:admin:login sliding-window 5/min/IP policy.
        //    Caller must have previously invoked services.AddRateLimiter(...) for this to take
        //    effect at request time.
        builder.Services.AddAdminRateLimits();

        // 12. Antiforgery (D-16) — pinned header + cookie names.
        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = AdminAuthenticationSchemeConstants.CsrfHeaderName;
            o.Cookie.Name = AdminAuthenticationSchemeConstants.CsrfCookieName;
            o.Cookie.HttpOnly = false; // JS reads and echoes via header
            o.Cookie.SameSite = SameSiteMode.Lax;
            // See admin auth cookie comment above — SameAsRequest lets dev run on HTTP.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        // 13. Blazor Server primitives (plan 03-08's App.razor + MapRazorComponents depend on
        //     these; registering them here keeps AddGameKitAdmin a single entry point).
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        // 14. MudBlazor services.
        builder.Services.AddMudServices();

        // 15. IHttpContextAccessor — App.razor reads ctx.Items[AdminCspNonceMiddleware.NonceItemKey].
        builder.Services.AddHttpContextAccessor();

        // 15b. Intentionally NO HttpClient registration. Admin pages access domain logic via
        //      DI services (IPlayerBanService, IAdminUserService, IGdprDeleteService, …) — never
        //      via HTTP loopback to /admin/api/*. Loopback from inside a Blazor interactive
        //      circuit is broken twice over: (a) the user's auth cookie does not propagate to
        //      the server's HttpClient, so RequireAuthorization endpoints 401; (b) cookie-mutating
        //      endpoints (login, logout, …) write Set-Cookie back to the server, never to the
        //      browser. Cookie-mutating actions go through the static-SSR HTML form route
        //      (POST /admin/login in AdminFormEndpoints) so the BROWSER makes the request.
        //      The /admin/api/* JSON surface remains for SPA / programmatic clients only.
        //      See the architecture note at the top of AdminEndpoints.cs.

        // 16. FluentValidation validators for admin DTOs (plan 03-07). ValidationEndpointFilter<T>
        //     resolves IValidator<T> lazily; unregistered types would be a silent no-op, so we
        //     register every DTO with a validator explicitly here.
        builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        builder.Services.AddScoped<IValidator<BanPlayerRequest>, BanPlayerRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateAdminRequest>, CreateAdminRequestValidator>();
        builder.Services.AddScoped<IValidator<PlayerSearchRequest>, PlayerSearchRequestValidator>();
        builder.Services.AddScoped<IValidator<MergePlayersRequest>, MergePlayersRequestValidator>();

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
