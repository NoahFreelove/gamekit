// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Text.RegularExpressions;

namespace GameKit.Auth.Providers.Steam;

/// <summary>
/// Steam OpenID 2.0 protocol constants. Hard-coded per CONTEXT D-09 (in-house Steam impl; no
/// <c>AspNet.Security.OpenId.Steam</c> contrib dependency — the contrib package is intentionally
/// absent from <c>Directory.Packages.props</c>).
/// </summary>
public static partial class SteamConstants
{
    /// <summary>Default OP endpoint URL (used both as the login redirect target AND the <c>check_authentication</c> POST target).</summary>
    public const string DefaultOpenIdEndpoint = "https://steamcommunity.com/openid/login";

    /// <summary>Key-Value form response body line that indicates the assertion is valid (OpenID 2.0 §11.4.2.2).</summary>
    public const string IsValidTrueLine = "is_valid:true";

    /// <summary>Regex extracting the 17-digit SteamID64 from <c>openid.claimed_id</c>.</summary>
    [GeneratedRegex(@"^https?://steamcommunity\.com/openid/id/(\d{17})$", RegexOptions.CultureInvariant)]
    public static partial Regex ClaimedIdRegex();
}
