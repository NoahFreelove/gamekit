// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/players/merge</c>. Both GUIDs are required and
/// validated by <see cref="Validators.MergePlayersRequestValidator"/> before the merge
/// transaction opens.
/// </summary>
/// <param name="SourcePlayerId">Player to absorb (will be soft-deleted after merge).</param>
/// <param name="TargetPlayerId">Player that survives the merge and inherits all foreign-key references.</param>
public sealed record MergePlayersRequest(System.Guid SourcePlayerId, System.Guid TargetPlayerId);

/// <summary>
/// HTTP response body for a successful or idempotent <c>POST /admin/api/players/merge</c>.
/// </summary>
/// <remarks>
/// <para>
/// CRITICAL (SC#5, T-10-04-03): This record intentionally does NOT include a
/// <c>SourcePlayerId</c> field. The source player id is never returned by the merge endpoint —
/// not in the success response, conflict response, or error response. Exposing the source id
/// after tombstoning it would leak a soft-deleted player identity to API consumers.
/// </para>
/// </remarks>
/// <param name="TargetPlayerId">The surviving player's id (the merge target).</param>
/// <param name="Status"><c>merged</c> for a newly completed merge; <c>already_merged</c> for an idempotent re-request.</param>
public sealed record MergePlayersResponse(System.Guid TargetPlayerId, string Status);
