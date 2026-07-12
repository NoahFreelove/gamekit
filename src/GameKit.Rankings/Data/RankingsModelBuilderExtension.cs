// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Data;
using GameKit.Rankings.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Rankings.Data;

/// <summary>
/// Sibling-package <see cref="IModelBuilderExtension"/> that contributes the seven Rankings entities
/// to the shared <c>GameKitDbContext</c> model at runtime. Registered via <c>TryAddEnumerable</c>
/// in <c>RankingsBuilderExtensions.AddRankings</c> (plan 04-04).
/// </summary>
internal sealed class RankingsModelBuilderExtension : IModelBuilderExtension
{
    /// <inheritdoc />
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new LadderConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerRankConfiguration());
        modelBuilder.ApplyConfiguration(new LadderSeasonConfiguration());
        modelBuilder.ApplyConfiguration(new SeasonRankArchiveConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceTokenConfiguration());
        modelBuilder.ApplyConfiguration(new PendingRatingUpdateConfiguration());
        modelBuilder.ApplyConfiguration(new SessionCompleteIdempotencyConfiguration());
    }
}
