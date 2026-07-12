// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Builder;

/// <summary>Default <see cref="IGameKitBuilder"/> implementation.</summary>
internal sealed class GameKitBuilder : IGameKitBuilder
{
    /// <summary>Constructs the builder.</summary>
    public GameKitBuilder(IServiceCollection services, GameKitOptions options)
    {
        Services = services;
        Options = options;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public GameKitOptions Options { get; }
}
