// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Verifies the D-04 / D-05 startup gate: Production without a superadmin throws
/// <see cref="InvalidOperationException"/> at host start; Development logs a warning; seeded
/// superadmin lets the host come up. The <c>admin_users</c> table is TRUNCATEd in the
/// constructor (per-test reset) so each case sees a deterministic starting state.
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class SuperadminGateTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    /// <summary>Shared fixture injection.</summary>
    public SuperadminGateTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        // Reset admin_users so per-test assertions are deterministic. The table exists (Core+Auth+Admin
        // migrations are applied exactly once per Postgres fixture lifetime; subsequent AdminTestHost
        // constructions are idempotent).
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    [Fact]
    public async Task Production_WithZeroSuperadmins_Throws_AtStartup()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var host = await AdminTestHost.StartAsync(_pg, _redis, env: "Production");
        });
        Assert.Contains("dotnet gamekit admin create", ex.Message);
        Assert.Contains("no superadmin", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Development_WithZeroSuperadmins_Logs_Warning()
    {
        await using var host = await AdminTestHost.StartAsync(_pg, _redis, env: "Development");
        Assert.Contains(host.LogMessages, m => m.Contains("dotnet gamekit admin create"));
    }

    [Fact]
    public async Task Production_WithSeededSuperadmin_StartsSuccessfully()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg,
            _redis,
            env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));
        Assert.NotNull(host.Client);
    }

    private static void ResetAdminUsers(string connectionString)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "TRUNCATE TABLE gamekit.admin_users";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table does not yet exist — migrations run on first AdminTestHost construction.
            // Nothing to reset.
        }
    }
}
