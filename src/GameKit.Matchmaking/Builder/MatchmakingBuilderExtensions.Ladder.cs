// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Partial-class extension methods registering per-ladder matchmaking configuration. Split
/// from <c>MatchmakingBuilderExtensions.cs</c> for readability — mirrors the Rankings
/// precedent (<c>RankingsBuilderExtensions.Ticker.cs</c> and friends).
/// </summary>
public static partial class MatchmakingBuilderExtensions
{
    /// <summary>
    /// Registers a named matchmaking ladder. Delegates to
    /// <see cref="IGameKitMatchmakingBuilder.AddLadder(string, Action{MatchmakingLadderConfig}?)"/>
    /// which enforces case-insensitive name dedup and per-ladder invariants at registration
    /// time (fail-fast at host startup).
    /// </summary>
    /// <param name="builder">The matchmaking builder returned from <c>AddMatchmaking(...)</c>.</param>
    /// <param name="name">Ladder name (case-insensitive JOIN KEY against the Rankings ladder of the same name).</param>
    /// <param name="configure">Optional callback to override defaults on the new <see cref="MatchmakingLadderConfig"/>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="name"/> is empty or invariants are violated.</exception>
    /// <exception cref="InvalidOperationException">When the ladder name is already registered.</exception>
    public static IGameKitMatchmakingBuilder AddLadder(
        this IGameKitMatchmakingBuilder builder,
        string name,
        Action<MatchmakingLadderConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // The interface method already enforces every guard (duplicate-name, invariants);
        // this surface exists so consumers can write the fluent form
        // services.AddGameKit().AddMatchmaking().AddLadder("main", cfg => ...).
        return builder.AddLadder(name, configure);
    }
}
