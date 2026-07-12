// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.OpenApi.Configuration;
using GameKit.OpenApi.Transformers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameKit.OpenApi.Builder;

/// <summary>
/// Fluent-builder extensions that wire <c>GameKit.OpenApi</c> into a consumer's
/// <see cref="IServiceCollection"/>. Declared <see langword="partial"/> so future
/// option-shaping helpers can land in a sibling <c>.Options.cs</c> partial without
/// modifying this base file — mirrors the Presence partial-split convention
/// (PATTERNS Block 5).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AddGameKitOpenApi"/> is an <see cref="IServiceCollection"/> extension
/// (not an <c>IGameKitBuilder</c> extension) because OpenAPI registration is orthogonal
/// to GameKit's per-package builder chain — a consumer may choose to opt in to OpenAPI
/// generation without touching their <c>AddGameKit()</c> + <c>AddAuth()</c> + … chain.
/// </para>
/// </remarks>
public static partial class OpenApiBuilderExtensions
{
    /// <summary>
    /// Registers the GameKit OpenAPI subsystem: options POCO + two document
    /// transformers (<c>GameKitInfoTransformer</c>, <c>GameKitBearerSchemeTransformer</c>) +
    /// the inline <c>ShouldInclude</c> lambda that filters out admin endpoints
    /// (D-19; PATTERNS Critical Misuse Warning #1).
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="configure">
    /// Optional callback to populate <see cref="GameKitOpenApiOptions"/>. If not
    /// supplied, defaults are used (<c>DocumentName="v1"</c>, <c>Title="GameKit API"</c>,
    /// <c>MountPath="/openapi"</c>) producing <c>/openapi/v1.json</c>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Admin filter (D-19 verbatim):</b> the admin-route exclusion is wired as
    /// an inline <see cref="OpenApiOptions.ShouldInclude"/> lambda, NOT a separate
    /// <see cref="IOpenApiOperationTransformer"/>. Operation transformers cannot
    /// remove paths from the document — only decorate existing ones (RESEARCH §Pattern
    /// 3 + §Pitfall 4). The filter literal is <c>"admin"</c> with NO trailing slash
    /// so the bare <c>/admin</c> Blazor console root is also caught.
    /// </para>
    /// <para>
    /// <b>Document-name collisions (T-06-06-03):</b> the underlying
    /// <c>Microsoft.AspNetCore.OpenApi</c> document is keyed by
    /// <see cref="GameKitOpenApiOptions.DocumentName"/>. A consumer who registers
    /// their own <c>AddOpenApi("v1", …)</c> will collide with the GameKit defaults;
    /// pass <c>opts =&gt; opts.DocumentName = "gamekit"</c> (or a custom value) to
    /// avoid the collision.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddGameKitOpenApi(
        this IServiceCollection services,
        Action<GameKitOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 1. Bind the options POCO and register as a Singleton so MapGameKitOpenApi can
        //    resolve DocumentName / MountPath at endpoint-mapping time without a scope.
        var opts = new GameKitOpenApiOptions();
        configure?.Invoke(opts);
        services.TryAddSingleton(opts);

        // 2. Bind via the standard IOptions pipeline as well, so GameKitInfoTransformer can
        //    take an IOptions<GameKitOpenApiOptions> dependency the canonical way.
        var optsBuilder = services.AddOptions<GameKitOpenApiOptions>();
        if (configure is not null)
        {
            optsBuilder.Configure(configure);
        }

        // 3. Register transformers as Singleton — Microsoft.AspNetCore.OpenApi resolves
        //    them via DI when AddDocumentTransformer<T>() is invoked.
        services.TryAddSingleton<GameKitInfoTransformer>();
        services.TryAddSingleton<GameKitBearerSchemeTransformer>();

        // 4. Wire AddOpenApi with the inline ShouldInclude admin-filter lambda + the two
        //    document transformers. D-19 verbatim: StartsWith("admin", OrdinalIgnoreCase)
        //    NO trailing slash so the bare /admin route is also filtered.
        services.AddOpenApi(opts.DocumentName, o =>
        {
            o.ShouldInclude = static description =>
                !(description.RelativePath ?? string.Empty)
                    .StartsWith("admin", StringComparison.OrdinalIgnoreCase);

            o.AddDocumentTransformer<GameKitInfoTransformer>();
            o.AddDocumentTransformer<GameKitBearerSchemeTransformer>();
        });

        return services;
    }
}
