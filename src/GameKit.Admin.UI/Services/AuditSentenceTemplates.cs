// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Read-time sentence-template registry for the Phase 03.1 audit page (D-12 / D-13). Maps
/// each <see cref="AdminAuditActions"/> namespace constant to a function that builds a
/// <see cref="SentenceModel"/> from the audit row's stored fields. Unknown actions fall
/// through to the generic D-14 fallback so the UI never shows a blank cell.
/// </summary>
/// <remarks>
/// <para>
/// The registry is computed in-process; no database call. Sentences are recomputed on every
/// <c>GET /admin/api/audit</c> request, which lets template improvements apply retroactively
/// to historical rows without a backfill. The <c>admin_audit_log</c> schema is unchanged.
/// </para>
/// <para>
/// Seven templates ship for the v1 known action namespaces (ban, unban, GDPR delete, rank
/// adjust, admin create, admin delete, signing-key rotate). The session.login.success and
/// session.login.failure namespaces fall through to the D-14 fallback by design — they are
/// read-only events whose default rendering is sufficient.
/// </para>
/// </remarks>
public static class AuditSentenceTemplates
{
    private static readonly IReadOnlyDictionary<string, Func<SentenceContext, SentenceModel>> Registry =
        new Dictionary<string, Func<SentenceContext, SentenceModel>>(StringComparer.Ordinal)
        {
            [AdminAuditActions.PlayerBan] = ctx =>
                new SentenceModel(ctx.ActorName, "banned", ctx.TargetName ?? "(unknown player)", null, ctx.Reason),

            [AdminAuditActions.PlayerUnban] = ctx =>
                new SentenceModel(ctx.ActorName, "unbanned", ctx.TargetName ?? "(unknown player)", null, ctx.Reason),

            [AdminAuditActions.PlayerGdprDelete] = ctx =>
                new SentenceModel(ctx.ActorName, "GDPR-deleted", ctx.TargetName ?? "(unknown player)", null, ctx.Reason),

            [AdminAuditActions.PlayerRankAdjust] = ctx =>
                new SentenceModel(
                    ctx.ActorName,
                    "adjusted rank for",
                    ctx.TargetName ?? "(unknown player)",
                    ExtractRatingDelta(ctx.Before, ctx.After),
                    ctx.Reason),

            [AdminAuditActions.AdminCreate] = ctx =>
                new SentenceModel(
                    ctx.ActorName,
                    "created admin",
                    ctx.TargetName ?? "(unknown admin)",
                    ExtractRoleAfter(ctx.After),
                    null),

            [AdminAuditActions.AdminDelete] = ctx =>
                new SentenceModel(ctx.ActorName, "deleted admin", ctx.TargetName ?? "(unknown admin)", null, null),

            [AdminAuditActions.SigningKeyRotate] = ctx =>
                new SentenceModel(ctx.ActorName, "rotated JWT signing key", ctx.TargetName ?? "current key", null, ctx.Reason),
        };

    /// <summary>
    /// Builds a sentence model for the given audit row context, falling through to the
    /// generic D-14 fallback when no template is registered for <see cref="SentenceContext.Action"/>.
    /// </summary>
    /// <param name="ctx">Bundle of the resolved actor/target display names + raw row fields.</param>
    /// <returns>A <see cref="SentenceModel"/> ready to be serialized to the wire.</returns>
    public static SentenceModel Render(SentenceContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Registry.TryGetValue(ctx.Action, out var fn) ? fn(ctx) : Fallback(ctx);
    }

    /// <summary>
    /// D-14 generic fallback. Operator never sees a blank cell — unmapped actions render as
    /// <c>{Actor} performed {action with dots replaced by spaces} on {Target} — {Reason}</c>.
    /// </summary>
    private static SentenceModel Fallback(SentenceContext ctx)
        => new(
            Actor: ctx.ActorName,
            Intro: "performed",
            Target: ctx.Action.Replace('.', ' '),
            Modifier: ctx.TargetName is null ? null : $"on {ctx.TargetName}",
            Reason: ctx.Reason);

    private static string? ExtractRatingDelta(JsonElement? before, JsonElement? after)
    {
        // Best-effort: when both Before and After contain a "rating" property, render the
        // signed delta. Returns null on any shape mismatch — the row still renders without
        // the modifier, never crashing the projection.
        try
        {
            if (before is null || after is null) return null;
            if (before.Value.ValueKind != JsonValueKind.Object || after.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (before.Value.TryGetProperty("rating", out var b) &&
                after.Value.TryGetProperty("rating", out var a) &&
                b.TryGetDouble(out var bv) &&
                a.TryGetDouble(out var av))
            {
                var delta = av - bv;
                var sign = delta >= 0 ? "+" : string.Empty;
                return $"({sign}{delta.ToString("F1", CultureInfo.InvariantCulture)})";
            }
        }
        catch (InvalidOperationException)
        {
            // Element kind mismatch — fall through to null modifier.
        }
        return null;
    }

    private static string? ExtractRoleAfter(JsonElement? after)
    {
        try
        {
            if (after is null) return null;
            if (after.Value.ValueKind != JsonValueKind.Object) return null;
            if (after.Value.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String)
            {
                return $"(as {r.GetString()})";
            }
        }
        catch (InvalidOperationException)
        {
            // Element kind mismatch — fall through to null modifier.
        }
        return null;
    }
}

/// <summary>
/// Bundle of the values an audit row contributes to its sentence projection. Built at the
/// projection site in <see cref="GameKit.Admin.UI.Http.AdminEndpoints"/> after display-name
/// resolution.
/// </summary>
/// <param name="Action">Stable action namespace from <see cref="AdminAuditActions"/>.</param>
/// <param name="ActorName">Resolved actor display name ("system" for automated rows).</param>
/// <param name="TargetName">Resolved target display name; null when no target exists.</param>
/// <param name="Before">Audit row Before JSON (jsonb-backed); null when not applicable.</param>
/// <param name="After">Audit row After JSON (jsonb-backed); null when not applicable.</param>
/// <param name="Reason">Audit row Reason free-text; null when not provided.</param>
public sealed record SentenceContext(
    string Action,
    string ActorName,
    string? TargetName,
    JsonElement? Before,
    JsonElement? After,
    string? Reason);
