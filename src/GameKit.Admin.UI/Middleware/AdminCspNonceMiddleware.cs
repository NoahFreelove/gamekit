// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace GameKit.Admin.UI.Middleware;

/// <summary>
/// Per-request CSP nonce middleware (D-15 / ADMIN-12). For every request whose path starts with
/// <see cref="GameKitAdminOptions.MountPath"/>, generates a 128-bit cryptographically random
/// nonce, stashes it in <c>HttpContext.Items["gamekit.admin.csp-nonce"]</c>, and emits a strict
/// <c>Content-Security-Policy</c> header on the response. Non-admin paths pass through unchanged.
/// </summary>
/// <remarks>
/// The full CSP emitted is:
/// <c>default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline';
/// img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none';
/// base-uri 'self'; form-action 'self'</c>.
/// See <c>.planning/phases/03-admin-ui/03-RESEARCH.md</c> §UI Hardening for rationale.
/// </remarks>
public sealed class AdminCspNonceMiddleware
{
    /// <summary>Key under which the per-request nonce is stored in <see cref="HttpContext.Items"/>.</summary>
    public const string NonceItemKey = "gamekit.admin.csp-nonce";

    private readonly RequestDelegate _next;
    private readonly PathString _adminPrefix;

    /// <summary>
    /// Constructs the middleware from the ASP.NET Core request-delegate pipeline plus the resolved
    /// <see cref="GameKitAdminOptions"/>. The admin mount-path prefix is snapshotted at construction;
    /// runtime changes to <see cref="GameKitAdminOptions.MountPath"/> require app restart.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="options">The resolved admin options (supplies <see cref="GameKitAdminOptions.MountPath"/>).</param>
    public AdminCspNonceMiddleware(RequestDelegate next, GameKitAdminOptions options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _adminPrefix = options.MountPath;
    }

    /// <summary>
    /// Invokes the middleware. For requests under the admin prefix, emits a per-request 128-bit base64
    /// nonce into <see cref="HttpContext.Items"/> and the CSP header onto the response. Other paths
    /// pass through unmodified.
    /// </summary>
    /// <param name="ctx">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        if (!ctx.Request.Path.StartsWithSegments(_adminPrefix))
        {
            await _next(ctx).ConfigureAwait(false);
            return;
        }

        // Generate 128-bit nonce, base64 encode.
        Span<byte> nonceBytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(nonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes);
        ctx.Items[NonceItemKey] = nonce;

        // Emit CSP header just before the response starts (OnStarting runs at header flush).
        // OVERRIDE any prior Content-Security-Policy: ASP.NET Core's static-SSR Blazor antiforgery
        // pipeline sets a less-strict default (e.g. `frame-ancestors 'self'`) on Razor-component
        // responses; the GameKit admin policy is strictly tighter (`frame-ancestors 'none'`,
        // per-request script nonce) and MUST take precedence on /admin/* responses. Indexing
        // assignment replaces; ContainsKey-guarded `Add` would silently leave the weaker default.
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                $"script-src 'self' 'nonce-{nonce}'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "font-src 'self'; " +
                // ws:/wss: required so SignalR's WebSocket upgrade for the Blazor Server
                // circuit (`/_blazor?id=...`) passes CSP — browsers treat ws:// as a distinct
                // scheme from http:// under `'self'`, so same-origin alone is insufficient.
                "connect-src 'self' ws: wss:; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'";
            return Task.CompletedTask;
        });

        await _next(ctx).ConfigureAwait(false);
    }
}
