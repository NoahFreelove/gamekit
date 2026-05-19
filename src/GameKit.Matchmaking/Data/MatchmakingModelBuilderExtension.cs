// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Data;
using GameKit.Matchmaking.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Matchmaking.Data;

/// <summary>
/// Sibling-package <see cref="IModelBuilderExtension"/> that contributes the five Matchmaking
/// entities to the shared <c>GameKitDbContext</c> model at runtime. Registered via
/// <c>TryAddEnumerable</c> in <c>MatchmakingBuilderExtensions.AddMatchmaking</c> (lands in
/// later Phase 5 plan; this file is consumed at test time today by
/// <c>MatchmakingTestModelCustomizer</c> from Plan 05-01).
/// </summary>
internal sealed class MatchmakingModelBuilderExtension : IModelBuilderExtension
{
    /// <inheritdoc />
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PartyConfiguration());
        modelBuilder.ApplyConfiguration(new PartyMemberConfiguration());
        modelBuilder.ApplyConfiguration(new MatchmakingTicketConfiguration());
        modelBuilder.ApplyConfiguration(new TicketEventConfiguration());
        modelBuilder.ApplyConfiguration(new DeclineHistoryConfiguration());
    }
}
