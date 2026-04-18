// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>Applies Core + Auth migrations to a fresh Postgres and asserts all three Auth tables + history table exist.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class AuthSchemaTests
{
    private readonly PostgresFixture _pg;

    public AuthSchemaTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task AuthInitial_Migration_Creates_Three_Tables_And_History_Table()
    {
        // Arrange: build a service collection that registers Core + the Auth IModelBuilderExtension.
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());
        await using var sp = services.BuildServiceProvider();

        // Apply Core migrations first, then Auth migrations (two passes with separate migration contexts).
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }
        // Auth migration pass — build a distinct DbContext whose MigrationsAssembly is GameKit.Auth.
        await using (var authCtx = BuildAuthMigrationContext(_pg.OwnerConnectionString, sp))
        {
            await authCtx.Database.MigrateAsync();
        }

        // Assert: all three Auth tables + the __ef_migrations_auth history table exist in gamekit schema.
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        foreach (var table in new[] { "player_identities", "player_credentials", "refresh_tokens", "__ef_migrations_auth" })
        {
            await using var c = conn.CreateCommand();
            c.CommandText = $"SELECT to_regclass('gamekit.{table}') IS NOT NULL";
            var exists = (bool)(await c.ExecuteScalarAsync() ?? false);
            Assert.True(exists, $"gamekit.{table} must exist after AuthInitial");
        }

        // Assert: Core's history table is still intact (per-package isolation).
        await using (var cc = conn.CreateCommand())
        {
            cc.CommandText = "SELECT to_regclass('gamekit.__ef_migrations_core') IS NOT NULL";
            var coreHistoryExists = (bool)(await cc.ExecuteScalarAsync() ?? false);
            Assert.True(coreHistoryExists, "Core's __ef_migrations_core history table must coexist with Auth's");
        }

        // Assert: the UNIQUE(provider, external_id) constraint is present on player_identities.
        await using var uq = conn.CreateCommand();
        uq.CommandText =
            "SELECT COUNT(*) FROM pg_indexes " +
            "WHERE schemaname = 'gamekit' AND tablename = 'player_identities' " +
            "AND indexdef ILIKE '%UNIQUE%' AND indexdef ILIKE '%Provider%' AND indexdef ILIKE '%ExternalId%'";
        var count = (long)(await uq.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1, count);

        // Assert: the UNIQUE TokenHash index on refresh_tokens is present.
        await using var uq2 = conn.CreateCommand();
        uq2.CommandText =
            "SELECT COUNT(*) FROM pg_indexes " +
            "WHERE schemaname = 'gamekit' AND tablename = 'refresh_tokens' " +
            "AND indexdef ILIKE '%UNIQUE%' AND indexdef ILIKE '%TokenHash%'";
        var count2 = (long)(await uq2.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1, count2);

        // Assert: the citext extension was installed by the migration (Username column depends on it).
        await using var ext = conn.CreateCommand();
        ext.CommandText = "SELECT COUNT(*) FROM pg_extension WHERE extname = 'citext'";
        var extCount = (long)(await ext.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1, extCount);
    }

    private static GameKitDbContext BuildAuthMigrationContext(string cs, System.IServiceProvider sp)
    {
        // Use AuthMigrationModelCustomizer — it applies Auth entity configurations directly AND
        // marks Core entities ExcludeFromMigrations so the Auth snapshot diff matches the migration
        // history (avoids PendingModelChangesWarning that fires when the model shape differs from
        // the snapshot the Migrator compares against).
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .UseApplicationServiceProvider(sp)
            .Options;
        return new GameKitDbContext(opts);
    }
}
