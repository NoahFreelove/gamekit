// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Providers.Steam;

/// <summary>
/// Steam OAuth provider (AUTH-06). Upserts the <see cref="Player"/> + <see cref="PlayerIdentity"/>
/// pair for the verified Steam ID (verification happens in the endpoint via
/// <see cref="SteamOpenIdVerifier"/>) and issues a root refresh-token family.
/// </summary>
/// <remarks>
/// This type does NOT trust the caller's <c>externalId</c> as a claim of authenticity — it assumes
/// the caller has already run <c>check_authentication</c>. The <c>/auth/callback/steam</c>
/// endpoint (plan 02-07) is the single call site that performs the verification before invoking
/// this provider.
/// </remarks>
internal sealed class SteamOAuthProvider : IOAuthProvider
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IRefreshTokenService _refresh;

    /// <summary>Constructs the provider.</summary>
    public SteamOAuthProvider(GameKitDbContext ctx, IClock clock, IIdGenerator ids, IRefreshTokenService refresh)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(refresh);
        _ctx = ctx; _clock = clock; _ids = ids; _refresh = refresh;
    }

    /// <inheritdoc />
    public string Provider => "steam";

    /// <inheritdoc />
    public async Task<OAuthResult> CompleteLoginAsync(
        string externalId,
        string? displayName,
        string? avatarUrl,
        string? fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(externalId);

        // Upsert: look up PlayerIdentity by (steam, externalId). If found, reuse its PlayerId; else create both.
        var existing = await _ctx.Set<PlayerIdentity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Provider == Provider && i.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);

        Guid playerId;
        if (existing is not null)
        {
            playerId = existing.PlayerId;
            // Refresh display name / avatar on subsequent login.
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
            var fallbackName = displayName ?? $"SteamUser-{externalId[^8..]}";
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
