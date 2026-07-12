// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// HLTH-05 / D-14: Asserts that no health response body contains connection-string fragments,
/// host/port substrings, or credential patterns — even on the Postgres-down failure path where
/// Npgsql exception text would otherwise leak <c>host:port</c>.
/// </summary>
/// <remarks>
/// Phase 13's PII Roslyn analyzer guards span tags, not health payloads — this is a separate
/// runtime/test guard (D-14). The <see cref="GameKit.Core.Health.GameKitHealthResponseWriter"/>
/// (D-12) uses a whitelist serializer that omits <c>Exception</c>, <c>Data</c>, and <c>Tags</c>
/// from <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthReportEntry"/>.
/// These tests prove the whitelist is effective at runtime.
/// </remarks>
[Collection("PostgresAndRedis")]
[Trait("Category", "Integration")]
public sealed class HealthLeakTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    /// <summary>Constructs the test class with the shared container fixtures.</summary>
    /// <param name="pg">Postgres container fixture.</param>
    /// <param name="redis">Redis container fixture.</param>
    public HealthLeakTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // ── HLTH-05: /health/ready — healthy path ────────────────────────────────────────────

    /// <summary>
    /// HLTH-05: <c>/health/ready</c> body does NOT contain connection-string fragments when
    /// Postgres is healthy and migrations are applied (the happy path).
    /// </summary>
    [Fact]
    public async Task ReadyPayload_Healthy_DoesNot_Contain_ConnectionString_Fragments()
    {
        var freshConnStr = BuildFreshDbConnectionString("healthleak_ready_ok");
        await EnsureDatabaseExistsAsync(freshConnStr);
        await TestHelpers.ApplyCoreOnlyMigrationsAsync(freshConnStr);

        var (app, client) = await HealthTestHost.StartAsync(freshConnStr);
        await using (app)
        {
            var response = await client.GetAsync("/health/ready");
            var body = await response.Content.ReadAsStringAsync();

            AssertNoInfraFragments(body, freshConnStr);
        }
    }

    // ── HLTH-05: /health/ready — Postgres-down failure path ──────────────────────────────

    /// <summary>
    /// HLTH-05: <c>/health/ready</c> body does NOT contain connection-string fragments when
    /// Postgres is DOWN (the critical failure path — D-14). Npgsql exception text normally
    /// embeds <c>Host=…;Port=…</c>; the whitelist ResponseWriter must never forward it.
    /// </summary>
    [Fact]
    public async Task ReadyPayload_PostgresDown_DoesNot_Contain_ConnectionString_Fragments()
    {
        // Point at an unreachable Postgres — NpgsqlException message would contain host:port.
        // Using a realistic-looking but dead connection string to produce realistic exception text.
        const string deadConnStr = "Host=db.internal.example.com;Port=5432;Database=gamekit;Username=gamekit_app;Password=supersecret";

        var (app, client) = await HealthTestHost.StartAsync(deadConnStr);
        await using (app)
        {
            var response = await client.GetAsync("/health/ready");
            var body = await response.Content.ReadAsStringAsync();

            // The response MUST be 503 (Postgres unreachable → Unhealthy).
            Assert.Equal(503, (int)response.StatusCode);

            // And the body MUST NOT contain any infra details (D-12 / HLTH-05).
            AssertNoInfraFragments(body, deadConnStr);
        }
    }

    // ── HLTH-05: /health/live — liveness path ────────────────────────────────────────────

    /// <summary>
    /// HLTH-05: <c>/health/live</c> body does NOT contain connection-string fragments.
    /// Liveness runs zero checks so the body is always <c>{"status":"Healthy","checks":[]}</c>,
    /// but we assert the invariant explicitly to guard against future ResponseWriter regressions.
    /// </summary>
    [Fact]
    public async Task LivePayload_DoesNot_Contain_ConnectionString_Fragments()
    {
        const string deadConnStr = "Host=db.internal.example.com;Port=5432;Database=gamekit;Username=gamekit_app;Password=supersecret";

        var (app, client) = await HealthTestHost.StartAsync(deadConnStr);
        await using (app)
        {
            var response = await client.GetAsync("/health/live");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(200, (int)response.StatusCode);
            AssertNoInfraFragments(body, deadConnStr);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Asserts that <paramref name="body"/> does not contain any common connection-string or
    /// credential fragments (D-14 / HLTH-05). Also extracts and asserts the configured
    /// host substring is absent.
    /// </summary>
    private static void AssertNoInfraFragments(string body, string connectionString)
    {
        // Structural connection-string key names.
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Port=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username=", body, StringComparison.OrdinalIgnoreCase);

        // The actual configured hostname must not appear verbatim in the payload.
        const string hostPrefix = "Host=";
        const string passwordPrefix = "Password=";

        var hostEntry = connectionString
            .Split(';')
            .Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith(hostPrefix, StringComparison.OrdinalIgnoreCase));
        var hostValue = hostEntry is not null ? hostEntry[hostPrefix.Length..] : string.Empty;

        if (!string.IsNullOrEmpty(hostValue) && hostValue != "127.0.0.1")
        {
            // Skip the localhost / loopback case — "127.0.0.1" could be a substring of valid JSON.
            Assert.DoesNotContain(hostValue, body, StringComparison.OrdinalIgnoreCase);
        }

        // Password value must not appear.
        var passwordEntry = connectionString
            .Split(';')
            .Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith(passwordPrefix, StringComparison.OrdinalIgnoreCase));
        var passwordValue = passwordEntry is not null ? passwordEntry[passwordPrefix.Length..] : string.Empty;

        if (!string.IsNullOrEmpty(passwordValue))
        {
            Assert.DoesNotContain(passwordValue, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Builds a Postgres connection string targeting a unique test database.
    /// </summary>
    private string BuildFreshDbConnectionString(string dbName)
    {
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
        const string dbPrefix = "Database=";
        var dbEntry = connStr
            .Split(';')
            .Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith(dbPrefix, StringComparison.OrdinalIgnoreCase));
        var dbName = dbEntry is not null ? dbEntry[dbPrefix.Length..] : string.Empty;

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
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P04")
        {
            // 42P04 = duplicate_database — already exists, fine.
        }
    }
}
