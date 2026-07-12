// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Internal implementation of <see cref="IGameKitRankingsBuilder"/>. Accumulates
/// <see cref="LadderConfig"/> instances at build time; read by <c>StartupLadderUpserter</c>
/// at startup to upsert the registered ladders into the database.
/// </summary>
internal sealed class GameKitRankingsBuilder : IGameKitRankingsBuilder
{
    private readonly List<LadderConfig> _ladders = new();

    internal GameKitRankingsBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <inheritdoc />
    public IReadOnlyList<LadderConfig> RegisteredLadders => _ladders;

    /// <inheritdoc />
    public IGameKitRankingsBuilder AddLadder(string name, Action<LadderConfig>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ladder name must be non-empty.", nameof(name));

        // Case-insensitive duplicate guard — mirrors the citext uniqueness enforced at the DB level.
        foreach (var existing in _ladders)
        {
            if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"A ladder with name '{name}' is already registered. Ladder names must be unique (case-insensitive).",
                    nameof(name));
        }

        var config = new LadderConfig { Name = name };
        configure?.Invoke(config);
        // Ensure the name field is consistent even if the caller overwrote it in the callback.
        config.Name = name;

        _ladders.Add(config);
        return this;
    }
}
