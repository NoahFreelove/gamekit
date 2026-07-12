// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.OpenApi.Configuration;
using GameKit.OpenApi.Internal;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace GameKit.OpenApi.Transformers;

/// <summary>
/// <see cref="IOpenApiDocumentTransformer"/> that populates
/// <c>document.Info.Title</c> from <see cref="GameKitOpenApiOptions.Title"/>
/// and <c>document.Info.Version</c> from the MinVer-derived
/// <see cref="GameKitMarker.GameKitVersion"/> const emitted at compile time
/// by the <c>GameKit.Build</c> source generator (D-10 / Plan 06-01).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GameKitMarker"/> lives in <c>GameKit.OpenApi.Internal</c> — it is
/// emitted into this same assembly by the source generator so the transformer
/// can read it via a plain <c>using GameKit.OpenApi.Internal;</c> import.
/// No reflection across assemblies.
/// </para>
/// </remarks>
internal sealed class GameKitInfoTransformer : IOpenApiDocumentTransformer
{
    private readonly IOptions<GameKitOpenApiOptions> _options;

    /// <summary>Creates the transformer with the DI-resolved options.</summary>
    /// <param name="options">The options snapshot bound by <c>AddGameKitOpenApi</c>.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="options"/> is null.</exception>
    public GameKitInfoTransformer(IOptions<GameKitOpenApiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var opts = _options.Value;
        document.Info ??= new OpenApiInfo();
        document.Info.Title   = opts.Title;
        document.Info.Version = GameKitMarker.GameKitVersion;
        return Task.CompletedTask;
    }
}
