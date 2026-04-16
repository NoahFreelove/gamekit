// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// CORE-14: Per-package migration history table isolation. Verifies that the migration history
/// table is <c>gamekit.__ef_migrations_core</c> (not the EF default <c>__EFMigrationsHistory</c>).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class MigrationHistoryIsolationTests
{
    private readonly PostgresFixture _pg;

    public MigrationHistoryIsolationTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task History_Table_Is_EfMigrationsCore_In_Gamekit_Schema()
    {
        // Ensure migrations applied
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        await using var sp = services.BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(
                scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();

        // The expected history table exists
        await using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT to_regclass('gamekit.__ef_migrations_core') IS NOT NULL";
            var result = (bool)(await c.ExecuteScalarAsync() ?? false);
            Assert.True(result, "gamekit.__ef_migrations_core must exist");
        }

        // The EF default name does NOT exist (proves per-package isolation)
        await using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT to_regclass('public.__EFMigrationsHistory') IS NOT NULL OR to_regclass('gamekit.__EFMigrationsHistory') IS NOT NULL";
            var result = (bool)(await c.ExecuteScalarAsync() ?? false);
            Assert.False(result, "EF default __EFMigrationsHistory must NOT be used");
        }
    }
}
