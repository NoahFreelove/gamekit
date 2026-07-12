// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Auth.Providers;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Google.Providers.Google;

/// <summary>
/// Google OAuth2 provider (AUTH-19). Consumes the Google <c>sub</c> claim from the
/// ASP.NET Core Google handler's <c>OnCreatingTicket</c> event; performs the Player + PlayerIdentity
/// upsert keyed by <c>(provider="google", external_id=sub)</c> and issues the refresh-token family.
/// The <c>sub</c> claim (Google's stable subject identifier) is used as the external ID — NOT email,
/// which can change and is not unique across Google accounts (T-07-03-01 mitigation).
/// </summary>
internal sealed class GoogleOAuthProvider : IOAuthProvider
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IRefreshTokenService _refresh;

    /// <summary>Constructs the provider.</summary>
    public GoogleOAuthProvider(GameKitDbContext ctx, IClock clock, IIdGenerator ids, IRefreshTokenService refresh)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(refresh);
        _ctx = ctx; _clock = clock; _ids = ids; _refresh = refresh;
    }

    /// <inheritdoc />
    public string Provider => "google";

    /// <inheritdoc />
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
            playerId = existing.PlayerId;
            var tracked = await _ctx.Set<PlayerIdentity>()
                .FirstAsync(i => i.Id == existing.Id, cancellationToken).ConfigureAwait(false);
            tracked.DisplayName = displayName ?? tracked.DisplayName;
            tracked.AvatarUrl = avatarUrl ?? tracked.AvatarUrl;
            tracked.UpdatedAt = _clock.UtcNow;
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            playerId = _ids.NewId();
            // IN-01: guard against ArgumentOutOfRangeException when externalId.Length < 6.
            // In practice, Google sub values are ~21 digits, but defensive for test doubles.
            var suffix = externalId.Length >= 6 ? externalId[^6..] : externalId;
            var fallbackName = displayName ?? $"GoogleUser-{suffix}";
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
                AvatarUrl = avatarUrl,
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
