// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Services;
using GameKit.Core.Entities;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="PlayerBanService"/>: Ban + Unban land the mutation and the
/// audit row inside the same SERIALIZABLE transaction, and the <c>before</c>/<c>after</c> JSON
/// snapshots match the player's state before/after the mutation (T-03-06-01).
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class PlayerBanServiceTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public PlayerBanServiceTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        // Reset per test so ban/unban counts start clean.
        ResetTables(_pg.OwnerConnectionString);
    }

    [Fact]
    public async Task BanAsync_Writes_Audit_Row_And_Flips_IsBanned()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        // Seed a player directly.
        var playerId = await SeedPlayerAsync("alice");
        var actorId = await GetSeededAdminIdAsync();

        // Ban through the service.
        var (scope, svc) = host.Resolve<IPlayerBanService>();
        using (scope)
        {
            await svc.BanAsync(playerId, actorId, "cheating", default);
        }

        // Assert: player.IsBanned=true AND admin_audit_log has one row with before.is_banned=false +
        // after.is_banned=true + reason="cheating".
        var (dbScope, ctx) = host.CreateDbScope();
        using (dbScope)
        {
            var player = await ctx.Set<Player>().AsNoTracking()
                .FirstAsync(p => p.Id == playerId);
            Assert.True(player.IsBanned);
            Assert.Equal("cheating", player.BanReason);
            Assert.NotNull(player.BannedAt);

            var audit = await ctx.Set<AdminAuditLog>().AsNoTracking()
                .Where(a => a.Action == AdminAuditActions.PlayerBan && a.TargetId == playerId)
                .SingleAsync();
            Assert.Equal("player", audit.TargetType);
            Assert.Equal(actorId, audit.ActorId);
            Assert.Equal("cheating", audit.Reason);
            Assert.NotNull(audit.Before);
            Assert.NotNull(audit.After);
            Assert.Contains("false", audit.Before!.RootElement.ToString());
            Assert.Contains("true", audit.After!.RootElement.ToString());
        }
    }

    [Fact]
    public async Task UnbanAsync_Writes_Audit_Row_And_Flips_IsBanned_To_False()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var playerId = await SeedPlayerAsync("bob", banned: true);
        var actorId = await GetSeededAdminIdAsync();

        var (scope, svc) = host.Resolve<IPlayerBanService>();
        using (scope)
        {
            await svc.UnbanAsync(playerId, actorId, "appealed successfully", default);
        }

        var (dbScope, ctx) = host.CreateDbScope();
        using (dbScope)
        {
            var player = await ctx.Set<Player>().AsNoTracking()
                .FirstAsync(p => p.Id == playerId);
            Assert.False(player.IsBanned);
            Assert.Null(player.BannedAt);
            Assert.Null(player.BanReason);

            var audit = await ctx.Set<AdminAuditLog>().AsNoTracking()
                .Where(a => a.Action == AdminAuditActions.PlayerUnban && a.TargetId == playerId)
                .SingleAsync();
            Assert.Equal("appealed successfully", audit.Reason);
        }
    }

    private async Task<Guid> SeedPlayerAsync(string displayName, bool banned = false)
    {
        var id = Guid.CreateVersion7();
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO gamekit.players " +
            "(\"Id\", \"DisplayName\", \"CreatedAt\", \"IsBanned\", \"BannedAt\", \"BanReason\") " +
            "VALUES ($1, $2, $3, $4, $5, $6)";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        cmd.Parameters.Add(new NpgsqlParameter { Value = displayName });
        cmd.Parameters.Add(new NpgsqlParameter { Value = DateTimeOffset.UtcNow });
        cmd.Parameters.Add(new NpgsqlParameter { Value = banned });
        cmd.Parameters.Add(new NpgsqlParameter { Value = banned ? (object)DateTimeOffset.UtcNow : DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { Value = banned ? (object)"prior" : DBNull.Value });
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> GetSeededAdminIdAsync()
    {
        await using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Id\" FROM gamekit.admin_users LIMIT 1";
        var id = (Guid)(await cmd.ExecuteScalarAsync() ?? Guid.Empty);
        if (id == Guid.Empty)
            throw new InvalidOperationException("No admin seeded — call SeedAdminAsync in the host seed callback.");
        return id;
    }

    private static void ResetTables(string connectionString)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "TRUNCATE TABLE gamekit.admin_audit_log; " +
                "TRUNCATE TABLE gamekit.admin_users; " +
                "DELETE FROM gamekit.players";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // First run: tables don't exist yet. Migrations apply on first AdminTestHost construction.
        }
    }
}
