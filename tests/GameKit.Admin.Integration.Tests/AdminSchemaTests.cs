// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Admin.UI.Data;
using GameKit.Auth.Data;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Applies Core + Auth + Admin migrations to a fresh Postgres and asserts the <c>admin_users</c>
/// table + <c>__ef_migrations_admin</c> history table exist alongside Core's and Auth's history
/// tables (per-package isolation per PITFALLS #3 / SP-14).
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class AdminSchemaTests
{
    private readonly PostgresFixture _pg;

    public AdminSchemaTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task AdminInitial_Creates_admin_users_And_History_Tables()
    {
        // Arrange: build a Core-only service provider — each migration pass uses its own
        // package-scoped customizer (Auth/Admin) which applies sibling configs directly without
        // needing IModelBuilderExtension registration. Per-package migration boundary (PITFALLS #3).
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        await using var sp = services.BuildServiceProvider();

        // Pass 1 — Core migrations (runtime path; uses GameKitModelCustomizer with empty extension list).
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // Pass 2 — Auth migrations (separate context with AuthMigrationModelCustomizer; the
        // customizer applies the three Auth configurations directly).
        await using (var authCtx = BuildAuthMigrationContext(_pg.OwnerConnectionString))
        {
            await MigrationRunner.MigrateWithLockAsync(
                authCtx, AuthMigrationConstants.AdvisoryLockKey);
        }

        // Pass 3 — Admin migrations (separate context with AdminMigrationModelCustomizer).
        await using (var adminCtx = BuildAdminMigrationContext(_pg.OwnerConnectionString))
        {
            await MigrationRunner.MigrateWithLockAsync(
                adminCtx, AdminMigrationConstants.AdvisoryLockKey);
        }

        // Assert: admin_users + __ef_migrations_admin exist.
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        foreach (var table in new[] { "admin_users", "__ef_migrations_admin" })
        {
            await using var c = conn.CreateCommand();
            c.CommandText = $"SELECT to_regclass('gamekit.{table}') IS NOT NULL";
            var exists = (bool)(await c.ExecuteScalarAsync() ?? false);
            Assert.True(exists, $"gamekit.{table} must exist after AdminInitial");
        }

        // Assert: Core + Auth history tables still coexist (per-package isolation; Admin migration
        // must not clobber its predecessors).
        foreach (var historyTable in new[] { "__ef_migrations_core", "__ef_migrations_auth" })
        {
            await using var c = conn.CreateCommand();
            c.CommandText = $"SELECT to_regclass('gamekit.{historyTable}') IS NOT NULL";
            var exists = (bool)(await c.ExecuteScalarAsync() ?? false);
            Assert.True(exists, $"gamekit.{historyTable} must coexist with __ef_migrations_admin");
        }

        // Assert: ck_admin_users_role CHECK constraint exists.
        await using (var ck = conn.CreateCommand())
        {
            ck.CommandText =
                "SELECT COUNT(*) FROM information_schema.check_constraints " +
                "WHERE constraint_schema = 'gamekit' AND constraint_name = 'ck_admin_users_role'";
            var count = (long)(await ck.ExecuteScalarAsync() ?? 0L);
            Assert.Equal(1, count);
        }

        // Assert: ix_admin_users_username UNIQUE index exists on the Username (citext) column.
        await using (var ux = conn.CreateCommand())
        {
            ux.CommandText =
                "SELECT COUNT(*) FROM pg_indexes " +
                "WHERE schemaname = 'gamekit' AND tablename = 'admin_users' " +
                "AND indexname = 'ix_admin_users_username' AND indexdef ILIKE '%UNIQUE%'";
            var count = (long)(await ux.ExecuteScalarAsync() ?? 0L);
            Assert.Equal(1, count);
        }
    }

    private static GameKitDbContext BuildAuthMigrationContext(string cs)
    {
        // No UseApplicationServiceProvider — AuthMigrationModelCustomizer applies the three Auth
        // entity configurations directly (matches AuthMigrationHostedService runtime path).
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .Options;
        return new GameKitDbContext(opts);
    }

    private static GameKitDbContext BuildAdminMigrationContext(string cs)
    {
        // No UseApplicationServiceProvider — the Admin migration path intentionally has no service
        // provider (FOLLOW-UP-02-03-01 resolution closed in 02-08). The customizer applies the
        // Admin entity directly + ExcludeFromMigrations on every Core/Auth entity.
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AdminMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AdminMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AdminMigrationModelCustomizer>()
            .Options;
        return new GameKitDbContext(opts);
    }
}
