// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Data;
using GameKit.Lobby.Data;
using GameKit.Matchmaking.Data;
using GameKit.Rankings.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Platformer3D.Integration.Tests;

/// <summary>
/// Test-only <see cref="RelationalModelCustomizer"/> that applies all five GameKit package
/// model extensions (Auth, Rankings, Matchmaking, Lobby) so the runtime
/// <see cref="GameKit.Core.Data.GameKitDbContext"/> can query all Platformer3D-relevant
/// entities in a single context.
/// </summary>
/// <remarks>
/// Applied via
/// <c>.ReplaceService&lt;IModelCustomizer, PlatformerTestModelCustomizer&gt;()</c>
/// in <see cref="PlatformerTestApp"/> — mirrors the <c>LobbyTestModelCustomizer</c> pattern
/// from <c>tests/GameKit.Lobby.Integration.Tests/LobbyTestModelCustomizer.cs</c>.
/// </remarks>
internal sealed class PlatformerTestModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public PlatformerTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new AuthModelBuilderExtension().ApplyTo(modelBuilder);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
        new MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
        new LobbyModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
