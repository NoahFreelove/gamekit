// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Builder;

/// <summary>
/// Fluent builder returned from <c>services.AddGameKit(...)</c>. Sibling packages extend it with
/// <c>.AddAuth(...)</c>, <c>.AddRankings(...)</c>, etc. Keeping the builder open-typed lets
/// customer apps mount only the packages they install.
/// </summary>
public interface IGameKitBuilder
{
    /// <summary>The underlying <see cref="IServiceCollection"/> — siblings register their services here.</summary>
    IServiceCollection Services { get; }

    /// <summary>The <see cref="GameKitOptions"/> configured at <c>AddGameKit</c> time.</summary>
    GameKitOptions Options { get; }
}
