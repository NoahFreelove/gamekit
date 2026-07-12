// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// Integration tests proving the Kubernetes probe contract: liveness stays 200 when Postgres
/// is unreachable (HLTH-01), and readiness gates 503→200 on migrations + Postgres (HLTH-01/02),
/// with Redis absence never blocking readiness (HLTH-02).
/// </summary>
[Collection("PostgresAndRedis")]
[Trait("Category", "Integration")]
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    /// <summary>Constructs the test class with the shared container fixtures.</summary>
    /// <param name="pg">Postgres container fixture.</param>
    /// <param name="redis">Redis container fixture.</param>
    public HealthEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // ── HLTH-01: Liveness ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// HLTH-01: <c>/health/live</c> returns 200 even when Postgres is unreachable.
    /// The liveness endpoint runs zero checks (Predicate = _ =&gt; false, D-03) so
    /// infrastructure failures never kill liveness.
    /// </summary>
    [Fact]
    public async Task Live_Returns_200_When_Postgres_Unreachable()
    {
        // Use a garbage connection string — Postgres is intentionally unreachable.
        const string deadConnStr = "Host=127.0.0.1;Port=9;Database=nope;Username=nope;Password=nope";

        var (app, client) = await HealthTestHost.StartAsync(deadConnStr);
        await using (app)
        {
            var response = await client.GetAsync("/health/live");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── HLTH-01/02: Readiness — migration gating ──────────────────────────────────────────

    /// <summary>
    /// HLTH-01/02: <c>/health/ready</c> returns 503 when Core migrations are pending
    /// (pointing at a clean DB with no migrations applied), then 200 once Core migrations
    /// are applied via the migration helper.
    /// </summary>
    [Fact]
    public async Task Ready_Returns_503_While_Migrations_Pending_Then_200_When_Applied()
    {
        // Use the admin connection string (full permissions) pointing at a fresh empty DB.
        // The test DB is managed by PostgresFixture; we want no migrations applied yet.
        // We use a new database name so no other test has run migrations against it.
        var freshConnStr = BuildFreshDbConnectionString("healthtest_migrations");

        // Create the database so Npgsql can connect to it (migrations check requires a connection).
        await EnsureDatabaseExistsAsync(freshConnStr);

        var (app, client) = await HealthTestHost.StartAsync(freshConnStr);
        await using (app)
        {
            // Before migrations: CoreMigrationReadinessReporter reports pending → Unhealthy → 503.
            var beforeResponse = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, beforeResponse.StatusCode);

            // Apply Core migrations using the owner connection string.
            await TestHelpers.ApplyCoreOnlyMigrationsAsync(freshConnStr);

            // After migrations: reporter latches → Healthy → 200.
            var afterResponse = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        }
    }

    // ── HLTH-02: Core-only (no Redis) ─────────────────────────────────────────────────────

    /// <summary>
    /// HLTH-02: On a Core-only host (no IConnectionMultiplexer in DI), <c>/health/ready</c>
    /// returns 503 when Postgres is unreachable (Postgres IS a hard gate, D-08).
    /// Redis absence never blocks readiness.
    /// </summary>
    [Fact]
    public async Task Ready_Returns_503_When_Postgres_Down_CoreOnly()
    {
        // Point at an unreachable host — no IConnectionMultiplexer registered (Core-only).
        const string deadConnStr = "Host=127.0.0.1;Port=9;Database=nope;Username=nope;Password=nope";

        var (app, client) = await HealthTestHost.StartAsync(deadConnStr, redisConnectionString: null);
        await using (app)
        {
            var response = await client.GetAsync("/health/ready");

            // Postgres unreachable → PostgresHealthCheck returns Unhealthy → 503.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
    }

    /// <summary>
    /// HLTH-02: On a Core-only host (no IConnectionMultiplexer), <c>/health/ready</c>
    /// returns 200 when Postgres is reachable and Core migrations are applied.
    /// Confirms that Redis absence NEVER blocks readiness (D-09).
    /// </summary>
    [Fact]
    public async Task Ready_Returns_200_When_Postgres_Up_CoreOnly_No_Redis()
    {
        // Use a fresh database with migrations applied, no Redis configured.
        var freshConnStr = BuildFreshDbConnectionString("healthtest_coreonly");
        await EnsureDatabaseExistsAsync(freshConnStr);
        await TestHelpers.ApplyCoreOnlyMigrationsAsync(freshConnStr);

        // No Redis connection string → no IConnectionMultiplexer → no "redis" check registered.
        var (app, client) = await HealthTestHost.StartAsync(freshConnStr, redisConnectionString: null);
        await using (app)
        {
            var response = await client.GetAsync("/health/ready");

            // Postgres reachable + migrations applied + no Redis gate → 200.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── HLTH-02: Redis configured but down ────────────────────────────────────────────────

    /// <summary>
    /// HLTH-02: When Redis IS configured (IConnectionMultiplexer in DI) but unreachable,
    /// <c>/health/ready</c> returns 503 (the "redis" check is Unhealthy, D-09).
    /// </summary>
    [Fact]
    public async Task Ready_Returns_503_When_Redis_Down()
    {
        // Use a fresh database with migrations applied.
        var freshConnStr = BuildFreshDbConnectionString("healthtest_redisdown");
        await EnsureDatabaseExistsAsync(freshConnStr);
        await TestHelpers.ApplyCoreOnlyMigrationsAsync(freshConnStr);

        // Register a Redis multiplexer pointing at a dead port so PING fails.
        // abortConnect=false prevents ConnectAsync from throwing — the multiplexer is created
        // but will be in a disconnected state, causing PingAsync to fail.
        const string deadRedis = "127.0.0.1:9,abortConnect=false";

        var (app, client) = await HealthTestHost.StartAsync(freshConnStr, redisConnectionString: deadRedis);
        await using (app)
        {
            var response = await client.GetAsync("/health/ready");

            // Redis unreachable → RedisHealthCheck returns Unhealthy → 503.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Postgres connection string targeting a unique test database under the
    /// owner role so each test that needs migration state isolation gets a clean slate.
    /// </summary>
    private string BuildFreshDbConnectionString(string dbName)
    {
        // Parse host/port from the fixture's owner connection string.
        var parts = _pg.OwnerConnectionString.Split(';');
        var host = string.Empty;
        var port = string.Empty;
        foreach (var part in parts)
        {
            if (part.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
                host = part["Host=".Length..];
            else if (part.StartsWith("Port=", StringComparison.OrdinalIgnoreCase))
                port = part["Port=".Length..];
        }
        return $"Host={host};Port={port};Database={dbName};Username=postgres;Password=postgres_test";
    }

    /// <summary>
    /// Creates the target database if it does not already exist (admin-role CREATE DATABASE).
    /// </summary>
    private async Task EnsureDatabaseExistsAsync(string connStr)
    {
        // Extract the database name from the connection string.
        var dbName = string.Empty;
        foreach (var part in connStr.Split(';'))
        {
            if (part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
            {
                dbName = part["Database=".Length..];
                break;
            }
        }

        // Connect to the "postgres" system db with superuser credentials.
        var parts = connStr.Split(';');
        var host = string.Empty;
        var port = string.Empty;
        foreach (var part in parts)
        {
            if (part.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
                host = part["Host=".Length..];
            else if (part.StartsWith("Port=", StringComparison.OrdinalIgnoreCase))
                port = part["Port=".Length..];
        }

        var adminConnStr = $"Host={host};Port={port};Database=postgres;Username=postgres;Password=postgres_test";
        await using var conn = new Npgsql.NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();
        // CREATE DATABASE is not transactional; use IF NOT EXISTS.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P04")
        {
            // 42P04 = duplicate_database — already exists, that's fine.
        }
    }
}
