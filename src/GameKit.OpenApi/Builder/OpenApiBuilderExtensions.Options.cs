// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.OpenApi.Builder;

/// <summary>
/// Partial-class slot for future options-shaping helpers (named options, PostConfigure
/// callbacks, document-name aliases). Empty in v1 — the GameKit OpenAPI options surface
/// is small (DocumentName + Title + MountPath) so all wiring lives in the base file
/// <c>OpenApiBuilderExtensions.cs</c>. Reserved here per the Presence partial-split
/// convention (PATTERNS Block 5) so a v2 multi-document or environment-aware override
/// helper has a natural home without forcing a base-file rewrite.
/// </summary>
public static partial class OpenApiBuilderExtensions
{
}
