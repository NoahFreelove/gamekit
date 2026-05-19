// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Rankings.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Test-only <see cref="RelationalModelCustomizer"/> that applies both
/// <c>MatchmakingModelBuilderExtension</c> and <see cref="RankingsModelBuilderExtension"/>
/// directly so cross-package integration tests can read <c>player_ranks</c>
/// (required by <c>EloRangeMatchmakingStrategy</c>) and write <c>matchmaking_tickets</c>
/// in a single <see cref="DbContext"/>.
/// </summary>
/// <remarks>
/// Bypasses EF's global model cache (PITFALLS §3) — mirrors
/// <c>tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs:323</c>
/// (<c>TickerTestModelCustomizer</c>). Applied via
/// <c>.ReplaceService&lt;IModelCustomizer, MatchmakingTestModelCustomizer&gt;()</c> in
/// per-test <see cref="DbContextOptionsBuilder"/> chains.
/// <para>
/// Wave 0 note (Plan 05-01): the fully-qualified
/// <c>GameKit.Matchmaking.Data.MatchmakingModelBuilderExtension</c> type does not yet
/// exist — it is created by Plan 05-02. This file therefore intentionally fails to
/// compile until 05-02 lands. The unit test project
/// (<c>GameKit.Matchmaking.Tests</c>) does not reference this customizer, so its build
/// remains green at Wave 0.
/// </para>
/// </remarks>
internal sealed class MatchmakingTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public MatchmakingTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new GameKit.Matchmaking.Data.MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
