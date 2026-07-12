// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Providers;

/// <summary>
/// Pluggable authentication-provider strategy (AUTH-05). Each provider knows how to turn a
/// provider-side identity (Steam ID, Discord snowflake, guest-seed, username+password) into a
/// GameKit <c>TokenPair</c>. Implementations are registered via Scrutor's assembly-scan in
/// <c>AddAuth</c> — customers can drop a custom <see cref="IOAuthProvider"/> into their own
/// assembly and GameKit will pick it up automatically.
/// </summary>
/// <remarks>
/// <b>Security contract:</b> implementers MUST perform provider-side verification (Steam's
/// OpenID 2.0 <c>check_authentication</c> roundtrip, Discord's OAuth2 token-exchange, etc.)
/// BEFORE calling <see cref="CompleteLoginAsync"/>. That method trusts the caller to have
/// proven the <c>externalId</c>'s authenticity — it performs no verification itself.
/// </remarks>
public interface IOAuthProvider
{
    /// <summary>Stable provider-name discriminator: <c>steam</c>, <c>discord</c>, <c>guest</c>, <c>password</c>.</summary>
    string Provider { get; }

    /// <summary>
    /// Completes a provider-verified login: upserts the <c>Player</c> + <c>PlayerIdentity</c>
    /// (or <c>PlayerCredential</c> for the password provider), then issues a root refresh-token
    /// family via <see cref="Services.IRefreshTokenService.IssueRootAsync"/>.
    /// </summary>
    /// <param name="externalId">Provider external id (Steam64 decimal string / Discord snowflake / guest seed GUID / username).</param>
    /// <param name="displayName">Provider-reported display name (nullable for guest).</param>
    /// <param name="avatarUrl">Provider-reported avatar URL (nullable).</param>
    /// <param name="fingerprint">Client-supplied X-GameKit-Device header (nullable).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="OAuthResult"/> carrying the issued tokens on success.</returns>
    Task<OAuthResult> CompleteLoginAsync(
        string externalId,
        string? displayName,
        string? avatarUrl,
        string? fingerprint,
        CancellationToken cancellationToken = default);
}
