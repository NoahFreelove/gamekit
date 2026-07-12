// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
using System;

namespace GameKit.Admin.UI;

/// <summary>
/// Configuration surface for <c>GameKit.Admin.UI</c>. Set via
/// <c>services.AddGameKit().AddGameKitAdmin(opts =&gt; { ... })</c>.
/// Defaults are production-safe.
/// </summary>
public sealed class GameKitAdminOptions
{
    /// <summary>
    /// Prefix for admin HTTP API endpoints (e.g. <c>/admin/api/players/search</c>). Default <c>/admin</c>.
    /// The Blazor admin console itself is served at <c>/admin/*</c> regardless of this setting — Razor pages
    /// are declared via static <c>@page</c> directives and MudBlazor static assets (<c>_content/MudBlazor/*</c>)
    /// are root-relative. Dynamic Blazor-route rewriting could be a v2 feature.
    /// </summary>
    public string MountPath { get; set; } = "/admin";

    /// <summary>Cookie authentication + CSRF cookie options.</summary>
    public AdminCookieOptions Cookie { get; } = new();

    /// <summary>Health + queue-depth panel refresh controls.</summary>
    public AdminPanelOptions Panel { get; } = new();

    /// <summary>Content-Security-Policy configuration.</summary>
    public AdminCspOptions Csp { get; } = new();
}

/// <summary>Admin session cookie defaults (D-01, D-02).</summary>
public sealed class AdminCookieOptions
{
    /// <summary>Cookie name — defaults to <c>gk_admin_session</c>.</summary>
    public string Name { get; set; } = "gk_admin_session";

    /// <summary>Session lifetime. Default 8 hours, sliding.</summary>
    public TimeSpan ExpireTimeSpan { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Sliding expiration — true by default.</summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>Remember-me duration extends the cookie to this value when checked at login. Default 30 days.</summary>
    public TimeSpan RememberMeDuration { get; set; } = TimeSpan.FromDays(30);
}

/// <summary>Panel refresh + health ring-buffer configuration (D-10).</summary>
public sealed class AdminPanelOptions
{
    /// <summary>Polling interval for health + queue-depth panels. Default 10 seconds.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Rolling window for the recent-error-rate health tile. Default 5 minutes.</summary>
    public TimeSpan HealthErrorRateWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bucket granularity inside the error-rate ring buffer. Default 1 second.</summary>
    public TimeSpan HealthErrorRateBucketSize { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>CSP tuning — the enforce-only policy is hard-coded in <c>AdminCspNonceMiddleware</c>.</summary>
public sealed class AdminCspOptions
{
    /// <summary>
    /// When true, emit <c>Content-Security-Policy-Report-Only</c> alongside the enforce header.
    /// Default false (no phone-home, matches "install only what you need"). Reserved for local dev hardening.
    /// </summary>
    public bool ReportOnly { get; set; } = false;
}
