// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Lobby.Data;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// Test-only <see cref="RelationalModelCustomizer"/> that applies
/// <c>LobbyModelBuilderExtension</c>, <c>MatchmakingModelBuilderExtension</c>, and
/// <c>RankingsModelBuilderExtension</c> so the runtime <c>GameKitDbContext</c> can query
/// <c>lobbies</c>, <c>lobby_members</c>, <c>matchmaking_tickets</c>, <c>parties</c>,
/// <c>party_members</c>, and <c>ladders</c> in a single context.
/// </summary>
/// <remarks>
/// Bypasses EF Core's global model cache (FOLLOW-UP-02-03-01) — applied via
/// <c>.ReplaceService&lt;IModelCustomizer, LobbyTestModelCustomizer&gt;()</c> in
/// <see cref="LobbyTestApp.StartAsync"/> so the runtime DbContext sees all three packages'
/// entity configurations without a design-time merge migration.
/// </remarks>
internal sealed class LobbyTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public LobbyTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new LobbyModelBuilderExtension().ApplyTo(modelBuilder);
        new MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
