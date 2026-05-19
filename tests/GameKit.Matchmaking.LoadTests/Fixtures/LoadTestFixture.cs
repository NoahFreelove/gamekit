// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Npgsql;
using Xunit;

namespace GameKit.Matchmaking.LoadTests.Fixtures;

/// <summary>
/// Phase 5 SC#3 load-harness fixture. Mirrors <c>MatchmakingIntegrationFixture</c>
/// but appends <c>Maximum Pool Size=25</c> to the Npgsql connection string (Pitfall §8 mitigation
/// — 1k-concurrent-ticket load tests must not exhaust the default Npgsql pool of 100 connections
/// shared across the WebApplicationFactory test host).
/// </summary>
/// <remarks>
/// <para>
/// Wave 0 scaffold (Plan 05-01). Plan 05-10 (SC#3 phase gate) wires the full
/// <c>WebApplicationFactory&lt;MatchmakingTestApp&gt;</c> harness on top of this fixture and runs
/// 1k tickets sustained 10 minutes against a single Redis + Postgres pair, asserting no matchmaker
/// iteration exceeds its configured budget and no Npgsql pool exhaustion is reported.
/// </para>
/// <para>
/// The 25-connection cap on a single test fixture is a deliberately conservative ceiling: the load
/// test exercises one ServiceProvider, and the matchmaker + reconciler + drain services together
/// open at most a handful of pooled connections. 25 leaves headroom for ad-hoc Npgsql connections
/// the test driver opens for seeding without ballooning the active count Postgres has to track.
/// </para>
/// </remarks>
internal sealed class LoadTestFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    /// <summary>
    /// Owner-role Postgres connection string with <c>Maximum Pool Size=25</c> appended
    /// (Pitfall §8 mitigation for the SC#3 1k-concurrent load test).
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>Redis connection string from the shared <see cref="RedisFixture"/>.</summary>
    public string RedisConnectionString => _redis.ConnectionString;

    /// <summary>Constructs the fixture with the shared Postgres + Redis collection fixtures.</summary>
    public LoadTestFixture(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        // Append Maximum Pool Size=25 via NpgsqlConnectionStringBuilder so the suffix is canonical
        // regardless of whether PostgresFixture's connection string already contains pool config.
        var builder = new NpgsqlConnectionStringBuilder(_pg.OwnerConnectionString)
        {
            MaxPoolSize = 25,
        };
        ConnectionString = builder.ConnectionString;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Builds a load-harness <see cref="IServiceProvider"/>. Wave 0 placeholder — Plan 05-10
    /// fills in the <c>WebApplicationFactory&lt;MatchmakingTestApp&gt;</c> harness body.
    /// </summary>
    public IServiceProvider BuildServiceProvider()
    {
        throw new NotImplementedException(
            "Wave 0 scaffold (Plan 05-01): LoadTestFixture.BuildServiceProvider() is wired by Plan 05-10 (SC#3 phase gate). " +
            "See .planning/phases/05-matchmaking-parties/05-01-PLAN.md Task 2 + 05-RESEARCH.md §Decision 13.");
    }
}
