// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace GameKit.Rankings.Services;

/// <summary>
/// Default implementation of <see cref="IRankAdjustService"/> (RANK-12 / D-19 / D-20).
/// Runs a SERIALIZABLE transaction that atomically updates <c>player_ranks</c> and writes
/// an <c>admin.player.rank_adjust</c> audit row to <c>admin_audit_log</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>IAdminAuditWriter boundary</b>: this service writes the audit row directly via
/// <c>_ctx.Set&lt;AdminAuditLog&gt;()</c> (a Core entity) rather than through
/// <c>IAdminAuditWriter</c> (which lives in <c>GameKit.Admin.UI</c>). This avoids the
/// circular-dependency problem: Admin.UI references Rankings for dialog injection, so
/// Rankings must NOT reference Admin.UI (D-22 invariant — same pattern used by
/// <c>EndSeasonService</c> in plan 04-07).
/// </para>
/// <para>
/// The audit action literal <c>"admin.player.rank_adjust"</c> must stay in sync with
/// <c>GameKit.Admin.UI.Services.AdminAuditActions.PlayerRankAdjust</c>.
/// </para>
/// </remarks>
public sealed class RankAdjustService : IRankAdjustService
{
    // Mirrors AdminAuditActions.PlayerRankAdjust — Rankings cannot reference Admin.UI (D-22).
    private const string AuditAction = "admin.player.rank_adjust";

    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGen;
    private readonly IOptions<GameKitRankingsOptions> _opts;
    private readonly ILogger<RankAdjustService>? _logger;
    private readonly ResiliencePipeline _serializationRetry;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">UTC clock abstraction.</param>
    /// <param name="idGen">Id generator for new <c>player_ranks</c> rows.</param>
    /// <param name="opts">Rankings options providing MinRating / MaxRating bounds.</param>
    /// <param name="logger">Logger used for serialization-failure retry diagnostics (CR-03).</param>
    public RankAdjustService(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator idGen,
        IOptions<GameKitRankingsOptions> opts,
        ILogger<RankAdjustService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGen);
        ArgumentNullException.ThrowIfNull(opts);
        _ctx = ctx;
        _clock = clock;
        _idGen = idGen;
        _opts = opts;
        _logger = logger;
        _serializationRetry = SerializationFailureRetry.Build(logger, nameof(RankAdjustService));
    }

    /// <inheritdoc />
    public async Task<RankAdjustResult> AdjustAsync(
        Guid playerId,
        Guid ladderId,
        double newRating,
        string reason,
        Guid actorId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);

        // Validate rating bounds before touching the DB (fail fast).
        var min = _opts.Value.RankAdjust.MinRating;
        var max = _opts.Value.RankAdjust.MaxRating;
        if (double.IsNaN(newRating) || double.IsInfinity(newRating) || newRating < min || newRating > max)
            throw new ArgumentOutOfRangeException(nameof(newRating),
                $"newRating {newRating} is outside the allowed range [{min}, {max}].");

        // Wrap the SERIALIZABLE transaction body in a Polly retry pipeline (CR-03).
        // Postgres 40001 serialization_failure under concurrent writes is now retried up to
        // 3 times with exponential backoff before bubbling to the caller as a 500.
        return await _serializationRetry.ExecuteAsync(async cancellationToken =>
        {
            await using var tx = await _ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            // 1. Resolve the ladder — 404 if not found.
            var ladder = await _ctx.Set<Ladder>()
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == ladderId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Ladder {ladderId} not found.");

            // 2. Find or lazy-create the player_ranks row (RANK-07 carry).
            var rank = await _ctx.Set<PlayerRank>()
                .FirstOrDefaultAsync(r => r.PlayerId == playerId && r.LadderId == ladderId, cancellationToken)
                .ConfigureAwait(false);

            var (_, defaultRd, defaultVolatility) = ReadLadderDefaults(ladder);

            // WR-02: track lazy-creation as an explicit bool rather than via a 0.0 sentinel
            // on beforeRating. A configured MinRating of 0 (legitimate skill scale) would
            // otherwise make "first rank ever" indistinguishable from "adjust from zero".
            bool wasLazyCreated;
            double beforeRating;
            if (rank is null)
            {
                // Lazy creation — new row with ladder defaults for RD + volatility (RANK-07).
                wasLazyCreated = true;
                beforeRating = 0; // value is undefined for lazy-created rows; consult WasLazyCreated.
                rank = new PlayerRank
                {
                    Id = _idGen.NewId(),
                    PlayerId = playerId,
                    LadderId = ladderId,
                    Rating = newRating,
                    RatingDeviation = defaultRd,
                    Volatility = defaultVolatility,
                    LastMatchAt = _clock.UtcNow,
                };
                _ctx.Set<PlayerRank>().Add(rank);
            }
            else
            {
                wasLazyCreated = false;
                beforeRating = rank.Rating;
                // Manual adjust: override Rating + LastMatchAt only (D-20 — RD + Volatility NOT touched).
                rank.Rating = newRating;
                rank.LastMatchAt = _clock.UtcNow;
            }

            // 3. Build before/after JSON snapshots for the audit row.
            var beforeSnapshot = wasLazyCreated
                ? null
                : JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    rating = beforeRating,
                    rating_deviation = rank.RatingDeviation,
                    volatility = rank.Volatility,
                }));

            var afterSnapshot = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                rating = newRating,
                rating_deviation = rank.RatingDeviation,
                volatility = rank.Volatility,
            }));

            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // 4. Write the audit row — rides the same SERIALIZABLE transaction.
            // Direct write via Core entity (not IAdminAuditWriter) to avoid circular dep (D-22).
            var auditRow = new AdminAuditLog
            {
                Id = _idGen.NewId(),
                Action = AuditAction,
                TargetType = "player",
                TargetId = playerId,
                ActorId = actorId,
                Before = beforeSnapshot,
                After = afterSnapshot,
                Reason = reason,
                CreatedAt = _clock.UtcNow,
            };
            _ctx.Set<AdminAuditLog>().Add(auditRow);
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

            // For lazy-created rows there is no meaningful "delta"; surface After as the
            // delta so existing UI continues to render, but callers can branch on
            // WasLazyCreated to suppress or relabel.
            var delta = wasLazyCreated ? newRating : (newRating - beforeRating);
            return new RankAdjustResult(beforeRating, newRating, delta, wasLazyCreated);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads default Glicko-2 parameters from the ladder's JSONB Config.
    /// Falls back to Glickman's standard defaults when Config is absent or unparseable.
    /// </summary>
    private static (double DefaultRating, double DefaultRd, double DefaultVolatility) ReadLadderDefaults(Ladder ladder)
    {
        const double defaultRating = 1500;
        const double defaultRd = 350;
        const double defaultVolatility = 0.06;

        if (ladder.Config is null)
            return (defaultRating, defaultRd, defaultVolatility);

        try
        {
            var root = ladder.Config.RootElement;
            var rating = root.TryGetProperty("DefaultRating", out var r) && r.TryGetDouble(out var rv)
                ? rv : defaultRating;
            var rd = root.TryGetProperty("DefaultRd", out var d) && d.TryGetDouble(out var dv)
                ? dv : defaultRd;
            var vol = root.TryGetProperty("DefaultVolatility", out var v) && v.TryGetDouble(out var vv)
                ? vv : defaultVolatility;
            return (rating, rd, vol);
        }
        catch
        {
            return (defaultRating, defaultRd, defaultVolatility);
        }
    }
}
