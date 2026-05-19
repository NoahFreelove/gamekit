// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Rankings.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/ladders/{id}/end-season</c> (D-11).
/// The operator must type the exact ladder name to confirm the irreversible action.
/// </summary>
/// <param name="ConfirmLadderName">
/// The operator's typed ladder name. The endpoint compares this to <c>Ladder.Name</c>
/// (case-sensitive) and returns <c>400 confirm_name_mismatch</c> when they do not match.
/// </param>
public sealed record EndSeasonRequest(string ConfirmLadderName);
