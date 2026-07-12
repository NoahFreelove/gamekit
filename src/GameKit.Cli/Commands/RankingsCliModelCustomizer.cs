// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Rankings.Data.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Cli.Commands;

/// <summary>
/// Runtime <see cref="IModelCustomizer"/> used by <c>gamekit service-token</c> commands to build a
/// <see cref="GameKit.Core.Data.GameKitDbContext"/> whose model contains the seven Rankings entities
/// applied directly on top of the Core entity baseline.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>AdminCliModelCustomizer</c> in rationale: EF Core caches the runtime model
/// <b>globally</b> per <c>GameKitDbContext</c> type across every service provider in the process
/// (the cache key does not include the application service provider). If any other
/// <c>AddGameKit</c>-based container in the same test runner created a context <em>without</em>
/// <c>RankingsModelBuilderExtension</c>, that extension-less model is cached and later
/// <c>AddGameKit + TryAddEnumerable</c> containers reuse it — crashing with
/// <c>"Cannot create a DbSet for 'ServiceToken'..."</c>.
/// </para>
/// <para>
/// Using <c>ReplaceService&lt;IModelCustomizer, RankingsCliModelCustomizer&gt;</c> bypasses the
/// <c>ApplicationServiceProvider</c> resolution entirely and builds a self-contained model that
/// always includes the Rankings entities (Pitfall 3).
/// </para>
/// </remarks>
internal sealed class RankingsCliModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer; forwards dependencies to the base.</summary>
    public RankingsCliModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.ApplyConfiguration(new LadderConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerRankConfiguration());
        modelBuilder.ApplyConfiguration(new LadderSeasonConfiguration());
        modelBuilder.ApplyConfiguration(new SeasonRankArchiveConfiguration());
        modelBuilder.ApplyConfiguration(new ServiceTokenConfiguration());
        modelBuilder.ApplyConfiguration(new PendingRatingUpdateConfiguration());
        modelBuilder.ApplyConfiguration(new SessionCompleteIdempotencyConfiguration());
    }
}
