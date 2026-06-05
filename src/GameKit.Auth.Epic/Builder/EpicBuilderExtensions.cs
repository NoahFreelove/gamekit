// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using GameKit.Auth.Epic.Configuration;
using GameKit.Auth.Epic.Providers.Epic;
using GameKit.Auth.Providers;
using GameKit.Core.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Auth.Epic.Builder;

/// <summary>
/// Fluent-builder extensions that mount <c>GameKit.Auth.Epic</c> onto an existing
/// <see cref="IGameKitBuilder"/>.
/// </summary>
/// <remarks>
/// <b>Call order:</b> <c>AddEpic</c> must be called AFTER <c>AddAuth</c> on the same builder.
/// The Epic authentication scheme is only registered when both <see cref="GameKitEpicOptions.ClientId"/>
/// and <see cref="GameKitEpicOptions.ClientSecret"/> are supplied; omitting credentials allows the
/// <c>IOAuthProvider</c> to remain resolvable in test harnesses without triggering the Epic handler
/// (T-07-05-04 mitigation).
/// </remarks>
public static class EpicBuilderExtensions
{
    /// <summary>
    /// Registers the Epic <see cref="IOAuthProvider"/> (unconditionally) and the custom
    /// <see cref="EpicOAuthHandler"/> scheme (only when both credentials are present).
    /// </summary>
    /// <param name="builder">The <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <param name="configure">Delegate to configure <see cref="GameKitEpicOptions"/>.</param>
    /// <returns>The same <see cref="IGameKitBuilder"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IGameKitBuilder AddEpic(
        this IGameKitBuilder builder,
        Action<GameKitEpicOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new GameKitEpicOptions();
        configure(opts);

        // CRITICAL: Scrutor's IOAuthProvider scan in AddAuth() is scoped to the GameKit.Auth
        // assembly only (FromAssemblyOf<IOAuthProvider>()). Sibling-package providers MUST
        // self-register here — they are NOT auto-discovered. See RESEARCH §Pitfall 4.
        builder.Services.AddScoped<IOAuthProvider, EpicOAuthProvider>();

        // Register the custom Epic authentication scheme only when both credentials are present.
        // Without ClientId+ClientSecret the EpicOAuthHandler would fail on the first
        // /auth/login/epic request, breaking test harnesses that omit credentials.
        // T-07-05-04 mitigation.
        if (!string.IsNullOrEmpty(opts.ClientId) && !string.IsNullOrEmpty(opts.ClientSecret))
        {
            builder.Services.AddAuthentication()
                .AddOAuth<EpicOAuthOptions, EpicOAuthHandler>("Epic", epic =>
                {
                    epic.ClientId     = opts.ClientId!;
                    epic.ClientSecret = opts.ClientSecret!;
                    epic.CallbackPath = opts.CallbackPath;
                    // SaveTokens is false — GameKit issues its own JWT/refresh-token pair.
                    epic.SaveTokens = false;
                    // Do NOT add extra scopes — AUTH-22 no-scope-creep (basic_profile
                    // is already pre-configured in EpicOAuthOptions constructor).

                    epic.Events.OnCreatingTicket = async ctx =>
                    {
                        // Epic's stable account_id mapped to NameIdentifier by EpicOAuthHandler.
                        // This is the external_id used for UNIQUE(provider, external_id) upserts.
                        // NOT email — Epic does not expose email in basic_profile scope and
                        // email cannot be used as a stable identity key (T-07-05-02 mitigation).
                        var accountId = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (string.IsNullOrEmpty(accountId)) return;

                        // Display name from the display_name claim mapped by EpicOAuthHandler.
                        var name = ctx.Principal?.FindFirst(ClaimTypes.Name)?.Value;

                        // Epic does not provide an avatar URL in the basic_profile scope.
                        // avatarUrl is null — the player can upload one later via the Admin UI.

                        // Resolve the Epic IOAuthProvider registered above. Filter by
                        // Provider == "epic" since Scrutor and this explicit registration both
                        // use the interface.
                        var providers = ctx.HttpContext.RequestServices.GetServices<IOAuthProvider>();
                        IOAuthProvider? provider = null;
                        foreach (var p in providers)
                        {
                            if (p.Provider == "epic") { provider = p; break; }
                        }
                        if (provider is null) return;

                        // X-GameKit-Device fingerprint for refresh-token family isolation.
                        var fingerprint = ctx.HttpContext.Request.Headers["X-GameKit-Device"].ToString();
                        var fp = string.IsNullOrEmpty(fingerprint) ? null : fingerprint;

                        var result = await provider.CompleteLoginAsync(
                            accountId, name, avatarUrl: null, fp, ctx.HttpContext.RequestAborted)
                            .ConfigureAwait(false);

                        if (result is { Success: true, Tokens: not null })
                        {
                            // Stash the token pair in auth properties so /auth/callback/epic
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
