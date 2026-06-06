// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
namespace GameKit.Admin.UI.Services;

/// <summary>
/// Admin audit-log action namespace constants (D-17). Every mutation through the admin
/// surface writes an <c>admin_audit_log</c> row whose <c>action</c> column matches one of
/// these literals.
/// </summary>
public static class AdminAuditActions
{
    /// <summary>A player was banned via the admin UI (reason required per D-09).</summary>
    public const string PlayerBan = "admin.player.ban";

    /// <summary>A player was unbanned via the admin UI.</summary>
    public const string PlayerUnban = "admin.player.unban";

    /// <summary>A player was GDPR-deleted through the admin UI (superadmin-only).</summary>
    public const string PlayerGdprDelete = "admin.player.gdpr_delete";

    /// <summary>A player's rating was manually adjusted (superadmin-only; Phase 4 surface).</summary>
    public const string PlayerRankAdjust = "admin.player.rank_adjust";

    /// <summary>A new admin user was created (superadmin-only).</summary>
    public const string AdminCreate = "admin.admin.create";

    /// <summary>An admin user was deleted (superadmin-only; blocked for last superadmin).</summary>
    public const string AdminDelete = "admin.admin.delete";

    /// <summary>JWT signing key was rotated (superadmin-only; Phase 2 operational surface).</summary>
    public const string SigningKeyRotate = "admin.signing_key.rotate";

    /// <summary>An admin successfully authenticated against <c>/admin/login</c>.</summary>
    public const string SessionLoginSuccess = "admin.session.login.success";

    /// <summary>An admin login attempt failed (wrong password, unknown user, or locked account).</summary>
    public const string SessionLoginFailure = "admin.session.login.failure";

    /// <summary>Audit action emitted when a superadmin ends a ladder season (RANK-10 / D-11 / plan 04-07).</summary>
    public const string LadderEndSeason = "admin.ladder.end_season";

    /// <summary>Audit action emitted when an operator exports a player's GDPR data bundle (RANK-13 / D-16 / plan 04-08).</summary>
    public const string PlayerGdprExport = "admin.player.gdpr_export";

    /// <summary>Audit action emitted when an operator pauses the matchmaking queue (MATCH-14 / Plan 05-08 / D-22 port).</summary>
    public const string MatchmakingPauseQueue = "admin.matchmaking.pause_queue";

    /// <summary>Audit action emitted when an operator drains the matchmaking queue (MATCH-14 / Plan 05-08 / D-22 port).</summary>
    public const string MatchmakingDrainQueue = "admin.matchmaking.drain_queue";

    /// <summary>Audit action emitted when the reconciler cancels an orphan game session (Plan 05-07 / D-22 port).</summary>
    /// <remarks>
    /// The matchmaking reconciler service writes this row with action == this constant; the
    /// literal is duplicated locally in <c>MatchmakingReconcilerService</c> to avoid a runtime
    /// dep on Admin.UI's registry. This constant exists so the audit-page sentence template
    /// has a matching key and so operators can reference it from the command palette.
    /// </remarks>
    public const string MatchmakingSessionOrphanCancelled = "admin.matchmaking.session_orphan_cancelled";

    /// <summary>
    /// Audit action emitted when a superadmin merges a source player into a target player (AUTH-23/SC#4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The merge service (<c>AccountMergeService</c> in <c>GameKit.Auth</c>) writes this row
    /// directly via <c>_ctx.Set&lt;AdminAuditLog&gt;()</c> — it cannot reference this constant
    /// because <c>GameKit.Admin.UI</c> already holds a <c>ProjectReference</c> to
    /// <c>GameKit.Auth</c> (adding the reverse reference would create a circular dependency).
    /// The literal <c>"auth.account_merge"</c> is duplicated in <c>AccountMergeService</c> as a
    /// private const with a comment pointing to this constant. The two values MUST stay in sync.
    /// </para>
    /// </remarks>
    public const string AccountMerge = "auth.account_merge";
}
