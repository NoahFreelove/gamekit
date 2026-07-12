// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/players/{id}/unban</c>. Reason is optional (an unban
/// does not require justification the way a ban does per D-09).
/// </summary>
/// <param name="Reason">Optional free-text reason for unbanning (e.g. "appealed successfully").</param>
public sealed record UnbanPlayerRequest(string? Reason);
