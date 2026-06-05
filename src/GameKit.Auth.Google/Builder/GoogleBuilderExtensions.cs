// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Security.Claims;
using GameKit.Auth.Google.Configuration;
using GameKit.Auth.Google.Providers.Google;
using GameKit.Auth.Providers;
using GameKit.Core.Builder;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Auth.Google.Builder;

/// <summary>
/// Fluent-builder extensions that mount <c>GameKit.Auth.Google</c> onto an existing
/// <see cref="IGameKitBuilder"/>.
/// </summary>
/// <remarks>
/// <b>Call order:</b> <c>AddGoogle</c> must be called AFTER <c>AddAuth</c> on the same builder.
/// The Google authentication scheme is only registered when both <see cref="GameKitGoogleOptions.ClientId"/>
/// and <see cref="GameKitGoogleOptions.ClientSecret"/> are supplied; omitting credentials allows the
/// <c>IOAuthProvider</c> to remain resolvable in test harnesses without triggering the Google handler
/// (T-07-03-04 mitigation).
/// </remarks>
public static class GoogleBuilderExtensions
{
    /// <summary>
    /// Registers the Google <see cref="IOAuthProvider"/> (unconditionally) and the
    /// <c>Microsoft.AspNetCore.Authentication.Google</c> scheme (only when credentials are present).
    /// </summary>
    /// <param name="builder">The <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <param name="configure">Delegate to configure <see cref="GameKitGoogleOptions"/>.</param>
    /// <returns>The same <see cref="IGameKitBuilder"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IGameKitBuilder AddGoogle(
        this IGameKitBuilder builder,
        Action<GameKitGoogleOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new GameKitGoogleOptions();
        configure(opts);

        // CRITICAL: Scrutor's IOAuthProvider scan in AddAuth() is scoped to the GameKit.Auth
        // assembly only (FromAssemblyOf<IOAuthProvider>()). Sibling-package providers MUST
        // self-register here — they are NOT auto-discovered. See RESEARCH §Pitfall 4.
        builder.Services.AddScoped<IOAuthProvider, GoogleOAuthProvider>();

        // Register the Google authentication scheme only when credentials are present.
        // Without ClientId+ClientSecret the Google handler would throw on the first
        // /auth/login/google request, breaking test harnesses that use SkipAuthenticationSchemeRegistration.
        if (!string.IsNullOrEmpty(opts.ClientId) && !string.IsNullOrEmpty(opts.ClientSecret))
        {
            builder.Services.AddAuthentication()
                .AddGoogle(google =>
                {
                    google.ClientId     = opts.ClientId!;
                    google.ClientSecret = opts.ClientSecret!;
                    google.CallbackPath = opts.CallbackPath;
                    // Do NOT add extra scopes — AUTH-22 no-scope-creep.
                    // The Google handler defaults to openid + profile + email, which is sufficient.
                    google.SaveTokens = false;

                    google.Events.OnCreatingTicket = async ctx =>
                    {
                        // Google's stable subject identifier — NOT email (T-07-03-01: using email
                        // would create identity-confusion risk since email can change and is not unique
                        // across Google accounts). ClaimTypes.NameIdentifier maps to "sub" after the
                        // Google handler processes the ID token / userinfo response.
                        var sub = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (string.IsNullOrEmpty(sub)) return;

                        // Display name from the "name" claim (given_name + family_name, set by
                        // the Google handler when profile scope is in effect).
                        var name = ctx.Principal?.FindFirst(ClaimTypes.Name)?.Value;

                        // Avatar URL from the "picture" claim (Google profile photo URL).
                        var avatar = ctx.Principal?.FindFirst("picture")?.Value;

                        // Resolve the Google IOAuthProvider registered above. We filter by
                        // Provider == "google" rather than using GetRequiredService<GoogleOAuthProvider>()
                        // because Scrutor and this explicit registration both use the interface — no
                        // second concrete-type registration is needed.
                        var providers = ctx.HttpContext.RequestServices.GetServices<IOAuthProvider>();
                        IOAuthProvider? provider = null;
                        foreach (var p in providers)
                        {
                            if (p.Provider == "google") { provider = p; break; }
                        }
                        if (provider is null) return;

                        // X-GameKit-Device fingerprint for refresh-token family isolation.
                        var fingerprint = ctx.HttpContext.Request.Headers["X-GameKit-Device"].ToString();
                        var fp = string.IsNullOrEmpty(fingerprint) ? null : fingerprint;

                        var result = await provider.CompleteLoginAsync(
                            sub, name, avatar, fp, ctx.HttpContext.RequestAborted)
                            .ConfigureAwait(false);

                        if (result is { Success: true, Tokens: not null })
                        {
                            // Stash the token pair in auth properties so /auth/callback/google
                            // can read and return it to the client (mirrors the Discord pattern).
                            ctx.Properties.Items["gamekit.access_jwt"]  = result.Tokens.AccessJwt;
                            ctx.Properties.Items["gamekit.refresh_raw"] = result.Tokens.RawRefresh;
                            ctx.Properties.Items["gamekit.player_id"]   = result.PlayerId?.ToString();
                        }
                    };
                });
        }

        return builder;
    }
}
