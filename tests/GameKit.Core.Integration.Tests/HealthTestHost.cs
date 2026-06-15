// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net.Http;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// Minimal ASP.NET Core test host wiring <see cref="GameKitHealthBuilderExtensions.AddGameKitHealthChecks"/>
/// and <see cref="GameKitHealthBuilderExtensions.MapGameKitHealth"/> so integration tests can
/// assert <c>/health/live</c> + <c>/health/ready</c> HTTP responses.
/// </summary>
/// <remarks>
/// This host is intentionally minimal — it wires GameKit.Core only (no Auth, Matchmaking,
/// Admin, etc.), which means the only migration reporter registered is
/// <c>CoreMigrationReadinessReporter</c>. Test methods that need an unhealthy Postgres use a
/// garbage / unreachable connection string. Dispose the returned host when the test finishes.
/// </remarks>
public static class HealthTestHost
{
    /// <summary>
    /// Builds a <see cref="WebApplication"/> wiring the Core health checks and maps
    /// <c>/health/live</c> + <c>/health/ready</c>, then returns an <see cref="HttpClient"/>
    /// connected to the in-process <see cref="TestServer"/>.
    /// </summary>
    /// <param name="connectionString">Postgres connection string the checks probe.</param>
    /// <param name="redisConnectionString">
    /// Optional Redis connection string. When supplied, an <see cref="IConnectionMultiplexer"/>
    /// is registered so the conditional Redis check fires. When <see langword="null"/>, the
    /// Redis check is skipped (Core-only install path, D-09).
    /// </param>
    /// <returns>
    /// A tuple of the <see cref="WebApplication"/> (for disposal) and a preconfigured
    /// <see cref="HttpClient"/> that talks to the in-process server.
    /// </returns>
    public static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        string connectionString,
        string? redisConnectionString = null)
    {
        var builder = WebApplication.CreateBuilder();

        // Use TestServer so there is no real port binding needed.
        builder.WebHost.UseTestServer();

        // Optionally register IConnectionMultiplexer BEFORE AddGameKitHealthChecks() so
        // the conditional D-09 Redis-check guard fires when Redis is configured (Pitfall 1).
        if (redisConnectionString is not null)
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisConnectionString));
        }

        // Core services: AddGameKit returns IGameKitBuilder which we pass to AddGameKitHealthChecks.
        // The connection string may be an unreachable host for liveness tests — that is intentional.
        var gameKitBuilder = builder.Services.AddGameKit(opts =>
        {
            opts.ConnectionString = connectionString;
        });

        // Register the GameKit health checks (Postgres + optional Redis + migrations aggregate).
        gameKitBuilder.AddGameKitHealthChecks();

        var app = builder.Build();

        // Map health endpoints — outside any auth or rate-limit group (D-02/D-03).
        app.MapGameKitHealth();

        await app.StartAsync();

        var client = app.GetTestServer().CreateClient();
        return (app, client);
    }
}
