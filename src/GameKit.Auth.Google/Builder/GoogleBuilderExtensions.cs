// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using GameKit.Auth;
using GameKit.Auth.Egress;
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
    /// Hosts that the Google OAuth2 backchannel must reach for token exchange and userinfo.
    /// Added to <see cref="GameKitAuthOptions.AllowedProviderHosts"/> at registration time
    /// so the egress allow-list covers the Google backchannel.
    /// </summary>
    /// <remarks>
    /// Google endpoints used by <c>Microsoft.AspNetCore.Authentication.Google</c>:
    /// <list type="bullet">
    ///   <item><c>oauth2.googleapis.com</c> — token endpoint</item>
    ///   <item><c>www.googleapis.com</c> — userinfo endpoint (profile/email)</item>
    ///   <item><c>accounts.google.com</c> — OpenID metadata / JWKS</item>
    /// </list>
    /// SEC-05: these hosts are declared in code rather than read from configuration so that
    /// a misconfigured appsettings.json can never silently clear them.
    /// </remarks>
    /// <summary>
    /// The Google backchannel provider hosts allowlisted by this package.
    /// Exposed as a public constant so consumers and tests can verify the allowlist.
    /// </summary>
    public static readonly string[] GoogleProviderHosts =
    {
        "oauth2.googleapis.com",
        "www.googleapis.com",
        "accounts.google.com",
    };

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

        // SEC-05: Append Google backchannel hosts to the egress allow-list. GameKitAuthOptions
        // is registered as a singleton by AddAuth(); resolving it here ensures the SAME
        // options instance that EgressAllowListHandler snapshots at construction time is the
        // one we're augmenting. This approach (b per plan) keeps provider hosts co-located
        // with the provider package that needs them, rather than forcing them into
        // DefaultAllowedHosts (which is scoped to the two built-in Steam+Discord providers).
        var authOpts = builder.Services.BuildServiceProvider().GetService<GameKitAuthOptions>();
        if (authOpts is not null)
        {
            foreach (var host in GoogleProviderHosts)
            {
                if (!authOpts.AllowedProviderHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                    authOpts.AllowedProviderHosts.Add(host);
            }
        }

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

                    // SEC-05: Route the Google backchannel through EgressAllowListHandler so
                    // token-exchange and userinfo calls go through the egress allow-list.
                    // EgressAllowListHandler is a DelegatingHandler; it requires an InnerHandler
                    // (HttpClientHandler) to forward the request after the host check passes.
                    var resolvedOpts = builder.Services.BuildServiceProvider().GetService<GameKitAuthOptions>();
                    if (resolvedOpts is not null)
                    {
                        var inner = new HttpClientHandler();
                        var egressHandler = new EgressAllowListHandler(resolvedOpts)
                        {
                            InnerHandler = inner,
                        };
                        google.BackchannelHttpHandler = egressHandler;
                    }

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
