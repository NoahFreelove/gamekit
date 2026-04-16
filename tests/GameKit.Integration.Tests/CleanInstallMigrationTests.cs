// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Integration.Tests;

/// <summary>
/// OPS-06 clean-install: AddGameKit + MigrateWithLockAsync against a fresh Testcontainers
/// Postgres creates all 4 Core tables plus the per-package history table.
/// </summary>
/// <remarks>
/// Phase 1 scope uses in-process <c>AddGameKit</c> rather than a full <c>dotnet pack</c>
/// roundtrip. Phase 6 extends this to the full pack-and-install loop once multi-package
/// dependency resolution is the failure mode worth testing.
/// </remarks>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class CleanInstallMigrationTests
{
    private readonly PostgresFixture _pg;

    public CleanInstallMigrationTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task CleanInstall_Core_Creates_Full_Schema()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        await using var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        var expected = new[]
        {
            "players",
            "game_sessions",
            "session_participants",
            "admin_audit_log",
            "__ef_migrations_core"
        };
        foreach (var table in expected)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT to_regclass('gamekit.{table}') IS NOT NULL";
            var exists = (bool)(await cmd.ExecuteScalarAsync() ?? false);
            Assert.True(exists, $"gamekit.{table} must exist after clean install");
        }
    }
}
