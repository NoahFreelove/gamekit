// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Wire-format projection of an audit-row sentence (D-12). Rendered server-side at read time
/// and serialized into the <c>GET /admin/api/audit</c> AuditRow response. The Razor row
/// template renders the model into the audit page's left column ("alice banned bob — spam").
/// </summary>
/// <remarks>
/// Storage is unchanged (D-13) — the <c>admin_audit_log</c> schema does not gain a sentence
/// column; the model is recomputed on every request. Template improvements therefore apply
/// retroactively to historical rows without a backfill.
/// </remarks>
/// <param name="Actor">Resolved display name of the admin who performed the action ("system" for automated rows).</param>
/// <param name="Intro">Past-tense verb phrase describing the action (e.g. "banned", "GDPR-deleted").</param>
/// <param name="Target">Resolved display name of the affected entity (player or admin), or fallback string when no target exists.</param>
/// <param name="Modifier">Optional inline qualifier (e.g. "(promoted to superadmin)" for create-admin); rendered after Target with leading space.</param>
/// <param name="Reason">Optional ban / GDPR / rank-adjust reason; rendered after the sentence with " — " separator and truncated at ~60 chars in the UI.</param>
public sealed record SentenceModel(
    string Actor,
    string Intro,
    string Target,
    string? Modifier,
    string? Reason);
