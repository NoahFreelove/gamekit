// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// DIST-01 role isolation: <c>gamekit_reader</c> cannot INSERT into <c>gamekit.game_sessions</c>
/// (SQLSTATE 42501), while <c>gamekit_app</c> can.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class RoleIsolationTests
{
    private readonly PostgresFixture _pg;

    public RoleIsolationTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task GamekitReader_Cannot_Insert_Into_GameSessions()
    {
        // Ensure schema
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        await using var sp = services.BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
            await MigrationRunner.MigrateWithLockAsync(
                scope.ServiceProvider.GetRequiredService<GameKitDbContext>());

        await using var conn = new NpgsqlConnection(_pg.ReaderConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """INSERT INTO gamekit.game_sessions ("Id", "State", "CreatedAt") VALUES (@id, 'Pending', now())""";
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }

    [Fact]
    public async Task GamekitApp_Can_Insert_Into_GameSessions()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        await using var sp = services.BuildServiceProvider();
        await using (var scope = sp.CreateAsyncScope())
            await MigrationRunner.MigrateWithLockAsync(
                scope.ServiceProvider.GetRequiredService<GameKitDbContext>());

        await using var conn = new NpgsqlConnection(_pg.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """INSERT INTO gamekit.game_sessions ("Id", "State", "CreatedAt") VALUES (@id, 'Pending', now())""";
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());

        var rows = await cmd.ExecuteNonQueryAsync();
        Assert.Equal(1, rows);
    }
}
