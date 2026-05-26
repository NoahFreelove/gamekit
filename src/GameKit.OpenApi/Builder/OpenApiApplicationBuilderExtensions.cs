// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.OpenApi.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.OpenApi.Builder;

/// <summary>
/// Extension methods that mount the GameKit OpenAPI document into the ASP.NET Core
/// endpoint pipeline. Mirrors <c>PresenceApplicationBuilderExtensions</c>.
/// </summary>
public static class OpenApiApplicationBuilderExtensions
{
    /// <summary>
    /// Maps the GameKit OpenAPI document at <c>{MountPath}/{DocumentName}.json</c>
    /// (default <c>/openapi/v1.json</c>). Anonymous GET is allowed — the document
    /// describes only the public, player-facing surface (no admin endpoints; no PII).
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="routes"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// The pattern passed to <c>MapOpenApi</c> uses the literal <c>DocumentName</c>
    /// from <see cref="GameKitOpenApiOptions"/> (not the <c>{documentName}</c>
    /// route parameter form), so the mounted endpoint is deterministic and matches
    /// the AddOpenApi document name registered by <c>AddGameKitOpenApi</c>.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapGameKitOpenApi(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var opts = routes.ServiceProvider.GetRequiredService<GameKitOpenApiOptions>();
        // Strip any trailing slash from MountPath to avoid double-slash routes when
        // the consumer overrides MountPath = "/openapi/".
        var mount = opts.MountPath.TrimEnd('/');
        var pattern = $"{mount}/{opts.DocumentName}.json";

        routes.MapOpenApi(pattern);
        return routes;
    }
}
