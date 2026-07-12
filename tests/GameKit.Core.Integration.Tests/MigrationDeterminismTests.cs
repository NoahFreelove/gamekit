// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// OPS-06: Migration idempotency — running <c>Database.MigrateAsync</c> twice against a fresh
/// Postgres container produces zero pending migrations and leaves every migration defined in
/// the model applied (no model drift), with <c>CoreInitial</c> first.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class MigrationDeterminismTests
{
    private readonly PostgresFixture _pg;

    public MigrationDeterminismTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Migrate_Twice_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.MigrationsConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        await using var sp = services.BuildServiceProvider();

        // First migrate
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
            var pending = await ctx.Database.GetPendingMigrationsAsync();
            Assert.Empty(pending);
        }

        // Second migrate — must be no-op (no model drift)
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var pendingBefore = await ctx.Database.GetPendingMigrationsAsync();
            Assert.Empty(pendingBefore);
            await MigrationRunner.MigrateWithLockAsync(ctx);
            var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
            // Idempotency = every migration defined in the model is applied, in order, and no
            // more (no drift). Compare against the model's defined set rather than a hard-coded
            // count so adding a migration doesn't make this test stale.
            var defined = ctx.Database.GetMigrations().ToList();
            Assert.Equal(defined, applied);
            Assert.Equal("20260415000000_CoreInitial", applied[0]);
        }
    }
}
