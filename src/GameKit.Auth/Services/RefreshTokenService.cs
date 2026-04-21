// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Services;

/// <summary>
/// Default <see cref="IRefreshTokenService"/>. Implements RESEARCH §6.4 Pattern 3 rotation with
/// a reuse-interval grace window + fingerprint gate, writing audit rows through
/// <see cref="IAuthAuditWriter"/> on every mutation.
/// </summary>
internal sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IJwtIssuer _jwtIssuer;
    private readonly IAuthAuditWriter _audit;
    private readonly GameKitAuthOptions _opts;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">UUIDv7 id generator.</param>
    /// <param name="jwtIssuer">Issuer that produces the access-token half of the returned <see cref="TokenPair"/>.</param>
    /// <param name="audit">Audit writer for every mutation.</param>
    /// <param name="opts">Root auth options (JWT section supplies <see cref="JwtOptions.RefreshReuseInterval"/> and <see cref="JwtOptions.RefreshTokenLifetime"/>).</param>
    public RefreshTokenService(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator ids,
        IJwtIssuer jwtIssuer,
        IAuthAuditWriter audit,
        GameKitAuthOptions opts)
    {
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
        _jwtIssuer = jwtIssuer;
        _audit = audit;
        _opts = opts;
    }

    /// <inheritdoc />
    public async Task<TokenPair> IssueRootAsync(Guid playerId, string provider, string? fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);

        var now = _clock.UtcNow;
        var familyId = _ids.NewId();
        var raw = GenerateRaw();
        var row = new RefreshToken
        {
            Id = _ids.NewId(),
            PlayerId = playerId,
            FamilyId = familyId,
            TokenHash = Sha256Hex(raw),
            DeviceFingerprint = fingerprint,
            Provider = provider,
            IssuedAt = now,
            ExpiresAt = now.Add(_opts.Jwt.RefreshTokenLifetime),
        };
        _ctx.Set<RefreshToken>().Add(row);
        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var access = await _jwtIssuer
            .IssueAsync(playerId, familyId, provider, cancellationToken)
            .ConfigureAwait(false);

        await _audit.WriteAsync(
            action: "auth.login.success",
            targetType: "player",
            targetId: playerId,
            actorId: playerId,
            after: new { provider, family_id = familyId },
            reason: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new TokenPair(access, raw);
    }

    /// <inheritdoc />
    public async Task<TokenPair> RotateAsync(string rawRefreshToken, string? fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawRefreshToken);

        var hash = Sha256Hex(rawRefreshToken);
        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);

        var current = await _ctx.Set<RefreshToken>()
            .FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
            throw new UnauthorizedException("unknown_refresh");

        var now = _clock.UtcNow;

        // ---- Already-rotated row: check grace window ----
        if (current.RevokedAt is not null)
        {
            var withinGrace = current.UsedAt is not null
                && (now - current.UsedAt.Value) <= _opts.Jwt.RefreshReuseInterval;

            var fingerprintMatches = current.DeviceFingerprint is not null
                && fingerprint is not null
                && string.Equals(current.DeviceFingerprint, fingerprint, StringComparison.Ordinal);

            if (withinGrace && fingerprintMatches && current.ReplacedByTokenHash is not null)
            {
                // Idempotent replay: return same already-issued child's access token, no new raw.
                var child = await _ctx.Set<RefreshToken>()
                    .FirstAsync(r => r.TokenHash == current.ReplacedByTokenHash, cancellationToken)
                    .ConfigureAwait(false);
                var accessReplay = await _jwtIssuer
                    .IssueAsync(child.PlayerId, child.FamilyId, child.Provider, cancellationToken)
                    .ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new TokenPair(accessReplay, /* raw */ null);
            }

            // Reuse outside grace OR fingerprint mismatch → family revoke.
            var revokeReason = withinGrace
                ? "refresh_fingerprint_mismatch"
                : "refresh_reuse_outside_grace";
            await RevokeFamilyInScope(current.FamilyId, revokeReason, current.PlayerId, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedException("refresh_revoked");
        }

        // ---- Live row: check expiry first ----
        if (current.ExpiresAt < now)
        {
            await RevokeFamilyInScope(current.FamilyId, "refresh_expired", current.PlayerId, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedException("refresh_expired");
        }

        // Fingerprint-match check on a LIVE token: a non-match here is a reuse signal even on first use.
        if (current.DeviceFingerprint is not null
            && fingerprint is not null
            && !string.Equals(current.DeviceFingerprint, fingerprint, StringComparison.Ordinal))
        {
            await RevokeFamilyInScope(current.FamilyId, "refresh_fingerprint_mismatch", current.PlayerId, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedException("refresh_revoked");
        }

        // Ban check (D-03): refuse to rotate for banned players; revoke the family so subsequent attempts
        // also fail. Uses the existing RevokeFamilyInScope helper with its exact parameter order
        // (familyId, reason, playerId, ct). Transaction `tx` and variable `current` are in scope here.
        var bannedPlayer = await _ctx.Set<GameKit.Core.Entities.Player>()
            .AsNoTracking()
            .FirstAsync(p => p.Id == current.PlayerId, cancellationToken)
            .ConfigureAwait(false);
        if (bannedPlayer.IsBanned)
        {
            await RevokeFamilyInScope(current.FamilyId, "player_banned", current.PlayerId, cancellationToken)
                .ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedException("player_banned");
        }

        // Happy path: rotate.
        var rawChild = GenerateRaw();
        var childHash = Sha256Hex(rawChild);
        var childRow = new RefreshToken
        {
            Id = _ids.NewId(),
            PlayerId = current.PlayerId,
            FamilyId = current.FamilyId,
            TokenHash = childHash,
            DeviceFingerprint = fingerprint ?? current.DeviceFingerprint,
            Provider = current.Provider,
            IssuedAt = now,
            ExpiresAt = now.Add(_opts.Jwt.RefreshTokenLifetime),
        };
        _ctx.Set<RefreshToken>().Add(childRow);

        current.UsedAt = now;
        current.RevokedAt = now;
        current.ReplacedByTokenHash = childHash;

        await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            action: "auth.refresh.rotated",
            targetType: "refresh_token",
            targetId: current.Id,
            actorId: current.PlayerId,
            after: new { child_token_id = childRow.Id, family_id = current.FamilyId, issued_at = now },
            reason: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        var accessJwt = await _jwtIssuer
            .IssueAsync(current.PlayerId, current.FamilyId, current.Provider, cancellationToken)
            .ConfigureAwait(false);
        return new TokenPair(accessJwt, rawChild);
    }

    /// <inheritdoc />
    public async Task RevokeFamilyAsync(string rawRefreshToken, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawRefreshToken);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        var hash = Sha256Hex(rawRefreshToken);
        var current = await _ctx.Set<RefreshToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
            return;   // logout of unknown token is idempotent no-op

        await RevokeFamilyInScope(current.FamilyId, reason, current.PlayerId, cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            action: "auth.logout",
            targetType: "refresh_token",
            targetId: current.FamilyId,
            actorId: current.PlayerId,
            after: null,
            reason: reason,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RevokeAllForPlayerAsync(Guid playerId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);

        var now = _clock.UtcNow;
        var affected = await _ctx.Set<RefreshToken>()
            .Where(r => r.PlayerId == playerId && r.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.RevokedAt, now), cancellationToken)
            .ConfigureAwait(false);

        await _audit.WriteAsync(
            action: "auth.logout.all",
            targetType: "player",
            targetId: playerId,
            actorId: playerId,
            after: new { families_revoked = affected },
            reason: reason,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task RevokeFamilyInScope(Guid familyId, string reason, Guid playerId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        await _ctx.Set<RefreshToken>()
            .Where(r => r.FamilyId == familyId && r.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.RevokedAt, now), ct)
            .ConfigureAwait(false);

        await _audit.WriteAsync(
            action: "auth.refresh.family_revoked",
            targetType: "refresh_token",
            targetId: familyId,
            actorId: null,   // server-initiated
            after: new { family_id = familyId, player_id = playerId },
            reason: reason,
            cancellationToken: ct).ConfigureAwait(false);
    }

    private static string Sha256Hex(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateRaw()
    {
        // 256-bit CSRNG; URL-safe base64.
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
