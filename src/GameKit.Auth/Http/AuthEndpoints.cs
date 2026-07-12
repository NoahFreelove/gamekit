// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Http.Contracts;
using GameKit.Auth.Http.EndpointFilters;
using GameKit.Auth.Providers;
using GameKit.Auth.Providers.Password;
using GameKit.Auth.Providers.Steam;
using GameKit.Auth.Services;
using GameKit.Core.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Auth.Http;

/// <summary>
/// Registers the Phase-2 <c>/auth/*</c> minimal-API endpoint group. Called from
/// <see cref="Builder.AuthApplicationBuilderExtensions.MapAuth"/> (plan 02-03 skeleton
/// extension point). Covers the 10 endpoints specified by CONTEXT + PLAN:
/// <list type="bullet">
///   <item><c>POST /auth/login/{provider}</c> — generic login for password / guest / external (body-driven).</item>
///   <item><c>POST /auth/refresh</c> — Pattern-3 rotation (45 s grace + fingerprint gate).</item>
///   <item><c>POST /auth/register</c> — password register or D-12 guest-upgrade-in-place when a guest Bearer is presented.</item>
///   <item><c>POST /auth/logout</c> — family revoke via the presented refresh token.</item>
///   <item><c>POST /auth/logout/all</c> — revoke every family for the current player.</item>
///   <item><c>GET /auth/me</c> — claim-bag probe (returns <c>sub</c> / <c>is_guest</c> / <c>provider</c>).</item>
///   <item><c>GET /auth/challenge/{provider}</c> — 302 to Steam OpenID OP or Discord OAuth2 challenge.</item>
///   <item><c>GET /auth/callback/{provider}</c> — Steam OpenID <c>check_authentication</c> roundtrip or Discord ticket consumption.</item>
///   <item><c>POST /auth/link/{provider}</c> — authenticated identity link delegated to <see cref="IIdentityLinker"/>.</item>
/// </list>
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Maps every Phase-2 /auth endpoint under <c>/auth</c>.</summary>
    /// <param name="routes">The endpoint route builder (typically the <c>WebApplication</c>).</param>
    /// <param name="policies">Rate-limit policy names (from <see cref="IGameKitRateLimitPolicies"/>).</param>
    /// <returns>The /auth route group so callers can further compose metadata.</returns>
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes, IGameKitRateLimitPolicies policies)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(policies);

        var grp = routes.MapGroup("/auth").WithTags("GameKit.Auth");

        grp.MapPost("/login/{provider}", LoginAsync)
            .AddEndpointFilter<ValidationEndpointFilter<LoginRequest>>()
            .RequireRateLimiting(policies.AuthLogin);

        grp.MapPost("/refresh", RefreshAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RefreshRequest>>()
            .RequireRateLimiting(policies.AuthRefresh);

        grp.MapPost("/register", RegisterAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RegisterRequest>>()
            .RequireRateLimiting(policies.AuthRegister);

        // /logout takes a refresh token in the body. The token itself is the revocation
        // capability (OAuth2 RFC 7009 semantics) — do NOT require a Bearer, because an
        // expired access token would then 401 logout and leave the refresh family un-revoked
        // (a real security hole if the refresh token leaked). The handler is idempotent:
        // revoking an unknown or already-revoked family is a no-op 204.
        grp.MapPost("/logout", LogoutAsync)
            .AddEndpointFilter<ValidationEndpointFilter<LogoutRequest>>();

        grp.MapPost("/logout/all", LogoutAllAsync)
            .RequireAuthorization();

        grp.MapGet("/me", MeAsync)
            .RequireAuthorization();

        grp.MapGet("/challenge/{provider}", ChallengeAsync);
        grp.MapGet("/callback/{provider}", CallbackAsync);

        grp.MapPost("/link/{provider}", LinkAsync)
            .AddEndpointFilter<ValidationEndpointFilter<LinkRequest>>()
            .RequireAuthorization();

        return grp;
    }

    // ---- handlers ----

    private static async Task<IResult> LoginAsync(
        string provider,
        LoginRequest req,
        HttpContext http,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var impl = sp.GetServices<IOAuthProvider>().FirstOrDefault(p => p.Provider == provider);
        if (impl is null)
        {
            return Results.BadRequest(new AuthErrorResponse("unknown_provider", provider));
        }

        var fingerprint = GetFingerprint(http);

        // Password provider convention (see PasswordOAuthProvider xml-doc): externalId = Username,
        // displayName = Password. Guest/Steam/Discord providers ignore both string parameters at
        // this entry point (Steam/Discord are normally reached via /auth/callback/{provider}).
        var result = await impl.CompleteLoginAsync(
            externalId: req.Username ?? string.Empty,
            displayName: req.Password,
            avatarUrl: null,
            fingerprint: fingerprint,
            cancellationToken: ct).ConfigureAwait(false);

        if (result.Success)
            return Results.Ok(new TokenResponse(result.Tokens!.AccessJwt, result.Tokens!.RawRefresh));

        // D-03 ban enforcement: BannedCheckHelper returns ErrorCode of shape "banned:<16hex>".
        // Surface as 403 Forbidden with a problem-shaped body the player + admin can work with:
        // error = "banned" (stable machine-readable discriminator), reason_hash = 16-char hex
        // so the admin can cross-reference the audit log without leaking the plaintext reason.
        var errorCode = result.ErrorCode ?? "invalid_credentials";
        if (errorCode.StartsWith("banned:", StringComparison.Ordinal))
        {
            var reasonHash = errorCode["banned:".Length..];
            return Results.Json(
                new AuthErrorResponse("banned", provider, reasonHash),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Json(
            new AuthErrorResponse(errorCode, provider),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest req,
        HttpContext http,
        IRefreshTokenService svc,
        CancellationToken ct)
    {
        var fingerprint = GetFingerprint(http);
        try
        {
            var pair = await svc.RotateAsync(req.RefreshToken, fingerprint, ct).ConfigureAwait(false);
            return Results.Ok(new TokenResponse(pair.AccessJwt, pair.RawRefresh));
        }
        catch (UnauthorizedException ux)
        {
            return Results.Json(new AuthErrorResponse(ux.Code), statusCode: StatusCodes.Status401Unauthorized);
        }
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest req,
        HttpContext http,
        PasswordOAuthProvider passwordProvider,
        IGuestUpgradeService upgrade,
        CancellationToken ct)
    {
        var fingerprint = GetFingerprint(http);

        // D-12: if caller is authenticated as a guest, upgrade in place (same Player.Id,
        // attach a PlayerCredential, re-issue a non-guest token).
        if (http.User.Identity?.IsAuthenticated == true)
        {
            var sub = http.User.FindFirst("sub")?.Value ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isGuest = string.Equals(http.User.FindFirst("is_guest")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            if (sub is not null && Guid.TryParse(sub, out var playerId) && isGuest)
            {
                try
                {
                    var tokens = await upgrade
                        .UpgradeToPasswordAsync(playerId, req.Username, req.Password, fingerprint, ct)
                        .ConfigureAwait(false);
                    return Results.Ok(new TokenResponse(tokens.AccessJwt, tokens.RawRefresh));
                }
                catch (UsernameAlreadyTakenException)
                {
                    return Results.Json(
                        new AuthErrorResponse("username_taken"),
                        statusCode: StatusCodes.Status409Conflict);
                }
            }
        }

        var result = await passwordProvider
            .RegisterAsync(req.Username, req.Password, req.DisplayName, fingerprint, ct)
            .ConfigureAwait(false);
        if (!result.Success)
        {
            var errorCode = result.ErrorCode ?? "bad_request";
            // D-03: BannedCheckHelper.CheckAsync runs inside RegisterAsync too (future-proof for
            // refactors that might reuse Player rows); surface same shape as login path.
            if (errorCode.StartsWith("banned:", StringComparison.Ordinal))
            {
                var reasonHash = errorCode["banned:".Length..];
                return Results.Json(
                    new AuthErrorResponse("banned", "password", reasonHash),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var status = errorCode == "username_taken"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            return Results.Json(
                new AuthErrorResponse(errorCode),
                statusCode: status);
        }
        return Results.Ok(new TokenResponse(result.Tokens!.AccessJwt, result.Tokens!.RawRefresh));
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest req,
        IRefreshTokenService svc,
        CancellationToken ct)
    {
        await svc.RevokeFamilyAsync(req.RefreshToken, "manual_logout", ct).ConfigureAwait(false);
        // 204 No Content — client already knows the token it logged out.
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(
        HttpContext http,
        IRefreshTokenService svc,
        CancellationToken ct)
    {
        var sub = http.User.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var playerId))
        {
            return Results.Unauthorized();
        }
        await svc.RevokeAllForPlayerAsync(playerId, "logout_all", ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static IResult MeAsync(HttpContext http)
    {
        var sub = http.User.FindFirst("sub")?.Value;
        if (sub is null)
        {
            return Results.Unauthorized();
        }
        var isGuest = string.Equals(http.User.FindFirst("is_guest")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        var provider = http.User.FindFirst("provider")?.Value;
        return Results.Ok(new { player_id = sub, is_guest = isGuest, provider });
    }

    private static IResult ChallengeAsync(
        string provider,
        HttpContext http,
        GameKitAuthOptions opts)
    {
        if (string.Equals(provider, "steam", StringComparison.OrdinalIgnoreCase))
        {
            var realm = string.IsNullOrEmpty(opts.Steam.Realm)
                ? $"{http.Request.Scheme}://{http.Request.Host}/"
                : opts.Steam.Realm;
            // Append the callback path relative to the realm so Steam redirects back to /auth/callback/steam.
            var returnTo = new Uri(new Uri(realm, UriKind.Absolute), opts.Steam.CallbackPath).ToString();
            var qs = new Dictionary<string, string>
            {
                ["openid.ns"] = "http://specs.openid.net/auth/2.0",
                ["openid.mode"] = "checkid_setup",
                ["openid.return_to"] = returnTo,
                ["openid.realm"] = realm,
                ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
                ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
            };
            var encoded = string.Join("&", qs.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            return Results.Redirect($"{opts.Steam.OpenIdEndpoint}?{encoded}");
        }

        if (string.Equals(provider, "discord", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = opts.Discord.CallbackPath },
                new[] { "Discord" });
        }

        return Results.BadRequest(new AuthErrorResponse("unknown_provider", provider));
    }

    private static async Task<IResult> CallbackAsync(
        string provider,
        HttpContext http,
        IServiceProvider sp,
        CancellationToken ct)
    {
        if (string.Equals(provider, "steam", StringComparison.OrdinalIgnoreCase))
        {
            var verifier = sp.GetRequiredService<SteamOpenIdVerifier>();
            var verification = await verifier.VerifyAsync(http.Request.Query, ct).ConfigureAwait(false);
            if (!verification.IsValid)
            {
                return Results.Json(
                    new AuthErrorResponse("invalid_assertion", "steam"),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var steamProvider = sp.GetServices<IOAuthProvider>().FirstOrDefault(p => p.Provider == "steam");
            if (steamProvider is null)
            {
                return Results.Json(
                    new AuthErrorResponse("provider_not_registered", "steam"),
                    statusCode: StatusCodes.Status500InternalServerError);
            }
            var fingerprint = GetFingerprint(http);
            var result = await steamProvider
                .CompleteLoginAsync(verification.SteamId64!, null, null, fingerprint, ct)
                .ConfigureAwait(false);
            if (result.Success)
                return BrowserTokenBridge(result.Tokens!.AccessJwt, result.Tokens!.RawRefresh);
            // D-03 ban enforcement mirrored on the callback path.
            var steamErrorCode = result.ErrorCode ?? "login_failed";
            if (steamErrorCode.StartsWith("banned:", StringComparison.Ordinal))
            {
                var reasonHash = steamErrorCode["banned:".Length..];
                return Results.Json(
                    new AuthErrorResponse("banned", "steam", reasonHash),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            return Results.Json(
                new AuthErrorResponse(steamErrorCode, "steam"),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.Equals(provider, "discord", StringComparison.OrdinalIgnoreCase))
        {
            // Plan 02-05's Events.OnCreatingTicket stashed the issued tokens in Properties.Items.
            var authResult = await http.AuthenticateAsync("Discord").ConfigureAwait(false);
            if (!authResult.Succeeded || authResult.Properties is null)
            {
                return Results.Unauthorized();
            }

            authResult.Properties.Items.TryGetValue("gamekit.access_jwt", out var access);
            authResult.Properties.Items.TryGetValue("gamekit.refresh_raw", out var refresh);
            if (string.IsNullOrEmpty(access))
            {
                return Results.Unauthorized();
            }
            return BrowserTokenBridge(access, refresh);
        }

        return Results.BadRequest(new AuthErrorResponse("unknown_provider", provider));
    }

    /// <summary>
    /// Returns a minimal HTML bridge page that stashes the issued tokens in <c>localStorage</c>
    /// and redirects to <c>/</c>. Used by <c>/auth/callback/{provider}</c> endpoints because the
    /// provider's redirect delivers the browser directly to the callback URL — raw JSON would
    /// render as-is. Tokens embedded via <see cref="System.Text.Json.JsonEncodedText"/> to
    /// guarantee they cannot inject script syntax.
    /// </summary>
    private static IResult BrowserTokenBridge(string accessJwt, string? refreshRaw)
    {
        var accessJson = System.Text.Json.JsonEncodedText.Encode(accessJwt).ToString();
        var refreshJson = System.Text.Json.JsonEncodedText.Encode(refreshRaw ?? string.Empty).ToString();
        var html =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>Signing you in…</title></head>"
            + "<body><p>Signing you in…</p><script>"
            + "try{localStorage.setItem('gk.access_token',\"" + accessJson + "\");"
            + "localStorage.setItem('gk.refresh_token',\"" + refreshJson + "\");}"
            + "catch(e){document.body.textContent='Unable to persist tokens: '+e;}"
            + "location.replace('/');"
            + "</script></body></html>";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> LinkAsync(
        string provider,
        LinkRequest req,
        HttpContext http,
        IIdentityLinker linker,
        SteamOpenIdVerifier steamVerifier,
        CancellationToken ct)
    {
        var sub = http.User.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var playerId))
        {
            return Results.Unauthorized();
        }

        string externalId;
        if (string.Equals(provider, "steam", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(req.ExternalId))
            {
                externalId = req.ExternalId!;
            }
            else
            {
                var v = await steamVerifier.VerifyAsync(http.Request.Query, ct).ConfigureAwait(false);
                if (!v.IsValid)
                {
                    return Results.BadRequest(new AuthErrorResponse("invalid_assertion", "steam"));
                }
                externalId = v.SteamId64!;
            }
        }
        else if (string.Equals(provider, "discord", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(req.ExternalId))
            {
                return Results.BadRequest(new AuthErrorResponse("external_id_required", "discord"));
            }
            externalId = req.ExternalId!;
        }
        else
        {
            return Results.BadRequest(new AuthErrorResponse("unknown_provider", provider));
        }

        var result = await linker.LinkAsync(playerId, provider, externalId, ct).ConfigureAwait(false);
        return result.Kind switch
        {
            LinkResultKind.Linked or LinkResultKind.AlreadyLinkedToSelf => Results.Ok(),
            LinkResultKind.AlreadyLinkedToOtherPlayer => Results.Json(
                new AuthErrorResponse("identity_already_linked", provider, result.ExternalIdHash),
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static string? GetFingerprint(HttpContext http)
    {
        var raw = http.Request.Headers["X-GameKit-Device"].ToString();
        return string.IsNullOrEmpty(raw) ? null : raw;
    }
}
