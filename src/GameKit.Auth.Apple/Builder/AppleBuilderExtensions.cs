// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GameKit.Auth;
using GameKit.Auth.Apple.Configuration;
using GameKit.Auth.Apple.Providers.Apple;
using GameKit.Auth.Egress;
using GameKit.Auth.Providers;
using GameKit.Core.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Auth.Apple.Builder;

/// <summary>
/// Fluent-builder extensions that mount <c>GameKit.Auth.Apple</c> onto an existing
/// <see cref="IGameKitBuilder"/>.
/// </summary>
/// <remarks>
/// <b>Call order:</b> <c>AddApple</c> must be called AFTER <c>AddAuth</c> on the same builder.
/// The Apple authentication scheme is only registered when <see cref="GameKitAppleOptions.ServiceId"/>
/// and <see cref="GameKitAppleOptions.PrivateKeyBase64"/> are both supplied; omitting credentials
/// allows the <c>IOAuthProvider</c> to remain resolvable in test harnesses without triggering
/// the Apple handler (T-07-04-05 mitigation).
/// <para>
/// <b>ES256 client secret:</b> Apple Sign-In uses a short-lived ES256 JWT as the client secret,
/// generated fresh per token exchange from the .p8 private key. This approach prevents the
/// 6-month <c>invalid_client</c> outage caused by a static pre-generated secret expiring
/// (T-07-04-01 mitigation). <c>GenerateClientSecret</c> is always
/// <see langword="true"/> — never use a static Apple client secret.
/// </para>
/// <para>
/// <b>Security:</b> The <see cref="GameKitAppleOptions.PrivateKeyBase64"/> value is the
/// base64-encoded content of the Apple .p8 key file. It must be loaded from an environment
/// variable or secrets manager — never baked into source code or container images.
/// </para>
/// </remarks>
public static class AppleBuilderExtensions
{
    /// <summary>
    /// Hosts that the Apple Sign-In backchannel must reach to exchange authorization codes
    /// for tokens. Added to <see cref="GameKitAuthOptions.AllowedProviderHosts"/> at
    /// registration time so the egress allow-list covers the Apple backchannel.
    /// </summary>
    /// <remarks>
    /// The Apple token endpoint is <c>https://appleid.apple.com/auth/token</c>.
    /// SEC-05: these hosts are declared in code rather than read from configuration so that
    /// a misconfigured appsettings.json can never silently clear them.
    /// </remarks>
    internal static readonly string[] AppleProviderHosts =
    {
        "appleid.apple.com",
    };

    /// <summary>
    /// Registers the Apple Sign-In <see cref="IOAuthProvider"/> (unconditionally) and the
    /// <c>AspNet.Security.OAuth.Apple</c> scheme (only when credentials are present).
    /// </summary>
    /// <param name="builder">The <see cref="IGameKitBuilder"/> from <c>AddGameKit()</c>.</param>
    /// <param name="configure">Delegate to configure <see cref="GameKitAppleOptions"/>.</param>
    /// <returns>The same <see cref="IGameKitBuilder"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IGameKitBuilder AddApple(
        this IGameKitBuilder builder,
        Action<GameKitAppleOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new GameKitAppleOptions();
        configure(opts);

        // CRITICAL: Scrutor's IOAuthProvider scan in AddAuth() is scoped to the GameKit.Auth
        // assembly only (FromAssemblyOf<IOAuthProvider>()). Sibling-package providers MUST
        // self-register here — they are NOT auto-discovered. See RESEARCH §Pitfall 4.
        builder.Services.AddScoped<IOAuthProvider, AppleOAuthProvider>();

        // SEC-05: Append Apple backchannel hosts to the egress allow-list. GameKitAuthOptions
        // is registered as a singleton by AddAuth(); resolving it here ensures the SAME
        // options instance that EgressAllowListHandler snapshots at construction time is the
        // one we're augmenting. This approach (b per plan) keeps provider hosts co-located
        // with the provider package that needs them, rather than forcing them into
        // DefaultAllowedHosts (which is scoped to the two built-in Steam+Discord providers).
        var authOpts = builder.Services.BuildServiceProvider().GetService<GameKitAuthOptions>();
        if (authOpts is not null)
        {
            foreach (var host in AppleProviderHosts)
            {
                if (!authOpts.AllowedProviderHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                    authOpts.AllowedProviderHosts.Add(host);
            }
        }

        // Register the Apple authentication scheme only when credentials are present.
        // Without ServiceId+PrivateKeyBase64 the Apple handler would throw on the first
        // /auth/login/apple request, breaking test harnesses that use SkipAuthenticationSchemeRegistration.
        if (!string.IsNullOrEmpty(opts.ServiceId) && !string.IsNullOrEmpty(opts.PrivateKeyBase64))
        {
            // WR-01: All four fields are required for ES256 client-secret generation. Fail fast at
            // registration time with an actionable message rather than null-forgiving (null!) into a
            // cryptic NullReferenceException deep inside the aspnet-contrib JWT-signing path.
            if (string.IsNullOrEmpty(opts.TeamId))
                throw new InvalidOperationException(
                    "GameKitAppleOptions.TeamId must be set when ServiceId and PrivateKeyBase64 are provided. " +
                    "TeamId is required by AspNet.Security.OAuth.Apple for ES256 client-secret generation.");
            if (string.IsNullOrEmpty(opts.KeyId))
                throw new InvalidOperationException(
                    "GameKitAppleOptions.KeyId must be set when ServiceId and PrivateKeyBase64 are provided. " +
                    "KeyId is required by AspNet.Security.OAuth.Apple for ES256 client-secret generation.");

            // Capture options into locals for the lambda closures below.
            var serviceId = opts.ServiceId;
            var teamId = opts.TeamId;
            var keyId = opts.KeyId;
            var privateKeyBase64 = opts.PrivateKeyBase64;
            var callbackPath = opts.CallbackPath;
            var expiresAfter = opts.ClientSecretExpiresAfter;

            builder.Services.AddAuthentication()
                .AddApple(apple =>
                {
                    apple.ClientId = serviceId;
                    apple.TeamId = teamId;   // non-null: guard above ensures TeamId is present
                    apple.KeyId = keyId;     // non-null: guard above ensures KeyId is present
                    apple.CallbackPath = callbackPath;

                    // SEC-05: Route the Apple backchannel through EgressAllowListHandler so
                    // token-exchange calls to appleid.apple.com go through the egress allow-list.
                    // EgressAllowListHandler is a DelegatingHandler; it requires an InnerHandler
                    // (HttpClientHandler) to forward the request after the host check passes.
                    // We use the GameKitAuthOptions singleton that AddAuth() registered — the
                    // same instance the DI-registered EgressAllowListHandler snapshots —
                    // so the Apple provider host added above is visible to this handler.
                    var resolvedOpts = builder.Services.BuildServiceProvider().GetService<GameKitAuthOptions>();
                    if (resolvedOpts is not null)
                    {
                        var inner = new HttpClientHandler();
                        var egressHandler = new EgressAllowListHandler(resolvedOpts)
                        {
                            InnerHandler = inner,
                        };
                        apple.BackchannelHttpHandler = egressHandler;
                    }

                    // T-07-04-01: GenerateClientSecret MUST be true. Apple client secrets
                    // are short-lived ES256 JWTs signed with the .p8 key. A static pre-generated
                    // secret will expire at most 180 days after creation, producing an
                    // invalid_client error for ALL users simultaneously. Setting this to true
                    // causes the handler to generate a fresh secret per token exchange.
                    apple.GenerateClientSecret = true;
                    apple.ClientSecretExpiresAfter = expiresAfter;

                    // Provide the .p8 private key PEM content via the PrivateKey delegate.
                    // The PrivateKey delegate is called per token exchange to supply the
                    // PKCS#8 PEM bytes to the aspnet-contrib ES256 signer.
                    // The base64 encoding wraps the UTF-8 .p8 file bytes (PEM text).
                    // NOTE: The raw PEM content is never logged — it is used only ephemerally
                    // inside the Apple handler's ES256 signing path (T-07-04-03 mitigation).
                    apple.PrivateKey = (_, _) =>
                    {
                        // Decode the base64-wrapped PEM content and return as ReadOnlyMemory<char>.
                        // The .p8 file downloaded from Apple Developer Portal is a PEM text file
                        // (-----BEGIN PRIVATE KEY----- / PKCS#8 body / -----END PRIVATE KEY-----)
                        // stored base64-encoded in PrivateKeyBase64 (from env/secret).
                        var pemBytes = Convert.FromBase64String(privateKeyBase64);
                        var pemString = Encoding.UTF8.GetString(pemBytes);
                        return Task.FromResult(pemString.AsMemory());
                    };

                    // Minimal scopes only: name + email (AUTH-22 no-scope-creep).
                    // Apple only returns name and email on the FIRST authorization.
                    apple.Scope.Clear();
                    apple.Scope.Add("name");
                    apple.Scope.Add("email");
                    apple.SaveTokens = false;

                    apple.Events.OnCreatingTicket = async ctx =>
                    {
                        // T-07-04-02: The Apple sub claim is the stable opaque user identifier.
                        // It is NOT the email address. The private-relay email (and name) is
                        // provided only on the first authorization — it must NOT be used as the
                        // identity key because:
                        //   (a) it is not returned on subsequent logins,
                        //   (b) the user may revoke relay email access.
                        // We extract sub directly from the claims principal.
                        var sub = ctx.Principal?.FindFirst("sub")?.Value;
                        if (string.IsNullOrEmpty(sub)) return;

                        // Name is only populated on first authorization; will be null on re-auth.
                        var name = ctx.Principal?.FindFirst(ClaimTypes.Name)?.Value
                                ?? ctx.Principal?.FindFirst("name")?.Value;

                        // Relay email — may be a private-relay address (@privaterelay.appleid.com).
                        // Stored as-is in Metadata JSONB on first login only. May be null on
                        // subsequent authorizations.
                        // Pass relay email through the avatarUrl slot to keep IOAuthProvider
                        // signature intact (Apple has no avatar URL concept).
                        var relayEmail = ctx.Principal?.FindFirst(ClaimTypes.Email)?.Value
                                      ?? ctx.Principal?.FindFirst("email")?.Value;

                        // Resolve the Apple IOAuthProvider registered above. We filter by
                        // Provider == "apple" rather than using GetRequiredService<AppleOAuthProvider>()
                        // because the interface registration is what callers depend on.
                        var providers = ctx.HttpContext.RequestServices.GetServices<IOAuthProvider>();
                        IOAuthProvider? provider = null;
                        foreach (var p in providers)
                        {
                            if (p.Provider == "apple") { provider = p; break; }
                        }
                        if (provider is null) return;

                        // X-GameKit-Device fingerprint for refresh-token family isolation.
                        var fingerprint = ctx.HttpContext.Request.Headers["X-GameKit-Device"].ToString();
                        var fp = string.IsNullOrEmpty(fingerprint) ? null : fingerprint;

                        // Pass sub as externalId, name as displayName, and relay email through
                        // the avatarUrl slot (the provider stores relay email in Metadata on first
                        // login only; it is NOT stored as avatarUrl in the database row).
                        var result = await provider.CompleteLoginAsync(
                            sub, name, relayEmail, fp, ctx.HttpContext.RequestAborted)
                            .ConfigureAwait(false);

                        if (result is { Success: true, Tokens: not null })
                        {
                            // Stash the token pair in auth properties so /auth/callback/apple
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
