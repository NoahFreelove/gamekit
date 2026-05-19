// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Rankings.Services;

/// <summary>
/// Implementation of <see cref="IPostSessionCompleteHandler"/> that enqueues one
/// <see cref="PendingRatingUpdate"/> row per participant per ranked ladder (D-22).
/// Also snapshots <c>session_participants.RatingBefore</c> from the current
/// <see cref="PlayerRank"/> when available, so the response can reflect the pre-completion
/// rating (Core cannot do this directly because it has zero compile-time reference to
/// <c>PlayerRank</c> per the D-22 invariant).
/// </summary>
/// <remarks>
/// <para>
/// This adapter runs inside the caller's ambient transaction (started by
/// <see cref="ISessionCompleteService.CompleteAsync"/>). It MUST NOT open its own transaction.
/// The single <see cref="GameKitDbContext"/> instance is shared with the caller.
/// </para>
/// <para>
/// Idempotency: duplicate invocations are prevented by the idempotency dedup layer in
/// <see cref="ISessionCompleteService"/>. If the same call somehow reaches this adapter
/// twice, the second INSERT on <c>pending_rating_updates</c> will produce an extra row —
/// this is acceptable because the ticker's drain logic is idempotent and the row will
/// be picked up on the next tick with no effect on player ratings (the session is already
/// applied after the first row is drained).
/// </para>
/// <para>
/// GDPR safety (Pitfall §12): participants with a null <see cref="SessionParticipantSnapshot.PlayerId"/>
/// are skipped. In practice, player ids in the snapshot are always non-null at completion time
/// (GDPR cascade happens post-enqueue), but the skip guard is retained as a defence-in-depth
/// measure consistent with the ticker's own skip logic.
/// </para>
/// </remarks>
public sealed class PendingRatingUpdatesAdapter : IPostSessionCompleteHandler
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;

    /// <summary>
    /// Constructs the adapter.
    /// </summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/> shared with the caller's transaction.</param>
    /// <param name="clock">Authoritative UTC clock.</param>
    /// <param name="ids">UUIDv7 id generator.</param>
    public PendingRatingUpdatesAdapter(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator ids)
    {
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
    }

    /// <inheritdoc />
    public async Task OnCompletedAsync(
        Guid sessionId,
        IReadOnlyList<SessionParticipantSnapshot> participants,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(participants);

        var now = _clock.UtcNow;

        foreach (var participant in participants)
        {
            // GDPR skip: PlayerId should never be null at enqueue time, but guard defensively
            // (Pitfall §12 — GDPR cascade post-enqueue).
            if (participant.PlayerId == Guid.Empty)
                continue;

            // Snapshot RatingBefore onto session_participants from the player's current rank
            // for this ladder (Core cannot do this — D-22 invariant).
            if (participant.LadderId.HasValue)
            {
                var playerRank = await _ctx.Set<PlayerRank>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        r => r.PlayerId == participant.PlayerId && r.LadderId == participant.LadderId.Value,
                        ct);

                if (playerRank is not null)
                {
                    await _ctx.SessionParticipants
                        .Where(sp => sp.SessionId == sessionId && sp.PlayerId == participant.PlayerId)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(sp => sp.RatingBefore, playerRank.Rating),
                            ct);
                }
            }

            // Enqueue a pending_rating_updates row for ranked sessions only.
            if (!participant.LadderId.HasValue)
                continue;

            var row = new PendingRatingUpdate
            {
                Id = _ids.NewId(),
                SessionId = sessionId,
                PlayerId = participant.PlayerId,
                LadderId = participant.LadderId.Value,
                Result = participant.Result.ToString(),
                Score = participant.Score,
                EnqueuedAt = now,
                ClaimedAt = null,
                AppliedAt = null,
            };

            _ctx.Set<PendingRatingUpdate>().Add(row);
        }

        // SaveChanges within the caller's ambient transaction — no explicit Commit here.
        await _ctx.SaveChangesAsync(ct);
    }
}
