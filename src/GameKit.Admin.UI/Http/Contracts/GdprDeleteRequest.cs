// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/players/{id}/gdpr-delete</c> (superadmin-only).
/// Requires confirmation of the target player's display name / username as a double-check
/// against misclicks on the admin UI's list view.
/// </summary>
/// <param name="ConfirmUsername">The target player's current display name — MUST equal the
/// server-side value or the service rejects. Defense-in-depth against wrong-target deletes.</param>
/// <param name="Reason">Optional GDPR-context reason (defaults to <c>"gdpr_request"</c> on the endpoint).</param>
public sealed record GdprDeleteRequest(string ConfirmUsername, string? Reason);
