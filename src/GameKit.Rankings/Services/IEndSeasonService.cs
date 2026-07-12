// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Rankings.Entities;

namespace GameKit.Rankings.Services;

/// <summary>
/// Admin-triggered seasonal reset for a ladder (RANK-10 / D-11).
/// Runs a SERIALIZABLE transaction that atomically:
/// <list type="number">
///   <item>Closes the current <c>ladder_seasons</c> row (sets <c>EndedAt</c> + <c>EndedByAdminId</c>).</item>
///   <item>Opens a new <c>ladder_seasons</c> row for the next season.</item>
///   <item>Archives every <c>player_ranks</c> row for the ladder into <c>season_rank_archive</c>.</item>
///   <item>Applies the configured <see cref="SeasonResetPolicy"/> to the live <c>player_ranks</c> rows.</item>
///   <item>Writes an <c>admin.ladder.end_season</c> audit row via <c>IAdminAuditWriter</c>.</item>
/// </list>
/// </summary>
public interface IEndSeasonService
{
    /// <summary>
    /// Ends the current season for the specified ladder.
    /// </summary>
    /// <param name="ladderId">The ladder whose season should be ended.</param>
    /// <param name="actorId">The acting admin user id (written to the audit row and the closed <c>ladder_seasons</c> row).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result record summarising the closed season, new season, and policy applied.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// Thrown when the ladder does not exist or has no current open season.
    /// </exception>
    Task<EndSeasonResult> EndAsync(Guid ladderId, Guid actorId, CancellationToken ct);
}

/// <summary>
/// Result of a successful <see cref="IEndSeasonService.EndAsync"/> call (D-11 / D-14).
/// </summary>
/// <param name="ClosedSeasonId">Id of the season row that was just closed.</param>
/// <param name="ClosedSeasonNumber">Season number of the closed season.</param>
/// <param name="OpenedSeasonId">Id of the newly-opened season row.</param>
/// <param name="NewSeasonNumber">Season number of the new season.</param>
/// <param name="ArchivedRowCount">Number of <c>player_ranks</c> rows that were archived.</param>
/// <param name="AppliedPolicy">The reset policy that was applied to the live <c>player_ranks</c>.</param>
public sealed record EndSeasonResult(
    Guid ClosedSeasonId,
    int ClosedSeasonNumber,
    Guid OpenedSeasonId,
    int NewSeasonNumber,
    int ArchivedRowCount,
    SeasonResetPolicy AppliedPolicy);
