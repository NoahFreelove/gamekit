// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Services;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="IRedisErrorRateCounter"/> / <see cref="RedisErrorRateCounter"/>
/// proving that errors written on one replica are visible in the health probe of another replica
/// via the shared Redis aggregate (ADMIN-14 SC#1).
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class RedisErrorCounterTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private AdminTestHost _hostA = default!;
    private AdminTestHost _hostB = default!;

    /// <summary>Initializes the test with shared Postgres + Redis fixtures.</summary>
    public RedisErrorCounterTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        // Reset admin_users so seeds below do not conflict with prior test runs.
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Both hosts share the same RedisFixture → same Redis container → shared error-rate keys.
        // Use distinct usernames to avoid the UNIQUE(username) constraint on the shared Postgres DB.
        _hostA = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("replica-a", "hunter2hunter2", AdminRoles.Superadmin));
        _hostB = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("replica-b", "hunter2hunter2", AdminRoles.Superadmin));
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _hostA.DisposeAsync();
        await _hostB.DisposeAsync();
    }

    /// <summary>
    /// SC#1: 15 errors incremented on host A surface as "Degraded" on host B's health probe
    /// via the shared Redis aggregate counter, proving cross-replica aggregation.
    /// </summary>
    [Fact(DisplayName = "SC#1: 15 errors on host A visible as Degraded on host B via Redis counter")]
    public async Task CrossReplica_ErrorRate_Visible_Across_Hosts()
    {
        // Write 15 errors via host A's IRedisErrorRateCounter.
        // 15 falls in the 10–99 band → "Degraded".
        var (scopeA, counterA) = _hostA.Resolve<IRedisErrorRateCounter>();
        using (scopeA)
        {
            for (var i = 0; i < 15; i++) counterA.IncrementError();
            // Allow fire-and-forget StringIncrementAsync writes to land in Redis.
            await Task.Delay(250);
        }

        // Read from host B's IHealthProbeService — should see the Redis aggregate.
        var (scopeB, probeB) = _hostB.Resolve<IHealthProbeService>();
        using (scopeB)
        {
            var report = await probeB.ProbeAsync(default);
            Assert.Equal("Degraded", report.ErrorRate.Status);
        }
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
            // Table does not yet exist — migrations will create it on first AdminTestHost start.
        }
    }
}
