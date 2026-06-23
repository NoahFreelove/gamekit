// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Net.Http;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace GameKit.DR.Tests;

/// <summary>
/// Minimal ASP.NET Core test host for the DR round-trip test, wiring only
/// <c>AddGameKit</c> + <c>AddGameKitHealthChecks</c> + <c>MapGameKitHealth</c>.
/// Copied from <c>tests/GameKit.Core.Integration.Tests/HealthTestHost.cs</c> to avoid
/// a cross-test-project reference (which would pull xUnit test-discovery into this project
/// as a transitive source).
/// </summary>
/// <remarks>
/// Only the Core <c>CoreMigrationReadinessReporter</c> is registered here (via
/// <c>AddGameKitHealthChecks</c>), so <c>/health/ready</c> returns 200 when:
/// (a) the Core migrations are present in the restored DB, and
/// (b) Postgres is reachable.
/// The other five packages' migration reporters are NOT registered — they are validated
/// indirectly via the seeded player-row assertion which can only exist if the full schema
/// round-tripped successfully.
/// </remarks>
internal static class DrHealthTestHost
{
    /// <summary>
    /// Builds a minimal <see cref="WebApplication"/> that maps
    /// <c>/health/live</c> + <c>/health/ready</c> using the in-process
    /// <see cref="TestServer"/>.
    /// </summary>
    /// <param name="connectionString">Postgres connection string the health checks probe.</param>
    /// <returns>
    /// A tuple of the <see cref="WebApplication"/> (dispose when done) and an
    /// <see cref="HttpClient"/> connected to the in-process test server.
    /// </returns>
    internal static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        string connectionString)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();

        var gkBuilder = builder.Services.AddGameKit(opts =>
        {
            opts.ConnectionString = connectionString;
        });

        gkBuilder.AddGameKitHealthChecks();

        var app = builder.Build();
        app.MapGameKitHealth();

        await app.StartAsync();

        var client = app.GetTestServer().CreateClient();
        return (app, client);
    }
}
