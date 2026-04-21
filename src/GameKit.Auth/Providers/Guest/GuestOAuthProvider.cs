// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;

namespace GameKit.Auth.Providers.Guest;

/// <summary>
/// Guest provider (AUTH-08). Creates a brand-new anonymous <see cref="Player"/> row with no
/// <c>PlayerIdentity</c> and no <c>PlayerCredential</c>. Because <see cref="IIsGuestResolver"/>
/// returns true for such a player, the root token issued via <see cref="IRefreshTokenService"/>
/// carries <c>is_guest=true</c> (CONTEXT D-13 computed-property claim).
/// </summary>
/// <remarks>
/// <para>
/// A "guest" in GameKit is synonymous with "a player row with zero linked identities and zero
/// credentials" — not a distinct row type. The <see cref="IOAuthProvider.CompleteLoginAsync"/>
/// contract's <c>externalId</c> is ignored here (guests have no provider-side id); every call
/// mints a fresh <see cref="Player"/>.
/// </para>
/// <para>
/// The upgrade-from-guest path lives in <c>IGuestUpgradeService</c> — plan 02-07 will wire
/// that service to <c>/auth/register</c> and <c>/auth/link/{provider}</c> endpoints.
/// </para>
/// </remarks>
internal sealed class GuestOAuthProvider : IOAuthProvider
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IRefreshTokenService _refresh;

    /// <summary>Constructs the provider.</summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">UUIDv7 id generator.</param>
    /// <param name="refresh">Refresh-token service that issues the root token + writes the audit row.</param>
    public GuestOAuthProvider(GameKitDbContext ctx, IClock clock, IIdGenerator ids, IRefreshTokenService refresh)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(refresh);
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
        _refresh = refresh;
    }

    /// <inheritdoc />
    public string Provider => "guest";

    /// <inheritdoc />
    public async Task<OAuthResult> CompleteLoginAsync(
        string externalId,
        string? displayName,
        string? avatarUrl,
        string? fingerprint,
        CancellationToken cancellationToken = default)
    {
        // externalId is ignored for guest (a guest has no provider-side id). avatarUrl is likewise
        // ignored — guests have no provider-supplied avatar. We create a new Player every call.
        _ = externalId;
        _ = avatarUrl;

        var playerId = _ids.NewId();
        var display = string.IsNullOrWhiteSpace(displayName)
            ? $"Guest-{playerId.ToString("N")[..8]}"
            : displayName!;

        _ctx.Players.Add(new Player
        {
            Id = playerId,
            DisplayName = display,
            CreatedAt = _clock.UtcNow,
        });
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // D-03 ban enforcement: a fresh guest player is never banned, but invoking the shared
        // helper keeps every provider on the same code path (and tolerates the edge case of a
        // future refactor that reuses an existing Player row across guest logins).
        var banned = await BannedCheckHelper.CheckAsync(_ctx, playerId, cancellationToken).ConfigureAwait(false);
        if (banned is not null) return banned;

        // IssueRootAsync also writes the "auth.login.success" audit row. IsGuestResolver, called
        // inside JwtIssuer, will see zero identities + zero credentials and emit is_guest=true.
        var tokens = await _refresh
            .IssueRootAsync(playerId, Provider, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        return OAuthResult.Ok(playerId, tokens);
    }
}
