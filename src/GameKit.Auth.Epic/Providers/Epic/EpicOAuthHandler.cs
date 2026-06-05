// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameKit.Auth.Epic.Providers.Epic;

/// <summary>
/// Custom ASP.NET Core <see cref="OAuthHandler{T}"/> for the Epic Games OAuth 2.0 provider.
/// </summary>
/// <remarks>
/// <para>
/// Epic's token endpoint requires client credentials in an HTTP Basic authorization header
/// (base64-encoded <c>clientId:clientSecret</c>), NOT as form fields. This handler overrides
/// <see cref="ExchangeCodeAsync"/> to inject the <c>Authorization: Basic ...</c> header and
/// omit <c>client_id</c> / <c>client_secret</c> from the request body.
/// </para>
/// <para>
/// <see cref="CreateTicketAsync"/> calls the Epic userInfo endpoint and maps the stable
/// <c>account_id</c> to <see cref="ClaimTypes.NameIdentifier"/> and the <c>display_name</c>
/// to <see cref="ClaimTypes.Name"/>. The <c>account_id</c> is used as the <c>external_id</c>
/// in the GameKit <c>player_identities</c> table (T-07-05-02 mitigation — NOT email).
/// </para>
/// <para>
/// Zero new NuGet dependencies: this class derives from <see cref="OAuthHandler{T}"/> which
/// ships in the <c>Microsoft.AspNetCore.App</c> shared framework.
/// </para>
/// </remarks>
internal class EpicOAuthHandler : OAuthHandler<EpicOAuthOptions>
{
    /// <summary>Constructs the handler.</summary>
    public EpicOAuthHandler(
        IOptionsMonitor<EpicOAuthOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Exchanges the authorization code for tokens, sending client credentials in an
    /// HTTP Basic authorization header per Epic EOS token-endpoint requirements
    /// (RESEARCH §Pitfall 6 / T-07-05-01 mitigation).
    /// </summary>
    /// <remarks>
    /// The default <see cref="OAuthHandler{T}.ExchangeCodeAsync"/> sends <c>client_id</c>
    /// and <c>client_secret</c> as form fields, which Epic's token endpoint does NOT accept.
    /// This override sends them in <c>Authorization: Basic base64(clientId:clientSecret)</c>
    /// and includes only <c>grant_type</c>, <c>code</c>, and <c>redirect_uri</c> in the body.
    /// The Authorization header value is NEVER logged (T-07-05-01).
    /// </remarks>
    protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(
        OAuthCodeExchangeContext context)
    {
        // Build the HTTP Basic auth header value from clientId:clientSecret.
        // The raw credentials are base64-encoded and sent only in the header; they are
        // never included in the form body or logged. (T-07-05-01 mitigation.)
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{Options.ClientId}:{Options.ClientSecret}"));

        // Build the token-exchange form body WITHOUT client_id / client_secret fields.
        // Epic's token endpoint expects them in the Authorization header only.
        var body = new Dictionary<string, string>
        {
            ["grant_type"]   = "authorization_code",
            ["code"]         = context.Code,
            ["redirect_uri"] = context.RedirectUri,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await Backchannel.SendAsync(request, Context.RequestAborted)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(Context.RequestAborted)
                .ConfigureAwait(false);
            return OAuthTokenResponse.Failed(
                new Exception($"Epic token endpoint returned {(int)response.StatusCode}: {errorBody}"));
        }

        var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(Context.RequestAborted).ConfigureAwait(false));
        return OAuthTokenResponse.Success(payload);
    }

    /// <summary>
    /// Calls the Epic userInfo endpoint and maps the <c>account_id</c> claim to
    /// <see cref="ClaimTypes.NameIdentifier"/> (stable external_id) and
    /// <c>display_name</c> to <see cref="ClaimTypes.Name"/>.
    /// </summary>
    /// <remarks>
    /// Epic's <c>account_id</c> is the stable canonical identifier for a player account.
    /// It does NOT change when the player changes their display name or email.
    /// Using email as the external_id would break the UNIQUE(provider, external_id)
    /// contract — T-07-05-02 mitigation.
    /// </remarks>
    protected override async Task<AuthenticationTicket> CreateTicketAsync(
        ClaimsIdentity identity,
        AuthenticationProperties properties,
        OAuthTokenResponse tokens)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await Backchannel.SendAsync(request, Context.RequestAborted)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(Context.RequestAborted)
            .ConfigureAwait(false);
        using var payload = JsonDocument.Parse(body);

        // Epic's stable account identifier — NOT email (T-07-05-02 mitigation).
        var accountId = payload.RootElement.TryGetProperty("account_id", out var idProp)
            ? idProp.GetString()
            : null;

        var displayName = payload.RootElement.TryGetProperty("display_name", out var nameProp)
            ? nameProp.GetString()
            : null;

        if (!string.IsNullOrEmpty(accountId))
        {
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, accountId));
        }

        if (!string.IsNullOrEmpty(displayName))
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
        }

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, properties, Scheme.Name);
        var context = new OAuthCreatingTicketContext(
            principal, properties, Context, Scheme, Options, Backchannel, tokens, payload.RootElement);
        context.RunClaimActions();
        await Events.CreatingTicket(context).ConfigureAwait(false);
        return ticket;
    }
}
