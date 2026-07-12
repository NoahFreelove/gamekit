// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Egress;
using Microsoft.AspNetCore.Http;

namespace GameKit.Auth.Providers.Steam;

/// <summary>
/// In-house Steam OpenID 2.0 verifier (CONTEXT D-09). POSTs <c>check_authentication</c> to the
/// OP endpoint using the named HttpClient <c>gamekit.auth.provider.steam</c> (which has the
/// <see cref="EgressAllowListHandler"/> + resilience pipeline attached). The server-side
/// roundtrip is what defeats forged-signature callbacks — an attacker cannot control Steam's
/// own response body, so a forged <c>openid.sig</c> produces <c>is_valid:false</c>.
/// See <see href="https://openid.net/specs/openid-authentication-2_0.html#verifying_signatures"/>.
/// </summary>
public sealed class SteamOpenIdVerifier
{
    private readonly HttpClient _httpClient;
    private readonly GameKitAuthOptions _opts;

    /// <summary>Constructs the verifier; pulls the pre-configured named HttpClient from the factory.</summary>
    /// <param name="factory">HTTP client factory used to resolve the <c>gamekit.auth.provider.steam</c> client.</param>
    /// <param name="opts">Auth options carrying <see cref="SteamOptions.OpenIdEndpoint"/>.</param>
    public SteamOpenIdVerifier(IHttpClientFactory factory, GameKitAuthOptions opts)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(opts);
        _httpClient = factory.CreateClient("gamekit.auth.provider.steam");
        _opts = opts;
    }

    /// <summary>
    /// Verifies an OpenID 2.0 positive assertion by roundtripping <c>check_authentication</c>.
    /// Returns a SteamID64 on success; an error code on failure. Never throws on malformed input.
    /// </summary>
    /// <param name="query">The incoming query collection from Steam's browser redirect back to <c>/auth/callback/steam</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SteamVerificationResult> VerifyAsync(IQueryCollection query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // 1) Extract claimed_id and its 17-digit SteamID.
        var claimedId = query["openid.claimed_id"].ToString();
        if (string.IsNullOrEmpty(claimedId))
            return SteamVerificationResult.Invalid("claimed_id_missing");

        var match = SteamConstants.ClaimedIdRegex().Match(claimedId);
        if (!match.Success)
            return SteamVerificationResult.Invalid("claimed_id_malformed");

        // 2) Build check_authentication payload — echo EVERY openid.* param, force mode.
        var form = new List<KeyValuePair<string, string>>();
        foreach (var kv in query)
        {
            if (kv.Key.StartsWith("openid.", StringComparison.Ordinal))
                form.Add(new KeyValuePair<string, string>(kv.Key, kv.Value.ToString()));
        }
        // Replace openid.mode with check_authentication (spec §11.4.2.1).
        form.RemoveAll(kv => kv.Key == "openid.mode");
        form.Add(new KeyValuePair<string, string>("openid.mode", "check_authentication"));

        using var req = new HttpRequestMessage(HttpMethod.Post, _opts.Steam.OpenIdEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        HttpResponseMessage resp;
        try
        {
            resp = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return SteamVerificationResult.Invalid("check_authentication_http_error");
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                return SteamVerificationResult.Invalid("check_authentication_http_error");

            // 3) Parse Key-Value form.
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var isValid = IsValidTrueInBody(body);

            return isValid
                ? SteamVerificationResult.Ok(match.Groups[1].Value)
                : SteamVerificationResult.Invalid("is_valid_false");
        }
    }

    private static bool IsValidTrueInBody(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            if (line.TrimEnd('\r') == SteamConstants.IsValidTrueLine)
                return true;
        }
        return false;
    }
}
