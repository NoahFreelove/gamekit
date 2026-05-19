// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Matchmaking.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Matchmaking.LoadTests;

/// <summary>
/// Test-only <see cref="RelationalModelCustomizer"/> that applies the
/// <see cref="MatchmakingModelBuilderExtension"/> so the load-host
/// <see cref="DbContext"/> sees the Matchmaking entities (<c>parties</c>,
/// <c>party_members</c>, <c>matchmaking_tickets</c>, <c>decline_history</c>)
/// without depending on the global EF model cache.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestModelCustomizer.cs</c>
/// but does NOT apply the Rankings extension — the load test never queries
/// <c>player_ranks</c> directly (the strategy reads cached aggregate-ratings from the Redis
/// ticket hash; <c>PartyService</c> reads only parties + party_members). Avoiding the
/// Rankings application means LoadTests does not need <c>InternalsVisibleTo</c> from
/// <c>GameKit.Rankings</c>.
/// </para>
/// <para>
/// The <see cref="MatchmakingModelBuilderExtension"/> reference is accessible only because
/// <c>GameKit.Matchmaking</c> grants
/// <c>[assembly: InternalsVisibleTo("GameKit.Matchmaking.LoadTests")]</c> in its
/// <c>AssemblyInfo.cs</c> (Plan 05-01 invariant).
/// </para>
/// </remarks>
internal sealed class LoadTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public LoadTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
