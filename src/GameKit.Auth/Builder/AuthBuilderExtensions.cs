// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using AspNet.Security.OAuth.Discord;
using FluentValidation;
using GameKit.Auth.Data;
using GameKit.Auth.Egress;
using GameKit.Auth.Health;
using GameKit.Auth.Http.Contracts;
using GameKit.Auth.Http.EndpointFilters;
using GameKit.Auth.Http.RateLimiting;
using GameKit.Auth.Http.Validators;
using GameKit.Auth.Providers;
using GameKit.Auth.Providers.Discord;
using GameKit.Auth.Providers.Steam;
using GameKit.Auth.Services;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Health;
using GameKit.Core.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GameKit.Auth.Builder;

/// <summary>Fluent-builder extensions that mount GameKit.Auth onto an existing <see cref="IGameKitBuilder"/>.</summary>
public static class AuthBuilderExtensions
{
    /// <summary>
    /// Registers GameKit.Auth services: options singleton, the Auth <see cref="IModelBuilderExtension"/>,
    /// the two named <c>HttpClient</c> instances (<c>gamekit.auth.provider.steam</c>,
    /// <c>gamekit.auth.provider.discord</c>) with resilience pipelines and the egress handler.
    /// Authentication-scheme (JwtBearer + Steam + Discord) registration and IOAuthProvider discovery
    /// land in later Phase-2 plans; this method wires the skeleton.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="JwtOptions.Issuer"/> or <see cref="JwtOptions.Audience"/> is empty,
    /// or when <see cref="JwtOptions.PrivateKeyPemPath"/> points at an unreadable file and
    /// <see cref="GameKitAuthOptions.SkipAuthenticationSchemeRegistration"/> is <c>false</c>.
    /// </exception>
    public static IGameKitBuilder AddAuth(
        this IGameKitBuilder builder,
        Action<GameKitAuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new GameKitAuthOptions();
        configure(opts);

        ValidateAuthOptions(opts);

        builder.Services.AddSingleton(opts);

        // 1. Register the Auth model-builder extension so AUTH entities land in GameKitDbContext.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());

        // 1a. Auth migration runner — hosted service applies __ef_migrations_auth after Core migrations
        //     run in UseGameKit, and before Kestrel accepts traffic. See AuthMigrationHostedService.
        builder.Services.AddHostedService<AuthMigrationHostedService>();
        // 1b. Auth migration readiness reporter — reports whether __ef_migrations_auth migrations are
        //     all applied. Registered as an enumerable singleton so the Core aggregate "migrations"
        //     health check can discover all six IMigrationReadinessReporter implementations.
        builder.Services.AddSingleton<IMigrationReadinessReporter, AuthMigrationReadinessReporter>();

        // 2. Egress handler — singleton-captured allow-list; registered as transient per MS guidance for DelegatingHandler.
        builder.Services.AddTransient<EgressAllowListHandler>();

        // 3. Named HttpClient: Steam provider.
        builder.Services.AddHttpClient("gamekit.auth.provider.steam")
            .AddHttpMessageHandler<EgressAllowListHandler>()
            .AddStandardResilienceHandler();

        // 4. Named HttpClient: Discord provider (aspnet-contrib Discord handler's Backchannel is later
        //    swapped to this client by plan 02-05 via IPostConfigureOptions<DiscordAuthenticationOptions>).
        builder.Services.AddHttpClient("gamekit.auth.provider.discord")
            .AddHttpMessageHandler<EgressAllowListHandler>()
            .AddStandardResilienceHandler();

        // 5. Auth services — leaf hashers (singleton, stateless) + scoped services that touch DbContext.
        //    Registered BEFORE the scheme guard so unit tests that skip scheme registration can still
        //    resolve the services via DI for direct testing.
        builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        builder.Services.AddSingleton<IExternalIdHasher, ExternalIdHasher>();
        builder.Services.AddScoped<IIsGuestResolver, IsGuestResolver>();
        builder.Services.AddScoped<IJwtIssuer, JwtIssuer>();
        builder.Services.AddScoped<IAuthAuditWriter, AuthAuditWriter>();
        builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        // Plan 02-06 transactional services — IdentityLinker + GuestUpgradeService drive the
        // D-14 guest-upgrade race + D-11 cross-player-collision paths. Both run SERIALIZABLE
        // transactions internally and depend on IAuthAuditWriter (scoped) + the scoped DbContext.
        builder.Services.AddScoped<IIdentityLinker, IdentityLinker>();
        builder.Services.AddScoped<IGuestUpgradeService, GuestUpgradeService>();

        // Plan 10-03 — irreversible superadmin merge service. Scoped (touches DbContext).
        // IConnectionMultiplexer is resolved as optional: Redis is only consumed by AccountMergeService
        // for post-commit presence-key cleanup; Auth installs and runs correctly without Redis
        // (stale keys TTL-expire naturally per Pitfall 7 / T-10-03-08).
        builder.Services.AddScoped<IAccountMergeService, AccountMergeService>();

        // 5a. SteamOpenIdVerifier — consumed directly by the /auth/callback/steam endpoint (plan 02-07).
        //     Registered as scoped so it participates in the same HttpClient/options scope as its caller.
        //     Not an IOAuthProvider (it's a helper, not a strategy); Scrutor skips it.
        builder.Services.AddScoped<SteamOpenIdVerifier>();

        // 5b. Scrutor scan for IOAuthProvider implementations. Picks up SteamOAuthProvider +
        //     DiscordOAuthProvider in THIS plan, and GuestOAuthProvider + PasswordOAuthProvider
        //     in plan 02-06. Customer-authored providers in a consuming assembly are ALSO picked
        //     up — customers can drop an IOAuthProvider into their assembly and AddAuth scans it
        //     along with our own.
        //
        //     FromAssemblyOf<IOAuthProvider>() scans the GameKit.Auth assembly (where the interface
        //     lives); customer assemblies extend this by registering their own providers manually
        //     via services.AddScoped<IOAuthProvider, MyCustomProvider>() BEFORE AddAuth — Scrutor
        //     deduplicates via service+implementation pair.
        // publicOnly: false — our built-in providers are internal sealed (SteamOAuthProvider,
        // DiscordOAuthProvider, and future GuestOAuthProvider / PasswordOAuthProvider in plan 02-06).
        // Scrutor's default `publicOnly: true` would silently skip them. Customer-authored providers
        // remain discoverable regardless of access modifier.
        builder.Services.Scan(scan => scan
            .FromAssemblyOf<IOAuthProvider>()
            .AddClasses(c => c.AssignableTo<IOAuthProvider>(), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        // 5c. Discord backchannel post-configure — routes Options.Backchannel through our named
        //     HttpClient. Registered UNCONDITIONALLY so unit tests with SkipAuthenticationSchemeRegistration=true
        //     can still introspect the registration via DI.
        builder.Services.AddSingleton<
            IPostConfigureOptions<DiscordAuthenticationOptions>,
            DiscordBackchannelPostConfigure>();

        // 5d. Plan 02-07 — /auth/* HTTP surface:
        //     (i) FluentValidation validators for each request DTO (scoped so the regex in
        //         RegisterRequestValidator can resolve the singleton GameKitAuthOptions),
        //     (ii) Rate-limit policies under the Phase-1 IGameKitRateLimitPolicies names.
        //     The Password provider is also registered under its concrete type so the
        //     /auth/register endpoint can call RegisterAsync (which is NOT on the IOAuthProvider
        //     interface — see PasswordOAuthProvider.RegisterAsync xml-doc).
        builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
        builder.Services.AddScoped<IValidator<RefreshRequest>, RefreshRequestValidator>();
        builder.Services.AddScoped<IValidator<LogoutRequest>, LogoutRequestValidator>();
        builder.Services.AddScoped<IValidator<LinkRequest>, LinkRequestValidator>();

        // Concrete-type registration for PasswordOAuthProvider so /auth/register can invoke
        // RegisterAsync. The Scrutor scan above registers it only under IOAuthProvider; adding
        // the concrete mapping here forwards to the same scoped instance already created for
        // the interface (AddScoped<TImpl> followed by AddScoped<IInterface, TImpl> produces
        // two separate registrations — we resolve from the interface set instead to keep
        // lifetime semantics consistent). To avoid a duplicate instance per scope we use a
        // factory that returns the already-registered IOAuthProvider matching Provider="password".
        builder.Services.AddScoped<GameKit.Auth.Providers.Password.PasswordOAuthProvider>(sp =>
        {
            foreach (var p in sp.GetServices<IOAuthProvider>())
            {
                if (p is GameKit.Auth.Providers.Password.PasswordOAuthProvider password)
                    return password;
            }
            throw new InvalidOperationException(
                "PasswordOAuthProvider was not discovered by the Scrutor IOAuthProvider scan. " +
                "Verify GameKit.Auth is in the DI composition root.");
        });

        // Rate-limit policies (AUTH-15). Uses the default GameKitRateLimitPolicies implementation
        // constants; IGameKitRateLimitPolicies is registered by AddGameKit (plan 01-05) so we
        // resolve a fresh instance here rather than calling BuildServiceProvider() mid-registration.
        builder.Services.AddAuthRateLimits(new GameKitRateLimitPolicies());

        // 6. Authentication schemes — JwtBearer is wired here (plan 02-04). Steam OpenID + Discord OAuth2
        //    land in plan 02-05. The skip flag lets unit tests that do not want to load PEMs build without
        //    scheme registration (primarily `AuthBuilderOptionsValidationTests` with SkipAuthenticationSchemeRegistration=true).
        if (!opts.SkipAuthenticationSchemeRegistration)
        {
            // Load public key for validation (separate from the signing key used in JwtIssuer —
            // JwtIssuer loads the PRIVATE key from opts.Jwt.PrivateKeyPemPath at construction).
            var publicRsa = System.Security.Cryptography.RSA.Create();
            publicRsa.ImportFromPem(File.ReadAllText(opts.Jwt.PublicKeyPemPath));
            var validationKey = new RsaSecurityKey(publicRsa) { KeyId = opts.Jwt.Kid };

            var authBuilder = builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwt =>
                {
                    jwt.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer           = true,
                        ValidateAudience         = true,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime         = true,
                        ValidIssuer              = opts.Jwt.Issuer,
                        ValidAudience            = opts.Jwt.Audience,
                        IssuerSigningKey         = validationKey,
                        ClockSkew                = opts.Jwt.ClockSkew,
                        RequireSignedTokens      = true,
                    };
                    jwt.MapInboundClaims = false;   // preserve "sub" literally (RESEARCH §15 open question #6)
                });

            // Steam is deliberately NOT registered as an authentication scheme — see CONTEXT D-09.
            // SteamOpenIdVerifier is invoked directly from the /auth/callback/steam endpoint (plan 02-07).
            // Discord IS registered as an authentication scheme via aspnet-contrib's .AddDiscord(),
            // but only when the consumer supplied ClientId + ClientSecret (otherwise the handler would
            // throw at runtime on the first /auth/login/discord request).
            if (!string.IsNullOrEmpty(opts.Discord.ClientId) && !string.IsNullOrEmpty(opts.Discord.ClientSecret))
            {
                authBuilder.AddDiscord(discord =>
                {
                    discord.ClientId     = opts.Discord.ClientId;
                    discord.ClientSecret = opts.Discord.ClientSecret;
                    discord.CallbackPath = opts.Discord.CallbackPath;
                    // Lock scope to "identify" ONLY per AUTH-07 / D-10. Clear whatever defaults
                    // aspnet-contrib seeded and re-add the single scope.
                    discord.Scope.Clear();
                    discord.Scope.Add("identify");
                    discord.SaveTokens = false;

                    discord.Events.OnCreatingTicket = async ctx =>
                    {
                        // ctx.User is a JsonElement of Discord's /users/@me response.
                        if (!ctx.User.TryGetProperty("id", out var idProp) ||
                            !ctx.User.TryGetProperty("username", out var nameProp))
                            return;

                        var discordId = idProp.GetString();
                        var username  = nameProp.GetString();
                        if (string.IsNullOrEmpty(discordId) || string.IsNullOrEmpty(username))
                            return;

                        // Resolve the concrete Discord provider from the scoped IOAuthProvider set.
                        // Scrutor registers it as IOAuthProvider only; we filter by `Provider == "discord"`
                        // so we don't require a second registration of the concrete type.
                        var providers = ctx.HttpContext.RequestServices
                            .GetServices<IOAuthProvider>();
                        IOAuthProvider? provider = null;
                        foreach (var p in providers)
                        {
                            if (p.Provider == "discord") { provider = p; break; }
                        }
                        if (provider is null) return;
                        var fingerprint = ctx.HttpContext.Request.Headers["X-GameKit-Device"].ToString();
                        var fp = string.IsNullOrEmpty(fingerprint) ? null : fingerprint;
                        var result = await provider.CompleteLoginAsync(
                            discordId, username, avatarUrl: null, fp, ctx.HttpContext.RequestAborted)
                            .ConfigureAwait(false);

                        if (result is { Success: true, Tokens: not null })
                        {
                            // Stash token pair in auth properties so /auth/callback/discord (plan 02-07)
                            // can read and return it to the client.
                            ctx.Properties.Items["gamekit.access_jwt"] = result.Tokens.AccessJwt;
                            ctx.Properties.Items["gamekit.refresh_raw"] = result.Tokens.RawRefresh;
                            ctx.Properties.Items["gamekit.player_id"] = result.PlayerId?.ToString();
                        }
                    };
                });
            }
        }

        return builder;
    }

    /// <summary>Fail-fast validator for the public options surface.</summary>
    internal static void ValidateAuthOptions(GameKitAuthOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.Jwt.Issuer))
            throw new ArgumentException(
                $"{nameof(GameKitAuthOptions)}.{nameof(GameKitAuthOptions.Jwt)}.{nameof(JwtOptions.Issuer)} must be set.",
                nameof(opts));

        if (string.IsNullOrWhiteSpace(opts.Jwt.Audience))
            throw new ArgumentException(
                $"{nameof(GameKitAuthOptions)}.{nameof(GameKitAuthOptions.Jwt)}.{nameof(JwtOptions.Audience)} must be set.",
                nameof(opts));

        if (!opts.SkipAuthenticationSchemeRegistration)
        {
            if (string.IsNullOrWhiteSpace(opts.Jwt.PrivateKeyPemPath) || !File.Exists(opts.Jwt.PrivateKeyPemPath))
                throw new ArgumentException(
                    $"{nameof(GameKitAuthOptions)}.{nameof(GameKitAuthOptions.Jwt)}.{nameof(JwtOptions.PrivateKeyPemPath)} " +
                    "must point at a readable RSA private key PEM file (mode 0600, owned by the process user). " +
                    $"Value received: '{opts.Jwt.PrivateKeyPemPath}'.",
                    nameof(opts));

            if (string.IsNullOrWhiteSpace(opts.Jwt.PublicKeyPemPath) || !File.Exists(opts.Jwt.PublicKeyPemPath))
                throw new ArgumentException(
                    $"{nameof(GameKitAuthOptions)}.{nameof(GameKitAuthOptions.Jwt)}.{nameof(JwtOptions.PublicKeyPemPath)} " +
                    "must point at a readable RSA public key PEM file. " +
                    $"Value received: '{opts.Jwt.PublicKeyPemPath}'.",
                    nameof(opts));
        }

        if (opts.AllowedProviderHosts.Count == 0)
            throw new ArgumentException(
                $"{nameof(GameKitAuthOptions)}.{nameof(GameKitAuthOptions.AllowedProviderHosts)} must contain at least one host. " +
                "Default is populated from GameKit.Auth.Egress.DefaultAllowedHosts.All; clearing it disables every provider.",
                nameof(opts));
    }
}
