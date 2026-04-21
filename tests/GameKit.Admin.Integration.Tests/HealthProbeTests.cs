// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="HealthProbeService"/> against the live Testcontainers
/// Postgres + Redis fixtures. All three tiles must report <c>OK</c> on a fresh deployment with
/// no errors logged. Extended error-rate assertion requires the ring buffer to have zero
/// events after host start — which it does (log provider is hooked but no Error logs fire
/// during the brief initialization window).
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class HealthProbeTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public HealthProbeTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    [Fact]
    public async Task ProbeAsync_Reports_Postgres_OK()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var (scope, svc) = host.Resolve<IHealthProbeService>();
        using (scope)
        {
            var report = await svc.ProbeAsync(default);
            Assert.Equal("OK", report.Postgres.Status);
            Assert.Contains("connected", report.Postgres.Detail);
            Assert.NotNull(report.Postgres.LatencyMs);
        }
    }

    [Fact]
    public async Task ProbeAsync_Reports_Redis_OK()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var (scope, svc) = host.Resolve<IHealthProbeService>();
        using (scope)
        {
            var report = await svc.ProbeAsync(default);
            Assert.Equal("OK", report.Redis.Status);
            Assert.NotNull(report.Redis.LatencyMs);
        }
    }

    [Fact]
    public async Task ProbeAsync_Reports_Zero_Errors_On_Fresh_Start()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("root", "hunter2hunter2", AdminRoles.Superadmin));

        var (scope, svc) = host.Resolve<IHealthProbeService>();
        using (scope)
        {
            var report = await svc.ProbeAsync(default);
            Assert.Equal("OK", report.ErrorRate.Status);
            Assert.StartsWith("0 errors", report.ErrorRate.Detail);
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
            // Table does not yet exist — migrations run on first AdminTestHost construction.
        }
    }
}
