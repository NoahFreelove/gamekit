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
/// GDPR safety (Pitfall §12): <see cref="SessionParticipantSnapshot.PlayerId"/> is <see cref="Guid"/>
/// (non-nullable value type) — no null-guard is needed here. The GDPR cascade that sets
/// <c>session_participants.PlayerId = NULL</c> in the DB happens post-enqueue (D-22 ordering), so
/// the snapshot is always built from a valid, non-zero player id. This differs from the ticker's
/// <c>PendingRatingUpdate.PlayerId</c> which is <see cref="Nullable{Guid}"/> and does need a skip guard.
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
            // PlayerId is Guid (non-nullable); GDPR cascade sets session_participants.PlayerId = NULL
            // in the DB post-completion, but this snapshot is built before that happens (D-22 ordering).
            // No null-guard needed here.

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

                    // RANK-16: atomic placement decrement inside the caller's ambient ReadCommitted tx.
                    // Uses ExecuteUpdateAsync (stateless WHERE predicate) — playerRank is loaded AsNoTracking
                    // so mutating it and calling SaveChanges would be a silent no-op (Pitfall §6).
                    // The WHERE guard PlacementMatchesRemaining > 0 prevents underflow under concurrent
                    // session-complete calls (race guard for T-08-03-01).
                    if (playerRank.IsInPlacement && playerRank.PlacementMatchesRemaining > 0)
                    {
                        await _ctx.Set<PlayerRank>()
                            .Where(r => r.PlayerId == participant.PlayerId
                                     && r.LadderId == participant.LadderId!.Value
                                     && r.IsInPlacement
                                     && r.PlacementMatchesRemaining > 0)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(r => r.PlacementMatchesRemaining, r => r.PlacementMatchesRemaining - 1)
                                .SetProperty(r => r.IsInPlacement,
                                    r => r.PlacementMatchesRemaining - 1 == 0 ? false : r.IsInPlacement),
                                ct);
                    }
                }
            }

            // Enqueue a pending_rating_updates row for ranked sessions only.
            if (!participant.LadderId.HasValue)
                continue;

            // MATCH-19 SC#4: participation-fraction guard.
            // Re-read ParticipationFraction from the session_participants row (column added by
            // Matchmaking migration 20260520000000). Null fraction = pre-Phase-9 row or full
            // participation → guard is skipped (v1 behaviour preserved).
            // MinParticipationFractionForRating is read from the ladder's JSONB Config via the
            // same pattern as RatingPeriodSeconds in RankingsTickerService.ReadRatingPeriod.
            var sp = await _ctx.SessionParticipants
                .AsNoTracking()
                .Where(s => s.SessionId == sessionId && s.PlayerId == participant.PlayerId)
                .Select(s => new { s.ParticipationFraction })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (sp?.ParticipationFraction.HasValue == true)
            {
                var ladder = await _ctx.Set<Ladder>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => l.Id == participant.LadderId!.Value, ct)
                    .ConfigureAwait(false);
                var minFraction = ReadMinParticipationFraction(ladder);
                if (minFraction.HasValue && sp.ParticipationFraction.Value < minFraction.Value)
                    continue; // Skip PendingRatingUpdate INSERT — no rating change for this participant.
            }

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

    /// <summary>
    /// Reads <c>MinParticipationFractionForRating</c> from the ladder's JSONB Config.
    /// Returns <see langword="null"/> when the property is absent, the config is null, or
    /// the value cannot be parsed — <see langword="null"/> means no guard is applied (v1
    /// behaviour: all participants receive rating updates). Mirrors the
    /// <c>RankingsTickerService.ReadRatingPeriod</c> JSONB read pattern (try/TryGetProperty/
    /// TryGetDouble with catch for JSON errors). T-09-04-02 mitigation: a corrupt Config
    /// never throws inside <see cref="OnCompletedAsync"/>.
    /// </summary>
    /// <param name="ladder">The ladder entity whose JSONB Config is to be read. May be null.</param>
    /// <returns>The configured minimum participation fraction, or null if not configured.</returns>
    private static double? ReadMinParticipationFraction(Ladder? ladder)
    {
        if (ladder?.Config is null) return null;
        try
        {
            if (ladder.Config.RootElement.TryGetProperty("MinParticipationFractionForRating", out var elem)
                && elem.TryGetDouble(out var value))
                return value;
        }
        catch
        {
            // Ignore JSON parse errors — treat absent/corrupt config as no guard (T-09-04-02).
        }
        return null;
    }
}
