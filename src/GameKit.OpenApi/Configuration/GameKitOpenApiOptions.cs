// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.OpenApi.Configuration;

/// <summary>
/// Configuration surface for the GameKit combined OpenAPI document. Consumers tune the
/// document name + mount path + title via <c>AddGameKitOpenApi(opts =&gt; ...)</c>.
/// Defaults are production-safe and match D-07 + D-22 verbatim.
/// </summary>
/// <remarks>
/// <para>
/// The defaults produce a single combined document at <c>/openapi/v1.json</c> titled
/// <c>"GameKit API"</c> covering every player-facing GameKit HTTP endpoint (auth,
/// sessions, matchmaking, parties, presence). Admin endpoints are excluded via the
/// inline <c>OpenApiOptions.ShouldInclude</c> lambda registered by
/// <c>AddGameKitOpenApi</c> (D-19; PATTERNS Critical Misuse Warning #1).
/// </para>
/// <para>
/// The document is regenerated per-request by default; Microsoft.AspNetCore.OpenApi
/// caches per <see cref="DocumentName"/> internally. Consumers who need build-time
/// generation can switch to <c>Microsoft.Extensions.ApiDescription.Server</c>
/// independently of this options surface (out of scope for Plan 06-06).
/// </para>
/// </remarks>
public sealed class GameKitOpenApiOptions
{
    /// <summary>
    /// OpenAPI document name passed to <c>AddOpenApi(name, ...)</c> and substituted
    /// into <see cref="MountPath"/> at <c>MapGameKitOpenApi</c> time. Defaults to
    /// <c>"v1"</c> producing <c>/openapi/v1.json</c>.
    /// </summary>
    public string DocumentName { get; set; } = "v1";

    /// <summary>
    /// Human-readable document title written into <c>document.Info.Title</c> by
    /// <c>GameKitInfoTransformer</c>. Defaults to <c>"GameKit API"</c>.
    /// </summary>
    public string Title { get; set; } = "GameKit API";

    /// <summary>
    /// HTTP route prefix under which the document is mounted. The
    /// <c>{documentName}.json</c> suffix is appended by <c>MapGameKitOpenApi</c>,
    /// so the default <c>"/openapi"</c> produces <c>/openapi/v1.json</c>.
    /// </summary>
    public string MountPath { get; set; } = "/openapi";
}
