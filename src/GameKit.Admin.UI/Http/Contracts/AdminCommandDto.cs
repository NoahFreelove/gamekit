// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Wire-format projection of <see cref="GameKit.Admin.UI.Services.AdminCommand"/> returned
/// by <c>GET /admin/api/commands</c> to the Phase 03.1 command-palette JS layer. Excludes
/// <c>RequiresSuperadmin</c> from the JSON because the server has already filtered rows by
/// role — admin operators must not learn that superadmin-only rows exist (D-11; never
/// grayed, always absent).
/// </summary>
/// <param name="Id">Stable command id (e.g. <c>"ban"</c>, <c>"nav.dashboard"</c>).</param>
/// <param name="Label">Operator-facing label rendered in the palette row.</param>
/// <param name="Category">Group key for the section header (<c>actions</c> / <c>admin</c> / <c>system</c> / <c>session</c> / <c>nav</c>).</param>
/// <param name="RequiresTarget">When <c>true</c>, the palette swaps into the target-search subview (D-10) before dispatching the action.</param>
public sealed record AdminCommandDto(
    string Id,
    string Label,
    string Category,
    bool RequiresTarget);
