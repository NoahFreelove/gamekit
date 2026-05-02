// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Stable, in-process command registry consumed by the Phase 03.1 verb-engine command
/// palette (Cmd+K). Each <see cref="AdminCommand"/> is a verb (action or nav target) the
/// operator can invoke from the palette overlay. The registry is a pure constants table —
/// no DI registration; consumed via the static <see cref="AllCommands"/> property.
/// </summary>
/// <remarks>
/// <para>
/// Per CONTEXT D-09 the v1 action set is 8 verbs: ban / unban / gdpr-delete / create-admin /
/// delete-admin / rank-adjust / rotate-signing-key / sign-out. The 9 nav rows enumerate the
/// existing <c>/admin/*</c> Blazor pages.
/// </para>
/// <para>
/// Per CONTEXT D-11 the server-side endpoint <c>GET /admin/api/commands</c> filters out
/// rows whose <see cref="AdminCommand.RequiresSuperadmin"/> is <c>true</c> when the
/// requesting operator is not a superadmin — never grayed, always absent. The palette JS
/// renders rows verbatim from the response.
/// </para>
/// </remarks>
public static class AdminCommandRegistry
{
    /// <summary>
    /// All commands registered in v1. Order is the canonical render order; the palette JS
    /// preserves it. 8 actions + 9 nav rows = 17 entries total.
    /// </summary>
    public static IReadOnlyList<AdminCommand> AllCommands { get; } = new List<AdminCommand>
    {
        // Actions ------------------------------------------------------------
        new("ban",                "Ban player",            "actions", RequiresSuperadmin: false, RequiresTarget: true),
        new("unban",              "Unban player",          "actions", RequiresSuperadmin: false, RequiresTarget: true),
        new("gdpr-delete",        "GDPR-delete player",    "actions", RequiresSuperadmin: true,  RequiresTarget: true),
        new("rank-adjust",        "Adjust player rank",    "actions", RequiresSuperadmin: true,  RequiresTarget: true),
        // Admin management ---------------------------------------------------
        new("create-admin",       "Create admin",          "admin",   RequiresSuperadmin: true,  RequiresTarget: false),
        new("delete-admin",       "Delete admin",          "admin",   RequiresSuperadmin: true,  RequiresTarget: true),
        // System -------------------------------------------------------------
        new("rotate-signing-key", "Rotate JWT signing key","system",  RequiresSuperadmin: true,  RequiresTarget: false),
        // Session ------------------------------------------------------------
        new("sign-out",           "Sign out",              "session", RequiresSuperadmin: false, RequiresTarget: false),

        // Navigation rows (port from SideNav.razor:13-26) --------------------
        // nav.player-detail removed (REVIEW-04): meaningless without a target; had RequiresTarget: false
        // but dispatching it as a nav row makes no sense — deleted in gap-closure plan 03.1-10.
        new("nav.dashboard",   "Go to Dashboard",   "nav", RequiresSuperadmin: false, RequiresTarget: false, Url: "/admin"),
        new("nav.players",     "Go to Players",     "nav", RequiresSuperadmin: false, RequiresTarget: false, Url: "/admin/players"),
        new("nav.matches",     "Go to Match history","nav", RequiresSuperadmin: false, RequiresTarget: false, Url: "/admin/matches"),
        new("nav.audit",       "Go to Audit log",   "nav", RequiresSuperadmin: false, RequiresTarget: false, Url: "/admin/audit"),
        new("nav.health",      "Go to Health",      "nav", RequiresSuperadmin: false, RequiresTarget: false, Url: "/admin/health"),
        new("nav.matchmaking", "Go to Queue depth", "nav", RequiresSuperadmin: false, RequiresTarget: false, Url: "/admin/matchmaking"),
        new("nav.rank-adjust", "Go to Rank adjust", "nav", RequiresSuperadmin: true,  RequiresTarget: false, Url: "/admin/rank-adjust"),
        new("nav.admins",      "Go to Admins",      "nav", RequiresSuperadmin: true,  RequiresTarget: false, Url: "/admin/admins"),
        new("nav.login",       "Go to Login",       "nav", RequiresSuperadmin: false, RequiresTarget: false, Url: "/admin/login"),
    };
}

/// <summary>
/// A single verb in the command palette. Positional record so the registry literal stays
/// terse. Serialized via <see cref="GameKit.Admin.UI.Http.Contracts.AdminCommandDto"/> on
/// the wire to keep the public DTO shape decoupled from the in-process record.
/// </summary>
/// <param name="Id">Stable command id ("ban", "nav.dashboard", etc.). Used by JS to dispatch.</param>
/// <param name="Label">Operator-facing label rendered in the palette row.</param>
/// <param name="Category">Group key ("actions", "admin", "system", "session", "nav") for section headers in the palette.</param>
/// <param name="RequiresSuperadmin">When true, the row is filtered out of the GET /admin/api/commands response for non-superadmin operators (D-11).</param>
/// <param name="RequiresTarget">When true, selecting the row in the palette switches to the two-step target-search subview (D-10) before launching the matching dialog.</param>
/// <param name="Url">When non-null, selecting the row navigates the browser to this absolute /admin/* path; used exclusively by nav.* rows. Action rows leave this null and dispatch through MainLayout.OpenDialog instead.</param>
public sealed record AdminCommand(
    string Id,
    string Label,
    string Category,
    bool RequiresSuperadmin,
    bool RequiresTarget,
    string? Url = null);
