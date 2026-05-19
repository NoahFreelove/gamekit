// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using GameKit.Rankings.Http.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GameKit.Rankings.Services;

/// <summary>
/// Default implementation of <see cref="IGdprExportService"/> (RANK-13 / D-15 / D-16 / D-17).
/// Runs a <c>REPEATABLE READ READ ONLY</c> Postgres transaction to produce a consistent
/// point-in-time snapshot across seven table reads without blocking writers.
/// </summary>
/// <remarks>
/// <para>
/// <b>REPEATABLE READ + READ ONLY</b>: the service opens the transaction at
/// <see cref="IsolationLevel.RepeatableRead"/>, then immediately executes
/// <c>SET TRANSACTION READ ONLY</c> (Pitfall 5). Postgres treats this as a serializable-quality
/// snapshot for reads, eliminating predicate-lock overhead while preventing accidental writes.
/// </para>
/// <para>
/// <b>PlayerId IS NULL filter (Pitfall 7)</b>: every read is filtered by
/// <c>WHERE PlayerId == playerId</c>. In SQL, <c>NULL != id</c> evaluates to UNKNOWN and is
/// excluded by Postgres, so tombstoned GDPR-cascade rows (where PlayerId was set to NULL)
/// never leak into the export.
/// </para>
/// <para>
/// <b>PII discipline</b>: <c>PlayerCredential.PasswordHash</c> is never materialized.
/// <c>PlayerIdentity.ExternalId</c> is hashed to <c>external_id_hash</c> using
/// SHA-256(<c>provider:externalId</c>) — the same algorithm as <c>ExternalIdHasher</c> in
/// <c>GameKit.Auth</c> — before inclusion in the response. Raw external IDs are never returned.
/// </para>
/// </remarks>
public sealed class GdprExportService : IGdprExportService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IOptions<GameKitRankingsOptions> _opts;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">UTC clock abstraction.</param>
    /// <param name="opts">Rankings options (contains GdprExport.MaxBytes cap).</param>
    public GdprExportService(
        GameKitDbContext ctx,
        IClock clock,
        IOptions<GameKitRankingsOptions> opts)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(opts);
        _ctx = ctx;
        _clock = clock;
        _opts = opts;
    }

    /// <inheritdoc />
    public async Task<GdprExportResponse?> ExportAsync(Guid playerId, CancellationToken ct)
    {
        // Open REPEATABLE READ transaction. All seven reads share a single Postgres snapshot.
        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            .ConfigureAwait(false);

        // SET TRANSACTION READ ONLY: tells Postgres this is guaranteed read-only,
        // eliminating predicate-locking overhead (Pitfall 5 / Pattern 3).
        await _ctx.Database
            .ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct)
            .ConfigureAwait(false);

        // 1. Resolve the player. If not found, commit + return null (caller maps to 404).
        var player = await _ctx.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId, ct)
            .ConfigureAwait(false);

        if (player is null)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return null;
        }

        // 2. External identities — use raw SQL to avoid a project-reference on GameKit.Auth (D-22 invariant).
        //    Pitfall 7: WHERE player_id = @playerId; ExternalId hashed before inclusion.
        var identities = new List<IdentityRaw>();
        var credentialUpdatedAts = new List<DateTimeOffset>();

        // Use the underlying Npgsql connection that EF already opened for this transaction.
        // Since BeginTransactionAsync opened the connection, we do NOT re-open it here.
        var txConn = (NpgsqlConnection)_ctx.Database.GetDbConnection();

        await using (var idCmd = txConn.CreateCommand())
        {
            // Use same transaction so reads participate in the REPEATABLE READ snapshot.
            idCmd.Transaction = (NpgsqlTransaction)tx.GetDbTransaction();
            // EF Core uses PascalCase column names (no snake_case mapping in this project).
            idCmd.CommandText =
                @"SELECT ""Provider"", ""ExternalId"", ""CreatedAt"" FROM gamekit.player_identities WHERE ""PlayerId"" = @pid";
            idCmd.Parameters.AddWithValue("pid", playerId);
            await using var reader = await idCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                identities.Add(new IdentityRaw(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetFieldValue<DateTimeOffset>(2)));
            }
        }

        // 3. Credential metadata — SELECT only UpdatedAt. PasswordHash is NEVER queried.
        await using (var credCmd = txConn.CreateCommand())
        {
            credCmd.Transaction = (NpgsqlTransaction)tx.GetDbTransaction();
            // EF Core uses PascalCase column names (no snake_case mapping in this project).
            credCmd.CommandText =
                @"SELECT ""UpdatedAt"" FROM gamekit.player_credentials WHERE ""PlayerId"" = @pid";
            credCmd.Parameters.AddWithValue("pid", playerId);
            await using var reader = await credCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                credentialUpdatedAts.Add(reader.GetFieldValue<DateTimeOffset>(0));
            }
        }

        // 4. Session participation history — Pitfall 7: WHERE sp.PlayerId == playerId.
        var sessions = await _ctx.SessionParticipants
            .AsNoTracking()
            .Where(sp => sp.PlayerId == playerId)
            .Join(_ctx.GameSessions, sp => sp.SessionId, gs => gs.Id, (sp, gs) => new
            {
                gs.Id,
                gs.LadderId,
                sp.Team,
                sp.Result,
                sp.RatingBefore,
                sp.RatingAfter,
                gs.CompletedAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 5. Live player_ranks (current ratings) — Pitfall 7.
        var ranks = await _ctx.Set<PlayerRank>()
            .AsNoTracking()
            .Where(r => r.PlayerId == playerId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 6. Season archive (historical rating snapshots) — Pitfall 7.
        var archive = await _ctx.Set<SeasonRankArchive>()
            .AsNoTracking()
            .Where(a => a.PlayerId == playerId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);

        // --- Build response ---
        var now = _clock.UtcNow;

        var playerSection = new PlayerSection
        {
            Id = player.Id,
            DisplayName = player.DisplayName,
            CreatedAt = player.CreatedAt,
            LastSeenAt = player.LastSeenAt,
            IsBanned = player.IsBanned,
            BannedAt = player.BannedAt,
            BanReason = player.BanReason,
        };

        var identitySections = identities
            .Select(i => new IdentitySection
            {
                Provider = i.Provider,
                // Hash the external id: SHA256(provider:externalId) — mirrors ExternalIdHasher.Hash.
                ExternalIdHash = ComputeExternalIdHash(i.Provider, i.ExternalId),
                CreatedAt = i.CreatedAt,
            })
            .ToList();

        var credentialSections = credentialUpdatedAts
            .Select(updatedAt => new CredentialMetadataSection
            {
                // PlayerCredential only stores UpdatedAt (when hash was last changed).
                // Use it as both CreatedAt proxy and LastUsedAt (no separate CreatedAt column).
                CreatedAt = updatedAt,
                LastUsedAt = updatedAt,
            })
            .ToList();

        var sessionSections = sessions
            .Select(s => new SessionSection
            {
                SessionId = s.Id,
                LadderId = s.LadderId,
                Team = s.Team,
                Result = s.Result?.ToString(),
                RatingBefore = s.RatingBefore,
                RatingAfter = s.RatingAfter,
                CompletedAt = s.CompletedAt,
            })
            .ToList();

        // Build rating history: archived season snapshots first, then live ranks (no SeasonId).
        var ratingHistory = new List<RatingHistorySection>();
        foreach (var a in archive)
        {
            ratingHistory.Add(new RatingHistorySection
            {
                LadderId = a.LadderId,
                SeasonId = a.SeasonId,
                Rating = a.Rating,
                Rd = a.RatingDeviation,
                Volatility = a.Volatility,
                SnapshotAt = a.ArchivedAt,
            });
        }
        foreach (var r in ranks)
        {
            ratingHistory.Add(new RatingHistorySection
            {
                LadderId = r.LadderId,
                SeasonId = null, // live row — no season id
                Rating = r.Rating,
                Rd = r.RatingDeviation,
                Volatility = r.Volatility,
                SnapshotAt = r.LastMatchAt ?? now,
            });
        }

        var response = new GdprExportResponse
        {
            Player = playerSection,
            Identities = identitySections,
            CredentialsMetadata = credentialSections,
            Sessions = sessionSections,
            RatingHistory = ratingHistory,
            ExportedAt = now,
        };

        // --- 25 MB cap enforcement (D-18) ---
        // Serialize to bytes; compare length against the configured cap.
        // No custom JsonSerializerOptions needed — explicit [JsonPropertyName] attributes handle
        // snake_case serialization deterministically regardless of ambient options (SC#5 pin).
        var json = JsonSerializer.SerializeToUtf8Bytes(response);
        if (json.Length > _opts.Value.GdprExport.MaxBytes)
            throw new GdprExportPayloadTooLargeException(json.Length, _opts.Value.GdprExport.MaxBytes);

        return response;
    }

    /// <summary>
    /// Computes the external-id hash using the same algorithm as
    /// <c>GameKit.Auth.Services.ExternalIdHasher.Hash</c>: SHA-256(UTF-8 bytes of
    /// <c>"{provider}:{externalId}"</c>), hex-encoded lowercase.
    /// Mirrored inline here to avoid a package dependency on GameKit.Auth (D-22 invariant).
    /// </summary>
    private static string ComputeExternalIdHash(string provider, string externalId)
    {
        var input = Encoding.UTF8.GetBytes($"{provider}:{externalId}");
        var digest = SHA256.HashData(input);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Internal projection from the raw identity query (avoids Auth package reference).</summary>
    private sealed record IdentityRaw(string Provider, string ExternalId, DateTimeOffset CreatedAt);
}
