// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Providers.Steam;

/// <summary>
/// Outcome of a Steam OpenID 2.0 <c>check_authentication</c> roundtrip. Use <see cref="Ok"/>
/// / <see cref="Invalid"/> to construct instances.
/// </summary>
/// <param name="IsValid">True iff the OP confirmed the assertion with <c>is_valid:true</c>.</param>
/// <param name="SteamId64">The 17-digit SteamID64 extracted from <c>openid.claimed_id</c> on success.</param>
/// <param name="ErrorCode">Stable error discriminator on failure; null on success.</param>
public sealed record SteamVerificationResult(bool IsValid, string? SteamId64, string? ErrorCode)
{
    /// <summary>Valid assertion carrying the SteamID64.</summary>
    public static SteamVerificationResult Ok(string steamId64) => new(true, steamId64, null);

    /// <summary>Invalid assertion; carries an error code.</summary>
    public static SteamVerificationResult Invalid(string errorCode) => new(false, null, errorCode);
}
