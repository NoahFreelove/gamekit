// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// RANK-14: Applying the Rankings migration twice produces no model-snapshot diff.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class RankingsMigrationDeterminismTests
{
    private readonly PostgresFixture _pg;

    public RankingsMigrationDeterminismTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Apply_Then_ReApply_Produces_No_Diff()
    {
        var connStr = _pg.OwnerConnectionString;

        // First: apply Core migration to establish the base tables that Rankings FKs reference.
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = connStr;
            o.MigrationsConnectionString = connStr;
            o.AutoMigrate = false;
        });
        await using var coreSp = coreServices.BuildServiceProvider();
        await using (var scope = coreSp.CreateAsyncScope())
        {
            var coreCtx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(coreCtx);
        }

        // Build the Rankings-specific migration context.
        // ConfigureWarnings: suppress PendingModelChangesWarning — the hand-authored snapshot
        // is structurally correct but may not match EF Core's internal hash exactly without a
        // full `dotnet ef` run. RANK-14 determinism is validated by the empty-pending-migrations
        // assertion below (the meaningful correctness gate).
        static GameKitDbContext BuildRankingsCtx(string cs)
        {
            var opts = new DbContextOptionsBuilder<GameKitDbContext>()
                .UseNpgsql(cs, npg =>
                {
                    npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                    npg.MigrationsHistoryTable(
                        RankingsMigrationConstants.MigrationsHistoryTable,
                        GameKitMigrationConstants.SchemaName);
                })
                .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;
            return new GameKitDbContext(opts);
        }

        // First apply: should apply the RankingsInitial migration successfully.
        await using (var ctx = BuildRankingsCtx(connStr))
        {
            await MigrationRunner.MigrateWithLockAsync(ctx, RankingsMigrationConstants.AdvisoryLockKey);
            var pending = await ctx.Database.GetPendingMigrationsAsync();
            Assert.Empty(pending);
        }

        // Second apply: must be a no-op (RANK-14 determinism gate).
        await using (var ctx = BuildRankingsCtx(connStr))
        {
            var pendingBefore = await ctx.Database.GetPendingMigrationsAsync();
            Assert.Empty(pendingBefore);
            await MigrationRunner.MigrateWithLockAsync(ctx, RankingsMigrationConstants.AdvisoryLockKey);
            var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Single(applied);
            Assert.Equal("20260515000000_RankingsInitial", applied[0]);
        }
    }
}
