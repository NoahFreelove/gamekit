// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;

namespace GameKit.Rankings.Services;

/// <summary>
/// Default <see cref="IEndSeasonService"/>. Runs a single SERIALIZABLE transaction that atomically
/// closes the current <c>ladder_seasons</c> row, opens a new one, archives all <c>player_ranks</c>
/// rows into <c>season_rank_archive</c>, applies the configured <see cref="SeasonResetPolicy"/>,
/// and writes an <c>admin.ladder.end_season</c> audit row directly to <c>admin_audit_log</c>
/// via the shared <see cref="GameKitDbContext"/> (D-11 / D-12 / D-13 / D-14 / RANK-10).
/// </summary>
/// <remarks>
/// <para>
/// The audit row is written directly to <c>_ctx.Set&lt;AdminAuditLog&gt;()</c> rather than through
/// <c>IAdminAuditWriter</c>. This preserves the D-22 invariant that <c>GameKit.Rankings</c> does
/// NOT project-reference <c>GameKit.Admin.UI</c> (Admin.UI references Rankings for the dialog,
/// not the reverse — adding the reverse reference would create a cycle). <c>AdminAuditLog</c> is
/// a <c>GameKit.Core</c> entity and therefore accessible to Rankings without any Admin.UI dependency.
/// </para>
/// <para>
/// All five mutations ride the same SERIALIZABLE transaction. Any failure rolls back the entire
/// operation — no partial archive, no season-cursor change, and no audit row are written
/// (T-04-07-AT repudiation mitigation).
/// </para>
/// </remarks>
public sealed class EndSeasonService : IEndSeasonService
{
    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly ResiliencePipeline _serializationRetry;

    // Audit action constant — mirrors AdminAuditActions.LadderEndSeason in GameKit.Admin.UI.
    // Duplicated here as a literal to avoid the circular dependency. The value MUST stay in sync.
    private const string LadderEndSeasonAction = "admin.ladder.end_season";

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">Id generator (UUIDv7).</param>
    /// <param name="logger">Logger for serialization-failure retry diagnostics (CR-03).</param>
    public EndSeasonService(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator ids,
        ILogger<EndSeasonService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
        _serializationRetry = SerializationFailureRetry.Build(logger, nameof(EndSeasonService));
    }

    /// <inheritdoc />
    public async Task<EndSeasonResult> EndAsync(Guid ladderId, Guid actorId, CancellationToken ct)
    {
        // Wrap the SERIALIZABLE transaction body in a Polly retry pipeline (CR-03) so
        // concurrent end-season calls against the same ladder do not surface 500s.
        return await _serializationRetry.ExecuteAsync(async cancellationToken =>
            await EndCoreAsync(ladderId, actorId, cancellationToken).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    private async Task<EndSeasonResult> EndCoreAsync(Guid ladderId, Guid actorId, CancellationToken ct)
    {
        await using var tx = await _ctx.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        // 1. Load the ladder.
        var ladder = await _ctx.Set<Ladder>()
            .FirstOrDefaultAsync(l => l.Id == ladderId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Ladder {ladderId} not found.");

        // 2. Find the current open season (EndedAt IS NULL).
        var currentSeason = await _ctx.Set<LadderSeason>()
            .FirstOrDefaultAsync(s => s.LadderId == ladderId && s.EndedAt == null, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Ladder {ladderId} has no current open season. " +
                "Ensure a season row with EndedAt IS NULL exists before calling EndAsync.");

        var now = _clock.UtcNow;
        var closedSeasonId = currentSeason.Id;
        var closedSeasonNumber = currentSeason.SeasonNumber;

        // 3. Close the current season.
        currentSeason.EndedAt = now;
        currentSeason.EndedByAdminId = actorId;

        // 4. Open a new season.
        var newSeasonId = _ids.NewId();
        var newSeasonNumber = closedSeasonNumber + 1;
        var newSeason = new LadderSeason
        {
            Id = newSeasonId,
            LadderId = ladderId,
            SeasonNumber = newSeasonNumber,
            StartedAt = now,
        };
        _ctx.Set<LadderSeason>().Add(newSeason);

        // 5. Load all current player_ranks for this ladder (snapshot + archive).
        var ranks = await _ctx.Set<PlayerRank>()
            .Where(r => r.LadderId == ladderId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Capture before snapshot (top 3 for audit brevity).
        var topBefore = ranks
            .OrderByDescending(r => r.Rating)
            .Take(3)
            .Select(r => new { r.PlayerId, r.Rating })
            .ToArray();

        // 6. Archive each player_ranks row into season_rank_archive.
        foreach (var rank in ranks)
        {
            _ctx.Set<SeasonRankArchive>().Add(new SeasonRankArchive
            {
                Id = _ids.NewId(),
                LadderId = ladderId,
                SeasonId = closedSeasonId,
                PlayerId = rank.PlayerId,
                Rating = rank.Rating,
                RatingDeviation = rank.RatingDeviation,
                Volatility = rank.Volatility,
                Wins = rank.Wins,
                Losses = rank.Losses,
                Draws = rank.Draws,
                ArchivedAt = now,
            });
        }

        // 7. Determine and apply reset policy.
        var policy = ReadResetPolicy(ladder);
        var (defaultRating, defaultRd, defaultVolatility, regressionFactor, rdCeiling, rdBump)
            = ReadPolicyConfig(ladder);

        foreach (var rank in ranks)
        {
            switch (policy)
            {
                case SeasonResetPolicy.SoftRegress:
                    rank.Rating = defaultRating + (rank.Rating - defaultRating) * regressionFactor;
                    rank.RatingDeviation = Math.Min(rdCeiling, rank.RatingDeviation + rdBump);
                    rank.Volatility = defaultVolatility;
                    break;

                case SeasonResetPolicy.HardReset:
                    rank.Rating = defaultRating;
                    rank.RatingDeviation = defaultRd;
                    rank.Volatility = defaultVolatility;
                    break;

                case SeasonResetPolicy.ArchiveOnly:
                    // No mutation — archive row written above, live ranks unchanged.
                    break;
            }
        }

        // Capture after snapshot.
        var topAfter = ranks
            .OrderByDescending(r => r.Rating)
            .Take(3)
            .Select(r => new { r.PlayerId, r.Rating })
            .ToArray();

        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        // 8. Write audit row directly to admin_audit_log (AdminAuditLog is a Core entity — no Admin.UI dep needed).
        // The action literal mirrors AdminAuditActions.LadderEndSeason = "admin.ladder.end_season".
        _ctx.Set<AdminAuditLog>().Add(new AdminAuditLog
        {
            Id = _ids.NewId(),
            ActorId = actorId,
            Action = LadderEndSeasonAction,
            TargetType = "ladder",
            TargetId = ladderId,
            Before = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                season_id = closedSeasonId,
                season_number = closedSeasonNumber,
                archived_row_count = ranks.Count,
                top_3 = topBefore,
            })),
            After = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                new_season_id = newSeasonId,
                new_season_number = newSeasonNumber,
                applied_policy = policy.ToString(),
                top_3_after = topAfter,
            })),
            Reason = null,
            CreatedAt = now,
        });
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);

        return new EndSeasonResult(
            ClosedSeasonId: closedSeasonId,
            ClosedSeasonNumber: closedSeasonNumber,
            OpenedSeasonId: newSeasonId,
            NewSeasonNumber: newSeasonNumber,
            ArchivedRowCount: ranks.Count,
            AppliedPolicy: policy);
    }

    // ----- private helpers -----

    private static SeasonResetPolicy ReadResetPolicy(Ladder ladder)
    {
        if (ladder.Config is null) return SeasonResetPolicy.SoftRegress;
        try
        {
            if (ladder.Config.RootElement.TryGetProperty("ResetPolicy", out var policyElem)
                && policyElem.ValueKind == JsonValueKind.String)
            {
                var s = policyElem.GetString();
                if (Enum.TryParse<SeasonResetPolicy>(s, ignoreCase: true, out var parsed))
                    return parsed;
            }
        }
        catch (InvalidOperationException) { /* malformed json — fall through to default */ }
        return SeasonResetPolicy.SoftRegress;
    }

    private static (double DefaultRating, double DefaultRd, double DefaultVolatility,
        double RegressionFactor, double RdCeiling, double RdBump)
        ReadPolicyConfig(Ladder ladder)
    {
        double defaultRating = 1500, defaultRd = 350, defaultVolatility = 0.06;
        double regressionFactor = 0.5, rdCeiling = 200, rdBump = 50;

        if (ladder.Config is null)
            return (defaultRating, defaultRd, defaultVolatility, regressionFactor, rdCeiling, rdBump);

        try
        {
            var root = ladder.Config.RootElement;
            if (root.TryGetProperty("DefaultRating", out var dr) && dr.TryGetDouble(out var d1))
                defaultRating = d1;
            if (root.TryGetProperty("DefaultRd", out var drd) && drd.TryGetDouble(out var d2))
                defaultRd = d2;
            if (root.TryGetProperty("DefaultVolatility", out var dv) && dv.TryGetDouble(out var d3))
                defaultVolatility = d3;
            if (root.TryGetProperty("RegressionFactor", out var rf) && rf.TryGetDouble(out var d4))
                regressionFactor = d4;
            if (root.TryGetProperty("RdCeiling", out var rc) && rc.TryGetDouble(out var d5))
                rdCeiling = d5;
            if (root.TryGetProperty("RdBump", out var rb) && rb.TryGetDouble(out var d6))
                rdBump = d6;
        }
        catch (InvalidOperationException) { /* malformed json — fall through to defaults */ }

        return (defaultRating, defaultRd, defaultVolatility, regressionFactor, rdCeiling, rdBump);
    }
}
