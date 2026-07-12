// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Matchmaking.Builder;

/// <summary>
/// Internal implementation of <see cref="IGameKitMatchmakingBuilder"/>. Accumulates
/// <see cref="MatchmakingLadderConfig"/> instances at build time and validates per-ladder
/// invariants at registration time (fail-fast — mirrors Rankings precedent from Plan 04-04).
/// </summary>
/// <remarks>
/// Marked <c>internal</c> for production callers; exposed to <c>GameKit.Matchmaking.Tests</c>
/// via the <c>InternalsVisibleTo</c> grant in <c>src/GameKit.Matchmaking/AssemblyInfo.cs</c>
/// so the per-ladder default + duplicate-name + invalid-range guards can be unit-tested
/// without a hosting fixture.
/// </remarks>
internal sealed class GameKitMatchmakingBuilder : IGameKitMatchmakingBuilder
{
    private readonly List<MatchmakingLadderConfig> _ladders = new();
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Constructs the builder with the host's <see cref="IServiceCollection"/>.</summary>
    /// <param name="services">The service collection.</param>
    internal GameKitMatchmakingBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public IReadOnlyList<MatchmakingLadderConfig> RegisteredLadders => _ladders;

    /// <inheritdoc />
    public IGameKitMatchmakingBuilder AddLadder(string name, Action<MatchmakingLadderConfig>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ladder name must be non-empty.", nameof(name));

        if (!_names.Add(name))
            throw new InvalidOperationException(
                $"A matchmaking ladder with name '{name}' is already registered. Ladder names must be unique (case-insensitive).");

        var config = new MatchmakingLadderConfig { Name = name };
        configure?.Invoke(config);
        // Force the name field to the registration argument — callers cannot bypass the
        // duplicate guard by rewriting Name inside the configure callback.
        config.Name = name;

        ValidateLadderConfig(config);

        _ladders.Add(config);
        return this;
    }

    /// <summary>
    /// Enforces the per-ladder invariants documented on <see cref="MatchmakingLadderConfig"/>.
    /// Fail-fast at host configuration time so misconfiguration never reaches the matcher hot
    /// path (mitigates T-05-03-01).
    /// </summary>
    private static void ValidateLadderConfig(MatchmakingLadderConfig config)
    {
        if (config.BracketRampSeconds <= 0)
            throw new ArgumentException(
                $"{nameof(config.BracketRampSeconds)} must be > 0 (got {config.BracketRampSeconds}).",
                nameof(config));

        if (config.BracketEnd < config.BracketStart)
            throw new ArgumentException(
                $"{nameof(config.BracketEnd)} ({config.BracketEnd}) must be >= {nameof(config.BracketStart)} ({config.BracketStart}).",
                nameof(config));

        if (config.MaxPartyRatingSpread.HasValue && config.MaxPartyRatingSpread.Value <= 0)
            throw new ArgumentException(
                $"{nameof(config.MaxPartyRatingSpread)} must be > 0 when set (got {config.MaxPartyRatingSpread.Value}); use null to disable the cap.",
                nameof(config));

        if (config.MaxBracketWidth.HasValue && config.MaxBracketWidth.Value <= 0)
            throw new ArgumentException(
                $"{nameof(config.MaxBracketWidth)} must be > 0 when set (got {config.MaxBracketWidth.Value}); use null to disable the cap.",
                nameof(config));

        if (config.MaxBracketWidth.HasValue && config.MaxBracketWidth.Value < config.BracketStart)
            throw new ArgumentException(
                $"{nameof(config.MaxBracketWidth)} ({config.MaxBracketWidth.Value}) must be >= {nameof(config.BracketStart)} ({config.BracketStart}) when set, or omit {nameof(config.MaxBracketWidth)} to leave {nameof(config.BracketStart)} effective.",
                nameof(config));

        if (config.MinPoolDepthBeforeBracketExpansion.HasValue && config.MinPoolDepthBeforeBracketExpansion.Value <= 0)
            throw new ArgumentException(
                $"{nameof(config.MinPoolDepthBeforeBracketExpansion)} must be > 0 when set (got {config.MinPoolDepthBeforeBracketExpansion.Value}); use null to disable the guard.",
                nameof(config));

        if (config.AllowedRegions is { Count: > 0 })
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var region in config.AllowedRegions)
            {
                if (string.IsNullOrWhiteSpace(region))
                    throw new ArgumentException(
                        $"{nameof(config.AllowedRegions)} must not contain null, empty, or whitespace-only entries.",
                        nameof(config));

                if (region.Length > 64)
                    throw new ArgumentException(
                        $"{nameof(config.AllowedRegions)} entry '{region}' exceeds the 64-character maximum (PoolName column constraint).",
                        nameof(config));

                if (region.Equals("default", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(
                        $"Region name 'default' is reserved. Use null or omit {nameof(config.AllowedRegions)} to allow unrouted tickets.",
                        nameof(config));

                if (!Regex.IsMatch(region, @"^[a-zA-Z0-9\-]+$"))
                    throw new ArgumentException(
                        $"{nameof(config.AllowedRegions)} entry '{region}' may only contain alphanumeric characters and hyphens (Redis key safety: colons break the 4-segment mm:queue:{{id}}:{{region}} key format; glob chars corrupt SCAN patterns).",
                        nameof(config));

                if (!seen.Add(region))
                    throw new ArgumentException(
                        $"{nameof(config.AllowedRegions)} contains duplicate region name '{region}' (case-insensitive).",
                        nameof(config));
            }
        }

        if (config.MinParticipationFractionForRating.HasValue
            && (config.MinParticipationFractionForRating.Value < 0.0
                || config.MinParticipationFractionForRating.Value > 1.0))
            throw new ArgumentException(
                $"{nameof(config.MinParticipationFractionForRating)} must be between 0.0 and 1.0 when set (got {config.MinParticipationFractionForRating.Value}).",
                nameof(config));
    }
}
