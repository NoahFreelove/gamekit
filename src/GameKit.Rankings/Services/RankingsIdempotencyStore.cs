// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Rankings.Services;

/// <summary>
/// Implementation of <see cref="IIdempotencyStore"/> that persists idempotency records to the
/// <c>session_complete_idempotency</c> table (D-08 / T-04-05-RP).
/// </summary>
/// <remarks>
/// <para>
/// IMPORTANT: This store runs inside the caller's ambient transaction (started by
/// <see cref="ISessionCompleteService.CompleteAsync"/>). It MUST NOT open its own transaction.
/// The dedup INSERT must commit atomically with the state-conditional UPDATE on
/// <c>game_sessions</c> — if the transaction is rolled back, the idempotency row is also
/// rolled back, ensuring the retry re-runs the full completion flow (T-04-05-RP mitigation).
/// </para>
/// <para>
/// The composite primary key <c>(SessionId, IdempotencyKey)</c> on the underlying table enforces
/// uniqueness at the database level (concurrent retries with the same key are prevented by the
/// unique index even under concurrent requests — Pitfall 8).
/// </para>
/// </remarks>
public sealed class RankingsIdempotencyStore : IIdempotencyStore
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;

    /// <summary>
    /// Constructs the store.
    /// </summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/> shared with the caller's transaction.</param>
    /// <param name="clock">Authoritative UTC clock for <c>CreatedAt</c> timestamps.</param>
    public RankingsIdempotencyStore(GameKitDbContext ctx, IClock clock)
    {
        _ctx = ctx;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<IdempotencyLookup> TryGetAsync(
        Guid sessionId,
        string idempotencyKey,
        CancellationToken ct)
    {
        var row = await _ctx.Set<SessionCompleteIdempotency>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.SessionId == sessionId && r.IdempotencyKey == idempotencyKey,
                ct);

        if (row is null)
            return new IdempotencyLookup(Found: false, ExistingRequestHash: null, CachedResponseBody: null);

        return new IdempotencyLookup(
            Found: true,
            ExistingRequestHash: row.RequestBodyHash,
            CachedResponseBody: row.CachedResponse);
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        Guid sessionId,
        string idempotencyKey,
        string requestBodyHash,
        byte[] responseBody,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requestBodyHash);
        ArgumentNullException.ThrowIfNull(responseBody);

        var row = new SessionCompleteIdempotency
        {
            SessionId = sessionId,
            IdempotencyKey = idempotencyKey,
            RequestBodyHash = requestBodyHash,
            CachedResponse = responseBody,
            CreatedAt = _clock.UtcNow,
        };

        _ctx.Set<SessionCompleteIdempotency>().Add(row);

        // SaveChanges within the caller's ambient transaction — no explicit Commit here.
        // The caller (SessionCompleteService) will call SaveChanges + Commit after this returns.
        // We call SaveChanges here to flush the INSERT before the caller does its own final save.
        await _ctx.SaveChangesAsync(ct);
    }
}
