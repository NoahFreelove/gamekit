// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Partial-class extension for <see cref="RankingsBuilderExtensions"/> that adds the
/// <c>.WithRatingsFrom&lt;T&gt;()</c> opt-in for rating-aware matchmaking (RANK-17).
/// </summary>
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Wires <typeparamref name="T"/> as the <see cref="IPlayerRatingProvider"/> for
    /// rating-aware matchmaking. Replaces the Core null-object default registered by
    /// <c>AddGameKit()</c> (RANK-17).
    /// </summary>
    /// <typeparam name="T">
    /// The <see cref="IPlayerRatingProvider"/> implementation to register. Use
    /// <c>RankingsRatingSource</c> for the built-in <c>player_ranks</c>-backed provider.
    /// </typeparam>
    /// <param name="builder">The <see cref="IGameKitRankingsBuilder"/> from <c>AddRankings()</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method uses <c>RemoveAll&lt;IPlayerRatingProvider&gt;()</c> followed by
    /// <c>AddScoped&lt;IPlayerRatingProvider, T&gt;()</c> —
    /// it does <b>NOT</b> use <c>TryAdd</c>. Core registers <c>NullPlayerRatingProvider</c> via
    /// <c>TryAddSingleton</c> in <c>AddGameKit()</c>; a second <c>TryAdd</c> would be a silent
    /// no-op, leaving the null-object active and matchmaking without real ratings.
    /// </para>
    /// <para>
    /// Omitting this call preserves the v1 zero-rating fallback: matchmaking still functions
    /// but uses rating=0 for all players (same behaviour as Phase 1–7).
    /// </para>
    /// </remarks>
    public static IGameKitRankingsBuilder WithRatingsFrom<T>(this IGameKitRankingsBuilder builder)
        where T : class, IPlayerRatingProvider
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<IPlayerRatingProvider>();
        builder.Services.AddScoped<IPlayerRatingProvider, T>();
        return builder;
    }
}
