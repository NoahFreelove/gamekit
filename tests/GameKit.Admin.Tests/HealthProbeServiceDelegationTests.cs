// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using GameKit.Admin.UI.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace GameKit.Admin.Tests;

/// <summary>
/// Wave-0 structural gate for HLTH-06 / D-15: asserts that <see cref="HealthProbeService"/>
/// was refactored to delegate Postgres and Redis health probes to
/// <see cref="HealthCheckService"/> (Core) rather than opening its own
/// <c>NpgsqlConnection</c> or calling <c>IDatabase.PingAsync</c>.
///
/// These are reflection-based constructor tests — no containers or stubs are spun up.
/// The runtime <c>GetTile</c> status mapping (Healthy-&gt;"OK", Degraded-&gt;"Degraded",
/// Unhealthy-&gt;"Down", absent-&gt;"Down"/"not configured") and the <c>ProbeAsync</c>
/// delegation behavior are covered by the Wave-3 integration tests
/// (<c>HealthProbeTests</c> in GameKit.Admin.Integration.Tests).
/// </summary>
public class HealthProbeServiceDelegationTests
{
    [Fact]
    public void ProbeAsync_Delegates_To_HealthCheckService_Not_NpgsqlConnection()
    {
        // Verify: no NpgsqlConnection / GameKitOptions / IConnectionMultiplexer constructor
        // parameter on HealthProbeService after the delegation refactor (HLTH-06 / D-15).
        var ctors = typeof(HealthProbeService).GetConstructors();
        foreach (var ctor in ctors)
        {
            var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToList();
            Assert.DoesNotContain(typeof(Npgsql.NpgsqlConnection), paramTypes);
            Assert.DoesNotContain(typeof(GameKit.Core.GameKitOptions), paramTypes);
            Assert.DoesNotContain(typeof(StackExchange.Redis.IConnectionMultiplexer), paramTypes);
        }
    }

    [Fact]
    public void Constructor_Takes_HealthCheckService()
    {
        // Verify: at least one constructor parameter is HealthCheckService — proves the
        // delegation seam exists and the class is wired for DI injection of the Core check set.
        var ctors = typeof(HealthProbeService).GetConstructors();
        var hasHealthCheckService = ctors.Any(ctor =>
            ctor.GetParameters().Any(p => p.ParameterType == typeof(HealthCheckService)));

        Assert.True(hasHealthCheckService,
            "HealthProbeService must take Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService " +
            "as a constructor parameter (D-15 delegation seam).");
    }
}
