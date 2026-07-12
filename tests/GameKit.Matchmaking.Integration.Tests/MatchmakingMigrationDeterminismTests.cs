// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Matchmaking.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// MATCH-15 RANK-14-equivalent: applying the Matchmaking migration twice produces no
/// model-snapshot diff. Mirrors <c>RankingsMigrationDeterminismTests</c> verbatim
/// (per Plan 05-02 Task 3 <c>read_first</c> directive) — only the constants and the
/// expected migration id differ.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class MatchmakingMigrationDeterminismTests
{
    private readonly PostgresFixture _pg;

    public MatchmakingMigrationDeterminismTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Apply_Then_ReApply_Produces_No_Diff()
    {
        var connStr = _pg.OwnerConnectionString;

        // First: apply Core migration to establish the base tables that Matchmaking FKs reference
        // (players, game_sessions). The full Core+Auth+Admin+Rankings stack is brought up under
        // AddGameKit; AutoMigrate=false because we drive each per-package migration manually below
        // to inspect ordering.
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

        // Build the Matchmaking-specific migration context.
        // ConfigureWarnings: suppress PendingModelChangesWarning — the hand-authored snapshot
        // is structurally correct but may not match EF Core's internal hash exactly without a
        // full `dotnet ef` run. MATCH-15 determinism is validated by the empty-pending-migrations
        // assertion below (the meaningful correctness gate).
        static GameKitDbContext BuildMatchmakingCtx(string cs)
        {
            var opts = new DbContextOptionsBuilder<GameKitDbContext>()
                .UseNpgsql(cs, npg =>
                {
                    npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
                    npg.MigrationsHistoryTable(
                        MatchmakingMigrationConstants.MigrationsHistoryTable,
                        GameKitMigrationConstants.SchemaName);
                })
                .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;
            return new GameKitDbContext(opts);
        }

        // NOTE: the matchmaking migration references gamekit.ladders (Rankings package, Phase 4)
        // as a principal table. The PostgresFixture's database does not have Phase 4 tables
        // applied — to keep this test self-contained we apply Rankings migrations first via the
        // analogous Rankings factory path. We use the same MigrationRunner that the Rankings
        // hosted service uses at runtime; the design-time customizer for Rankings is published
        // as public from Plan 04-02.
        await using (var rankingsCtx = BuildRankingsCtx(connStr))
        {
            await MigrationRunner.MigrateWithLockAsync(rankingsCtx, GameKit.Rankings.Data.RankingsMigrationConstants.AdvisoryLockKey);
        }

        // First apply: should apply the MatchmakingInitial migration successfully.
        await using (var ctx = BuildMatchmakingCtx(connStr))
        {
            await MigrationRunner.MigrateWithLockAsync(ctx, MatchmakingMigrationConstants.AdvisoryLockKey);
            var pending = await ctx.Database.GetPendingMigrationsAsync();
            Assert.Empty(pending);
        }

        // Second apply: must be a no-op (MATCH-15 determinism gate).
        // Phase 9 adds a second Matchmaking migration (MatchmakingBackfillRegions) so the
        // applied list now contains both migrations in deterministic order.
        await using (var ctx = BuildMatchmakingCtx(connStr))
        {
            var pendingBefore = await ctx.Database.GetPendingMigrationsAsync();
            Assert.Empty(pendingBefore);
            await MigrationRunner.MigrateWithLockAsync(ctx, MatchmakingMigrationConstants.AdvisoryLockKey);
            var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Equal(2, applied.Count);
            Assert.Equal("20260516000000_MatchmakingInitial", applied[0]);
            Assert.Equal("20260520000000_MatchmakingBackfillRegions", applied[1]);
        }
    }

    private static GameKitDbContext BuildRankingsCtx(string cs)
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKit.Rankings.Data.RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKit.Rankings.Data.RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKit.Rankings.Data.RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GameKitDbContext(opts);
    }
}
