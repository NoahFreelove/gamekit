// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;

namespace GameKit.Auth.Services;

/// <summary>
/// Default <see cref="IAccountMergeService"/>. Performs an irreversible, SERIALIZABLE,
/// crash-resumable merge of a source player into a target player (AUTH-23/24/25/26).
/// </summary>
/// <remarks>
/// <para>
/// The operation is structured as a three-phase state machine tracked in <c>account_merges</c>:
/// <list type="bullet">
///   <item><description><c>Pending → Committed</c>: one SERIALIZABLE transaction that re-points every FK,
///   conflict-resolves player_ranks, revokes source tokens, tombstones the source row, and writes a
///   single audit row (SC#1/SC#2/SC#3/SC#4).</description></item>
///   <item><description><c>Committed → RedisCleaned</c>: removes stale matchmaking sorted-set entries
///   for the source player OUTSIDE the transaction (non-transactional by necessity, hence the
///   separate checkpoint). Missing Redis (IConnectionMultiplexer) is a no-op — keys TTL-expire
///   naturally and the source's tokens are already revoked (Pitfall 7).</description></item>
/// </list>
/// </para>
/// <para>
/// The SERIALIZABLE isolation level + UNIQUE(SourcePlayerId) on <c>account_merges</c> together
/// prevent double-merge under concurrent requests (T-10-03-02). 40001 serialization failures are
/// retried up to <see cref="MaxRetries"/> times with change-tracker detach between attempts.
/// </para>
/// <para>
/// Cross-package table mutations (player_ranks, pending_rating_updates, season_rank_archive,
/// party_members, parties, decline_history, lobby_members) are issued as parameterized SQL via
/// <c>Database.ExecuteSqlAsync</c> — GameKit.Auth does not hold a ProjectReference to
/// GameKit.Rankings, GameKit.Matchmaking, or GameKit.Lobby (adding the reverse reference would
/// create a circular dependency). The shared <see cref="GameKitDbContext"/>
/// model includes these entities at runtime (via IModelBuilderExtension) so the SQL executes
/// correctly inside the same SERIALIZABLE transaction.
/// </para>
/// <para>
/// The audit row (<c>auth.account_merge</c>) is written via <c>_ctx.Set&lt;AdminAuditLog&gt;()</c>
/// directly — <c>AdminAuditLog</c> is a <c>GameKit.Core</c> entity accessible to Auth with no
/// additional dependency. This follows the <c>EndSeasonService</c> precedent (D-22).
/// </para>
/// </remarks>
internal sealed class AccountMergeService : IAccountMergeService
{
    private const int MaxRetries = 3;

    // Audit action constant — mirrors AdminAuditActions.AccountMerge in GameKit.Admin.UI.
    // Duplicated here as a literal to avoid the circular dependency. The value MUST stay in sync.
    private const string AccountMergeAction = "auth.account_merge";

    // Presence key format — mirrors PresenceRedisKeys.Player(playerId) in GameKit.Presence.
    // Duplicated here to avoid the circular dependency (Matchmaking already references Auth;
    // adding Presence→Auth or Auth→Presence would create a cycle). The value MUST stay in sync
    // with PresenceRedisKeys.Player in GameKit.Presence.
    private const string PresenceKeyPrefix = "presence:";

    private readonly GameKitDbContext _ctx;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IRefreshTokenService _refresh;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<AccountMergeService>? _logger;

    /// <summary>Constructs the merge service.</summary>
    /// <param name="ctx">Request-scoped <see cref="GameKitDbContext"/>.</param>
    /// <param name="clock">Clock abstraction.</param>
    /// <param name="ids">UUIDv7 id generator.</param>
    /// <param name="refresh">Refresh-token service for source token revocation.</param>
    /// <param name="redis">
    /// Optional Redis connection for post-commit cleanup of stale matchmaking keys.
    /// When null the Redis cleanup step is skipped and keys TTL-expire naturally (Pitfall 7).
    /// </param>
    /// <param name="logger">Optional logger for retry diagnostics.</param>
    public AccountMergeService(
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator ids,
        IRefreshTokenService refresh,
        IConnectionMultiplexer? redis = null,
        ILogger<AccountMergeService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(refresh);
        _ctx = ctx;
        _clock = clock;
        _ids = ids;
        _refresh = refresh;
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MergeResult> MergeAsync(
        Guid sourcePlayerId,
        Guid targetPlayerId,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        // ── SC#1 CRASH-RESUME LADDER ──────────────────────────────────────────────────────────
        // Read the existing account_merges row ONCE, outside the SERIALIZABLE tx. This is the
        // authoritative resume signal: the DB is the single source of truth for the state machine.
        var existing = await _ctx.Set<AccountMerge>()
            .AsNoTracking()
            .FirstOrDefaultAsync(am => am.SourcePlayerId == sourcePlayerId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Guard: a different target was requested for an already-merged (or in-progress) source.
            // Applies to ALL statuses — a completed merge (RedisCleaned/Committed) is as terminal
            // as a pending one from the source player's perspective.
            if (existing.TargetPlayerId != targetPlayerId)
            {
                throw new MergeConflictException(
                    MergeConflictReason.SourceAlreadyMerged,
                    $"Source player {sourcePlayerId} was previously merged (or started merging) " +
                    $"into {existing.TargetPlayerId}, not {targetPlayerId}.");
            }

            if (existing.Status == MergeStatus.RedisCleaned)
            {
                // Already fully complete with the same target. Return AlreadyMerged — no work,
                // no double-revoke, no duplicate audit (T-10-03-03).
                return MergeResult.AlreadyMerged(existing.TargetPlayerId);
            }

            if (existing.Status == MergeStatus.Committed)
            {
                // The DB tx committed but the process crashed before Redis cleanup.
                // Skip the entire DB transaction body — jump straight to Redis cleanup.
                _logger?.LogInformation(
                    "AccountMergeService: resuming committed merge {MergeId} from Committed→RedisCleaned checkpoint.",
                    existing.Id);

                await RunRedisCleanupAsync(existing.Id, sourcePlayerId, cancellationToken)
                    .ConfigureAwait(false);

                return MergeResult.AlreadyMerged(existing.TargetPlayerId);
            }

            // Status == Pending: the tx rolled back (or the process crashed before INSERT committed).
            // Same target already confirmed above. Re-run the transaction body.

            // Re-run the SERIALIZABLE tx body — all UPDATEs are idempotent; the INSERT in step 4
            // is skipped because the Pending row already exists.
            _logger?.LogInformation(
                "AccountMergeService: resuming pending merge {MergeId} — re-running SERIALIZABLE tx.",
                existing.Id);
        }

        // ── SERIALIZABLE TX BODY (with up to MaxRetries on 40001) ────────────────────────────
        Guid mergeRowId = Guid.Empty;

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            await using var tx = await _ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                mergeRowId = await MergeTransactionBodyAsync(
                    sourcePlayerId, targetPlayerId, actorId, existing, cancellationToken)
                    .ConfigureAwait(false);

                // WR-02: Guid.Empty is returned by MergeTransactionBodyAsync when a concurrent
                // request already committed a merge to the same target (same-target TOCTOU).
                // Roll back the empty tx and return AlreadyMerged — not a conflict.
                if (mergeRowId == Guid.Empty)
                {
                    await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return MergeResult.AlreadyMerged(targetPlayerId);
                }

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

                // ── POST-COMMIT: Redis cleanup (OUTSIDE the SERIALIZABLE tx) ────────────────
                await RunRedisCleanupAsync(mergeRowId, sourcePlayerId, cancellationToken)
                    .ConfigureAwait(false);

                return MergeResult.Merged(targetPlayerId);
            }
            catch (Exception ex) when (TryFindPostgresException(ex) is { } pg)
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);

                // Detach in-flight entities so the scoped DbContext stays usable on retry.
                // Required by Pitfall 5 / IdentityLinker + GuestUpgradeService precedent.
                foreach (var entry in _ctx.ChangeTracker.Entries())
                {
                    entry.State = EntityState.Detached;
                }

                if (pg.SqlState == "23505")
                {
                    // UNIQUE(SourcePlayerId) violation — a concurrent attempt won the race
                    // and inserted the account_merges row first. Return AlreadyMerged.
                    var concurrent = await _ctx.Set<AccountMerge>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            am => am.SourcePlayerId == sourcePlayerId,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (concurrent is not null)
                        return MergeResult.AlreadyMerged(concurrent.TargetPlayerId);

                    // Row vanished between the 23505 and the re-read — re-throw to surface the
                    // unexpected constraint violation.
                    throw;
                }

                if (pg.SqlState == "40001" && attempt < MaxRetries - 1)
                {
                    _logger?.LogWarning(
                        "AccountMergeService: serialization failure on attempt {Attempt}/{MaxRetries} " +
                        "for source={Source}. Retrying.",
                        attempt + 1, MaxRetries, sourcePlayerId);
                    continue;
                }

                throw;
            }
        }

        // This line is unreachable: on the last attempt (attempt == MaxRetries-1) a 40001
        // serialization failure hits the `pg.SqlState == "40001" && attempt < MaxRetries - 1`
        // condition as false and falls through to `throw;`, propagating the exception out of the
        // loop. All other exits are `return` (success) or `throw` (non-retryable postgres error).
        // The statement is retained for C# control-flow completeness.
        throw new InvalidOperationException("AccountMergeService: SERIALIZABLE retries exhausted.");
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // TRANSACTION BODY
    // All mutations inside a single SERIALIZABLE transaction. The order matters — guards first,
    // then idempotency row, then FK surgery, audit, tombstone, status advance.
    //
    // Cross-package table mutations (player_ranks, pending_rating_updates, season_rank_archive,
    // party_members, parties, decline_history, lobby_members) use parameterized SQL via
    // Database.ExecuteSqlAsync because GameKit.Auth cannot hold a ProjectReference to
    // GameKit.Rankings, GameKit.Matchmaking, or GameKit.Lobby (adding the reverse reference would
    // create a cycle).
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<Guid> MergeTransactionBodyAsync(
        Guid sourcePlayerId,
        Guid targetPlayerId,
        Guid actorId,
        AccountMerge? existingMergeRow,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // ── STEP 1: Load source + target Player rows ─────────────────────────────────────────
        var source = await _ctx.Set<Player>()
            .FirstOrDefaultAsync(p => p.Id == sourcePlayerId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Source player {sourcePlayerId} not found.");

        var target = await _ctx.Set<Player>()
            .FirstOrDefaultAsync(p => p.Id == targetPlayerId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException(
                $"Target player {targetPlayerId} not found.");

        // ── STEP 2: GUARDS ───────────────────────────────────────────────────────────────────
        if (sourcePlayerId == targetPlayerId)
            throw new MergeConflictException(
                MergeConflictReason.SelfMerge,
                "Cannot merge a player into themselves.");

        if (source.MergedIntoPlayerId.HasValue)
        {
            // WR-02: TOCTOU guard — a concurrent request may have committed the merge between
            // the outer crash-resume read (outside this tx) and this SERIALIZABLE tx body.
            // If the completed merge is for the SAME target, this is an idempotent re-entry:
            // signal the caller to return AlreadyMerged (success, not conflict).
            // Only throw SourceAlreadyMerged when the target differs.
            if (source.MergedIntoPlayerId.Value == targetPlayerId)
            {
                // Return the sentinel Guid.Empty to tell MergeAsync this is an idempotent
                // same-target concurrent re-entry — the merge is already complete.
                return Guid.Empty;
            }

            throw new MergeConflictException(
                MergeConflictReason.SourceAlreadyMerged,
                $"Source player {sourcePlayerId} has already been merged into " +
                $"{source.MergedIntoPlayerId.Value}.");
        }

        if (target.IsBanned)
            throw new MergeConflictException(
                MergeConflictReason.TargetBanned,
                $"Target player {targetPlayerId} is banned — cannot merge into a banned account.");

        // Banned source is ALLOWED (A3) — recorded in the audit metadata.

        // ── STEP 3: PARTY CONFLICT CHECK (pre-mutation) ─────────────────────────────────────
        // Abort if source and target are both members of the same active party.
        // RESEARCH decision: abort-merge rather than remove-source-member silently.
        // Uses raw SQL — GameKit.Auth does not reference GameKit.Matchmaking.
        var samePartyCount = await _ctx.Database
            .SqlQuery<int>(
                $"""
                SELECT COUNT(*)::int AS "Value"
                FROM gamekit.party_members pm_source
                JOIN gamekit.party_members pm_target
                    ON pm_source."PartyId" = pm_target."PartyId"
                WHERE pm_source."PlayerId" = {sourcePlayerId}
                  AND pm_target."PlayerId" = {targetPlayerId}
                """)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (samePartyCount > 0)
            throw new MergeConflictException(
                MergeConflictReason.PlayersInSameParty,
                $"Players {sourcePlayerId} and {targetPlayerId} are both members of the same " +
                "party. Remove one from the party before merging.");

        // ── STEP 4: IDEMPOTENCY ROW ──────────────────────────────────────────────────────────
        // INSERT-if-absent. If a Pending row already exists (crash-resume path), skip the INSERT.
        // The crash-resume ladder at the top of MergeAsync is authoritative for Committed/
        // RedisCleaned short-circuits; within this tx body we only handle absent-or-Pending.
        Guid mergeRowId;

        if (existingMergeRow is null)
        {
            mergeRowId = _ids.NewId();
            _ctx.Set<AccountMerge>().Add(new AccountMerge
            {
                Id = mergeRowId,
                SourcePlayerId = sourcePlayerId,
                TargetPlayerId = targetPlayerId,
                Status = MergeStatus.Pending,
                ActorId = actorId,
                RequestedAt = now,
            });
            await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            // Pending row already exists from a previous attempt that rolled back.
            mergeRowId = existingMergeRow.Id;
        }

        // ── STEP 5: RE-POINT PLAYER_IDENTITIES ──────────────────────────────────────────────
        // No UNIQUE conflict: UNIQUE is on (provider, external_id), NOT on player_id. All Phase 7
        // Google/Apple/Epic rows are ordinary player_identities rows — no special-casing needed.
        await _ctx.Set<PlayerIdentity>()
            .Where(pi => pi.PlayerId == sourcePlayerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(pi => pi.PlayerId, targetPlayerId),
                ct)
            .ConfigureAwait(false);

        // ── STEP 6: PLAYER_CREDENTIALS ──────────────────────────────────────────────────────
        // PlayerCredential uses PlayerId as PK (one-per-player constraint).
        // If target already has a credential, DELETE source's credential (Pitfall 1).
        // If target has no credential, re-point source credential to target.
        var targetHasCredential = await _ctx.Set<PlayerCredential>()
            .AnyAsync(pc => pc.PlayerId == targetPlayerId, ct)
            .ConfigureAwait(false);

        if (targetHasCredential)
        {
            // Target already has a credential. Delete source's credential to avoid PK conflict.
            await _ctx.Set<PlayerCredential>()
                .Where(pc => pc.PlayerId == sourcePlayerId)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
        }
        else
        {
            // Target has no credential — re-point source credential to target.
            await _ctx.Set<PlayerCredential>()
                .Where(pc => pc.PlayerId == sourcePlayerId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(pc => pc.PlayerId, targetPlayerId),
                    ct)
                .ConfigureAwait(false);
        }

        // ── STEP 7: REVOKE SOURCE REFRESH TOKENS ────────────────────────────────────────────
        // Revoke all source player token families. NOT re-pointed — the tokens represent the
        // source identity which is being retired. Called exactly once (only on the Pending path).
        await _refresh.RevokeAllForPlayerAsync(sourcePlayerId, "account_merge", ct)
            .ConfigureAwait(false);

        // ── STEP 8: SESSION_PARTICIPANTS (FULL HISTORY) ──────────────────────────────────────
        // Re-point ALL source session_participants rows — active AND completed. The target player
        // inherits the source's full match history (SC#2 literal: "ALL source rows", not active-only).
        // PlayerId is Guid? in SessionParticipant — use nullable Guid cast for the update.
        await _ctx.Set<SessionParticipant>()
            .Where(sp => sp.PlayerId == (Guid?)sourcePlayerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(sp => sp.PlayerId, (Guid?)targetPlayerId),
                ct)
            .ConfigureAwait(false);

        // ── STEP 9: PLAYER_RANKS CONFLICT RESOLUTION (SC#3) ─────────────────────────────────
        // Uses raw SQL — GameKit.Auth does not reference GameKit.Rankings.
        // Load source + target rank rows per ladder to determine conflict resolution strategy.
        //
        // A5: Rating — keep the higher rating row.
        // A6: IsInPlacement — false if either side completed placement (a merged account that
        //     completed placement on at least one account should not restart placement).
        //     Logic: IsInPlacement = source.IsInPlacement AND target.IsInPlacement.
        // A7: LastMatchAt — take the more recent of the two.
        //
        // Algorithm is executed in three raw SQL passes:
        //   1. For (source, target) pairs on the same ladder where source.Rating > target.Rating:
        //      a. UPDATE the source row: re-point to target, SUM W/L/D, MAX RD, recalc IsInPlacement/LastMatchAt.
        //      b. DELETE the old target row.
        //   2. For (source, target) pairs on the same ladder where source.Rating <= target.Rating:
        //      a. UPDATE the target row: SUM W/L/D, MAX RD, recalc IsInPlacement/LastMatchAt.
        //      b. DELETE the source row.
        //   3. For source-only rows (no conflicting target row): simple re-point.

        // Count conflicts resolved (for audit metadata).
        int ranksMerged = await _ctx.Database
            .SqlQuery<int>(
                $"""
                SELECT COUNT(*)::int AS "Value"
                FROM gamekit.player_ranks sr
                JOIN gamekit.player_ranks tr
                    ON sr."LadderId" = tr."LadderId"
                WHERE sr."PlayerId" = {sourcePlayerId}
                  AND tr."PlayerId" = {targetPlayerId}
                """)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // Pass 1: source.Rating > target.Rating — use a CTE to atomically delete the old target row
        // (capturing its W/L/D) and then re-point + merge-stats the source row into target.
        // Ordering: DELETE first to avoid a unique-constraint violation on (PlayerId, LadderId) when
        // the source row's PlayerId is updated to targetPlayerId while the original target row still
        // exists on the same ladder.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            WITH deleted_tgt AS (
                DELETE FROM gamekit.player_ranks AS tr
                USING gamekit.player_ranks AS sr
                WHERE sr."PlayerId" = {sourcePlayerId}
                  AND tr."PlayerId" = {targetPlayerId}
                  AND sr."LadderId" = tr."LadderId"
                  AND sr."Rating"   > tr."Rating"
                RETURNING tr."Id"        AS deleted_id,
                          sr."Id"        AS src_id,
                          tr."Wins"      AS tgt_wins,
                          tr."Losses"    AS tgt_losses,
                          tr."Draws"     AS tgt_draws,
                          tr."RatingDeviation"            AS tgt_rd,
                          tr."IsInPlacement"              AS tgt_inp,
                          tr."PlacementMatchesRemaining"  AS tgt_pmr,
                          tr."LastMatchAt"                AS tgt_lma
            )
            UPDATE gamekit.player_ranks AS sr
            SET "PlayerId" = {targetPlayerId},
                "Wins"   = sr."Wins"   + d.tgt_wins,
                "Losses" = sr."Losses" + d.tgt_losses,
                "Draws"  = sr."Draws"  + d.tgt_draws,
                "RatingDeviation" = GREATEST(sr."RatingDeviation", d.tgt_rd),
                "IsInPlacement" = (sr."IsInPlacement" AND d.tgt_inp),
                "PlacementMatchesRemaining" = CASE
                    WHEN sr."IsInPlacement" AND d.tgt_inp
                    THEN GREATEST(sr."PlacementMatchesRemaining", d.tgt_pmr)
                    ELSE 0
                    END,
                "LastMatchAt" = GREATEST(sr."LastMatchAt", d.tgt_lma)
            FROM deleted_tgt d
            WHERE sr."Id" = d.src_id
            """,
            ct)
            .ConfigureAwait(false);

        // Pass 2a: source.Rating <= target.Rating — merge source stats into target row.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            UPDATE gamekit.player_ranks AS tr
            SET "Wins"   = tr."Wins"   + sr."Wins",
                "Losses" = tr."Losses" + sr."Losses",
                "Draws"  = tr."Draws"  + sr."Draws",
                "RatingDeviation" = GREATEST(tr."RatingDeviation", sr."RatingDeviation"),
                "IsInPlacement" = (tr."IsInPlacement" AND sr."IsInPlacement"),
                "PlacementMatchesRemaining" = CASE
                    WHEN tr."IsInPlacement" AND sr."IsInPlacement"
                    THEN GREATEST(tr."PlacementMatchesRemaining", sr."PlacementMatchesRemaining")
                    ELSE 0
                    END,
                "LastMatchAt" = GREATEST(tr."LastMatchAt", sr."LastMatchAt")
            FROM gamekit.player_ranks AS sr
            WHERE tr."PlayerId" = {targetPlayerId}
              AND sr."PlayerId" = {sourcePlayerId}
              AND tr."LadderId" = sr."LadderId"
              AND sr."Rating"   <= tr."Rating"
            """,
            ct)
            .ConfigureAwait(false);

        // Pass 2b: delete source rows for ladders where target won the Rating comparison.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM gamekit.player_ranks
            WHERE "PlayerId" = {sourcePlayerId}
              AND "LadderId" IN (
                SELECT sr."LadderId"
                FROM gamekit.player_ranks sr
                JOIN gamekit.player_ranks tr ON sr."LadderId" = tr."LadderId"
                WHERE sr."PlayerId" = {sourcePlayerId}
                  AND tr."PlayerId" = {targetPlayerId}
                  AND sr."Rating" <= tr."Rating"
              )
            """,
            ct)
            .ConfigureAwait(false);

        // Pass 3: re-point source-only rank rows (no conflicting target row) to target.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            UPDATE gamekit.player_ranks
            SET "PlayerId" = {targetPlayerId}
            WHERE "PlayerId" = {sourcePlayerId}
            """,
            ct)
            .ConfigureAwait(false);

        // ── STEP 10: PENDING_RATING_UPDATES + SEASON_RANK_ARCHIVE ───────────────────────────
        // Uses raw SQL — GameKit.Auth does not reference GameKit.Rankings.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            UPDATE gamekit.pending_rating_updates
            SET "PlayerId" = {targetPlayerId}
            WHERE "PlayerId" = {sourcePlayerId}
            """,
            ct)
            .ConfigureAwait(false);

        // CR-03: season_rank_archive has no UNIQUE(PlayerId, SeasonId, LadderId) constraint.
        // EndSeasonService writes one archive row per player per (SeasonId, LadderId). If both
        // players competed in the same season+ladder, a blind re-point would produce duplicate
        // rows for the target (leaderboard queries would show the target twice — silent data
        // corruption). Resolution: keep the higher-rated row for conflicting (SeasonId, LadderId)
        // pairs, mirroring the player_ranks conflict-resolution strategy.
        //
        // Pass A: for (SeasonId, LadderId) pairs where source.Rating > target.Rating, delete
        //         the target row and re-point the source row to the target player in one CTE.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            WITH deleted_tgt AS (
                DELETE FROM gamekit.season_rank_archive AS ta
                USING gamekit.season_rank_archive AS sa
                WHERE sa."PlayerId" = {sourcePlayerId}
                  AND ta."PlayerId" = {targetPlayerId}
                  AND sa."SeasonId" = ta."SeasonId"
                  AND sa."LadderId" = ta."LadderId"
                  AND sa."Rating"   > ta."Rating"
                RETURNING ta."Id" AS deleted_id,
                          sa."Id" AS src_id
            )
            UPDATE gamekit.season_rank_archive AS sa
            SET "PlayerId" = {targetPlayerId}
            FROM deleted_tgt d
            WHERE sa."Id" = d.src_id
            """,
            ct)
            .ConfigureAwait(false);

        // Pass B: for (SeasonId, LadderId) pairs where source.Rating <= target.Rating,
        //         delete the source row (target row already has the higher or equal rating).
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM gamekit.season_rank_archive
            WHERE "PlayerId" = {sourcePlayerId}
              AND ("SeasonId", "LadderId") IN (
                SELECT sa."SeasonId", sa."LadderId"
                FROM gamekit.season_rank_archive sa
                JOIN gamekit.season_rank_archive ta
                  ON sa."SeasonId" = ta."SeasonId"
                 AND sa."LadderId" = ta."LadderId"
                WHERE sa."PlayerId" = {sourcePlayerId}
                  AND ta."PlayerId" = {targetPlayerId}
                  AND sa."Rating"   <= ta."Rating"
              )
            """,
            ct)
            .ConfigureAwait(false);

        // Pass C: re-point source-only rows (no conflicting target row) to target.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            UPDATE gamekit.season_rank_archive
            SET "PlayerId" = {targetPlayerId}
            WHERE "PlayerId" = {sourcePlayerId}
            """,
            ct)
            .ConfigureAwait(false);

        // ── STEP 11: PARTY_MEMBERS + PARTIES + DECLINE_HISTORY ──────────────────────────────
        // Uses raw SQL — GameKit.Auth does not reference GameKit.Matchmaking.
        // party_members: no same-party conflict at this point (checked in Step 3 above).
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            UPDATE gamekit.party_members
            SET "PlayerId" = {targetPlayerId}
            WHERE "PlayerId" = {sourcePlayerId}
            """,
            ct)
            .ConfigureAwait(false);

        // parties.owner_player_id: transfer ownership to target.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            UPDATE gamekit.parties
            SET "OwnerPlayerId" = {targetPlayerId}
            WHERE "OwnerPlayerId" = {sourcePlayerId}
            """,
            ct)
            .ConfigureAwait(false);

        // decline_history: analytics only, no unique constraint.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            UPDATE gamekit.decline_history
            SET "PlayerId" = {targetPlayerId}
            WHERE "PlayerId" = {sourcePlayerId}
            """,
            ct)
            .ConfigureAwait(false);

        // ── STEP 11b: LOBBY_MEMBERS ───────────────────────────────────────────────────────────
        // Uses raw SQL — GameKit.Auth does not reference GameKit.Lobby (adding the reverse
        // reference would create a circular dependency; Lobby references Core, not Auth).
        //
        // lobby_members has UNIQUE(LobbyId, PlayerId). If both source and target are already
        // members of the same lobby, a blind re-point of "PlayerId" would violate that constraint.
        // Resolution: dedup-then-repoint — identical to the player_credentials precedent (Step 6).
        //
        // Lobby membership is ephemeral state (the lobby hub routes events; there is no long-lived
        // audit or rating implication). The correct resolution is therefore:
        //   1. DELETE the source's duplicate row when the target is already in that lobby.
        //   2. UPDATE the source's remaining (source-only) rows to point at the target.
        //
        // This differs from party_members (Step 11) which aborts on same-party conflict (Step 3)
        // because parties carry matchmaking implications. Lobby membership is ephemeral and has no
        // audit purpose, so dedup-then-repoint is appropriate.
        //
        // Pass 1: DELETE source lobby_members rows for any lobby where the target is already a member.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM gamekit.lobby_members
            WHERE "PlayerId" = {sourcePlayerId}
              AND "LobbyId" IN (
                SELECT "LobbyId" FROM gamekit.lobby_members
                WHERE "PlayerId" = {targetPlayerId}
              )
            """,
            ct)
            .ConfigureAwait(false);

        // Pass 2: UPDATE remaining source lobby_members rows (source-only lobbies) to the target.
        await _ctx.Database.ExecuteSqlAsync(
            $"""
            UPDATE gamekit.lobby_members
            SET "PlayerId" = {targetPlayerId}
            WHERE "PlayerId" = {sourcePlayerId}
            """,
            ct)
            .ConfigureAwait(false);

        // ── STEP 12: ADMIN_AUDIT_LOG actor_id RE-POINT ──────────────────────────────────────
        // Re-point historical audit rows authored by the source player to the target player.
        // Re-homes historical admin_audit_log rows whose actor_id still points to the source player.
        // After tombstoning, any future query for the source player's audit history should resolve
        // to the target player's actor_id instead (e.g. for GDPR export requests).
        await _ctx.Set<AdminAuditLog>()
            .Where(a => a.ActorId == (Guid?)sourcePlayerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(a => a.ActorId, (Guid?)targetPlayerId),
                ct)
            .ConfigureAwait(false);

        // ── STEP 13: TOMBSTONE SOURCE PLAYER ────────────────────────────────────────────────
        source.MergedIntoPlayerId = targetPlayerId;
        source.DeletedAt = now;
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        // ── STEP 14: WRITE AUDIT ROW ─────────────────────────────────────────────────────────
        // Written directly via _ctx.Set<AdminAuditLog>() (EndSeasonService precedent, D-22).
        // AdminAuditLog is a Core entity accessible to Auth with no additional dependency.
        //
        // TargetId = target (NEVER source — SC#5 / T-10-03-04).
        // Audit row written EXACTLY ONCE: only on the Pending→Committed path (SC#1, T-10-03-03).
        var identityCountAfter = await _ctx.Set<PlayerIdentity>()
            .CountAsync(pi => pi.PlayerId == targetPlayerId, ct)
            .ConfigureAwait(false);

        _ctx.Set<AdminAuditLog>().Add(new AdminAuditLog
        {
            Id = _ids.NewId(),
            ActorId = actorId,
            Action = AccountMergeAction,
            TargetType = "player",
            TargetId = targetPlayerId, // NEVER source — SC#5
            Before = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                source_player_id = sourcePlayerId,
                source_display_name = source.DisplayName,
                source_is_banned = source.IsBanned,
                source_created_at = source.CreatedAt,
                target_player_id = targetPlayerId,
                target_display_name = target.DisplayName,
            })),
            After = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                target_player_id = targetPlayerId,
                identities_total_after = identityCountAfter,
                ranks_conflict_resolved = ranksMerged,
                tokens_revoked = true,
                source_tombstoned = true,
            })),
            Reason = null,
            CreatedAt = now,
        });
        await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        // ── STEP 15: ADVANCE ACCOUNT_MERGES STATUS TO COMMITTED ─────────────────────────────
        await _ctx.Set<AccountMerge>()
            .Where(am => am.Id == mergeRowId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(am => am.Status, MergeStatus.Committed)
                .SetProperty(am => am.CommittedAt, now),
                ct)
            .ConfigureAwait(false);

        return mergeRowId;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // REDIS CLEANUP (OUTSIDE SERIALIZABLE TX — checkpoint redis_cleaned)
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private async Task RunRedisCleanupAsync(
        Guid mergeRowId,
        Guid sourcePlayerId,
        CancellationToken ct)
    {
        // Remove stale presence keys for the source player from Redis.
        // If no IConnectionMultiplexer is available, degrade gracefully:
        // - Source tokens are already revoked, so no phantom proposal can be accepted (Pitfall 7).
        // - Sorted-set and string entries TTL-expire naturally.
        if (_redis is not null)
        {
            try
            {
                var db = _redis.GetDatabase();

                // Remove the source player's presence key if it exists.
                // Key format mirrors PresenceRedisKeys.Player in GameKit.Presence (see PresenceKeyPrefix).
                await db.KeyDeleteAsync($"{PresenceKeyPrefix}{sourcePlayerId}").ConfigureAwait(false);

                _logger?.LogDebug(
                    "AccountMergeService: Redis cleanup completed for source player {SourcePlayerId}.",
                    sourcePlayerId);
            }
            catch (Exception ex)
            {
                // Redis cleanup failure is non-fatal — the DB is fully consistent. Log and proceed.
                // The redis_cleaned checkpoint is still recorded so a re-entry does not retry Redis.
                _logger?.LogWarning(ex,
                    "AccountMergeService: Redis cleanup failed for source player {SourcePlayerId}. " +
                    "Keys will TTL-expire naturally.",
                    sourcePlayerId);
            }
        }

        // Advance to RedisCleaned regardless of whether Redis cleanup succeeded.
        var redisCleanedAt = _clock.UtcNow;
        await _ctx.Set<AccountMerge>()
            .Where(am => am.Id == mergeRowId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(am => am.Status, MergeStatus.RedisCleaned)
                .SetProperty(am => am.RedisCleanedAt, (DateTimeOffset?)redisCleanedAt),
                ct)
            .ConfigureAwait(false);
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks an exception's InnerException chain (bounded to a small depth) looking for a
    /// <see cref="PostgresException"/>. Needed because Npgsql's default execution strategy wraps
    /// transient failures (incl. 40001 serialization_failure) in
    /// <see cref="InvalidOperationException"/>, and EF Core further wraps the underlying
    /// provider exception in <see cref="DbUpdateException"/>. A plain
    /// <c>when (ex.InnerException is PostgresException pg)</c> pattern misses both wrappings.
    /// </summary>
    /// <remarks>Copied verbatim from <c>IdentityLinker.cs</c>.</remarks>
    private static PostgresException? TryFindPostgresException(Exception? ex)
    {
        for (var i = 0; i < 8 && ex is not null; i++)
        {
            if (ex is PostgresException pg) return pg;
            ex = ex.InnerException;
        }
        return null;
    }
}
