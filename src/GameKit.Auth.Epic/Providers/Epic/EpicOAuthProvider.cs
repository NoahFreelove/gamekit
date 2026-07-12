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

namespace GameKit.Auth.Epic.Providers.Epic;

/// <summary>
/// Epic Games OAuth2 provider (AUTH-21). Consumes the Epic <c>account_id</c> from the
/// <see cref="EpicOAuthHandler"/>'s <c>OnCreatingTicket</c> event; performs the
/// Player + PlayerIdentity upsert keyed by <c>(provider="epic", external_id=account_id)</c>
/// and issues the refresh-token family.
/// </summary>
/// <remarks>
/// The Epic <c>account_id</c> is the stable canonical identifier for a player account — it
/// does NOT change when the player updates their display name or email. Using email as
/// <c>external_id</c> would break the <c>UNIQUE(provider, external_id)</c> contract since
/// Epic does not expose email in the <c>basic_profile</c> scope and does not guarantee
/// email uniqueness across EOS products (T-07-05-02 mitigation).
/// </remarks>
internal sealed class EpicOAuthProvider : IOAuthProvider
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IRefreshTokenService _refresh;

    /// <summary>Constructs the provider.</summary>
    public EpicOAuthProvider(GameKitDbContext ctx, IClock clock, IIdGenerator ids, IRefreshTokenService refresh)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(refresh);
        _ctx = ctx; _clock = clock; _ids = ids; _refresh = refresh;
    }

    /// <inheritdoc />
    public string Provider => "epic";

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
            // Fallback display name when Epic does not supply one (e.g. guest/anonymous account).
            // IN-01: guard against ArgumentOutOfRangeException when externalId.Length < 6.
            // In practice, Epic account_id values are 32 hex chars, but defensive for test doubles.
            var suffix = externalId.Length >= 6 ? externalId[^6..] : externalId;
            var fallbackName = displayName ?? $"EpicUser-{suffix}";
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
