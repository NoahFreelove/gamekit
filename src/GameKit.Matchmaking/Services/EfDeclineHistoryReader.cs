// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IDeclineHistoryReader"/> backed by a scoped
/// <see cref="GameKitDbContext"/>. Reads/writes the <c>decline_history</c> table created by
/// the Plan 05-02 migration; consumed by <see cref="DeclineCooldownService"/> for D-08
/// escalating cooldown evaluation and by <c>IProposalService.DeclineAsync</c> for
/// the cooldown-record write that fires on every decline (Plan 05-06 Task 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>UTC discipline (Pitfall §4):</b> the caller passes <c>since</c> and
/// <c>declinedAt</c> values already sourced from <see cref="IClock"/>; the reader never
/// touches <see cref="DateTime"/>.<c>Now</c> / <see cref="DateTime"/>.<c>UtcNow</c>.
/// </para>
/// <para>
/// <b>Query shape:</b> the <c>(PlayerId, DeclinedAt DESC)</c> index from Plan 05-02 covers
/// the rolling-window lookup — Postgres uses an index-only scan with the LIMIT 3 clause.
/// </para>
/// </remarks>
internal sealed class EfDeclineHistoryReader : IDeclineHistoryReader
{
    private readonly GameKitDbContext _ctx;
    private readonly IIdGenerator _ids;

    /// <summary>Constructs the reader.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="ids">Id generator (UUIDv7) for new <see cref="DeclineHistory"/> rows.</param>
    public EfDeclineHistoryReader(GameKitDbContext ctx, IIdGenerator ids)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(ids);
        _ctx = ctx;
        _ids = ids;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeclineHistory>> GetRecentDeclinesAsync(
        Guid playerId, DateTimeOffset since, int take, CancellationToken ct)
    {
        if (take < 1)
            throw new ArgumentOutOfRangeException(nameof(take), take, "take must be >= 1.");

        var rows = await _ctx.Set<DeclineHistory>()
            .AsNoTracking()
            .Where(d => d.PlayerId == playerId && d.DeclinedAt > since)
            .OrderByDescending(d => d.DeclinedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public async Task RecordDeclineAsync(
        Guid playerId, Guid proposalId, DateTimeOffset declinedAt, CancellationToken ct)
    {
        var row = new DeclineHistory
        {
            Id = _ids.NewId(),
            PlayerId = playerId,
            ProposalId = proposalId,
            DeclinedAt = declinedAt,
        };
        _ctx.Set<DeclineHistory>().Add(row);
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
