// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Fluent builder returned from <c>services.AddGameKit().AddMatchmaking(...)</c>. Sibling
/// concern-specific plan files (05-04 strategy, 05-05 ticker, etc.) extend this with partial
/// <c>MatchmakingBuilderExtensions</c> files.
/// </summary>
/// <remarks>
/// The builder accumulates <see cref="MatchmakingLadderConfig"/> instances at build time
/// (registered via <see cref="AddLadder(string, Action{MatchmakingLadderConfig}?)"/>). The
/// list is also registered as a singleton <see cref="IReadOnlyList{T}"/> so downstream
/// matchmaker services can inject the per-ladder config tree directly.
/// </remarks>
public interface IGameKitMatchmakingBuilder
{
    /// <summary>The underlying <see cref="IServiceCollection"/> for further service registration.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers a named ladder at build time. The matchmaker reads per-ladder config from
    /// this collection at runtime (bracket curve, aggregator, spread cap).
    /// </summary>
    /// <param name="name">Ladder name. Must be non-empty and not previously registered (case-insensitive guard).</param>
    /// <param name="configure">Optional callback to override defaults on the new <see cref="MatchmakingLadderConfig"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentException">When <paramref name="name"/> is empty or invariants on <see cref="MatchmakingLadderConfig"/> are violated.</exception>
    /// <exception cref="InvalidOperationException">When a ladder with the same name (case-insensitive) is already registered.</exception>
    IGameKitMatchmakingBuilder AddLadder(string name, Action<MatchmakingLadderConfig>? configure = null);

    /// <summary>
    /// All <see cref="MatchmakingLadderConfig"/> instances registered so far. Read by the
    /// matchmaker services at runtime via DI as <c>IReadOnlyList&lt;MatchmakingLadderConfig&gt;</c>.
    /// </summary>
    IReadOnlyList<MatchmakingLadderConfig> RegisteredLadders { get; }
}
