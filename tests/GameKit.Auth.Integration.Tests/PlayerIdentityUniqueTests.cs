// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Auth.Entities;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// Proves the UNIQUE(provider, external_id) constraint on <c>player_identities</c> surfaces as
/// Postgres SqlState 23505 when two different players try to claim the same Steam/Discord identity.
/// This is the database-level D-14 race anchor — no application-layer guard can substitute.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class PlayerIdentityUniqueTests
{
    private readonly PostgresFixture _pg;

    public PlayerIdentityUniqueTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Concurrent_Insert_Same_Provider_ExternalId_For_Different_Players_Throws_23505()
    {
        await ApplyCoreAndAuthMigrations(_pg.OwnerConnectionString);

        // Seed two players + one PlayerIdentity row for player A.
        var playerA = Guid.CreateVersion7();
        var playerB = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await using (var ctx = OpenContext(_pg.OwnerConnectionString))
        {
            ctx.Players.AddRange(
                new Player { Id = playerA, DisplayName = "Alice", CreatedAt = now },
                new Player { Id = playerB, DisplayName = "Bob", CreatedAt = now });
            ctx.Set<PlayerIdentity>().Add(new PlayerIdentity
            {
                Id = Guid.CreateVersion7(),
                PlayerId = playerA,
                Provider = "steam",
                ExternalId = "76561198000000001",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await ctx.SaveChangesAsync();
        }

        // Attempt to insert same (provider, external_id) for player B — expect 23505.
        await using (var ctx = OpenContext(_pg.OwnerConnectionString))
        {
            ctx.Set<PlayerIdentity>().Add(new PlayerIdentity
            {
                Id = Guid.CreateVersion7(),
                PlayerId = playerB,
                Provider = "steam",
                ExternalId = "76561198000000001",
                CreatedAt = now,
                UpdatedAt = now,
            });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
            Assert.IsType<PostgresException>(ex.InnerException);
            var pg = (PostgresException)ex.InnerException!;
            Assert.Equal("23505", pg.SqlState);
        }
    }

    private static GameKitDbContext OpenContext(string cs)
    {
        // Runtime query context — we need PlayerIdentity + the Core entities visible (different
        // from the migration context, which hides Core entities). Use the runtime
        // GameKitModelCustomizer but bypass DI by pre-building the extension list directly,
        // because EF's internal service provider does not always forward app services to
        // ReplaceService constructor injection (surfaced here in Phase 2 integration — see
        // SUMMARY deviation notes). We explicitly add the Auth model extension via a local
        // customizer wrapper so PlayerIdentity appears in the model at query time.
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs)
            .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>()
            .Options;
        return new GameKitDbContext(opts);
    }

    /// <summary>
    /// Runtime <see cref="IModelCustomizer"/> for the Auth query-side context. Applies Core's
    /// OnModelCreating (via base) AND Auth's three configurations directly — equivalent to the
    /// (runtime) GameKitModelCustomizer + AuthModelBuilderExtension composition, but without the
    /// DI-resolution path which does not always flow through EF's internal service provider for
    /// ReplaceService dependencies.
    /// </summary>
    internal sealed class AuthRuntimeQueryCustomizer : Microsoft.EntityFrameworkCore.Infrastructure.RelationalModelCustomizer
    {
        public AuthRuntimeQueryCustomizer(Microsoft.EntityFrameworkCore.Infrastructure.ModelCustomizerDependencies dependencies)
            : base(dependencies) { }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            new AuthModelBuilderExtension().ApplyTo(modelBuilder);
        }
    }

    private static async Task ApplyCoreAndAuthMigrations(string cs)
    {
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; });
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());
        await using var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            // Match the design-time Auth customizer so the model shape matches the Auth snapshot
            // (excludes Core entities from the Auth diff) and no PendingModelChangesWarning fires.
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .UseApplicationServiceProvider(sp)
            .Options;
        await using var authCtx = new GameKitDbContext(authOpts);
        await authCtx.Database.MigrateAsync();
    }
}
