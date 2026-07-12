// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Fluent builder returned from <c>services.AddGameKit().AddRankings(...)</c>. Sibling concern-specific
/// plan files (04-05 session-complete, 04-06 ticker, 04-07 season, 04-08 GDPR) extend it with partial
/// <c>RankingsBuilderExtensions</c> files.
/// </summary>
/// <remarks>
/// The builder accumulates <see cref="LadderConfig"/> instances at build time (registered via
/// <see cref="AddLadder(string, Action{LadderConfig}?)"/>). At startup,
/// <see cref="GameKit.Rankings.Services.StartupLadderUpserter"/> reads
/// <see cref="RegisteredLadders"/> to upsert each config into the <c>ladders</c> table (D-21).
/// </remarks>
public interface IGameKitRankingsBuilder
{
    /// <summary>The underlying <see cref="IServiceCollection"/> for further service registration.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers a named ladder at build time. The ladder row is upserted into the <c>ladders</c>
    /// table by <c>StartupLadderUpserter</c> when the host starts.
    /// </summary>
    /// <param name="name">Ladder name. Must be non-empty and not previously registered (case-insensitive guard).</param>
    /// <param name="configure">Optional callback to override defaults on the new <see cref="LadderConfig"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty or already registered.</exception>
    IGameKitRankingsBuilder AddLadder(string name, Action<LadderConfig>? configure = null);

    /// <summary>
    /// All <see cref="LadderConfig"/> instances registered so far. Used internally by
    /// <c>StartupLadderUpserter</c> — not part of the stable public surface.
    /// </summary>
    IReadOnlyList<LadderConfig> RegisteredLadders { get; }
}
