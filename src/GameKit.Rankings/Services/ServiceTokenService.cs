// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Rankings.Services;

/// <summary>
/// Concrete implementation of <see cref="IServiceTokenService"/>. Manages the service-account
/// bearer tokens stored in <c>service_tokens</c>. Raw tokens are generated with 32 bytes of
/// CSRNG entropy (256-bit) and are never stored — only the SHA-256 hex digest is persisted.
/// Mirrors the Phase-2 refresh-token storage discipline (D-06).
/// </summary>
internal sealed class ServiceTokenService : IServiceTokenService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    /// <summary>Constructs the service.</summary>
    public ServiceTokenService(GameKitDbContext ctx, IClock clock, IIdGenerator ids)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
    }

    /// <inheritdoc />
    public async Task<(string Raw, ServiceToken Row)> IssueAsync(
        string name,
        DateTimeOffset? expiresAt,
        CancellationToken ct)
    {
        var raw = GenerateRaw();
        var hash = Sha256Hex(raw);

        var token = new ServiceToken
        {
            Id = _ids.NewId(),
            Name = name,
            TokenHash = hash,
            CreatedAt = _clock.UtcNow,
            ExpiresAt = expiresAt,
        };

        _ctx.Set<ServiceToken>().Add(token);

        try
        {
            await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ServiceTokenNameAlreadyExistsException(name);
        }

        return (raw, token);
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(string name, CancellationToken ct)
    {
        var token = await _ctx.Set<ServiceToken>()
            .FirstOrDefaultAsync(t => t.Name == name, ct)
            .ConfigureAwait(false);

        if (token is null)
            return false;

        // Idempotent — already revoked tokens are considered successfully revoked.
        if (token.RevokedAt is null)
        {
            await _ctx.Set<ServiceToken>()
                .Where(t => t.Id == token.Id)
                .ExecuteUpdateAsync(
                    u => u.SetProperty(t => t.RevokedAt, _clock.UtcNow),
                    ct)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceTokenSummaryDto>> ListAsync(CancellationToken ct)
    {
        var rows = await _ctx.Set<ServiceToken>()
            .AsNoTracking()
            .OrderBy(t => t.CreatedAt)
            .Select(t => new ServiceTokenSummaryDto(
                t.Id,
                t.Name,
                t.CreatedAt,
                t.ExpiresAt,
                t.RevokedAt,
                t.LastUsedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public async Task<ServiceToken?> FindByRawAsync(string raw, CancellationToken ct)
    {
        var hash = Sha256Hex(raw);

        return await _ctx.Set<ServiceToken>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            .ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Helpers — mirrored verbatim from RefreshTokenService (Phase 2, lines 280-292)
    // -------------------------------------------------------------------------

    /// <summary>SHA-256 hex (64 chars, lower-case) of a UTF-8-encoded string.</summary>
    private static string Sha256Hex(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>32-byte CSRNG token encoded as URL-safe base64 (no padding).</summary>
    private static string GenerateRaw()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Walks the exception chain looking for a Postgres <c>23505</c> unique-violation.</summary>
    private static bool IsUniqueViolation(Exception? ex)
    {
        for (var i = 0; i < 8 && ex is not null; i++)
        {
            if (ex is Npgsql.PostgresException { SqlState: "23505" }) return true;
            ex = ex.InnerException;
        }
        return false;
    }
}
