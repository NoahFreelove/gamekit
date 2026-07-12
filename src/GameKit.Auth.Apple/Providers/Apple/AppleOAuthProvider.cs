// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Auth.Providers;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Apple.Providers.Apple;

/// <summary>
/// Apple Sign-In provider (AUTH-20). Consumes the Apple <c>sub</c> claim from the
/// aspnet-contrib Apple handler's <c>OnCreatingTicket</c> event; performs the Player + PlayerIdentity
/// upsert keyed by <c>(provider="apple", external_id=sub)</c> and issues the refresh-token family.
/// </summary>
/// <remarks>
/// <b>sub vs email:</b> Apple's <c>sub</c> claim is the stable opaque user identifier. The relay
/// email (<c>privaterelay.appleid.com</c>) and display name are ONLY available on the first
/// authorization and are persisted to <see cref="PlayerIdentity.Metadata"/> JSONB on that first
/// login only (T-07-04-02 mitigation). On subsequent logins Apple does NOT resend the email/name —
/// do NOT attempt to update Metadata from a null/empty relay-email field on subsequent logins.
/// </remarks>
internal sealed class AppleOAuthProvider : IOAuthProvider
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IRefreshTokenService _refresh;

    /// <summary>Constructs the provider.</summary>
    public AppleOAuthProvider(GameKitDbContext ctx, IClock clock, IIdGenerator ids, IRefreshTokenService refresh)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(refresh);
        _ctx = ctx; _clock = clock; _ids = ids; _refresh = refresh;
    }

    /// <inheritdoc />
    public string Provider => "apple";

    /// <inheritdoc />
    /// <param name="externalId">
    /// The Apple <c>sub</c> claim — a stable opaque subject identifier (NOT email).
    /// This is the canonical identity key for the <c>UNIQUE(provider, external_id)</c> constraint.
    /// </param>
    /// <param name="displayName">
    /// The player's full name from Apple's first-login user payload (may be null on subsequent logins).
    /// Stored in <c>PlayerIdentity.DisplayName</c> and in <c>Metadata</c> on first login only.
    /// </param>
    /// <param name="avatarUrl">Not used by Apple Sign-In (Apple does not return a profile photo URL). Pass <see langword="null"/>.</param>
    /// <param name="fingerprint">
    /// Optional device fingerprint from the <c>X-GameKit-Device</c> header, used for
    /// refresh-token family isolation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OAuthResult> CompleteLoginAsync(
        string externalId,
        string? displayName,
        string? avatarUrl,
        string? fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(externalId);

        var existing = await _ctx.Set<PlayerIdentity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Provider == Provider && i.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);

        Guid playerId;
        if (existing is not null)
        {
            // Subsequent login: Apple does NOT resend name or relay email after first authorization.
            // Update DisplayName only if a non-null value was passed (should be null on most logins).
            // Never overwrite Metadata — first-login-only relay-email contract (T-07-04-02 mitigation).
            playerId = existing.PlayerId;
            var tracked = await _ctx.Set<PlayerIdentity>()
                .FirstAsync(i => i.Id == existing.Id, cancellationToken).ConfigureAwait(false);
            tracked.DisplayName = displayName ?? tracked.DisplayName;
            // avatarUrl intentionally not updated — Apple does not provide profile photos.
            tracked.UpdatedAt = _clock.UtcNow;
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // First login: persist relay email + name to Metadata JSONB so the application can
            // use them without storing them in a separate column. Apple will NOT return these on
            // subsequent authorizations — this is the only opportunity to capture them.
            playerId = _ids.NewId();
            // IN-01: guard against ArgumentOutOfRangeException when externalId.Length < 6.
            // In practice, Apple sub values are 30–50 chars, but defensive for test doubles.
            var suffix = externalId.Length >= 6 ? externalId[^6..] : externalId;
            var fallbackName = displayName ?? $"AppleUser-{suffix}";

            // avatarUrl is the relay email, passed via the builder via avatarUrl parameter slot
            // to keep the IOAuthProvider signature intact (Apple has no avatar URL).
            // The builder stores the relay email in avatarUrl when calling CompleteLoginAsync.
            var relayEmail = avatarUrl; // relay email passed through the avatarUrl slot

            JsonDocument? metadata = null;
            if (!string.IsNullOrEmpty(relayEmail) || !string.IsNullOrEmpty(displayName))
            {
                // Serialize the first-login Apple payload to Metadata JSONB.
                // Only relay_email and name are stored; sub is already in ExternalId.
                metadata = JsonSerializer.SerializeToDocument(new
                {
                    relay_email = relayEmail,
                    name = displayName,
                });
            }

            _ctx.Players.Add(new Player
            {
                Id = playerId,
                DisplayName = fallbackName,
                CreatedAt = _clock.UtcNow,
            });
            _ctx.Set<PlayerIdentity>().Add(new PlayerIdentity
            {
                Id = _ids.NewId(),
                PlayerId = playerId,
                Provider = Provider,
                ExternalId = externalId,
                DisplayName = displayName,
                AvatarUrl = null,           // Apple does not provide an avatar URL
                Metadata = metadata,        // relay email + name; first-login only
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow,
            });
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var banned = await BannedCheckHelper.CheckAsync(_ctx, playerId, cancellationToken).ConfigureAwait(false);
        if (banned is not null) return banned;
        var tokens = await _refresh
            .IssueRootAsync(playerId, Provider, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        return OAuthResult.Ok(playerId, tokens);
    }
}
